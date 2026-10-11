using NetSwitch.Model;
using NetSwitch.UI;
using Xunit;

namespace NetSwitch.Tests
{
    public class DescribeTests
    {
        [Fact]
        public void Connected_adapter_shows_what_it_actually_has()
        {
            var adapter = new Adapter();
            adapter.Active.Addresses.Add("10.68.12.37/24");
            adapter.Active.Gateways.Add("10.68.12.1");
            adapter.Active.Dns.Add("10.0.68.10");
            adapter.Active.Dns.Add("10.0.68.11");

            Assert.Equal("10.68.12.37/24", Describe.Address(adapter));
            Assert.Equal("10.68.12.1", Describe.Gateway(adapter));
            Assert.Equal("10.0.68.10, 10.0.68.11", Describe.Dns(adapter));
            Assert.Equal("авто", Describe.Mode(adapter.Configured));
        }

        [Fact]
        public void Adapter_without_link_shows_its_manual_settings()
        {
            var adapter = new Adapter
            {
                State = AdapterState.NoLink,
                Configured = new Ipv4Settings { AutoAddress = false, Address = "10.68.12.37", Mask = "255.255.255.0", Gateway = "10.68.12.1", AutoDns = false }
            };
            adapter.Configured.Dns.Add("10.0.68.10");

            Assert.Equal("10.68.12.37/24", Describe.Address(adapter));
            Assert.Equal("10.68.12.1", Describe.Gateway(adapter));
            Assert.Equal("10.0.68.10", Describe.Dns(adapter));
            Assert.Equal("вручную", Describe.Mode(adapter.Configured));
        }

        [Fact]
        public void Automatic_adapter_without_link_has_nothing_to_show()
        {
            var adapter = new Adapter { State = AdapterState.NoLink };

            Assert.Equal("—", Describe.Address(adapter));
            Assert.Equal("—", Describe.Gateway(adapter));
            Assert.Equal("—", Describe.Dns(adapter));
        }

        [Fact]
        public void Manual_dns_with_automatic_address_is_called_out()
        {
            Assert.Equal("авто, DNS вручную", Describe.Mode(new Ipv4Settings { AutoAddress = true, AutoDns = false }));
        }

        [Fact]
        public void Disabled_proxy_is_just_off()
        {
            Assert.Equal("выключен", Describe.Proxy(ProxySettings.FromRaw(false, "old:8080", "a;b")));
        }

        [Fact]
        public void Enabled_proxy_lists_server_exceptions_and_local_bypass()
        {
            ProxySettings settings = ProxySettings.FromRaw(true, "10.0.68.52:3128", "*.corp.local;10.*;<local>");

            Assert.Equal("10.0.68.52:3128; исключения: *.corp.local, 10.*; не для локальных адресов", Describe.Proxy(settings));
        }

        [Fact]
        public void System_proxy_mentions_auto_detect_and_script()
        {
            var state = new SystemProxyState { AutoDetect = true, AutoConfigUrl = "http://wpad/proxy.pac" };

            Assert.Equal("выключен; автоопределение включено; сценарий: http://wpad/proxy.pac", Describe.SystemProxy(state));
        }
    }
}
