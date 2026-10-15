using System;
using System.Collections.Generic;
using Microsoft.Win32;
using NetSwitch.Model;

namespace NetSwitch.Windows
{
    /// <summary>Значения IPv4 адаптера в том виде, в каком они лежат в реестре.</summary>
    internal sealed class TcpipInterfaceValues
    {
        public int? EnableDhcp { get; set; }
        public int? DisableDhcpOnConnect { get; set; }
        public string[] IpAddress { get; set; } = new string[0];
        public string[] SubnetMask { get; set; } = new string[0];
        public string[] DefaultGateway { get; set; } = new string[0];
        public string NameServer { get; set; } = "";
    }

    /// <summary>
    /// Настроенное на адаптере хранится в реестре и доступно всегда. WMI и сетевой стек отдают
    /// настройки только пока адаптер включён и есть соединение.
    /// </summary>
    internal static class TcpipRegistry
    {
        public const string InterfacesKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

        public static TcpipInterfaceValues? Read(string adapterId)
        {
            using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(InterfacesKey + "\\" + adapterId))
            {
                if (key == null) return null;

                return new TcpipInterfaceValues
                {
                    EnableDhcp = Number(key.GetValue("EnableDHCP")),
                    DisableDhcpOnConnect = Number(key.GetValue("DisableDhcpOnConnect")),
                    IpAddress = Strings(key.GetValue("IPAddress")),
                    SubnetMask = Strings(key.GetValue("SubnetMask")),
                    DefaultGateway = Strings(key.GetValue("DefaultGateway")),
                    NameServer = key.GetValue("NameServer") as string ?? ""
                };
            }
        }

        /// <summary>Настроенное на адаптере; у адаптера без раздела в реестре всё автоматически.</summary>
        public static Ipv4Settings ReadSettings(string adapterId, AdapterState state)
        {
            return ToSettings(Read(adapterId) ?? new TcpipInterfaceValues(), state);
        }

        /// <param name="state">Без соединения признак «автоматически» в реестре не окончательный.</param>
        public static Ipv4Settings ToSettings(TcpipInterfaceValues values, AdapterState state)
        {
            // Без соединения Windows записывает ручные настройки, но EnableDHCP не сбрасывает
            // до подключения: о том, что адрес задан вручную, говорит только DisableDhcpOnConnect.
            // Флаг может остаться от прежних ручных настроек, поэтому без ручного адреса ему не верим
            bool manualPending = state != AdapterState.Connected
                && values.DisableDhcpOnConnect == 1
                && Array.Exists(values.IpAddress, address => !IsBlank(address));

            // Без явного запрета Windows получает адрес автоматически
            var settings = new Ipv4Settings { AutoAddress = values.EnableDhcp != 0 && !manualPending };

            if (!settings.AutoAddress)
            {
                for (int i = 0; i < values.IpAddress.Length; i++)
                {
                    if (IsBlank(values.IpAddress[i])) continue;

                    settings.Address = values.IpAddress[i];
                    settings.Mask = i < values.SubnetMask.Length ? values.SubnetMask[i] : "";
                    break;
                }
                foreach (string gateway in values.DefaultGateway)
                {
                    if (IsBlank(gateway)) continue;

                    settings.Gateway = gateway;
                    break;
                }
            }

            // При адресе вручную Windows не берёт DNS автоматически: пустой список значит «DNS нет»
            settings.Dns.AddRange(Split(values.NameServer));
            settings.AutoDns = settings.AutoAddress && settings.Dns.Count == 0;
            return settings;
        }

        private static bool IsBlank(string address)
        {
            return address.Trim().Length == 0 || address.Trim() == "0.0.0.0";
        }

        private static int? Number(object? value)
        {
            return value is int ? (int?)(int)value : null;
        }

        // Windows разных версий пишет списки то многострочным значением, то одной строкой
        private static string[] Strings(object? value)
        {
            var many = value as string[];
            if (many != null) return many;

            var one = value as string;
            return one != null ? Split(one) : new string[0];
        }

        private static string[] Split(string list)
        {
            return list.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
