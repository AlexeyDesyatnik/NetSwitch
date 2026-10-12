using System;
using System.Collections.Generic;
using NetSwitch.Model;
using NetSwitch.Windows;
using Xunit;

namespace NetSwitch.Tests
{
    public class NetshTests
    {
        private const string Name = "name=\"Подключение по локальной сети\"";

        private static IList<string> Commands(Ipv4Settings current, Ipv4Settings target)
        {
            return Netsh.Ipv4Commands("Подключение по локальной сети", current, target);
        }

        [Fact]
        public void Nothing_to_do_when_settings_already_match()
        {
            Assert.Empty(Commands(new Ipv4Settings(), new Ipv4Settings()));
            Assert.Empty(Commands(FakeSystem.Static("10.68.12.37", "10.68.12.1", "8.8.8.8"), FakeSystem.Static("10.68.12.37", "10.68.12.1", "8.8.8.8")));
        }

        [Fact]
        public void From_automatic_to_manual_with_two_dns()
        {
            IList<string> commands = Commands(new Ipv4Settings(), FakeSystem.Static("10.68.12.37", "10.68.12.1", "95.167.167.95", "95.167.167.96"));

            Assert.Equal(new[]
            {
                "interface ipv4 set address " + Name + " source=static address=10.68.12.37 mask=255.255.255.0 gateway=10.68.12.1",
                "interface ipv4 set dnsservers " + Name + " source=static address=95.167.167.95 validate=no",
                "interface ipv4 add dnsservers " + Name + " address=95.167.167.96 index=2 validate=no"
            }, commands);
        }

        [Fact]
        public void From_manual_to_fully_automatic_resets_both_address_and_dns()
        {
            IList<string> commands = Commands(FakeSystem.Static("10.68.12.37", "10.68.12.1", "8.8.8.8"), new Ipv4Settings());

            Assert.Equal(new[]
            {
                "interface ipv4 set address " + Name + " source=dhcp",
                "interface ipv4 set dnsservers " + Name + " source=dhcp"
            }, commands);
        }

        [Fact]
        public void Manual_address_without_gateway_and_dns()
        {
            IList<string> commands = Commands(new Ipv4Settings(), FakeSystem.Static("10.68.12.37", ""));

            Assert.Equal(new[]
            {
                "interface ipv4 set address " + Name + " source=static address=10.68.12.37 mask=255.255.255.0 gateway=none",
                "interface ipv4 set dnsservers " + Name + " source=static address=none validate=no"
            }, commands);
        }

        [Fact]
        public void Changing_gateway_goes_through_automatic_to_clear_the_old_one()
        {
            IList<string> commands = Commands(FakeSystem.Static("10.68.12.37", "10.68.12.1"), FakeSystem.Static("10.68.12.37", "10.68.12.254"));

            Assert.Equal(new[]
            {
                "interface ipv4 set address " + Name + " source=dhcp",
                "interface ipv4 set address " + Name + " source=static address=10.68.12.37 mask=255.255.255.0 gateway=10.68.12.254"
            }, commands);
        }

        [Fact]
        public void Changing_only_the_address_keeps_gateway_and_dns_untouched()
        {
            IList<string> commands = Commands(FakeSystem.Static("10.68.12.37", "10.68.12.1", "8.8.8.8"), FakeSystem.Static("10.68.12.99", "10.68.12.1", "8.8.8.8"));

            Assert.Equal(new[] { "interface ipv4 set address " + Name + " source=static address=10.68.12.99 mask=255.255.255.0 gateway=10.68.12.1" }, commands);
        }

        [Fact]
        public void Changing_only_dns_leaves_address_alone()
        {
            var current = new Ipv4Settings();
            var target = new Ipv4Settings { AutoDns = false, Dns = { "8.8.8.8" } };

            Assert.Equal(new[] { "interface ipv4 set dnsservers " + Name + " source=static address=8.8.8.8 validate=no" }, Commands(current, target));
            Assert.Equal(new[] { "interface ipv4 set dnsservers " + Name + " source=dhcp" }, Commands(target, current));
        }

