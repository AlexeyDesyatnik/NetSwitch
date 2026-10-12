using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using Microsoft.Win32;
using NetSwitch.Diagnostics;
using NetSwitch.Model;

namespace NetSwitch.Windows
{
    /// <summary>
    /// Собирает отчёт об управляемом компьютере. Сведения берутся из всех источников сразу и в сыром
    /// виде: отчёт нужен, чтобы разбираться в расхождениях между версиями Windows, не возвращаясь к компьютеру.
    /// </summary>
    internal static class WindowsDiagnostics
    {
        private const int MaxDumpedBytes = 64;

        private static readonly string[] AdapterProperties =
        {
            "Index", "Name", "GUID", "PNPDeviceID", "NetConnectionStatus", "NetEnabled", "ConfigManagerErrorCode",
            "PhysicalAdapter", "AdapterType", "AdapterTypeId", "MACAddress", "ServiceName", "Manufacturer", "Speed"
        };

        private static readonly string[] ConfigurationProperties =
        {
            "Index", "Description", "SettingID", "IPEnabled", "DHCPEnabled", "DHCPServer", "IPAddress", "IPSubnet",
            "DefaultIPGateway", "DNSServerSearchOrder"
        };

        /// <param name="applyLog">Шаги применения, выполненные за этот запуск.</param>
        public static Report BuildReport(Exception? crash, IList<string> applyLog)
        {
            var report = new Report();
            report.Line("NetSwitch — отчёт");
            report.Value("Версия инструмента", AppInfo.Version);
            report.Value("Создан", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            report.Value("Компьютер", Environment.MachineName);

            if (crash != null) report.Section("Сбой инструмента", () => report.Error(crash));

            report.Section("Применения за этот запуск", () => Lines(report, applyLog));
            report.Section("Команды netsh за этот запуск", () => Lines(report, Netsh.History));

            report.Section("Система", () => SystemInfo(report));
            report.Section("Антивирус", () => Antivirus(report));
            report.Section("Интерактивный пользователь", () => User(report));
            report.Section("Адаптеры глазами инструмента", () => AdaptersAsSeen(report));
            report.Section("WMI: Win32_NetworkAdapter", () => WmiAdapters(report));
            report.Section("WMI: Win32_NetworkAdapterConfiguration", () => WmiConfigurations(report));
            report.Section("Реестр: настройки IPv4 адаптеров", () => TcpipKeys(report));
            report.Section("Сетевой стек: NetworkInterface", () => NetworkInterfaces(report));
            report.Section("Системный прокси", () => SystemProxy(report));
            report.Section("WinHTTP-прокси", () => WinHttpProxy(report));
            report.Section("Политики прокси", () => ProxyPolicies(report));
            return report;
        }

        private static void Lines(Report report, IList<string> lines)
        {
            if (lines.Count == 0) report.Line("(не было)");
            foreach (string line in new List<string>(lines)) report.Line(line);
        }

        private static void SystemInfo(Report report)
        {
            report.Section("Windows", () =>
            {
                // Windows 11 называет себя в реестре «Windows 10» и отличается только номером сборки
                if (Environment.OSVersion.Version.Major == 10 && Environment.OSVersion.Version.Build >= 22000)
                {
                    report.Line("Это Windows 11");
                }

                using (RegistryKey machine = ProxyReader.OpenLocalMachine())
                using (RegistryKey? key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    foreach (string name in new[] { "ProductName", "EditionID", "InstallationType", "DisplayVersion", "ReleaseId", "CurrentVersion", "CurrentBuild", "UBR", "CSDVersion" })
                    {
                        report.Value(name, key != null ? key.GetValue(name) : null);
                    }
                }
                report.Value("Environment.OSVersion", Environment.OSVersion.VersionString);
                report.Value("64-разрядная система", Environment.Is64BitOperatingSystem);
                report.Value("Язык интерфейса", CultureInfo.CurrentUICulture.Name);
            });

            report.Section(".NET Framework", () =>
            {
                report.Value("Среда выполнения", Environment.Version);
                using (RegistryKey machine = ProxyReader.OpenLocalMachine())
                {
                    foreach (string path in new[] { @"v4\Full", @"v4\Client", "v3.5" })
                    {
                        using (RegistryKey? key = machine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\" + path))
                        {
                            report.Value(path, key == null
                                ? "(не установлен)"
                                : "Version=" + Report.Format(key.GetValue("Version")) + ", Release=" + Report.Format(key.GetValue("Release")));
                        }
                    }
                }
            });

            report.Section("Процесс", () =>
            {
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                report.Value("Файл", exe);
                report.Value("Тип диска", new DriveInfo(Path.GetPathRoot(exe)!).DriveType);
                report.Value("64-разрядный процесс", Environment.Is64BitProcess);
                report.Value("Запущен от имени", InteractiveUserReader.ProcessOwnerName());
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    report.Value("Права администратора", new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator));
                }
            });

            report.Section("Домен", () =>
            {
                foreach (WmiRow row in Wmi.Query("SELECT * FROM Win32_ComputerSystem"))
                {
                    foreach (string name in new[] { "PartOfDomain", "Domain", "Workgroup", "Manufacturer", "Model" })
                    {
                        report.Value(name, row.ContainsKey(name) ? row[name] : null);
                    }
                }
            });
        }

