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

            Ipv4Settings settings = TcpipRegistry.ToSettings(values);

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

            Ipv4Settings settings = TcpipRegistry.ToSettings(values);

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

            Ipv4Settings settings = TcpipRegistry.ToSettings(values);

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

            Ipv4Settings settings = TcpipRegistry.ToSettings(values);

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

            Ipv4Settings settings = TcpipRegistry.ToSettings(values);

            Assert.Equal("10.68.12.37", settings.Address);
            Assert.Equal("255.255.255.0", settings.Mask);
            Assert.Equal("10.68.12.1", settings.Gateway);
        }

        [Fact]
        public void Missing_dhcp_flag_means_automatic()
        {
            Assert.True(TcpipRegistry.ToSettings(new TcpipInterfaceValues()).AutoAddress);
        }
    }
}
