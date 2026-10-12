using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using NetSwitch.Diagnostics;
using NetSwitch.Model;
using NetSwitch.Profiles;
using NetSwitch.UI;
using Xunit;

namespace NetSwitch.Tests
{
    public class MainFormTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "NetSwitch.Tests." + Guid.NewGuid().ToString("N"));
        private readonly FakeSystem _system = new FakeSystem();
        private readonly ProfileStore _store;

        public MainFormTests()
        {
            Directory.CreateDirectory(_folder);
            _store = new ProfileStore(_folder);

            Adapter ethernet = FakeSystem.Ethernet();
            ethernet.Configured = FakeSystem.Static("10.68.12.37", "10.68.12.1", "10.0.68.10");
            ethernet.Active.Addresses.Add("10.68.12.37/24");
            ethernet.Active.Gateways.Add("10.68.12.1");
            ethernet.Active.Dns.Add("10.0.68.10");

            _system.Adapters.Add(new Adapter { Id = "{V}", Name = "vEthernet (Default Switch)", Description = "Hyper-V Virtual Ethernet Adapter", State = AdapterState.Connected });
            _system.Adapters.Add(new Adapter { Id = "{W}", Name = "Беспроводная сеть", Description = "Intel(R) Wi-Fi 6 AX200", IsPhysical = true, IsWireless = true, State = AdapterState.NoLink });
            _system.Adapters.Add(ethernet);
            _system.Adapters.Add(new Adapter { Id = "{U}", Name = "USB Ethernet", Description = "Realtek USB GbE Family Controller", IsPhysical = true, State = AdapterState.Disabled });

            _system.User = new InteractiveUser(@"OFFICE\ivanov", "S-1-5-21-1-2-3-1001", false);
            _system.SystemProxy = new SystemProxyState { Settings = ProxySettings.FromRaw(true, "10.0.68.52:3128", "*.corp.local;<local>") };
        }

        public void Dispose()
        {
            Directory.Delete(_folder, true);
        }

        // Окна WinForms живут в потоке STA, а xUnit запускает тесты в MTA
        private void OnShownForm(Action<MainForm> check)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new MainForm(_system, new Applier(_system), _store, () => new Report()))
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

            if (failure != null) throw new InvalidOperationException("Сбой в окне: " + failure, failure);
        }

        private static List<T> All<T>(Control root) where T : Control
        {
            var found = new List<T>();
            foreach (Control child in root.Controls)
            {
                var match = child as T;
                if (match != null) found.Add(match);
                found.AddRange(All<T>(child));
            }
            return found;
        }

        private static T Find<T>(Control root) where T : Control
        {
            List<T> found = All<T>(root);
            if (found.Count == 0) throw new InvalidOperationException("В окне нет элемента " + typeof(T).Name);
            return found[0];
        }

        private static Button ButtonNamed(Control root, string text)
        {
            return All<Button>(root).Find(button => button.Text == text)
                ?? throw new InvalidOperationException("В окне нет кнопки «" + text + "»");
        }

        private static string AllText(Control root)
        {
            string text = root is Label || root is GroupBox ? root.Text + "\n" : "";
            foreach (Control child in root.Controls) text += AllText(child);
            return text;
        }

        private static List<string> ProfileNames(ListBox list)
        {
            var names = new List<string>();
            foreach (object item in list.Items) names.Add((string)item);
            return names;
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
            OnShownForm(form =>
            {
                Assert.Equal(new[] { "Ethernet", "USB Ethernet", "Беспроводная сеть" }, AdapterNames(Find<ListView>(form)));
            });
        }

        [Fact]
        public void Virtual_adapters_appear_on_request()
        {
            OnShownForm(form =>
            {
                Find<CheckBox>(form).Checked = true;

                Assert.Contains("vEthernet (Default Switch)", AdapterNames(Find<ListView>(form)));
                Assert.Equal(4, Find<ListView>(form).Items.Count);
            });
        }

        [Fact]
        public void Adapter_row_shows_state_address_and_mode()
        {
            OnShownForm(form =>
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
        public void The_only_connected_wired_adapter_is_selected_and_its_settings_are_ready_for_editing()
        {
            OnShownForm(form =>
            {
                ListView list = Find<ListView>(form);

                Assert.Equal("Ethernet", list.SelectedItems[0].Text);
                Assert.Contains("IPv4 — Ethernet", AllText(form));
                Assert.True(Find<Ipv4Editor>(form).Value.SameAs(FakeSystem.Static("10.68.12.37", "10.68.12.1", "10.0.68.10")));
                Assert.Equal("Выключить", ButtonNamed(form, "Выключить").Text);
            });
        }

        [Fact]
        public void Selecting_a_disabled_adapter_offers_to_enable_it()
        {
            OnShownForm(form =>
            {
                ListView list = Find<ListView>(form);
                list.Items[1].Selected = true;
                Application.DoEvents();

                Assert.Contains("IPv4 — USB Ethernet", AllText(form));
                Assert.NotNull(ButtonNamed(form, "Включить"));
            });
        }

        [Fact]
        public void Switching_the_adapter_off_takes_one_click_and_no_confirmation()
        {
            OnShownForm(form =>
            {
                ButtonNamed(form, "Выключить").PerformClick();

                Assert.Equal(new[] { "adapter Ethernet off" }, _system.Calls);
                Assert.Equal("выключен", Find<ListView>(form).Items[0].SubItems[1].Text);
                Assert.NotNull(ButtonNamed(form, "Включить"));
            });
        }

        [Fact]
        public void Current_proxy_is_shown_and_loaded_into_the_editor()
        {
            OnShownForm(form =>
            {
                string text = AllText(form);

                Assert.Contains(@"OFFICE\ivanov (инструмент запущен от имени другой учётной записи)", text);
                Assert.Contains("10.0.68.52:3128; исключения: *.corp.local; не для локальных адресов", text);
                Assert.Contains("Системный прокси и WinHTTP-прокси различаются.", text);
                Assert.DoesNotContain("групповой политикой", text);
                Assert.True(Find<ProxyEditor>(form).Value.SameAs(_system.SystemProxy.Settings));
            });
        }

        [Fact]
        public void Policy_managed_proxy_is_flagged()
        {
            _system.Policy = new ProxyPolicy { PerMachine = true };

            OnShownForm(form => Assert.Contains("Прокси на этом компьютере задаётся групповой политикой.", AllText(form)));
        }

        [Fact]
        public void Failure_to_read_adapters_is_shown_and_proxy_is_still_read()
        {
            _system.AdaptersFailure = new InvalidOperationException("служба WMI остановлена");

            OnShownForm(form =>
            {
                Assert.Empty(Find<ListView>(form).Items);
                Assert.Contains("адаптеры — служба WMI остановлена", AllText(form));
                Assert.Contains("10.0.68.52:3128", AllText(form));
                Assert.Contains("IPv4 — адаптер не выбран", AllText(form));
            });
        }

        [Fact]
        public void Profiles_are_listed_in_stored_order_with_details_of_the_selected_one()
        {
            _store.Profiles.Add(new Profile { Name = "Авто, без прокси" });
            _store.Profiles.Add(new Profile { Name = "Подсеть 10.68.12.0/24", Ipv4 = FakeSystem.Static("10.68.12.", "10.68.12.1"), AskAddress = true });

            OnShownForm(form =>
            {
                ListBox list = Find<ListBox>(form);

                Assert.Equal(new[] { "Авто, без прокси", "Подсеть 10.68.12.0/24" }, ProfileNames(list));
                Assert.Equal(0, list.SelectedIndex);
                Assert.Contains("Адрес: автоматически", AllText(form));

                list.SelectedIndex = 1;
                Assert.Contains("10.68.12./24 (уточняется)", AllText(form));
            });
        }

        [Fact]
        public void Without_profiles_there_is_nothing_to_apply_or_edit_but_one_can_be_created()
        {
            OnShownForm(form =>
            {
                Assert.False(ButtonNamed(form, "Применить").Enabled);
                Assert.False(ButtonNamed(form, "Изменить…").Enabled);
                Assert.False(ButtonNamed(form, "Удалить").Enabled);
                Assert.True(ButtonNamed(form, "Создать…").Enabled);
                Assert.True(ButtonNamed(form, "Из текущих…").Enabled);
            });
        }

        [Fact]
        public void Moving_a_profile_changes_the_order_and_saves_it()
        {
            _store.Profiles.Add(new Profile { Name = "Первый" });
            _store.Profiles.Add(new Profile { Name = "Второй" });

            OnShownForm(form =>
            {
                ButtonNamed(form, "▼").PerformClick();

                Assert.Equal(new[] { "Второй", "Первый" }, ProfileNames(Find<ListBox>(form)));
                Assert.Equal(1, Find<ListBox>(form).SelectedIndex);
            });

            var saved = new ProfileStore(_folder);
            saved.Load();
            Assert.Equal(new[] { "Второй", "Первый" }, saved.Profiles.ConvertAll(profile => profile.Name));
        }

        [Fact]
        public void Rollback_is_offered_only_after_something_was_applied()
        {
            OnShownForm(form => Assert.False(ButtonNamed(form, "Откат").Enabled));
        }

        [Fact]
        public void Window_can_be_rendered()
        {
            _store.Profiles.Add(new Profile { Name = "Авто, без прокси" });
            _store.Profiles.Add(new Profile
            {
                Name = "Подсеть 10.68.12.0/24, прокси",
                Ipv4 = FakeSystem.Static("10.68.12.", "10.68.12.1", "95.167.167.95", "95.167.167.96"),
                AskAddress = true,
                Proxy = ProxySettings.FromRaw(true, "10.0.68.52:3128", "<local>")
            });

            OnShownForm(form =>
            {
                Find<ListBox>(form).SelectedIndex = 1;
                Application.DoEvents();
                Save(form, "main-form.png");

                using (var dialog = new ProfileForm("Изменение профиля", _store.Profiles[1].Clone(), name => false))
                {
                    dialog.Show();
                    Application.DoEvents();
                    Save(dialog, "profile-form.png");
                }

                Adapter adapter = _system.Adapters[2];
                ChangeRequest request = ChangeRequest.For("Профиль «Подсеть 10.68.12.0/24, прокси»", adapter, _store.Profiles[1].Ipv4, _store.Profiles[1].Proxy, _system.ReadProxy());
                request.Ipv4!.Address = ProfileRules.SuggestAddress(_store.Profiles[1].Ipv4, adapter);
                using (var dialog = new ConfirmForm(request, _system.ReadProxy(), "10.68.12.", "Применить"))
                {
                    dialog.Show();
                    Application.DoEvents();
                    Save(dialog, "confirm-form.png");
                }
            });
        }

        // Снимки остаются в каталоге сборки тестов, чтобы окна можно было посмотреть глазами
        private static void Save(Form form, string file)
        {
            using (var image = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file), ImageFormat.Png);
            }
        }
    }
}
