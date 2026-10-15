using NetSwitch.Model;
using NetSwitch.Windows;
using Xunit;

namespace NetSwitch.Tests
{
    public class TcpipRegistryTests
    {
        [Fact]
        public void Dhcp_adapter_has_everything_automatic()
        {
            var values = new TcpipInterfaceValues { EnableDhcp = 1 };

            Ipv4Settings settings = TcpipRegistry.ToSettings(values, AdapterState.Connected);

            Assert.True(settings.AutoAddress);
            Assert.True(settings.AutoDns);
            Assert.Equal("", settings.Address);
            Assert.Empty(settings.Dns);
        }

        [Fact]
        public void Static_adapter_gives_address_mask_gateway_and_dns()
        {
            var values = new TcpipInterfaceValues
            {
                EnableDhcp = 0,
                IpAddress = new[] { "10.68.12.37" },
                SubnetMask = new[] { "255.255.255.0" },
                DefaultGateway = new[] { "10.68.12.1" },
                NameServer = "10.0.68.10,10.0.68.11"
            };

            Ipv4Settings settings = TcpipRegistry.ToSettings(values, AdapterState.Connected);

            Assert.False(settings.AutoAddress);
            Assert.Equal("10.68.12.37", settings.Address);
            Assert.Equal("255.255.255.0", settings.Mask);
            Assert.Equal("10.68.12.1", settings.Gateway);
            Assert.False(settings.AutoDns);
            Assert.Equal(new[] { "10.0.68.10", "10.0.68.11" }, settings.Dns);
        }

        [Fact]
        public void Automatic_address_can_have_manual_dns()
        {
            var values = new TcpipInterfaceValues { EnableDhcp = 1, NameServer = "8.8.8.8 1.1.1.1" };

            Ipv4Settings settings = TcpipRegistry.ToSettings(values, AdapterState.Connected);

            Assert.True(settings.AutoAddress);
            Assert.False(settings.AutoDns);
            Assert.Equal(new[] { "8.8.8.8", "1.1.1.1" }, settings.Dns);
        }

        [Fact]
        public void Stale_static_values_are_ignored_while_address_is_automatic()
        {
            var values = new TcpipInterfaceValues
            {
                EnableDhcp = 1,
                IpAddress = new[] { "192.168.0.5" },
                SubnetMask = new[] { "255.255.255.0" },
                DefaultGateway = new[] { "192.168.0.1" }
            };

            Ipv4Settings settings = TcpipRegistry.ToSettings(values, AdapterState.Connected);

            Assert.Equal("", settings.Address);
            Assert.Equal("", settings.Mask);
            Assert.Equal("", settings.Gateway);
        }

        [Fact]
        public void First_real_address_wins_when_several_are_configured()
        {
            var values = new TcpipInterfaceValues
            {
                EnableDhcp = 0,
                IpAddress = new[] { "0.0.0.0", "10.68.12.37", "10.68.13.37" },
                SubnetMask = new[] { "0.0.0.0", "255.255.255.0", "255.255.0.0" },
                DefaultGateway = new[] { "", "10.68.12.1" }
            };

            Ipv4Settings settings = TcpipRegistry.ToSettings(values, AdapterState.Connected);

            Assert.Equal("10.68.12.37", settings.Address);
            Assert.Equal("255.255.255.0", settings.Mask);
            Assert.Equal("10.68.12.1", settings.Gateway);
        }

        [Fact]
        public void Manual_address_without_dns_servers_means_no_dns_not_automatic_dns()
        {
            var values = new TcpipInterfaceValues { EnableDhcp = 0, IpAddress = new[] { "10.68.12.37" }, SubnetMask = new[] { "255.255.255.0" } };

            Ipv4Settings settings = TcpipRegistry.ToSettings(values, AdapterState.Connected);

            Assert.False(settings.AutoDns);
            Assert.Empty(settings.Dns);
        }

        [Fact]
        public void Manual_settings_waiting_for_connection_are_read_as_manual()
        {
            // Так Windows записывает ручные настройки на адаптере с вынутым кабелем
            var values = new TcpipInterfaceValues
            {
                EnableDhcp = 1,
                DisableDhcpOnConnect = 1,
                IpAddress = new[] { "192.168.10.50" },
                SubnetMask = new[] { "255.255.255.0" },
                DefaultGateway = new[] { "192.168.10.1" },
                NameServer = "192.168.10.1,192.168.10.2"
            };

            Ipv4Settings settings = TcpipRegistry.ToSettings(values, AdapterState.NoLink);

            Assert.False(settings.AutoAddress);
            Assert.Equal("192.168.10.50", settings.Address);
            Assert.Equal("255.255.255.0", settings.Mask);
            Assert.Equal("192.168.10.1", settings.Gateway);
            Assert.False(settings.AutoDns);
            Assert.Equal(new[] { "192.168.10.1", "192.168.10.2" }, settings.Dns);
        }

        [Fact]
        public void Waiting_flag_without_manual_address_keeps_address_automatic()
        {
            var values = new TcpipInterfaceValues { EnableDhcp = 1, DisableDhcpOnConnect = 1, IpAddress = new[] { "0.0.0.0" } };

            Ipv4Settings settings = TcpipRegistry.ToSettings(values, AdapterState.NoLink);

            Assert.True(settings.AutoAddress);
            Assert.True(settings.AutoDns);
        }

        [Fact]
        public void Connected_adapter_follows_dhcp_flag_even_with_manual_settings_waiting()
        {
            var values = new TcpipInterfaceValues
            {
                EnableDhcp = 1,
                DisableDhcpOnConnect = 1,
                IpAddress = new[] { "192.168.10.50" },
                SubnetMask = new[] { "255.255.255.0" }
            };

            Ipv4Settings settings = TcpipRegistry.ToSettings(values, AdapterState.Connected);

            Assert.True(settings.AutoAddress);
            Assert.Equal("", settings.Address);
        }

        [Fact]
        public void Missing_dhcp_flag_means_automatic()
        {
            Assert.True(TcpipRegistry.ToSettings(new TcpipInterfaceValues(), AdapterState.Connected).AutoAddress);
        }
    }
}