        private static void Antivirus(Report report)
        {
            // Пространство имён есть только на клиентских Windows; на серверных запрос завершится ошибкой
            List<WmiRow> products = Wmi.Query("SELECT * FROM AntiVirusProduct", @"root\SecurityCenter2");
            if (products.Count == 0) report.Line("(не зарегистрирован)");

            foreach (WmiRow row in products)
            {
                int? state = row.Number("productState");
                report.Line(row.Text("displayName")
                    + " | productState=0x" + (state ?? 0).ToString("X6", CultureInfo.InvariantCulture)
                    + " | " + row.Text("pathToSignedProductExe"));
            }
        }

        private static void User(Report report)
        {
            report.Value("Сеанс", InteractiveUserReader.CurrentSession());
            report.Value("Процесс запущен от имени", InteractiveUserReader.ProcessOwnerName());

            report.Section("По сведениям о сеансе", () => Describe(report, InteractiveUserReader.Read()));
            report.Section("По владельцу explorer.exe", () => Describe(report, InteractiveUserReader.ReadFromShell()));
        }

        private static void Describe(Report report, InteractiveUser? user)
        {
            if (user == null)
            {
                report.Line("(не найден)");
                return;
            }
            report.Value("Имя", user.Name);
            report.Value("SID", user.Sid);
            report.Value("Он же владелец процесса", user.IsProcessOwner);
        }

        private static void AdaptersAsSeen(Report report)
        {
            foreach (Adapter adapter in AdapterReader.ReadAll())
            {
                report.Section(adapter.Name, () =>
                {
                    report.Value("Устройство", adapter.Description);
                    report.Value("GUID", adapter.Id);
                    report.Value("Физический", adapter.IsPhysical);
                    report.Value("Беспроводной", adapter.IsWireless);
                    report.Value("Состояние", adapter.State);
                    report.Value("Конфликт адресов", adapter.Active.HasConflict);
                    report.Value("Настроено: адрес автоматически", adapter.Configured.AutoAddress);
                    report.Value("Настроено: адрес", adapter.Configured.Address);
                    report.Value("Настроено: маска", adapter.Configured.Mask);
                    report.Value("Настроено: шлюз", adapter.Configured.Gateway);
                    report.Value("Настроено: DNS автоматически", adapter.Configured.AutoDns);
                    report.Value("Настроено: DNS", adapter.Configured.Dns.ToArray());
                    report.Value("Сейчас: адреса", adapter.Active.Addresses.ToArray());
                    report.Value("Сейчас: шлюзы", adapter.Active.Gateways.ToArray());
                    report.Value("Сейчас: DNS", adapter.Active.Dns.ToArray());
                });
            }
        }

