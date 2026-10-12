using System;
using System.Collections.Generic;
using System.IO;
using NetSwitch.Diagnostics;
using NetSwitch.Model;
using NetSwitch.Windows;
using Xunit;

namespace NetSwitch.Tests
{
    /// <summary>
    /// Читают настройки компьютера, на котором запущены тесты. Ничего не меняют
    /// и прав администратора не требуют.
    /// </summary>
    [Trait("Category", "Machine")]
    public class MachineSmokeTests
    {
        [Fact]
        public void Reads_adapters_of_this_machine()
        {
            IList<Adapter> adapters = new WindowsNetworkSystem().ReadAdapters();

            Assert.NotEmpty(adapters);
            Assert.All(adapters, adapter =>
            {
                Assert.NotEqual("", adapter.Name);
                Assert.StartsWith("{", adapter.Id);
            });
        }

        [Fact]
        public void Reads_proxy_of_this_machine()
        {
            ProxyState state = new WindowsNetworkSystem().ReadProxy();

            Assert.StartsWith("S-1-5-", state.User.Sid);
            Assert.True(state.User.IsProcessOwner);
        }

        [Fact]
        public void Builds_a_full_report_for_this_machine()
        {
            Report report = WindowsDiagnostics.BuildReport(null, new List<string> { "12:00:00 | Профиль «Тест»" });

            // Отчёт остаётся в каталоге сборки тестов, чтобы его можно было просмотреть глазами
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "machine-report.txt"), report.Text);

            foreach (string section in new[] { "Система", "Интерактивный пользователь", "Адаптеры глазами инструмента", "Системный прокси", "WinHTTP-прокси", "Политики прокси" })
            {
                Assert.Contains("== " + section + " ==", report.Lines);
            }
            Assert.Contains("  12:00:00 | Профиль «Тест»", report.Lines);
            Assert.False(report.HasErrors, report.Text);
        }
    }
}
