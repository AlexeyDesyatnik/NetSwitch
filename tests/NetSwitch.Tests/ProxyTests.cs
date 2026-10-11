using System;
using System.Collections.Generic;
using System.Text;
using NetSwitch.Model;
using NetSwitch.Windows;
using Xunit;

namespace NetSwitch.Tests
{
    public class ProxySettingsTests
    {
        [Fact]
        public void Splits_bypass_list_into_exceptions_and_local_flag()
        {
            ProxySettings settings = ProxySettings.FromRaw(true, " 10.0.68.52:3128 ", "*.corp.local; 10.*;<LOCAL>");

            Assert.True(settings.Enabled);
            Assert.Equal("10.0.68.52:3128", settings.Server);
            Assert.Equal(new[] { "*.corp.local", "10.*" }, settings.Exceptions);
            Assert.True(settings.BypassLocal);
        }

        [Fact]
        public void Missing_values_give_disabled_proxy_without_exceptions()
        {
            ProxySettings settings = ProxySettings.FromRaw(false, null, null);

            Assert.False(settings.Enabled);
            Assert.Equal("", settings.Server);
            Assert.Empty(settings.Exceptions);
            Assert.False(settings.BypassLocal);
        }

        [Fact]
        public void Bypass_list_survives_a_round_trip()
        {
            ProxySettings settings = ProxySettings.FromRaw(true, "proxy:3128", "*.corp.local;10.*;<local>");

            Assert.Equal("*.corp.local;10.*;<local>", settings.ToBypassList());
        }

        [Fact]
        public void Disabled_proxies_are_the_same_whatever_is_left_in_their_fields()
        {
            ProxySettings one = ProxySettings.FromRaw(false, "old:8080", "a;b");
            ProxySettings other = ProxySettings.FromRaw(false, "", "");

            Assert.True(one.SameAs(other));
        }

        [Fact]
        public void Enabled_proxies_are_compared_ignoring_case_and_exception_order()
        {
            ProxySettings one = ProxySettings.FromRaw(true, "Proxy:3128", "a.local;B.local;<local>");
            ProxySettings other = ProxySettings.FromRaw(true, "proxy:3128", "<local>;b.local;A.LOCAL");

            Assert.True(one.SameAs(other));
        }

        [Theory]
        [InlineData(true, "proxy:3128", "", false, "proxy:3128", "")]
        [InlineData(true, "proxy:3128", "", true, "proxy:8080", "")]
        [InlineData(true, "proxy:3128", "a", true, "proxy:3128", "a;b")]
        [InlineData(true, "proxy:3128", "", true, "proxy:3128", "<local>")]
        public void Different_proxies_are_not_the_same(bool on1, string server1, string bypass1, bool on2, string server2, string bypass2)
        {
            Assert.False(ProxySettings.FromRaw(on1, server1, bypass1).SameAs(ProxySettings.FromRaw(on2, server2, bypass2)));
        }

        [Fact]
        public void Any_policy_sign_makes_proxy_managed()
        {
            Assert.False(new ProxyPolicy().IsManaged);
            Assert.True(new ProxyPolicy { PerMachine = true }.IsManaged);
            Assert.True(new ProxyPolicy { SetsValues = true }.IsManaged);
            Assert.True(new ProxyPolicy { ChangeLocked = true }.IsManaged);
        }
    }

    public class InternetSettingsBlobTests
    {
        private static byte[] Blob(int flags, params string?[] strings)
        {
            var bytes = new List<byte>();
            bytes.AddRange(BitConverter.GetBytes(0x46));
            bytes.AddRange(BitConverter.GetBytes(5));
            bytes.AddRange(BitConverter.GetBytes(flags));
            foreach (string? text in strings)
            {
                if (text == null) continue;
                bytes.AddRange(BitConverter.GetBytes(text.Length));
                bytes.AddRange(Encoding.ASCII.GetBytes(text));
            }
            return bytes.ToArray();
        }

        [Fact]
        public void Reads_manual_proxy_with_bypass_list()
        {
            byte[] data = Blob(0x3, "10.0.68.52:3128", "<local>", "");

            InternetSettingsBlob blob = InternetSettingsBlob.Parse(data);

            Assert.Equal(0x46, blob.Version);
            Assert.Equal(5, blob.Counter);
            Assert.True(blob.ProxyEnabled);
            Assert.False(blob.AutoDetect);
            Assert.False(blob.AutoConfigEnabled);
            Assert.Equal("10.0.68.52:3128", blob.Proxy);
            Assert.Equal("<local>", blob.Bypass);
            Assert.Equal("", blob.AutoConfigUrl);
        }

        [Fact]
        public void Reads_auto_detect_and_script()
        {
            byte[] data = Blob(0x1 | 0x4 | 0x8, "", "", "http://wpad.corp.local/proxy.pac");

            InternetSettingsBlob blob = InternetSettingsBlob.Parse(data);

            Assert.False(blob.ProxyEnabled);
            Assert.True(blob.AutoDetect);
            Assert.True(blob.AutoConfigEnabled);
            Assert.Equal("http://wpad.corp.local/proxy.pac", blob.AutoConfigUrl);
        }

        [Fact]
        public void Trailing_bytes_after_the_strings_are_ignored()
        {
            var data = new List<byte>(Blob(0x9, "", "", ""));
            data.AddRange(new byte[32]);

            Assert.True(InternetSettingsBlob.Parse(data.ToArray()).AutoDetect);
        }

        [Fact]
        public void Short_winhttp_form_without_script_is_accepted()
        {
            byte[] data = Blob(0x3, "proxy:3128", "*.local");

            InternetSettingsBlob blob = InternetSettingsBlob.Parse(data);

            Assert.Equal("proxy:3128", blob.Proxy);
            Assert.Equal("*.local", blob.Bypass);
            Assert.Equal("", blob.AutoConfigUrl);
        }

        [Fact]
        public void Header_only_value_means_direct_connection()
        {
            InternetSettingsBlob blob = InternetSettingsBlob.Parse(Blob(0x1));

            Assert.False(blob.ProxyEnabled);
            Assert.Equal("", blob.Proxy);
        }

        [Fact]
        public void Value_shorter_than_header_is_rejected()
        {
            Assert.Throws<FormatException>(() => InternetSettingsBlob.Parse(new byte[8]));
        }

        [Fact]
        public void String_running_past_the_end_is_rejected()
        {
            var data = new List<byte>(Blob(0x3));
            data.AddRange(BitConverter.GetBytes(100));
            data.AddRange(Encoding.ASCII.GetBytes("proxy"));

            Assert.Throws<FormatException>(() => InternetSettingsBlob.Parse(data.ToArray()));
        }
    }
}
