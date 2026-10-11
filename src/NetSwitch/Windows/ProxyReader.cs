using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NetSwitch.Model;

namespace NetSwitch.Windows
{
    internal static class ProxyReader
    {
        public const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        public const string InternetSettingsPolicyKey = @"Software\Policies\Microsoft\Windows\CurrentVersion\Internet Settings";
        public const string ControlPanelPolicyKey = @"Software\Policies\Microsoft\Internet Explorer\Control Panel";

        private const int WinHttpAccessNamedProxy = 3;

        /// <summary>Системный прокси пользователя с данным SID; пользователь должен быть в системе.</summary>
        public static SystemProxyState ReadSystemProxy(string userSid)
        {
            string path = userSid + "\\" + InternetSettingsKey;
            using (RegistryKey? key = Registry.Users.OpenSubKey(path))
            {
                if (key == null)
                    throw new InvalidOperationException("Не найдены настройки пользователя в реестре: HKEY_USERS\\" + path);

                var state = new SystemProxyState
                {
                    Settings = ProxySettings.FromRaw(
                        IsSet(key.GetValue("ProxyEnable")),
                        key.GetValue("ProxyServer") as string,
                        key.GetValue("ProxyOverride") as string),
                    AutoConfigUrl = key.GetValue("AutoConfigURL") as string ?? ""
                };

                using (RegistryKey? connections = key.OpenSubKey("Connections"))
                {
                    var blob = connections != null ? connections.GetValue("DefaultConnectionSettings") as byte[] : null;
                    if (blob != null) state.AutoDetect = InternetSettingsBlob.Parse(blob).AutoDetect;
                }
                return state;
            }
        }

        public static ProxySettings ReadWinHttp()
        {
            var info = new WinHttpProxyInfo();
            if (!WinHttpGetDefaultProxyConfiguration(ref info)) throw new Win32Exception();
            try
            {
                return ProxySettings.FromRaw(
                    info.AccessType == WinHttpAccessNamedProxy,
                    Marshal.PtrToStringUni(info.Proxy),
                    Marshal.PtrToStringUni(info.ProxyBypass));
            }
            finally
            {
                if (info.Proxy != IntPtr.Zero) GlobalFree(info.Proxy);
                if (info.ProxyBypass != IntPtr.Zero) GlobalFree(info.ProxyBypass);
            }
        }

        public static ProxyPolicy ReadPolicy(string userSid)
        {
            var policy = new ProxyPolicy();

            using (RegistryKey machine = OpenLocalMachine())
            using (RegistryKey? key = machine.OpenSubKey(InternetSettingsPolicyKey))
            {
                // 0 — прокси один на компьютер; отсутствие значения или 1 — у каждого пользователя свой
                object? perUser = key != null ? key.GetValue("ProxySettingsPerUser") : null;
                policy.PerMachine = perUser is int && (int)perUser == 0;
            }

            using (RegistryKey? key = Registry.Users.OpenSubKey(userSid + "\\" + InternetSettingsPolicyKey))
            {
                policy.SetsValues = key != null
                    && (key.GetValue("ProxyEnable") != null || key.GetValue("ProxyServer") != null || key.GetValue("AutoConfigURL") != null);
            }

            using (RegistryKey machine = OpenLocalMachine())
            using (RegistryKey? machineKey = machine.OpenSubKey(ControlPanelPolicyKey))
            using (RegistryKey? userKey = Registry.Users.OpenSubKey(userSid + "\\" + ControlPanelPolicyKey))
            {
                policy.ChangeLocked = (machineKey != null && IsSet(machineKey.GetValue("Proxy")))
                    || (userKey != null && IsSet(userKey.GetValue("Proxy")));
            }

            return policy;
        }

        /// <summary>Раздел HKLM без перенаправления в 32-разрядное представление.</summary>
        public static RegistryKey OpenLocalMachine()
        {
            return RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine,
                Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);
        }

        private static bool IsSet(object? value)
        {
            return value is int && (int)value != 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinHttpProxyInfo
        {
            public int AccessType;
            public IntPtr Proxy;
            public IntPtr ProxyBypass;
        }

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpGetDefaultProxyConfiguration(ref WinHttpProxyInfo info);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalFree(IntPtr memory);
    }
}
