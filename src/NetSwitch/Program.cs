using System;
using System.Windows.Forms;
using NetSwitch.Diagnostics;
using NetSwitch.UI;
using NetSwitch.Windows;

namespace NetSwitch
{
    internal static class AppInfo
    {
        public const string Edition = "разведочная сборка, только чтение";

        public static string Version
        {
            get { return typeof(AppInfo).Assembly.GetName().Version!.ToString(3); }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (sender, e) => ReportCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => ReportCrash(e.ExceptionObject as Exception);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(new WindowsNetworkSystem(), () => WindowsDiagnostics.BuildReport(null)));
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
                Report report = WindowsDiagnostics.BuildReport(error);
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
