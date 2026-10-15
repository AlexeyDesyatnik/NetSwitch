using System;
using System.Collections.Generic;
using NetSwitch.Model;

namespace NetSwitch.Windows
{
    internal sealed class WindowsNetworkSystem : INetworkSystem
    {
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
            foreach (string command in Netsh.Ipv4Commands(adapter.Name, ReadConfigured(adapter), settings))
            {
                string? failure = Netsh.Run(command);
                if (failure != null) failures.Add(failure);
            }

            // netsh иногда сообщает об ошибке, сделав дело (например, «DHCP уже включён»), и наоборот.
            // Поэтому верим не коду возврата, а тому, что в итоге записано на адаптере.
            if (ReadConfigured(adapter).SameAs(settings)) return;

            throw new InvalidOperationException(failures.Count > 0
                ? string.Join(" ", failures)
                : "Windows приняла команды, но настройки адаптера не совпали с заданными.");
        }

        // Прочитанное раньше состояние могло устареть: адаптер только что включили или в него воткнули кабель
        private static Ipv4Settings ReadConfigured(Adapter adapter)
        {
            return TcpipRegistry.ReadSettings(adapter.Id, AdapterReader.ReadState(adapter.Id) ?? adapter.State);
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
