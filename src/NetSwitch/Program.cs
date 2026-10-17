using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using NetSwitch.Diagnostics;
using NetSwitch.Model;
using NetSwitch.Profiles;
using NetSwitch.UI;
using NetSwitch.Windows;

namespace NetSwitch
{
    internal static class AppInfo
    {
        // Полная версия с суффиксом: локальную сборку «0.0.0-dev» видно сразу
        public static string Version
        {
            get
            {
                var attribute = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                    typeof(AppInfo).Assembly, typeof(AssemblyInformationalVersionAttribute))!;
                return attribute.InformationalVersion;
            }
        }
    }

    internal static class Program
    {
        // Что инструмент успел сделать до сбоя, должно попасть в отчёт о сбое
        private static IList<string> _applyLog = new List<string>();

        [STAThread]
        private static void Main()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (sender, e) => ReportCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => ReportCrash(e.ExceptionObject as Exception);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var system = new WindowsNetworkSystem();
            var applier = new Applier(system);
            _applyLog = applier.Log;

            var store = new ProfileStore(Application.StartupPath);
            store.Load();

            Application.Run(new MainForm(system, applier, store, () => WindowsDiagnostics.BuildReport(null, applier.Log), new WindowPrompts()));
        }

        // Сбой на чужом компьютере — как раз то, ради чего существует отчёт
        private static void ReportCrash(Exception? error)
        {
            string details = error != null ? error.GetType().Name + ": " + error.Message : "неизвестная ошибка";
            DialogResult answer = MessageBox.Show(
                "В NetSwitch произошла ошибка:\n\n" + details + "\n\nСохранить отчёт рядом с программой?",
                "NetSwitch", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
            if (answer != DialogResult.Yes) return;

            try
            {
                Report report = WindowsDiagnostics.BuildReport(error, _applyLog);
                string path = ReportFile.Save(Application.StartupPath, Environment.MachineName, DateTime.Now, report.Text);
                MessageBox.Show("Отчёт сохранён:\n" + path, "NetSwitch", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось сохранить отчёт:\n" + ex.Message, "NetSwitch", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