        private static void WmiAdapters(Report report)
        {
            var hidden = new List<string>();
            foreach (WmiRow row in Wmi.Query("SELECT * FROM Win32_NetworkAdapter"))
            {
                if (row.Text("NetConnectionID").Length == 0)
                {
                    hidden.Add(row.Text("Index") + " | " + row.Text("Name") + " | " + row.Text("PNPDeviceID"));
                    continue;
                }
                report.Section(row.Text("NetConnectionID"), () => Dump(report, row, AdapterProperties));
            }

            report.Section("Без имени подключения (в «Сетевых подключениях» не видны)", () =>
            {
                foreach (string line in hidden) report.Line(line);
            });
        }

        private static void WmiConfigurations(Report report)
        {
            var visible = new HashSet<string>();
            foreach (WmiRow row in Wmi.Query(AdapterReader.AdaptersQuery)) visible.Add(row.Text("Index"));

            foreach (WmiRow row in Wmi.Query("SELECT * FROM Win32_NetworkAdapterConfiguration"))
            {
                if (!visible.Contains(row.Text("Index"))) continue;
                report.Section(row.Text("Description"), () => Dump(report, row, ConfigurationProperties));
            }
        }

        private static void Dump(Report report, WmiRow row, string[] properties)
        {
            foreach (string name in properties)
            {
                report.Value(name, row.ContainsKey(name) ? row[name] : "(свойства нет)");
            }
        }

        private static void TcpipKeys(Report report)
        {
            foreach (WmiRow row in Wmi.Query(AdapterReader.AdaptersQuery))
            {
                string id = row.Text("GUID");
                report.Section(row.Text("NetConnectionID") + " " + id, () =>
                {
                    using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(TcpipRegistry.InterfacesKey + "\\" + id))
                    {
                        DumpKey(report, key);
                    }
                });
            }
        }

