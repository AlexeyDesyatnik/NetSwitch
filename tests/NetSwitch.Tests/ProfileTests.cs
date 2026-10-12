using System;
using System.Collections.Generic;
using System.IO;
using NetSwitch.Model;
using NetSwitch.Profiles;
using Xunit;

namespace NetSwitch.Tests
{
    public class ProfileRulesTests
    {
        private static Profile Valid()
        {
            return new Profile
            {
                Name = "Подсеть 10.68.12.0/24",
                Ipv4 = FakeSystem.Static("10.68.12.37", "10.68.12.1", "95.167.167.95", "95.167.167.96"),
                Proxy = ProxySettings.FromRaw(true, "10.0.68.52:3128", "<local>")
            };
        }

        [Fact]
        public void Complete_profile_passes()
        {
            Assert.Empty(ProfileRules.Check(Valid()));
        }

        [Fact]
        public void Profile_needs_a_name()
        {
            Profile profile = Valid();
            profile.Name = "   ";

            Assert.Contains("Укажите название профиля.", ProfileRules.Check(profile));
        }

        [Theory]
        [InlineData("/24", "255.255.255.0")]
        [InlineData("24", "255.255.255.0")]
        [InlineData(" 255.255.252.0 ", "255.255.252.0")]
        [InlineData("/ 16", "255.255.0.0")]
        public void Mask_is_accepted_in_either_notation_and_stored_dotted(string entered, string stored)
        {
            Ipv4Settings settings = FakeSystem.Static("10.68.12.37");
            settings.Mask = entered;

            Assert.Empty(ProfileRules.CheckIpv4(settings, false));
            Assert.Equal(stored, settings.Mask);
        }

        [Theory]
        [InlineData("")]
        [InlineData("255.0.255.0")]
        [InlineData("/33")]
        [InlineData("/0")]
        [InlineData("abc")]
        public void Bad_mask_is_rejected(string mask)
        {
            Ipv4Settings settings = FakeSystem.Static("10.68.12.37");
            settings.Mask = mask;

            Assert.Contains(ProfileRules.CheckIpv4(settings, false), error => error.StartsWith("Маска задана неверно"));
        }

        [Fact]
        public void Leading_zeros_are_removed_so_windows_does_not_read_them_as_octal()
        {
            Ipv4Settings settings = FakeSystem.Static("010.068.012.037", "010.068.012.001", "010.000.068.010");

            Assert.Empty(ProfileRules.CheckIpv4(settings, false));
            Assert.Equal("10.68.12.37", settings.Address);
            Assert.Equal("10.68.12.1", settings.Gateway);
            Assert.Equal(new[] { "10.0.68.10" }, settings.Dns);
        }

        [Fact]
        public void Gateway_is_optional_and_manual_dns_may_be_empty_with_manual_address()
        {
            Assert.Empty(ProfileRules.CheckIpv4(FakeSystem.Static("10.68.12.37", ""), false));
        }

        [Fact]
        public void Manual_address_forces_manual_dns()
        {
            Ipv4Settings settings = FakeSystem.Static("10.68.12.37");
            settings.AutoDns = true;

            ProfileRules.CheckIpv4(settings, false);

            Assert.False(settings.AutoDns);
        }

        [Fact]
        public void Automatic_address_drops_leftover_manual_values()
        {
            Ipv4Settings settings = FakeSystem.Static("10.68.12.37", "10.68.12.1", "8.8.8.8");
            settings.AutoAddress = true;
            settings.AutoDns = true;

            Assert.Empty(ProfileRules.CheckIpv4(settings, false));
            Assert.Equal("", settings.Address);
            Assert.Equal("", settings.Mask);
            Assert.Equal("", settings.Gateway);
            Assert.Empty(settings.Dns);
        }

        [Fact]
        public void Automatic_address_with_manual_dns_needs_a_server()
        {
            var settings = new Ipv4Settings { AutoAddress = true, AutoDns = false };

            Assert.Contains("Укажите DNS-сервер или выберите автоматическое получение DNS.", ProfileRules.CheckIpv4(settings, false));
        }

        [Fact]
        public void No_more_than_two_dns_servers()
        {
            Ipv4Settings settings = FakeSystem.Static("10.68.12.37", "", "1.1.1.1", "8.8.8.8", "9.9.9.9");

            Assert.Contains("DNS-серверов может быть не больше двух.", ProfileRules.CheckIpv4(settings, false));
        }

