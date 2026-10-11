using System;
using System.IO;
using System.Linq;
using NetSwitch.Diagnostics;
using Xunit;

namespace NetSwitch.Tests
{
    public class ReportTests
    {
        [Fact]
        public void Sections_are_titled_and_indented()
        {
            var report = new Report();
            report.Line("Заголовок");
            report.Section("Система", () =>
            {
                report.Value("Версия", "10.0");
                report.Section("Процесс", () => report.Value("Права администратора", true));
            });

            Assert.Equal(
                new[] { "Заголовок", "", "== Система ==", "  Версия: 10.0", "  [Процесс]", "    Права администратора: True" },
                report.Lines);
            Assert.False(report.HasErrors);
        }

        [Fact]
        public void Failure_in_one_section_is_recorded_and_does_not_stop_the_rest()
        {
            var report = new Report();
            report.Section("Антивирус", () => { throw new InvalidOperationException("нет доступа"); });
            report.Section("Адаптеры", () => report.Line("Ethernet"));

            Assert.True(report.HasErrors);
            Assert.Contains(report.Lines, line => line.Contains("ОШИБКА") && line.Contains("InvalidOperationException") && line.Contains("нет доступа"));
            Assert.Contains("  Ethernet", report.Lines);
        }

        [Fact]
        public void Failure_cause_is_recorded_too()
        {
            var report = new Report();
            report.Section("Прокси", () => { throw new InvalidOperationException("снаружи", new FormatException("внутри")); });

            Assert.Contains(report.Lines, line => line.Contains("FormatException") && line.Contains("внутри"));
        }

        [Fact]
        public void Formats_values_for_reading()
        {
            Assert.Equal("(нет)", Report.Format(null));
            Assert.Equal("(пусто)", Report.Format(""));
            Assert.Equal("(пусто)", Report.Format(new string[0]));
            Assert.Equal("10.0.0.1, 10.0.0.2", Report.Format(new[] { "10.0.0.1", "10.0.0.2" }));
            Assert.Equal("46 00 0F", Report.Format(new byte[] { 0x46, 0x00, 0x0F }));
            Assert.Equal("22", Report.Format((uint)22));
        }
    }

    public class ReportFileTests : IDisposable
    {
        private static readonly DateTime When = new DateTime(2026, 10, 11, 14, 3, 22);
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "NetSwitch.Tests." + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        [Fact]
        public void File_is_named_after_computer_and_time()
        {
            Assert.Equal("BUH-PC-07_2026-10-11_14-03-22.txt", ReportFile.NameFor("BUH-PC-07", When));
        }

        [Fact]
        public void Characters_not_allowed_in_file_names_are_replaced()
        {
            Assert.Equal("a_b_c_2026-10-11_14-03-22.txt", ReportFile.NameFor(@"a\b:c", When));
        }

        [Fact]
        public void Reports_land_in_a_folder_next_to_the_tool()
        {
            string path = ReportFile.Save(_folder, "PC-1", When, "текст отчёта");

            Assert.Equal(Path.Combine(_folder, ReportFile.FolderName, "PC-1_2026-10-11_14-03-22.txt"), path);
            Assert.Equal("текст отчёта", File.ReadAllText(path));
        }

        [Fact]
        public void Reports_from_different_computers_accumulate()
        {
            ReportFile.Save(_folder, "PC-1", When, "первый");
            ReportFile.Save(_folder, "PC-2", When, "второй");
            ReportFile.Save(_folder, "PC-1", When.AddMinutes(5), "третий");

            Assert.Equal(3, Directory.GetFiles(Path.Combine(_folder, ReportFile.FolderName)).Length);
        }

        [Fact]
        public void Report_taken_in_the_same_second_does_not_overwrite_the_previous_one()
        {
            string first = ReportFile.Save(_folder, "PC-1", When, "первый");
            string second = ReportFile.Save(_folder, "PC-1", When, "второй");
            string third = ReportFile.Save(_folder, "PC-1", When, "третий");

            Assert.Equal(3, new[] { first, second, third }.Distinct().Count());
            Assert.Equal("первый", File.ReadAllText(first));
            Assert.Equal("второй", File.ReadAllText(second));
            Assert.Equal("третий", File.ReadAllText(third));
        }
    }
}