        private static void NetworkInterfaces(Report report)
        {
            foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
            {
                report.Section(item.Name, () =>
                {
                    report.Value("Id", item.Id);
                    report.Value("Описание", item.Description);
                    report.Value("Тип", item.NetworkInterfaceType);
                    report.Value("Состояние", item.OperationalStatus);

                    IPInterfaceProperties properties = item.GetIPProperties();
                    foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        report.Value("Адрес", unicast.Address + " маска " + Report.Format(unicast.IPv4Mask));
                    }
                    foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses)
                    {
                        report.Value("Шлюз", gateway.Address);
                    }
                    foreach (System.Net.IPAddress dns in properties.DnsAddresses)
                    {
                        report.Value("DNS", dns);
                    }
                    if (item.Supports(NetworkInterfaceComponent.IPv4))
                    {
                        report.Value("DHCP", properties.GetIPv4Properties().IsDhcpEnabled);
                    }
                });
            }
        }

        private static void SystemProxy(Report report)
        {
            InteractiveUser user = InteractiveUserReader.Read();
            report.Section("Интерактивный пользователь " + user.Name, () => DumpInternetSettings(report, Registry.Users, user.Sid + "\\"));

            if (!user.IsProcessOwner)
            {
                report.Section("Владелец процесса " + InteractiveUserReader.ProcessOwnerName(), () => DumpInternetSettings(report, Registry.CurrentUser, ""));
            }

            report.Section("Общий для компьютера (действует только при политике ProxySettingsPerUser=0)", () =>
            {
                using (RegistryKey machine = ProxyReader.OpenLocalMachine())
                {
                    DumpInternetSettings(report, machine, "");
                }
            });
        }

        private static void DumpInternetSettings(Report report, RegistryKey root, string prefix)
        {
            using (RegistryKey? key = root.OpenSubKey(prefix + ProxyReader.InternetSettingsKey))
            {
                if (key == null)
                {
                    report.Line("(раздела нет)");
                    return;
                }
                foreach (string name in new[] { "ProxyEnable", "ProxyServer", "ProxyOverride", "AutoConfigURL", "AutoDetect", "MigrateProxy" })
                {
                    report.Value(name, key.GetValue(name));
                }
                using (RegistryKey? connections = key.OpenSubKey("Connections"))
                {
                    foreach (string name in new[] { "DefaultConnectionSettings", "SavedLegacySettings", "WinHttpSettings" })
                    {
                        var blob = connections != null ? connections.GetValue(name) as byte[] : null;
                        report.Section(name, () => DumpBlob(report, blob));
                    }
                }
            }
        }

        private static void DumpBlob(Report report, byte[]? data)
        {
            if (data == null)
            {
                report.Line("(значения нет)");
                return;
            }
            report.Value("Байты", data);

            InternetSettingsBlob blob = InternetSettingsBlob.Parse(data);
            report.Value("Версия", blob.Version);
            report.Value("Счётчик", blob.Counter);
            report.Value("Флаги", "0x" + blob.Flags.ToString("X", CultureInfo.InvariantCulture));
            report.Value("Прокси", blob.Proxy);
            report.Value("Исключения", blob.Bypass);
            report.Value("Сценарий", blob.AutoConfigUrl);
        }

        private static void WinHttpProxy(Report report)
        {
            report.Section("WinHttpGetDefaultProxyConfiguration", () =>
            {
                ProxySettings settings = ProxyReader.ReadWinHttp();
                report.Value("Включён", settings.Enabled);
                report.Value("Сервер", settings.Server);
                report.Value("Исключения", settings.ToBypassList());
            });

            var views = new List<RegistryView> { RegistryView.Registry32 };
            if (Environment.Is64BitOperatingSystem) views.Insert(0, RegistryView.Registry64);

            foreach (RegistryView view in views)
            {
                report.Section("Реестр, " + view, () =>
                {
                    using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (RegistryKey? key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings\Connections"))
                    {
                        DumpBlob(report, key != null ? key.GetValue("WinHttpSettings") as byte[] : null);
                    }
                });
            }
        }

        private static void ProxyPolicies(Report report)
        {
            InteractiveUser user = InteractiveUserReader.Read();

            report.Section("Вывод инструмента", () =>
            {
                ProxyPolicy policy = ProxyReader.ReadPolicy(user.Sid);
                report.Value("Прокси один на компьютер", policy.PerMachine);
                report.Value("Политика задаёт значения", policy.SetsValues);
                report.Value("Изменение запрещено", policy.ChangeLocked);
            });

            foreach (string path in new[] { ProxyReader.InternetSettingsPolicyKey, ProxyReader.ControlPanelPolicyKey })
            {
                report.Section("HKLM\\" + path, () =>
                {
                    using (RegistryKey machine = ProxyReader.OpenLocalMachine())
                    using (RegistryKey? key = machine.OpenSubKey(path))
                    {
                        DumpKey(report, key);
                    }
                });
                report.Section("HKU\\" + user.Sid + "\\" + path, () =>
                {
                    using (RegistryKey? key = Registry.Users.OpenSubKey(user.Sid + "\\" + path))
                    {
                        DumpKey(report, key);
                    }
                });
            }
        }

        private static void DumpKey(Report report, RegistryKey? key)
        {
            if (key == null)
            {
                report.Line("(раздела нет)");
                return;
            }

            string[] names = key.GetValueNames();
            if (names.Length == 0) report.Line("(значений нет)");

            Array.Sort(names, StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                object? value = key.GetValue(name);

                // Служебные двоичные значения (параметры аренды DHCP) занимают сотни байт и ничего не объясняют
                var bytes = value as byte[];
                if (bytes != null && bytes.Length > MaxDumpedBytes) value = "(двоичное значение, " + bytes.Length + " байт)";

                report.Value(name.Length == 0 ? "(по умолчанию)" : name, value);
            }
        }
    }
}