        [Theory]
        [InlineData("10.68.12")]
        [InlineData("10.68.12.")]
        [InlineData("10.68.12.300")]
        [InlineData("")]
        public void Fixed_address_must_be_complete(string address)
        {
            Assert.Contains(ProfileRules.CheckIpv4(FakeSystem.Static(address), false), error => error.StartsWith("Адрес IPv4 задан неверно"));
        }

        [Theory]
        [InlineData("10.68.12.")]
        [InlineData("10.68.12")]
        [InlineData("10.68.")]
        [InlineData("")]
        [InlineData("10.68.12.37")]
        public void Address_to_be_completed_may_be_only_a_beginning(string stub)
        {
            Assert.Empty(ProfileRules.CheckIpv4(FakeSystem.Static(stub), true));
        }

        [Theory]
        [InlineData("10.68.300.")]
        [InlineData("сеть")]
        [InlineData("10.68.12.1.5")]
        public void Beginning_of_address_must_still_look_like_an_address(string stub)
        {
            Assert.Contains(ProfileRules.CheckIpv4(FakeSystem.Static(stub), true), error => error.StartsWith("Начало адреса задано неверно"));
        }

        [Fact]
        public void Automatic_address_cannot_be_asked_for()
        {
            var profile = new Profile { Name = "Авто", AskAddress = true };

            Assert.Empty(ProfileRules.Check(profile));
            Assert.False(profile.AskAddress);
        }

        [Theory]
        [InlineData("10.0.68.52:3128")]
        [InlineData("proxy.corp.local:8080")]
        [InlineData("http://127.0.0.1:10809")]
        public void Proxy_server_with_port_is_accepted(string server)
        {
            Assert.Empty(ProfileRules.CheckProxy(ProxySettings.FromRaw(true, server, "")));
        }

        [Theory]
        [InlineData("", "Укажите прокси-сервер")]
        [InlineData("10.0.68.52", "Укажите порт")]
        [InlineData("10.0.68.52:", "Укажите порт")]
        [InlineData("10.0.68.52:0", "Укажите порт")]
        [InlineData("10.0.68.52:70000", "Укажите порт")]
        public void Enabled_proxy_needs_server_and_port(string server, string expected)
        {
            Assert.Contains(ProfileRules.CheckProxy(ProxySettings.FromRaw(true, server, "")), error => error.StartsWith(expected));
        }

        [Fact]
        public void Disabled_proxy_needs_nothing()
        {
            Assert.Empty(ProfileRules.CheckProxy(new ProxySettings()));
        }

        [Theory]
        [InlineData("10.68.12.37", true)]
        [InlineData("10.68.12.254", true)]
        [InlineData("10.68.13.37", false)]
        [InlineData("192.168.1.5", false)]
        public void Subnet_of_profile_comes_from_address_beginning_and_mask(string address, bool expected)
        {
            Assert.Equal(expected, ProfileRules.IsInProfileSubnet(address, "10.68.12.", "255.255.255.0"));
        }

        [Fact]
        public void Entered_address_must_be_complete_to_be_resolved()
        {
            Assert.True(ProfileRules.TryResolveAddress(" 10.68.12.037 ", out string address));
            Assert.Equal("10.68.12.37", address);
            Assert.False(ProfileRules.TryResolveAddress("10.68.12.", out _));
        }

        [Fact]
        public void Suggested_address_is_the_one_adapter_already_has_in_profile_subnet()
        {
            Ipv4Settings profile = FakeSystem.Static("10.68.12.");
            Adapter manual = FakeSystem.Ethernet();
            manual.Configured = FakeSystem.Static("10.68.12.7");
            Adapter leased = FakeSystem.Ethernet();
            leased.Active.Addresses.Add("10.68.12.120/24");

            Assert.Equal("10.68.12.7", ProfileRules.SuggestAddress(profile, manual));
            Assert.Equal("10.68.12.120", ProfileRules.SuggestAddress(profile, leased));
        }

