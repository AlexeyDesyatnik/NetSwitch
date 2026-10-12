using System;
using System.Collections.Generic;
using System.Linq;
using NetSwitch.Model;
using Xunit;

namespace NetSwitch.Tests
{
    public class ChangeRequestTests
    {
        private static ProxyState Current(ProxySettings system)
        {
            return new ProxyState(
                new InteractiveUser("PC\\user", "S-1-5-21-1", true),
                new SystemProxyState { Settings = system, AutoDetect = true, AutoConfigUrl = "http://wpad/proxy.pac" },
                new ProxySettings(),
                new ProxyPolicy());
        }

        [Fact]
        public void One_proxy_setting_goes_to_both_system_and_winhttp()
        {
            ProxySettings proxy = ProxySettings.FromRaw(true, "10.0.68.52:3128", "*.corp.local;<local>");

            ChangeRequest request = ChangeRequest.For("Профиль", null, null, proxy, Current(new ProxySettings()));

            Assert.True(request.SystemProxy!.Settings.SameAs(proxy));
            Assert.True(request.WinHttp!.SameAs(proxy));
        }

        [Fact]
        public void Applying_proxy_turns_auto_detect_and_script_off()
        {
            ChangeRequest request = ChangeRequest.For("Профиль", null, null, ProxySettings.FromRaw(true, "p:3128", ""), Current(new ProxySettings()));

            Assert.False(request.SystemProxy!.AutoDetect);
            Assert.Equal("", request.SystemProxy.AutoConfigUrl);
        }

        [Fact]
        public void Turning_proxy_off_keeps_the_remembered_server()
        {
            ProxySettings remembered = ProxySettings.FromRaw(true, "10.0.68.52:3128", "<local>");

            ChangeRequest request = ChangeRequest.For("Профиль", null, null, new ProxySettings(), Current(remembered));

            Assert.False(request.SystemProxy!.Settings.Enabled);
            Assert.Equal("10.0.68.52:3128", request.SystemProxy.Settings.Server);
            Assert.True(request.SystemProxy.Settings.BypassLocal);
            Assert.False(request.WinHttp!.Enabled);
        }

        [Fact]
        public void Parts_that_are_not_given_are_left_alone()
        {
            ChangeRequest proxyOnly = ChangeRequest.For("Ручная правка прокси", null, null, new ProxySettings(), null);
            ChangeRequest ipv4Only = ChangeRequest.For("Ручная правка IPv4", FakeSystem.Ethernet(), new Ipv4Settings(), null, null);

            Assert.Null(proxyOnly.Ipv4);
            Assert.Null(proxyOnly.Adapter);
            Assert.Null(ipv4Only.SystemProxy);
            Assert.Null(ipv4Only.WinHttp);
        }

        [Fact]
        public void Request_does_not_share_settings_with_the_profile()
        {
            var profile = new Profile { Ipv4 = FakeSystem.Static("10.68.12.") };

            ChangeRequest request = ChangeRequest.For("Профиль", FakeSystem.Ethernet(), profile.Ipv4, profile.Proxy, null);
            request.Ipv4!.Address = "10.68.12.37";

            Assert.Equal("10.68.12.", profile.Ipv4.Address);
        }
    }

    public class ApplierTests
    {
        private readonly FakeSystem _system = new FakeSystem();
        private readonly Applier _applier;

        public ApplierTests()
        {
            _system.Adapters.Add(FakeSystem.Ethernet());
            _applier = new Applier(_system);
        }

        private ChangeRequest Profile(Ipv4Settings ipv4, ProxySettings proxy)
        {
            return ChangeRequest.For("Профиль «Тест»", _system.Adapters[0], ipv4, proxy, _system.ReadProxy());
        }

        [Fact]
        public void Steps_run_in_order_ipv4_then_system_proxy_then_winhttp()
        {
            IList<StepResult> results = _applier.Apply(Profile(FakeSystem.Static("10.68.12.37"), ProxySettings.FromRaw(true, "10.0.68.52:3128", "")));

            Assert.Equal(new[] { "ipv4 Ethernet", "system proxy", "winhttp" }, _system.Calls);
            Assert.All(results, result => Assert.True(result.Ok));
            Assert.Equal("10.68.12.37", _system.Adapters[0].Configured.Address);
            Assert.Equal("10.0.68.52:3128", _system.SystemProxy.Settings.Server);
            Assert.True(_system.WinHttp.Enabled);
        }

        [Fact]
        public void Disabled_adapter_is_enabled_before_its_settings_are_applied()
        {
            _system.Adapters[0].State = AdapterState.Disabled;

            IList<StepResult> results = _applier.Apply(Profile(FakeSystem.Static("10.68.12.37"), new ProxySettings()));

            Assert.Equal(new[] { "adapter Ethernet on", "ipv4 Ethernet", "system proxy", "winhttp" }, _system.Calls);
            Assert.StartsWith("Включение адаптера", results[0].Title);
        }

        [Fact]
        public void Enabled_adapter_is_not_touched()
        {
            _system.Adapters[0].State = AdapterState.NoLink;

            _applier.Apply(Profile(new Ipv4Settings(), new ProxySettings()));

            Assert.DoesNotContain("adapter Ethernet on", _system.Calls);
        }

