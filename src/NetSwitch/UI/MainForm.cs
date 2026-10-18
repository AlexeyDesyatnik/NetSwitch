using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using NetSwitch.Diagnostics;
using NetSwitch.Model;
using NetSwitch.Profiles;
using static NetSwitch.UI.Fields;

namespace NetSwitch.UI
{
    internal sealed class MainForm : Form
    {
        // Windows нужно время, чтобы проверить новый адрес на конфликт, прежде чем состояние можно перечитать
        private const int SettleMs = 1500;
        private const string ProxyNow = "Прокси сейчас";
        private const string ManualIpv4 = "Ручная правка IPv4 — ";

        private readonly INetworkSystem _system;
        private readonly Applier _applier;
        private readonly ProfileStore _store;
        private readonly Func<Report> _buildReport;
        private readonly IPrompts _prompts;

        private readonly ColoredListView _adapters = new ColoredListView();
        private readonly ListViewGroup _physical = new ListViewGroup("Физические");
        private readonly ListViewGroup _virtual = new ListViewGroup("Виртуальные");
        private readonly CheckBox _showVirtual = new CheckBox { Text = "Показать виртуальные", AutoSize = true, Anchor = AnchorStyles.Right };
        private readonly Button _toggle;

        private readonly ListBox _profiles = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly Label _profileDetails = new Label { Dock = DockStyle.Fill, AutoSize = false, Height = 64, AutoEllipsis = true, ForeColor = SystemColors.GrayText };
        private readonly List<Button> _profileButtons = new List<Button>();
        private readonly List<Button> _storeButtons = new List<Button>();
        private readonly List<Button> _commands = new List<Button>();

        private readonly GroupBox _ipv4Box = new GroupBox { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };
        private readonly Ipv4Editor _ipv4 = new Ipv4Editor();
        private readonly ProxyEditor _proxyEditor = new ProxyEditor();
        private readonly Button _applyIpv4;
        private readonly Button _applyProxy;