        [Fact]
        public void Suggested_address_falls_back_to_the_beginning_from_profile()
        {
            Ipv4Settings profile = FakeSystem.Static("10.68.12.");
            Adapter elsewhere = FakeSystem.Ethernet();
            elsewhere.Configured = FakeSystem.Static("192.168.1.50");
            elsewhere.Active.Addresses.Add("192.168.1.50/24");

            Assert.Equal("10.68.12.", ProfileRules.SuggestAddress(profile, elsewhere));
            Assert.Equal("10.68.12.", ProfileRules.SuggestAddress(profile, FakeSystem.Ethernet()));
        }
    }

    public class Ipv4SettingsTests
    {
        [Fact]
        public void Automatic_settings_are_the_same_whatever_is_left_in_manual_fields()
        {
            var one = new Ipv4Settings { Address = "10.0.0.1", Mask = "255.0.0.0" };

            Assert.True(one.SameAs(new Ipv4Settings()));
        }

        [Fact]
        public void Manual_settings_compare_addresses_not_their_spelling()
        {
            Assert.True(FakeSystem.Static("10.68.12.37", "10.68.12.1", "8.8.8.8").SameAs(FakeSystem.Static("010.068.012.037", "10.68.12.001", "8.8.8.8")));
        }

        [Fact]
        public void Any_difference_in_effective_values_counts()
        {
            Ipv4Settings settings = FakeSystem.Static("10.68.12.37", "10.68.12.1", "8.8.8.8", "1.1.1.1");

            Assert.False(settings.SameAs(FakeSystem.Static("10.68.12.38", "10.68.12.1", "8.8.8.8", "1.1.1.1")));
            Assert.False(settings.SameAs(FakeSystem.Static("10.68.12.37", "", "8.8.8.8", "1.1.1.1")));
            Assert.False(settings.SameAs(FakeSystem.Static("10.68.12.37", "10.68.12.1", "1.1.1.1", "8.8.8.8")));
            Assert.False(settings.SameAs(FakeSystem.Static("10.68.12.37", "10.68.12.1", "8.8.8.8")));
            Assert.False(settings.SameAs(new Ipv4Settings()));
        }

        [Fact]
        public void Clone_is_independent()
        {
            Ipv4Settings original = FakeSystem.Static("10.68.12.37", "10.68.12.1", "8.8.8.8");
            Ipv4Settings copy = original.Clone();
            copy.Address = "10.68.12.99";
            copy.Dns.Add("1.1.1.1");

            Assert.Equal("10.68.12.37", original.Address);
            Assert.Single(original.Dns);
        }
    }

