using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using NetSwitch.Diagnostics;
using NetSwitch.Model;
using NetSwitch.UI;
using Xunit;

namespace NetSwitch.Tests
{
    public class MainFormTests
    {
        private sealed class FakeSystem : INetworkSystem
        {
            public Exception? AdaptersFailure;
            public ProxyState Proxy = new ProxyState(
                new InteractiveUser(@"OFFICE\ivanov", "S-1-5-21-1-2-3-1001", false),
                new SystemProxyState { Settings = ProxySettings.FromRaw(true, "10.0.68.52:3128", "*.corp.local;<local>") },
                ProxySettings.FromRaw(false, "", ""),
                new ProxyPolicy());

            public IList<Adapter> ReadAdapters()
            {
                if (AdaptersFailure != null) throw AdaptersFailure;

                var connected = new Adapter { Name = "Ethernet", Description = "Intel(R) Ethernet Controller I225-V", IsPhysical = true, State = AdapterState.Connected };
                connected.Configured = new Ipv4Settings { AutoAddress = false, Address = "10.68.12.37", Mask = "255.255.255.0", Gateway = "10.68.12.1", AutoDns = false };
                connected.Active.Addresses.Add("10.68.12.37/24");
                connected.Active.Gateways.Add("10.68.12.1");
                connected.Active.Dns.Add("10.0.68.10");

                return new List<Adapter>
                {
                    new Adapter { Name = "vEthernet (Default Switch)", Description = "Hyper-V Virtual Ethernet Adapter", IsPhysical = false, State = AdapterState.Connected },
                    new Adapter { Name = "Беспроводная сеть", Description = "Intel(R) Wi-Fi 6 AX200", IsPhysical = true, State = AdapterState.NoLink },
                    connected,
                    new Adapter { Name = "Ethernet 2", Description = "Realtek USB GbE Family Controller", IsPhysical = true, State = AdapterState.Disabled }
                };
            }

            public ProxyState ReadProxy()
            {
                return Proxy;
            }
        }

        // Окна WinForms живут в потоке STA, а xUnit запускает тесты в MTA
        private static void OnShownForm(INetworkSystem system, Action<MainForm> check)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new MainForm(system, () => new Report()))
                    {
                        form.Show();
                        Application.DoEvents();
                        check(form);
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null) throw new InvalidOperationException("Сбой в окне: " + failure.Message, failure);
        }

        private static T Find<T>(Control root) where T : Control
        {
            T? found = FindOrNull<T>(root);
            if (found == null) throw new InvalidOperationException("В окне нет элемента " + typeof(T).Name);
            return found;
        }

        private static T? FindOrNull<T>(Control root) where T : Control
        {
            foreach (Control child in root.Controls)
            {
                var match = child as T;
                if (match != null) return match;

                T? nested = FindOrNull<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }

        private static string AllText(Control root)
        {
            string text = root is Label ? root.Text + "\n" : "";
            foreach (Control child in root.Controls) text += AllText(child);
            return text;
        }

        private static List<string> AdapterNames(ListView list)
        {
            var names = new List<string>();
            foreach (ListViewItem item in list.Items) names.Add(item.Text);
            return names;
        }

        [Fact]
        public void Only_physical_adapters_are_listed_by_default()
        {
            OnShownForm(new FakeSystem(), form =>
            {
                Assert.Equal(new[] { "Ethernet", "Ethernet 2", "Беспроводная сеть" }, AdapterNames(Find<ListView>(form)));
            });
        }

        [Fact]
        public void Virtual_adapters_appear_on_request()
        {
            OnShownForm(new FakeSystem(), form =>
            {
                Find<CheckBox>(form).Checked = true;

                Assert.Contains("vEthernet (Default Switch)", AdapterNames(Find<ListView>(form)));
                Assert.Equal(4, Find<ListView>(form).Items.Count);
            });
        }

        [Fact]
        public void Adapter_row_shows_state_address_and_mode()
        {
            OnShownForm(new FakeSystem(), form =>
            {
                ListViewItem row = Find<ListView>(form).Items[0];
                var cells = new List<string>();
                foreach (ListViewItem.ListViewSubItem cell in row.SubItems) cells.Add(cell.Text);

                Assert.Equal(
                    new[] { "Ethernet", "подключён", "10.68.12.37/24", "вручную", "10.68.12.1", "10.0.68.10", "Intel(R) Ethernet Controller I225-V" },
                    cells);
            });
        }

        [Fact]
        public void Proxy_panel_names_the_interactive_user_and_flags_mismatch()
        {
            OnShownForm(new FakeSystem(), form =>
            {
                string text = AllText(form);

                Assert.Contains(@"OFFICE\ivanov (инструмент запущен от имени другой учётной записи)", text);
                Assert.Contains("10.0.68.52:3128; исключения: *.corp.local; не для локальных адресов", text);
                Assert.Contains("Системный прокси и WinHTTP-прокси различаются.", text);
                Assert.DoesNotContain("групповой политикой", text);
            });
        }

        [Fact]
        public void Policy_managed_proxy_is_flagged()
        {
            var system = new FakeSystem();
            system.Proxy = new ProxyState(system.Proxy.User, system.Proxy.System, system.Proxy.WinHttp, new ProxyPolicy { PerMachine = true });

            OnShownForm(system, form => Assert.Contains("Прокси на этом компьютере задаётся групповой политикой.", AllText(form)));
        }

        [Fact]
        public void Failure_to_read_adapters_is_shown_and_proxy_is_still_read()
        {
            var system = new FakeSystem { AdaptersFailure = new InvalidOperationException("служба WMI остановлена") };

            OnShownForm(system, form =>
            {
                Assert.Empty(Find<ListView>(form).Items);
                Assert.Contains("адаптеры — служба WMI остановлена", AllText(form));
                Assert.Contains("10.0.68.52:3128", AllText(form));
            });
        }

        [Fact]
        public void Window_can_be_rendered()
        {
            OnShownForm(new FakeSystem(), form =>
            {
                Find<CheckBox>(form).Checked = true;
                Application.DoEvents();

                // Снимок остаётся в каталоге сборки тестов, чтобы окно можно было посмотреть глазами
                using (var image = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                    image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "main-form.png"), ImageFormat.Png);
                }
            });
        }
    }
}
