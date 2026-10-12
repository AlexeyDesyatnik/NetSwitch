using System;
using System.Collections.Generic;
using NetSwitch.Model;

namespace NetSwitch.Tests
{
    /// <summary>
    /// Управляемый компьютер в памяти: запись меняет то, что вернёт следующее чтение,
    /// как это происходит с настоящей системой.
    /// </summary>
    internal sealed class FakeSystem : INetworkSystem
    {
        public readonly List<Adapter> Adapters = new List<Adapter>();
        public readonly List<string> Calls = new List<string>();
        public readonly HashSet<string> Failing = new HashSet<string>();

        public Exception? AdaptersFailure;
        public Exception? ProxyFailure;

        public InteractiveUser User = new InteractiveUser(@"OFFICE\ivanov", "S-1-5-21-1-2-3-1001", true);
        public SystemProxyState SystemProxy = new SystemProxyState();
        public ProxySettings WinHttp = new ProxySettings();
        public ProxyPolicy Policy = new ProxyPolicy();

        public static Adapter Ethernet(AdapterState state = AdapterState.Connected)
        {
            return new Adapter
            {
                Id = "{00000000-0000-0000-0000-000000000001}",
                Name = "Ethernet",
                Description = "Intel(R) Ethernet Controller I225-V",
                IsPhysical = true,
                State = state
            };
        }

        public static Ipv4Settings Static(string address, string gateway = "10.68.12.1", params string[] dns)
        {
            var settings = new Ipv4Settings { AutoAddress = false, Address = address, Mask = "255.255.255.0", Gateway = gateway, AutoDns = false };
            settings.Dns.AddRange(dns);
            return settings;
        }

        public IList<Adapter> ReadAdapters()
        {
            if (AdaptersFailure != null) throw AdaptersFailure;
            return new List<Adapter>(Adapters);
        }

        public ProxyState ReadProxy()
        {
            if (ProxyFailure != null) throw ProxyFailure;
            return new ProxyState(User, SystemProxy.Clone(), WinHttp.Clone(), Policy);
        }

        public void SetAdapterEnabled(Adapter adapter, bool enabled)
        {
            Do("adapter " + adapter.Name + (enabled ? " on" : " off"), "adapter");
            Find(adapter).State = enabled ? AdapterState.NoLink : AdapterState.Disabled;
        }

        public void ApplyIpv4(Adapter adapter, Ipv4Settings settings)
        {
            Do("ipv4 " + adapter.Name, "ipv4");
            Find(adapter).Configured = settings.Clone();
        }

        public void ApplySystemProxy(SystemProxyState state)
        {
            Do("system proxy", "system proxy");
            SystemProxy = state.Clone();
        }

        public void ApplyWinHttpProxy(ProxySettings settings)
        {
            Do("winhttp", "winhttp");
            WinHttp = settings.Clone();
        }

        private void Do(string call, string kind)
        {
            Calls.Add(call);
            if (Failing.Contains(kind)) throw new InvalidOperationException("отказ: " + kind);
        }

        private Adapter Find(Adapter adapter)
        {
            return Adapters.Find(candidate => candidate.Id == adapter.Id);
        }
    }
}
