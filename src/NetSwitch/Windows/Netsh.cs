using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using NetSwitch.Model;

namespace NetSwitch.Windows
{
    /// <summary>
    /// Настройки IPv4 меняются штатной утилитой netsh: в отличие от WMI она работает и тогда,
    /// когда на адаптере нет соединения, и одинаково ведёт себя от Windows 7 до 11.
    /// </summary>
    internal static class Netsh
    {
        private const int TimeoutMs = 30000;

        /// <summary>Все запуски netsh за время работы инструмента — для отчёта.</summary>
        public static readonly List<string> History = new List<string>();

        /// <summary>Команды, переводящие адаптер из текущих настроек в заданные. Для совпадающих частей команд нет.</summary>
        public static IList<string> Ipv4Commands(string adapterName, Ipv4Settings current, Ipv4Settings target)
        {
            var commands = new List<string>();
            string adapter = "name=\"" + adapterName + "\"";

            if (target.AutoAddress)
            {
                if (!current.AutoAddress) commands.Add("interface ipv4 set address " + adapter + " source=dhcp");
            }
            else if (current.AutoAddress || !SameAddress(current, target))
            {
                // Нет уверенности, что на всех версиях Windows netsh заменит прежний шлюз, а не оставит
                // его рядом с новым. Проход через автополучение надёжно стирает всё заданное вручную.
                if (!current.AutoAddress && !Ipv4.Same(current.Gateway, target.Gateway))
                {
                    commands.Add("interface ipv4 set address " + adapter + " source=dhcp");
                }
                commands.Add("interface ipv4 set address " + adapter + " source=static address=" + target.Address
                    + " mask=" + target.Mask + " gateway=" + (target.Gateway.Length > 0 ? target.Gateway : "none"));
            }

            if (target.AutoDns)
            {
                if (!current.AutoDns) commands.Add("interface ipv4 set dnsservers " + adapter + " source=dhcp");
            }
            else if (current.AutoDns || !SameDns(current, target))
            {
                // validate=no: без соединения проверка сервера только задерживает и даёт ложную ошибку
                commands.Add("interface ipv4 set dnsservers " + adapter + " source=static address="
                    + (target.Dns.Count > 0 ? target.Dns[0] : "none") + " validate=no");
                for (int i = 1; i < target.Dns.Count; i++)
                {
                    commands.Add("interface ipv4 add dnsservers " + adapter + " address=" + target.Dns[i]
                        + " index=" + (i + 1).ToString(CultureInfo.InvariantCulture) + " validate=no");
                }
            }

            return commands;
        }

        /// <summary>
        /// Команды, стирающие записанные на адаптере ручные адреса и шлюзы. Нужны там, где «source=dhcp»
        /// не срабатывает: на адаптере без соединения ручные настройки ждут подключения, а DHCP формально включён.
        /// </summary>
        public static IList<string> DeleteAddressCommands(string adapterName, TcpipInterfaceValues values)
        {
            var commands = new List<string>();
            foreach (string address in values.IpAddress)
            {
                if (TcpipRegistry.IsBlank(address)) continue;

                // Шлюзы удаляются один раз, вместе с первым адресом
                commands.Add("interface ipv4 delete address name=\"" + adapterName + "\" address=" + address.Trim()
                    + (commands.Count == 0 ? " gateway=all" : ""));
            }
            return commands;
        }

        /// <summary>Выполняет команду и возвращает описание сбоя; null, если netsh завершился успешно.</summary>
        public static string? Run(string arguments)
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"), arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // netsh пишет в кодировке консоли, а не в ANSI
                StandardOutputEncoding = Encoding.GetEncoding((int)GetOEMCP()),
                StandardErrorEncoding = Encoding.GetEncoding((int)GetOEMCP())
            };

            using (Process process = Process.Start(start)!)
            {
                var output = new StringBuilder();
                process.OutputDataReceived += (sender, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (sender, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(TimeoutMs))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    History.Add("netsh " + arguments + " → не завершился за " + TimeoutMs / 1000 + " с");
                    return "netsh не завершился за " + TimeoutMs / 1000 + " секунд.";
                }
                process.WaitForExit();

                string text;
                lock (output) text = output.ToString().Trim();

                History.Add("netsh " + arguments + " → код " + process.ExitCode + (text.Length > 0 ? ": " + text.Replace("\r\n", " ") : ""));
                if (process.ExitCode == 0) return null;
                return text.Length > 0 ? text : "netsh завершился с кодом " + process.ExitCode + ".";
            }
        }

        private static bool SameAddress(Ipv4Settings one, Ipv4Settings other)
        {
            return Ipv4.Same(one.Address, other.Address) && Ipv4.Same(one.Mask, other.Mask) && Ipv4.Same(one.Gateway, other.Gateway);
        }

        private static bool SameDns(Ipv4Settings one, Ipv4Settings other)
        {
            if (one.Dns.Count != other.Dns.Count) return false;
            for (int i = 0; i < one.Dns.Count; i++)
            {
                if (!Ipv4.Same(one.Dns[i], other.Dns[i])) return false;
            }
            return true;
        }

        [DllImport("kernel32.dll")]
        private static extern uint GetOEMCP();
    }
}
