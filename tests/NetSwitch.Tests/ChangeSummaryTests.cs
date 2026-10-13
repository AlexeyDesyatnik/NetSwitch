using System.Collections.Generic;
using System.Linq;
using NetSwitch.Model;
using NetSwitch.UI;
using Xunit;

namespace NetSwitch.Tests
{
    public class ChangeSummaryTests
    {
        private static ProxyState Current(bool processOwner = true, bool managed = false)
        {
            return new ProxyState(
                new InteractiveUser(@"OFFICE\ivanov", "S-1-5-21-1", processOwner),
                new SystemProxyState { Settings = ProxySettings.FromRaw(false, "10.0.68.52:3128", "<local>"), AutoDetect = true },
                new ProxySettings(),
                new ProxyPolicy { PerMachine = managed });
        }

        private static string[] Row(ChangeLine line)
        {
            return new[] { line.What, line.Before, line.After };
        }

        [Fact]
        public void Profile_shows_what_was_and_what_will_be_for_adapter_and_both_proxies()
        {
            Adapter adapter = FakeSystem.Ethernet();
            ChangeRequest request = ChangeRequest.For(
                "Профиль", adapter, FakeSystem.Static("10.68.12.37", "10.68.12.1", "95.167.167.95"),
                ProxySettings.FromRaw(true, "10.0.68.52:3128", "<local>"), Current());

            IList<ChangeLine> lines = ChangeSummary.Lines(request, Current());

            Assert.Equal(new[] { "Адрес", "автоматически", "10.68.12.37/24" }, Row(lines[0]));
            Assert.Equal(new[] { "Шлюз", "автоматически", "10.68.12.1" }, Row(lines[1]));
            Assert.Equal(new[] { "DNS", "автоматически", "95.167.167.95" }, Row(lines[2]));
            Assert.Equal(
                new[] { "Системный прокси", "выключен; автоопределение включено", "10.0.68.52:3128; не для локальных адресов" },
                Row(lines[3]));
            Assert.Equal(new[] { "WinHTTP-прокси", "выключен", "10.0.68.52:3128; не для локальных адресов" }, Row(lines[4]));
            Assert.All(lines, line => Assert.True(line.Changed));
        }

        [Fact]
        public void Unchanged_lines_are_marked_as_such()
        {
            Adapter adapter = FakeSystem.Ethernet();
            adapter.Configured = FakeSystem.Static("10.68.12.37", "10.68.12.1", "8.8.8.8");
            ChangeRequest request = ChangeRequest.For("Ручная правка IPv4", adapter, FakeSystem.Static("10.68.12.99", "10.68.12.1", "8.8.8.8"), null, null);

            IList<ChangeLine> lines = ChangeSummary.Lines(request, null);

            Assert.Equal(new[] { true, false, false }, lines.Select(line => line.Changed));
        }

        [Fact]
        public void Disabled_adapter_is_shown_as_going_to_be_enabled()
        {
            ChangeRequest request = ChangeRequest.For("Профиль", FakeSystem.Ethernet(AdapterState.Disabled), new Ipv4Settings(), null, null);

            Assert.Equal(new[] { "Адаптер «Ethernet»", "выключен", "включён" }, Row(ChangeSummary.Lines(request, null)[0]));
        }

        [Fact]
        public void Proxy_only_request_has_no_adapter_lines()
        {
            ChangeRequest request = ChangeRequest.For("Ручная правка прокси", null, null, new ProxySettings(), Current());

            Assert.Equal(new[] { "Системный прокси", "WinHTTP-прокси" }, ChangeSummary.Lines(request, Current()).Select(line => line.What));
        }

        [Fact]
        public void Unknown_current_proxy_is_said_to_be_unknown()
        {
            ChangeRequest request = ChangeRequest.For("Ручная правка прокси", null, null, new ProxySettings(), null);

            Assert.All(ChangeSummary.Lines(request, null), line => Assert.Equal("неизвестно", line.Before));
        }

        [Fact]
        public void Notes_warn_about_no_link_other_user_and_policy()
        {
            ChangeRequest request = ChangeRequest.For("Профиль", FakeSystem.Ethernet(AdapterState.NoLink), new Ipv4Settings(), new ProxySettings(), Current());

            IList<string> notes = ChangeSummary.Notes(request, Current(processOwner: false, managed: true));

            Assert.Equal(3, notes.Count);
            Assert.Contains(notes, note => note.Contains("нет соединения"));
            Assert.Contains(notes, note => note.Contains(@"для пользователя OFFICE\ivanov"));
            Assert.Contains(notes, note => note.Contains("групповой политикой"));
        }

        [Fact]
        public void Ordinary_case_needs_no_notes()
        {
            ChangeRequest request = ChangeRequest.For("Профиль", FakeSystem.Ethernet(), new Ipv4Settings(), new ProxySettings(), Current());

            Assert.Empty(ChangeSummary.Notes(request, Current()));
        }
    }
}