        [Fact]
        public void Failed_step_is_reported_and_the_rest_still_run()
        {
            _system.Failing.Add("ipv4");

            IList<StepResult> results = _applier.Apply(Profile(FakeSystem.Static("10.68.12.37"), ProxySettings.FromRaw(true, "p:3128", "")));

            Assert.False(results[0].Ok);
            Assert.Equal("отказ: ipv4", results[0].Error);
            Assert.True(results[1].Ok);
            Assert.True(results[2].Ok);
            Assert.Equal("p:3128", _system.SystemProxy.Settings.Server);
        }

        [Fact]
        public void Only_requested_parts_are_applied()
        {
            _applier.Apply(ChangeRequest.For("Ручная правка прокси", null, null, ProxySettings.FromRaw(true, "p:3128", ""), _system.ReadProxy()));

            Assert.Equal(new[] { "system proxy", "winhttp" }, _system.Calls);
        }

        [Fact]
        public void Rollback_restores_what_was_there_before_the_last_apply()
        {
            _system.Adapters[0].Configured = FakeSystem.Static("192.168.1.50", "192.168.1.1", "192.168.1.1");
            _system.SystemProxy = new SystemProxyState { Settings = ProxySettings.FromRaw(true, "old:8080", "a.local"), AutoDetect = true, AutoConfigUrl = "http://wpad/proxy.pac" };
            _system.WinHttp = ProxySettings.FromRaw(true, "other:8080", "");

            _applier.Apply(Profile(new Ipv4Settings(), new ProxySettings()));
            Assert.True(_system.Adapters[0].Configured.AutoAddress);
            Assert.False(_system.SystemProxy.AutoDetect);

            Assert.True(_applier.CanRollback);
            IList<StepResult> results = _applier.Rollback();

            Assert.All(results, result => Assert.True(result.Ok));
            Assert.True(_system.Adapters[0].Configured.SameAs(FakeSystem.Static("192.168.1.50", "192.168.1.1", "192.168.1.1")));
            Assert.Equal("old:8080", _system.SystemProxy.Settings.Server);
            Assert.True(_system.SystemProxy.Settings.Enabled);
            Assert.True(_system.SystemProxy.AutoDetect);
            Assert.Equal("http://wpad/proxy.pac", _system.SystemProxy.AutoConfigUrl);
            Assert.Equal("other:8080", _system.WinHttp.Server);
        }

        [Fact]
        public void Rollback_touches_only_what_the_last_apply_touched()
        {
            _applier.Apply(ChangeRequest.For("Ручная правка прокси", null, null, ProxySettings.FromRaw(true, "p:3128", ""), _system.ReadProxy()));
            _system.Calls.Clear();

            _applier.Rollback();

            Assert.Equal(new[] { "system proxy", "winhttp" }, _system.Calls);
        }

        [Fact]
        public void Rollback_is_available_once()
        {
            Assert.False(_applier.CanRollback);

            _applier.Apply(Profile(new Ipv4Settings(), new ProxySettings()));
            _applier.Rollback();

            Assert.False(_applier.CanRollback);
            Assert.Throws<InvalidOperationException>(() => _applier.Rollback());
        }

        [Fact]
        public void Second_apply_makes_rollback_return_to_the_state_between_them()
        {
            _applier.Apply(Profile(FakeSystem.Static("10.68.12.37"), new ProxySettings()));
            _applier.Apply(Profile(FakeSystem.Static("10.68.12.99"), new ProxySettings()));

            _applier.Rollback();

            Assert.Equal("10.68.12.37", _system.Adapters[0].Configured.Address);
        }

        [Fact]
        public void Nothing_is_changed_when_previous_settings_cannot_be_read()
        {
            ChangeRequest request = Profile(FakeSystem.Static("10.68.12.37"), ProxySettings.FromRaw(true, "p:3128", ""));
            _system.ProxyFailure = new InvalidOperationException("нет пользователя");

            Assert.Throws<InvalidOperationException>(() => _applier.Apply(request));

            Assert.Empty(_system.Calls);
            Assert.False(_applier.CanRollback);
        }

        [Fact]
        public void Adapter_state_is_taken_at_the_moment_of_applying()
        {
            ChangeRequest request = Profile(new Ipv4Settings(), new ProxySettings());
            _system.Adapters[0] = FakeSystem.Ethernet(AdapterState.Disabled);

            _applier.Apply(request);

            Assert.Equal("adapter Ethernet on", _system.Calls[0]);
        }

        [Fact]
        public void Switching_adapter_is_logged_but_cannot_be_rolled_back()
        {
            StepResult result = _applier.SetAdapterEnabled(_system.Adapters[0], false);

            Assert.True(result.Ok);
            Assert.Equal(AdapterState.Disabled, _system.Adapters[0].State);
            Assert.False(_applier.CanRollback);
            Assert.Contains(_applier.Log, line => line.Contains("Выключение адаптера «Ethernet»") && line.Contains("выполнено"));
        }

        [Fact]
        public void Log_keeps_every_step_with_its_outcome()
        {
            _system.Failing.Add("winhttp");

            _applier.Apply(Profile(new Ipv4Settings(), new ProxySettings()));

            Assert.Contains(_applier.Log, line => line.Contains("Профиль «Тест»"));
            Assert.Contains(_applier.Log, line => line.Contains("Системный прокси") && line.Contains("выполнено"));
            Assert.Contains(_applier.Log, line => line.Contains("WinHTTP-прокси") && line.Contains("ОШИБКА: отказ: winhttp"));
            Assert.Equal(4, _applier.Log.Count(line => line.Length > 0));
        }
    }
}