        private readonly GroupBox _proxyBox = new GroupBox { Text = ProxyNow, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        private readonly Label _systemProxy = ValueLabel();
        private readonly Label _winHttp = ValueLabel();
        private readonly Label _warning = ValueLabel();
        private readonly Label _status = ValueLabel();
        private readonly Button _rollback;

        private IList<Adapter> _all = new List<Adapter>();
        private ProxyState? _proxy;
        private string? _selectedId;
        private bool _choseDefault;
        private bool _filling;
        private bool _busy;

        public MainForm(INetworkSystem system, Applier applier, ProfileStore store, Func<Report> buildReport, IPrompts prompts)
        {
            _system = system;
            _applier = applier;
            _store = store;
            _buildReport = buildReport;
            _prompts = prompts;

            _toggle = Command("Выключить", delegate { ToggleAdapter(); });
            _applyIpv4 = Command("Применить IPv4", delegate { ApplyManualIpv4(); });
            _applyProxy = Command("Применить прокси", delegate { ApplyManualProxy(); });
            _rollback = Command("Откат", delegate { Rollback(); });

            Text = "NetSwitch " + AppInfo.Version;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1000, 680);
            MinimumSize = new Size(860, 600);
            StartPosition = FormStartPosition.CenterScreen;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(layout, BuildHeader(), false);
            AddRow(layout, BuildAdapterList(), true);
            AddRow(layout, BuildAdapterButtons(), false);
            AddRow(layout, BuildMiddle(), false);
            AddRow(layout, BuildProxyBox(), false);
            AddRow(layout, BuildBottom(), false);
            Controls.Add(layout);

            Load += delegate
            {
                ShowProfiles(0);
                Reload();
            };
            // Окно, закрытое посреди применения, не покажет, в каком состоянии остались настройки
            FormClosing += (sender, e) => { if (_busy) e.Cancel = true; };
            Shown += delegate
            {
                if (_store.LoadError != null)
                {
                    MessageBox.Show(this, _store.LoadError + "\n\nПрофили недоступны, пока файл не исправлен или не удалён:\n" + _store.FilePath,
                        "NetSwitch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
        }

        // ---------- Построение окна ----------

        private static void AddRow(TableLayoutPanel layout, Control control, bool fill)
        {
            control.Dock = DockStyle.Fill;
            layout.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(control, 0, layout.RowStyles.Count - 1);
        }

        private Control BuildHeader()
        {
            var header = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 1 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _showVirtual.CheckedChanged += delegate
            {
                ShowAdapters();
                AdapterSelected();
            };

            header.Controls.Add(new Label { Text = "Адаптеры", AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
            header.Controls.Add(_showVirtual, 1, 0);
            return header;
        }

        private Control BuildAdapterList()
        {
            _adapters.View = View.Details;
            _adapters.FullRowSelect = true;
            _adapters.HideSelection = false;
            _adapters.MultiSelect = false;
            _adapters.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _adapters.ShowItemToolTips = true;
            _adapters.MinimumSize = new Size(0, 90);
            _adapters.Groups.Add(_physical);
            _adapters.Groups.Add(_virtual);
            foreach (string column in new[] { "Адаптер", "Состояние", "Адрес", "Получение", "Шлюз", "DNS", "Устройство" })
            {
                _adapters.Columns.Add(column);
            }
            _adapters.SelectedIndexChanged += delegate { if (!_filling) AdapterSelected(); };
            return _adapters;
        }

        private Control BuildAdapterButtons()
        {
            var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            panel.Controls.Add(_toggle);
            panel.Controls.Add(Command("Обновить", delegate { Reload(); }));
            return panel;
        }

        private Control BuildMiddle()
        {
            _ipv4Box.AutoSize = true;
            _ipv4Box.Controls.Add(Stack(_ipv4, _applyIpv4));

            var proxyBox = new GroupBox { Text = "Ручная правка прокси", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
            proxyBox.Controls.Add(Stack(_proxyEditor, _applyProxy));

            Button save = Command("Сохранить правку как профиль…", delegate { CreateProfile(ProfileFromEditors(), "Профиль из ручной правки"); });
            save.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _storeButtons.Add(save);

            // Блок IPv4 выше блока прокси; кнопка сохранения занимает место под прокси
            var middle = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, RowCount = 2 };
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            middle.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            middle.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Control profiles = BuildProfiles();
            middle.Controls.Add(profiles, 0, 0);
            middle.SetRowSpan(profiles, 2);
            middle.Controls.Add(_ipv4Box, 1, 0);
            middle.SetRowSpan(_ipv4Box, 2);
            middle.Controls.Add(proxyBox, 2, 0);
            middle.Controls.Add(save, 2, 1);
            return middle;
        }

        private Control BuildProfiles()
        {
            _profiles.SelectedIndexChanged += delegate { ProfileSelected(); };
            _profiles.DoubleClick += delegate { ApplyProfile(); };

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            Button apply = ProfileButton("Применить", delegate { ApplyProfile(); }, false);
            Button create = Command("Создать…", delegate { CreateProfile(new Profile(), "Новый профиль"); });
            Button current = Command("Из текущих…", delegate { CreateProfile(ProfileFromMachine(), "Профиль из текущих настроек"); });
            _storeButtons.Add(create);
            _storeButtons.Add(current);
            foreach (Button button in new[]
            {
                apply, create, current,
                ProfileButton("Изменить…", delegate { EditProfile(); }, true),
                ProfileButton("Удалить", delegate { DeleteProfile(); }, true)
            })
            {
                button.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                buttons.Controls.Add(button);
            }

            var order = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            foreach (Button arrow in new[] { ProfileButton("▲", delegate { MoveProfile(-1); }, true), ProfileButton("▼", delegate { MoveProfile(1); }, true) })
            {
                arrow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                order.Controls.Add(arrow);
            }
            buttons.Controls.Add(order);

            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(_profiles, 0, 0);
            table.Controls.Add(buttons, 1, 0);
            table.Controls.Add(_profileDetails, 0, 1);
            table.SetColumnSpan(_profileDetails, 2);

            var box = new GroupBox { Text = "Профили", Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };
            box.Controls.Add(table);
            return box;
        }

        /// <param name="changesStore">Кнопка меняет файл профилей и недоступна, пока он не прочитан.</param>
        private Button ProfileButton(string text, EventHandler onClick, bool changesStore)
        {
            Button button = Command(text, onClick);
            _profileButtons.Add(button);
            if (changesStore) _storeButtons.Add(button);
            return button;
        }

        // Пока идёт действие, недоступны все кнопки окна: и изменения, и чтение посреди изменения
        private Button Command(string text, EventHandler onClick)
        {
            Button button = ActionButton(text, onClick);
            _commands.Add(button);
            return button;
        }

        private static Control Stack(Control editor, Button button)
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            button.Anchor = AnchorStyles.Left;
            button.Margin = new Padding(3, 8, 3, 3);
            table.Controls.Add(editor, 0, 0);
            table.Controls.Add(button, 0, 1);
            return table;
        }

        private Control BuildProxyBox()
        {
            TableLayoutPanel table = Table();
            table.Padding = new Padding(4, 0, 4, 0);
            Row(table, Caption("Системный прокси:"), _systemProxy);
            Row(table, Caption("WinHTTP-прокси:"), _winHttp);

            _warning.ForeColor = Color.Firebrick;
            _warning.Visible = false;
            Span(table, _warning);

            _proxyBox.Controls.Add(table);
            return _proxyBox;
        }

        private Control BuildBottom()
        {
            var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            panel.Controls.Add(_rollback);
            panel.Controls.Add(Command("Сохранить отчёт", delegate { SaveReport(); }));

            _status.Anchor = AnchorStyles.Left;
            _status.ForeColor = SystemColors.GrayText;
            panel.Controls.Add(_status);
            return panel;
        }

        private static Label ValueLabel()
        {
            return new Label { AutoSize = true, Margin = new Padding(3) };
        }

        // ---------- Чтение и показ состояния ----------

        private void Reload()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                var failures = new List<string>();

                try
                {
                    _all = _system.ReadAdapters();
                }
                catch (Exception ex)
                {
                    _all = new List<Adapter>();
                    failures.Add("адаптеры — " + ex.Message);
                }

                if (!_choseDefault && _all.Count > 0)
                {
                    Adapter? preferred = AdapterChoice.Default(_all);
                    _selectedId = preferred != null ? preferred.Id : null;
                    _choseDefault = true;
                }
                ShowAdapters();
                AdapterSelected();

                try
                {
                    _proxy = _system.ReadProxy();
                    ShowProxy(_proxy);
                    _proxyEditor.Value = _proxy.System.Settings;
                }
                catch (Exception ex)
                {
                    _proxy = null;
                    _proxyBox.Text = ProxyNow;
                    _systemProxy.Text = _winHttp.Text = "—";
                    _warning.Visible = false;
                    failures.Add("прокси — " + ex.Message);
                }

                _rollback.Enabled = _applier.CanRollback;
                _status.Text = failures.Count == 0
                    ? "Обновлено в " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                    : "Не удалось прочитать: " + string.Join("; ", failures);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void ShowAdapters()
        {
            var visible = new List<Adapter>();
            foreach (Adapter adapter in _all)
            {
                if (adapter.IsPhysical || _showVirtual.Checked) visible.Add(adapter);
            }
            visible.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

            _filling = true;
            _adapters.BeginUpdate();
            try
            {
                _adapters.Items.Clear();
                foreach (Adapter adapter in visible)
                {
                    var item = new ListViewItem(adapter.Name, adapter.IsPhysical ? _physical : _virtual) { Tag = adapter };
                    item.SubItems.Add(Describe.State(adapter));
                    item.SubItems.Add(Describe.Address(adapter));
                    item.SubItems.Add(Describe.Mode(adapter.Configured));
                    item.SubItems.Add(Describe.Gateway(adapter));
                    item.SubItems.Add(Describe.Dns(adapter));
                    item.SubItems.Add(adapter.Description);

                    if (adapter.Active.HasConflict)
                        item.ForeColor = Color.Firebrick;
                    else if (adapter.State == AdapterState.Disabled || adapter.State == AdapterState.Unavailable)
                        item.ForeColor = SystemColors.GrayText;

                    item.Selected = string.Equals(adapter.Id, _selectedId, StringComparison.OrdinalIgnoreCase);
                    _adapters.Items.Add(item);
                }

                // Ширина по содержимому, но не уже заголовка
                foreach (ColumnHeader column in _adapters.Columns) column.Width = -2;
            }
            finally
            {
                _adapters.EndUpdate();
                _filling = false;
            }
        }

        private Adapter? SelectedAdapter
        {
            get { return _adapters.SelectedItems.Count > 0 ? (Adapter)_adapters.SelectedItems[0].Tag : null; }
        }

        /// <summary>Тот же адаптер в последнем прочитанном состоянии; null, если его больше нет.</summary>
        private Adapter? Listed(Adapter adapter)
        {
            foreach (Adapter candidate in _all)
            {
                if (string.Equals(candidate.Id, adapter.Id, StringComparison.OrdinalIgnoreCase)) return candidate;
            }
            return null;
        }

        private void AdapterSelected()
        {
            Adapter? adapter = SelectedAdapter;
            _selectedId = adapter != null ? adapter.Id : null;

            _toggle.Text = adapter != null && adapter.State == AdapterState.Disabled ? "Включить" : "Выключить";
            EnableAdapterButtons();

            _ipv4Box.Text = ManualIpv4 + (adapter != null ? adapter.Name : "адаптер не выбран");
            _ipv4.Enabled = adapter != null;
            _ipv4.Value = adapter != null ? adapter.Configured : new Ipv4Settings();
        }

        private void EnableAdapterButtons()
        {
            Adapter? adapter = SelectedAdapter;
            _toggle.Enabled = !_busy && adapter != null && adapter.State != AdapterState.Unavailable;
            _applyIpv4.Enabled = !_busy && adapter != null;
        }

        private void ShowProxy(ProxyState state)
        {
            _proxyBox.Text = ProxyNow + " — " + (state.User.IsProcessOwner
                ? state.User.Name
                : state.User.Name + " (инструмент запущен от имени другой учётной записи)");
            _systemProxy.Text = Describe.SystemProxy(state.System);
            _winHttp.Text = Describe.Proxy(state.WinHttp);

            var warnings = new List<string>();
            if (state.Policy.IsManaged) warnings.Add("Прокси на этом компьютере задаётся групповой политикой.");
            if (!state.System.Settings.SameAs(state.WinHttp)) warnings.Add("Системный прокси и WinHTTP-прокси различаются.");

            _warning.Text = string.Join(" ", warnings);
            _warning.Visible = warnings.Count > 0;
        }

        // ---------- Профили ----------

        private Profile? SelectedProfile
        {
            get { return _profiles.SelectedIndex >= 0 ? _store.Profiles[_profiles.SelectedIndex] : null; }
        }

        private void ShowProfiles(int select)
        {
            _profiles.BeginUpdate();
            _profiles.Items.Clear();
            foreach (Profile profile in _store.Profiles) _profiles.Items.Add(profile.Name);
            _profiles.EndUpdate();

            if (_profiles.Items.Count > 0) _profiles.SelectedIndex = Math.Max(0, Math.Min(select, _profiles.Items.Count - 1));
            ProfileSelected();
        }

        private void ProfileSelected()
        {
            Profile? profile = SelectedProfile;
            _profileDetails.Text = profile != null ? Describe.Profile(profile) : "";

            foreach (Button button in _profileButtons) button.Enabled = !_busy && profile != null;
            if (_store.LoadError != null)
            {
                foreach (Button button in _storeButtons) button.Enabled = false;
            }
        }

        private void CreateProfile(Profile draft, string title)
        {
            using (var dialog = new ProfileForm(title, draft, name => _store.Find(name) != null))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                _store.Profiles.Add(dialog.Result);
                SaveProfiles(_store.Profiles.Count - 1);
            }
        }

        private void EditProfile()
        {
            Profile? profile = SelectedProfile;
            if (profile == null) return;

            using (var dialog = new ProfileForm("Изменение профиля", profile.Clone(), name =>
            {
                Profile? other = _store.Find(name);
                return other != null && other != profile;
            }))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                int index = _store.Profiles.IndexOf(profile);
                _store.Profiles[index] = dialog.Result;
                SaveProfiles(index);
            }
        }

        private void DeleteProfile()
        {
            Profile? profile = SelectedProfile;
            if (profile == null) return;

            DialogResult answer = MessageBox.Show(this, "Удалить профиль «" + profile.Name + "»?", "NetSwitch",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            int index = _store.Profiles.IndexOf(profile);
            _store.Profiles.RemoveAt(index);
            SaveProfiles(index);
        }

        private void MoveProfile(int direction)
        {
            int index = _profiles.SelectedIndex;
            int target = index + direction;
            if (index < 0 || target < 0 || target >= _store.Profiles.Count) return;

            Profile moved = _store.Profiles[index];
            _store.Profiles[index] = _store.Profiles[target];
            _store.Profiles[target] = moved;
            SaveProfiles(target);
        }

        private void SaveProfiles(int select)
        {
            try
            {
                _store.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось сохранить профили:\n" + ex.Message, "NetSwitch", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            ShowProfiles(select);
        }

        private Profile ProfileFromMachine()
        {
            Adapter? adapter = SelectedAdapter;
            return new Profile
            {
                Ipv4 = adapter != null ? adapter.Configured.Clone() : new Ipv4Settings(),
                Proxy = _proxy != null ? _proxy.System.Settings.Clone() : new ProxySettings()
            };
        }

        private Profile ProfileFromEditors()
        {
            return new Profile { Ipv4 = _ipv4.Value, Proxy = _proxyEditor.Value };
        }

        // ---------- Применение ----------

        private void ApplyProfile()
        {
            // Двойной щелчок по профилю кнопкой не заблокируешь
            Profile? profile = SelectedProfile;
            if (profile == null || _busy) return;

            Adapter? adapter = SelectedAdapter;
            if (adapter == null)
            {
                _prompts.Tell(this, "Выберите адаптер, к которому применить профиль.", "NetSwitch", MessageBoxIcon.Information);
                return;
            }

            ChangeRequest request = ChangeRequest.For("Профиль «" + profile.Name + "»", adapter, profile.Ipv4, profile.Proxy, _proxy);
            string? stub = profile.AskAddress ? profile.Ipv4.Address : null;
            if (stub != null) request.Ipv4!.Address = ProfileRules.SuggestAddress(profile.Ipv4, adapter);

            Confirm(request, stub);
        }

        private void ApplyManualIpv4()
        {
            Adapter? adapter = SelectedAdapter;
            if (adapter == null) return;

            Ipv4Settings settings = _ipv4.Value;
            if (!Accepted(ProfileRules.CheckIpv4(settings, false))) return;

            Confirm(ChangeRequest.For("Ручная правка IPv4", adapter, settings, null, _proxy), null);
        }

        private void ApplyManualProxy()
        {
            ProxySettings settings = _proxyEditor.Value;
            if (!Accepted(ProfileRules.CheckProxy(settings))) return;

            Confirm(ChangeRequest.For("Ручная правка прокси", null, null, settings, _proxy), null);
        }

        private bool Accepted(List<string> errors)
        {
            if (errors.Count == 0) return true;

            _prompts.Tell(this, string.Join("\n", errors), "NetSwitch", MessageBoxIcon.Warning);
            return false;
        }

        private void Confirm(ChangeRequest request, string? addressStub)
        {
            if (!_prompts.Confirm(this, request, _proxy, addressStub, "Применить")) return;
            Execute(() => _applier.Apply(request), request);
        }

        private void Rollback()
        {
            ChangeRequest? undo = _applier.Undo;
            if (undo == null) return;

            // В подтверждении «было» — это то, что на адаптере сейчас, а не в момент последнего применения
            var shown = new ChangeRequest { Title = undo.Title, Ipv4 = undo.Ipv4, SystemProxy = undo.SystemProxy, WinHttp = undo.WinHttp };
            if (undo.Adapter != null) shown.Adapter = Listed(undo.Adapter) ?? undo.Adapter;

            if (!_prompts.Confirm(this, shown, _proxy, null, "Откатить")) return;
            Execute(() => _applier.Rollback(), undo);
        }

        private void ToggleAdapter()
        {
            Adapter? adapter = SelectedAdapter;
            if (adapter == null) return;

            bool enable = adapter.State == AdapterState.Disabled;
            StepResult? result = null;
            InBackground((enable ? "Включение" : "Выключение") + " адаптера «" + adapter.Name + "»",
                () => result = _applier.SetAdapterEnabled(adapter, enable),
                () =>
                {
                    Reload();
                    if (!result!.Ok) _prompts.Tell(this, result.Error!, "NetSwitch", MessageBoxIcon.Warning);
                });
        }

        /// <summary>
        /// Изменение настроек может идти секундами (включение адаптера — до 15), поэтому выполняется
        /// вне потока окна; <paramref name="done"/> вызывается снова в потоке окна.
        /// </summary>
        private void InBackground(string doing, Action work, Action done)
        {
            SetBusy(true);
            string status = _status.Text;
            _status.Text = "Выполняется: " + doing + "…";

            var thread = new Thread(() =>
            {
                Exception? failure = null;
                try
                {
                    work();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                BeginInvoke((MethodInvoker)delegate
                {
                    SetBusy(false);
                    _status.Text = status;
                    // Непредвиденный сбой уходит в общий обработчик, который предложит сохранить отчёт
                    if (failure != null) throw new InvalidOperationException(failure.Message, failure);
                    done();
                });
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            UseWaitCursor = busy;
            foreach (Button button in _commands) button.Enabled = !busy;
            if (busy) return;

            // Доступность части кнопок зависит от выбора и от того, есть ли что откатывать;
            // правку в редакторах не трогаем — её могли не применить
            EnableAdapterButtons();
            ProfileSelected();
            _rollback.Enabled = _applier.CanRollback;
        }

        private void Execute(Func<IList<StepResult>> action, ChangeRequest request)
        {
            IList<StepResult>? results = null;
            Exception? readFailure = null;
            InBackground(request.Title, () =>
            {
                try
                {
                    results = action();
                }
                catch (Exception ex)
                {
                    readFailure = ex;
                    return;
                }
                if (request.Ipv4 != null) Thread.Sleep(SettleMs);
            }, () =>
            {
                if (readFailure != null)
                {
                    _prompts.Tell(this, "Ничего не изменено: не удалось прочитать прежние настройки.\n" + readFailure.Message, "NetSwitch", MessageBoxIcon.Error);
                    return;
                }

                Reload();
                ShowResults(results!, request);
            });
        }

        private void ShowResults(IList<StepResult> results, ChangeRequest request)
        {
            bool allOk = true;
            var lines = new List<string>();
            foreach (StepResult result in results)
            {
                allOk = allOk && result.Ok;
                lines.Add(result.Ok ? "✔ " + result.Title : "✖ " + result.Title + ":\n    " + result.Error);
            }

            // Пока шло действие, в списке могли выбрать другой адаптер
            Adapter? adapter = request.Adapter != null ? Listed(request.Adapter) : null;
            if (request.Ipv4 != null && adapter != null && adapter.Active.HasConflict)
            {
                allOk = false;
                lines.Add("");
                lines.Add("Windows обнаружила в сети другой компьютер с таким же адресом. Адрес не действует.");
            }
            if (request.SystemProxy != null)
            {
                lines.Add("");
                lines.Add("Уже запущенным программам может понадобиться перезапуск, чтобы подхватить новый прокси.");
            }

            _prompts.Tell(this, string.Join("\n", lines), request.Title, allOk ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        // ---------- Отчёт ----------

        private void SaveReport()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                Report report = _buildReport();
                string path = ReportFile.Save(Application.StartupPath, Environment.MachineName, DateTime.Now, report.Text);

                string message = "Отчёт сохранён:\n" + path;
                if (report.HasErrors) message += "\n\nЧасть сведений собрать не удалось — подробности в отчёте.";
                MessageBox.Show(this, message, "NetSwitch", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось сохранить отчёт:\n" + ex.Message, "NetSwitch", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
    }
}