        [Fact]
        public void Automatic_address_with_manual_dns_kept_when_only_address_mode_changes()
        {
            Ipv4Settings current = FakeSystem.Static("10.68.12.37", "10.68.12.1", "8.8.8.8");
            var target = new Ipv4Settings { AutoDns = false, Dns = { "8.8.8.8" } };

            Assert.Equal(new[] { "interface ipv4 set address " + Name + " source=dhcp" }, Commands(current, target));
        }
    }

    public class InternetSettingsBlobBuildTests
    {
        private static SystemProxyState State(bool enabled, string server, string bypass, bool autoDetect = false, string script = "")
        {
            return new SystemProxyState { Settings = ProxySettings.FromRaw(enabled, server, bypass), AutoDetect = autoDetect, AutoConfigUrl = script };
        }

        [Fact]
        public void Built_value_reads_back_as_the_same_proxy()
        {
            byte[] data = InternetSettingsBlob.Build(null, State(true, "10.0.68.52:3128", "*.corp.local;<local>"));

            InternetSettingsBlob blob = InternetSettingsBlob.Parse(data);

            Assert.Equal(0x46, blob.Version);
            Assert.Equal(0x3, blob.Flags);
            Assert.Equal("10.0.68.52:3128", blob.Proxy);
            Assert.Equal("*.corp.local;<local>", blob.Bypass);
            Assert.Equal("", blob.AutoConfigUrl);
            Assert.Equal(32, blob.Tail.Length);
        }

        [Fact]
        public void Disabled_proxy_keeps_server_but_clears_the_proxy_flag()
        {
            InternetSettingsBlob blob = InternetSettingsBlob.Parse(InternetSettingsBlob.Build(null, State(false, "10.0.68.52:3128", "<local>")));

            Assert.Equal(0x1, blob.Flags);
            Assert.False(blob.ProxyEnabled);
            Assert.Equal("10.0.68.52:3128", blob.Proxy);
        }

        [Fact]
        public void Auto_detect_and_script_are_written_when_asked_for()
        {
            InternetSettingsBlob blob = InternetSettingsBlob.Parse(InternetSettingsBlob.Build(null, State(false, "", "", true, "http://wpad/proxy.pac")));

            Assert.Equal(0x1 | 0x4 | 0x8, blob.Flags);
            Assert.Equal("http://wpad/proxy.pac", blob.AutoConfigUrl);
        }

        [Fact]
        public void Rewriting_bumps_the_counter_and_carries_over_what_windows_keeps_after_the_strings()
        {
            // Значение с Windows 7 из отчёта CAB07: автоопределение включено, после строк — служебные данные
            var original = new List<byte>();
            original.AddRange(new byte[] { 0x46, 0, 0, 0, 0x11, 0, 0, 0, 0x09, 0, 0, 0, 0x0F, 0, 0, 0 });
            original.AddRange(System.Text.Encoding.ASCII.GetBytes("10.0.68.52:3128"));
            original.AddRange(new byte[] { 0x07, 0, 0, 0 });
            original.AddRange(System.Text.Encoding.ASCII.GetBytes("<local>"));
            original.AddRange(new byte[] { 0, 0, 0, 0 });
            byte[] tail = { 0x05, 0, 0, 0, 0xFD, 0xD4, 0xFC, 0x4C, 0x29, 0xF8, 0x17, 0x00, 0xC0, 0xA8, 0x01, 0x66 };
            original.AddRange(tail);
            InternetSettingsBlob previous = InternetSettingsBlob.Parse(original.ToArray());

            InternetSettingsBlob rewritten = InternetSettingsBlob.Parse(InternetSettingsBlob.Build(previous, State(true, "10.0.68.52:3128", "<local>")));

            Assert.True(previous.AutoDetect);
            Assert.Equal(0x12, rewritten.Counter);
            Assert.Equal(0x3, rewritten.Flags);
            Assert.False(rewritten.AutoDetect);
            Assert.Equal(tail, rewritten.Tail);
        }
    }
}
