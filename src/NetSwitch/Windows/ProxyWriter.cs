using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NetSwitch.Model;

namespace NetSwitch.Windows
{
    internal static class ProxyWriter
    {
        private const string ConnectionsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings\Connections";
        private const string WinHttpValue = "WinHttpSettings";

        private const int WinHttpAccessNoProxy = 1;
        private const int WinHttpAccessNamedProxy = 3;
        private const int InternetOptionRefresh = 37;
        private const int InternetOptionSettingsChanged = 39;

        /// <summary>
        /// Системный прокси пишется прямо в раздел реестра пользователя: штатная функция WinINet меняет
        /// настройки только того, от чьего имени запущен процесс, а это не всегда интерактивный пользователь.
        /// </summary>
        public static void WriteSystemProxy(InteractiveUser user, SystemProxyState state)
        {
            string path = user.Sid + "\\" + ProxyReader.InternetSettingsKey;
            using (RegistryKey? key = Registry.Users.OpenSubKey(path, true))
            {
                if (key == null)
                    throw new InvalidOperationException("Не найдены настройки пользователя " + user.Name + " в реестре.");

                key.SetValue("ProxyEnable", state.Settings.Enabled ? 1 : 0, RegistryValueKind.DWord);
                SetOrDelete(key, "ProxyServer", state.Settings.Server);
                SetOrDelete(key, "ProxyOverride", state.Settings.ToBypassList());
                SetOrDelete(key, "AutoConfigURL", state.AutoConfigUrl);

                using (RegistryKey connections = key.CreateSubKey("Connections")!)
                {
                    // Окно «Свойства браузера» читает первое значение, WinINet после перезагрузки — второе
                    foreach (string name in new[] { "DefaultConnectionSettings", "SavedLegacySettings" })
                    {
                        connections.SetValue(name, InternetSettingsBlob.Build(Previous(connections, name), state), RegistryValueKind.Binary);
                    }
                }
            }

            // Сообщить запущенным программам можно только в своём сеансе настроек; чужому пользователю —
            // никак, его программы увидят перемену сами или после перезапуска
            if (user.IsProcessOwner)
            {
                InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
            }
        }

        public static void WriteWinHttp(ProxySettings settings)
        {
            if (settings.Enabled && settings.Server.Length == 0)
                throw new InvalidOperationException("Не задан адрес прокси-сервера.");

            string bypass = settings.ToBypassList();
            var info = new WinHttpProxyInfo
            {
                AccessType = settings.Enabled ? WinHttpAccessNamedProxy : WinHttpAccessNoProxy,
                Proxy = settings.Enabled ? Marshal.StringToHGlobalUni(settings.Server) : IntPtr.Zero,
                ProxyBypass = settings.Enabled && bypass.Length > 0 ? Marshal.StringToHGlobalUni(bypass) : IntPtr.Zero
            };
            try
            {
                if (!WinHttpSetDefaultProxyConfiguration(ref info)) throw new Win32Exception();
            }
            finally
            {
                Marshal.FreeHGlobal(info.Proxy);
                Marshal.FreeHGlobal(info.ProxyBypass);
            }

            MirrorWinHttpTo32Bit();
        }

        // На 64-разрядной Windows у 32-разрядных программ своя копия WinHTTP-прокси
        private static void MirrorWinHttpTo32Bit()
        {
            if (!Environment.Is64BitOperatingSystem || !Environment.Is64BitProcess) return;

            byte[]? written;
            using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey? key = machine.OpenSubKey(ConnectionsKey))
            {
                written = key != null ? key.GetValue(WinHttpValue) as byte[] : null;
            }
            if (written == null) return;

            using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
            using (RegistryKey key = machine.CreateSubKey(ConnectionsKey)!)
            {
                key.SetValue(WinHttpValue, written, RegistryValueKind.Binary);
            }
        }

        private static InternetSettingsBlob? Previous(RegistryKey connections, string name)
        {
            var data = connections.GetValue(name) as byte[];
            if (data == null) return null;

            try
            {
                return InternetSettingsBlob.Parse(data);
            }
            catch (FormatException)
            {
                // Испорченное значение не чиним, а заменяем новым
                return null;
            }
        }

        private static void SetOrDelete(RegistryKey key, string name, string value)
        {
            if (value.Length > 0)
                key.SetValue(name, value, RegistryValueKind.String);
            else
                key.DeleteValue(name, false);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinHttpProxyInfo
        {
            public int AccessType;
            public IntPtr Proxy;
            public IntPtr ProxyBypass;
        }

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpSetDefaultProxyConfiguration(ref WinHttpProxyInfo info);

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr internet, int option, IntPtr buffer, int length);
    }
}
