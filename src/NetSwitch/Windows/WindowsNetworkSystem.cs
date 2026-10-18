using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;
using NetSwitch.Model;

namespace NetSwitch.Windows
{
    internal sealed class WindowsNetworkSystem : INetworkSystem
    {
        private const int SettleTimeoutMs = 5000;
        private const int SettlePollMs = 300;

        public IList<Adapter> ReadAdapters()
        {
            return AdapterReader.ReadAll();
        }

        public ProxyState ReadProxy()
        {
            InteractiveUser user = InteractiveUserReader.Read();
            return new ProxyState(
                user,
                ProxyReader.ReadSystemProxy(user.Sid),
                ProxyReader.ReadWinHttp(),
                ProxyReader.ReadPolicy(user.Sid));
        }

        public void SetAdapterEnabled(Adapter adapter, bool enabled)
        {
            AdapterSwitch.SetEnabled(adapter.Id, adapter.Name, enabled);
        }

        public void ApplyIpv4(Adapter adapter, Ipv4Settings settings)
        {
            var failures = new List<string>();
            RunAll(Netsh.Ipv4Commands(adapter.Name, ReadConfigured(adapter), settings), failures);

            // Без соединения «source=dhcp» не снимает ждущие подключения ручные настройки: netsh отвечает,
            // что DHCP уже включён. Тогда стираем ручные адреса и включаем DHCP в постоянных настройках (ADR 0003).
            if (settings.AutoAddress && CurrentState(adapter) != AdapterState.Connected && !ReadConfigured(adapter).AutoAddress)
            {
                ReturnToDhcp(adapter, failures);
            }

            // netsh иногда сообщает об ошибке, сделав дело (например, «DHCP уже включён»), и наоборот.
            // Поэтому верим не коду возврата, а тому, что в итоге записано на адаптере.
            if (Settles(adapter, settings)) return;

            throw new InvalidOperationException(failures.Count > 0
                ? string.Join(" ", failures)
                : "Windows приняла команды, но настройки адаптера не совпали с заданными.");
        }

        private static void ReturnToDhcp(Adapter adapter, List<string> failures)
        {
            try
            {
                // Без MSFT_NetIPInterface (до Windows 8) флаг не снять, а стёртый адрес оставил бы адаптер
                // совсем без адреса после подключения. Поэтому сначала убеждаемся, что включить DHCP получится.
                DhcpSwitch.EnsureAvailable(adapter.Id, adapter.Name);

                TcpipInterfaceValues recorded = TcpipRegistry.Read(adapter.Id) ?? new TcpipInterfaceValues();
                // Если адрес или шлюз не стёрлись, включённый DHCP скрыл бы их от проверки после применения
                if (RunAll(Netsh.DeleteAddressCommands(adapter.Name, recorded), failures))
                {
                    DhcpSwitch.EnablePersistent(adapter.Id, adapter.Name);
                }
            }
            catch (InvalidOperationException e)
            {
                failures.Add(e.Message);
            }
            catch (ManagementException e)
            {
                failures.Add("Windows не смогла включить DHCP на адаптере «" + adapter.Name + "»: " + e.Message);
            }
        }

        /// <summary>
        /// Только что включённый адаптер подключается несколько секунд, и всё это время Windows переписывает
        /// его настройки в реестре (#9). Поэтому несовпадение перепроверяем, пока оно не устоится.
        /// </summary>
        private static bool Settles(Adapter adapter, Ipv4Settings settings)
        {
            for (int waited = 0; !ReadConfigured(adapter).SameAs(settings); waited += SettlePollMs)
            {
                if (waited >= SettleTimeoutMs) return false;
                Thread.Sleep(SettlePollMs);
            }
            return true;
        }

        /// <returns>true, если все команды выполнились успешно.</returns>
        private static bool RunAll(IList<string> commands, List<string> failures)
        {
            int before = failures.Count;
            foreach (string command in commands)
            {
                string? failure = Netsh.Run(command);
                if (failure != null) failures.Add(failure);
            }
            return failures.Count == before;
        }

        // Прочитанное раньше состояние могло устареть: адаптер только что включили или в него воткнули кабель
        private static AdapterState CurrentState(Adapter adapter)
        {
            return AdapterReader.ReadState(adapter.Id) ?? adapter.State;
        }

        private static Ipv4Settings ReadConfigured(Adapter adapter)
        {
            return TcpipRegistry.ReadSettings(adapter.Id, CurrentState(adapter));
        }

        public void ApplySystemProxy(SystemProxyState state)
        {
            ProxyWriter.WriteSystemProxy(InteractiveUserReader.Read(), state);
        }

        public void ApplyWinHttpProxy(ProxySettings settings)
        {
            ProxyWriter.WriteWinHttp(settings);
        }
    }
}