    public class ProfileStoreTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "NetSwitch.Tests." + Guid.NewGuid().ToString("N"));

        public ProfileStoreTests()
        {
            Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            Directory.Delete(_folder, true);
        }

        private static List<Profile> Sample()
        {
            return new List<Profile>
            {
                new Profile { Name = "Авто, без прокси" },
                new Profile
                {
                    Name = "Кабинет \"12\" \\ подсеть 10.68.12.0/24",
                    Ipv4 = FakeSystem.Static("10.68.12.", "10.68.12.1", "95.167.167.95", "95.167.167.96"),
                    AskAddress = true,
                    Proxy = ProxySettings.FromRaw(true, "10.0.68.52:3128", "*.corp.local;10.*;<local>")
                },
                new Profile { Name = "Авто, свои DNS", Ipv4 = new Ipv4Settings { AutoDns = false, Dns = { "8.8.8.8" } } }
            };
        }

        [Fact]
        public void Profiles_survive_saving_and_loading_in_the_same_order()
        {
            var store = new ProfileStore(_folder);
            store.Profiles.AddRange(Sample());
            store.Save();

            var loaded = new ProfileStore(_folder);
            loaded.Load();

            Assert.Null(loaded.LoadError);
            Assert.Equal(new[] { "Авто, без прокси", "Кабинет \"12\" \\ подсеть 10.68.12.0/24", "Авто, свои DNS" }, loaded.Profiles.ConvertAll(p => p.Name));

            Profile room = loaded.Profiles[1];
            Assert.True(room.AskAddress);
            Assert.True(room.Ipv4.SameAs(FakeSystem.Static("10.68.12.", "10.68.12.1", "95.167.167.95", "95.167.167.96")));
            Assert.Equal("10.68.12.", room.Ipv4.Address);
            Assert.True(room.Proxy.SameAs(ProxySettings.FromRaw(true, "10.0.68.52:3128", "*.corp.local;10.*;<local>")));

            Assert.True(loaded.Profiles[0].Ipv4.AutoAddress);
            Assert.False(loaded.Profiles[0].Proxy.Enabled);
            Assert.Equal(new[] { "8.8.8.8" }, loaded.Profiles[2].Ipv4.Dns);
        }

        [Fact]
        public void Missing_file_means_no_profiles_yet()
        {
            var store = new ProfileStore(_folder);
            store.Load();

            Assert.Empty(store.Profiles);
            Assert.Null(store.LoadError);
        }

        [Fact]
        public void File_is_readable_by_a_human()
        {
            string text = ProfileJson.Write(Sample());

            Assert.Contains("      \"name\": \"Авто, без прокси\",", text);
            Assert.Contains("        \"dns\": [\"95.167.167.95\", \"95.167.167.96\"]", text);
            Assert.Contains("        \"server\": \"10.0.68.52:3128\",", text);
        }

        [Fact]
        public void Hand_written_file_with_only_the_essentials_is_accepted()
        {
            List<Profile> profiles = ProfileJson.Read(
                "{ \"profiles\": [ { \"name\": \"Офис\", \"ipv4\": { \"autoAddress\": false, \"address\": \"10.68.12.37\", \"mask\": \"/24\" }," +
                " \"proxy\": { \"enabled\": true, \"server\": \"10.0.68.52:3128\" } }, { \"name\": \"Дом\" } ] }");

            Assert.Equal("255.255.255.0", profiles[0].Ipv4.Mask);
            Assert.False(profiles[0].Ipv4.AutoDns);
            Assert.True(profiles[0].Proxy.Enabled);
            Assert.True(profiles[1].Ipv4.AutoAddress);
            Assert.False(profiles[1].Proxy.Enabled);
        }

        [Theory]
        [InlineData("{ \"profiles\": [ { \"name\": \"Офис\", ")]
        [InlineData("[1, 2, 3]")]
        [InlineData("{ \"profiles\": 5 }")]
        [InlineData("{ \"profiles\": [ \"Офис\" ] }")]
        [InlineData("{ \"profiles\": [ { \"name\": \"Офис\", \"proxy\": { \"enabled\": \"да\" } } ] }")]
        [InlineData("{ \"profiles\": [ { \"name\": \"Офис\", \"ipv4\": { \"autoAddress\": false, \"address\": \"10.68.12.999\", \"mask\": \"/24\" } } ] }")]
        public void Broken_file_is_reported_not_guessed_at(string text)
        {
            Assert.Throws<FormatException>(() => ProfileJson.Read(text));
        }

        [Fact]
        public void Unreadable_file_is_never_overwritten()
        {
            string path = Path.Combine(_folder, ProfileStore.FileName);
            File.WriteAllText(path, "{ это не JSON");

            var store = new ProfileStore(_folder);
            store.Load();

            Assert.NotNull(store.LoadError);
            Assert.Empty(store.Profiles);
            Assert.Throws<InvalidOperationException>(() => store.Save());
            Assert.Equal("{ это не JSON", File.ReadAllText(path));
        }

        [Fact]
        public void Saving_replaces_the_previous_file_and_leaves_no_temporary_one()
        {
            var store = new ProfileStore(_folder);
            store.Profiles.AddRange(Sample());
            store.Save();
            store.Profiles.RemoveAt(0);
            store.Save();

            var loaded = new ProfileStore(_folder);
            loaded.Load();

            Assert.Equal(2, loaded.Profiles.Count);
            Assert.Equal(new[] { Path.Combine(_folder, ProfileStore.FileName) }, Directory.GetFiles(_folder));
        }

        [Fact]
        public void Names_are_found_ignoring_case_and_free_name_avoids_them()
        {
            var store = new ProfileStore(_folder);
            store.Profiles.Add(new Profile { Name = "Офис" });
            store.Profiles.Add(new Profile { Name = "Офис (2)" });

            Assert.NotNull(store.Find(" офис "));
            Assert.Null(store.Find("Дом"));
            Assert.Equal("Офис (3)", store.FreeName("Офис"));
            Assert.Equal("Дом", store.FreeName("Дом"));
        }
    }
}
