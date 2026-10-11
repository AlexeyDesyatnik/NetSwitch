using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using NetSwitch.Diagnostics;
using NetSwitch.Model;

namespace NetSwitch.UI
{
    internal sealed class MainForm : Form
    {
        private readonly INetworkSystem _system;
        private readonly Func<Report> _buildReport;

        private readonly ListView _adapters = new ListView();
        private readonly ListViewGroup _physical = new ListViewGroup("Физические");
        private readonly ListViewGroup _virtual = new ListViewGroup("Виртуальные");
        private readonly CheckBox _showVirtual = new CheckBox();
        private readonly Label _user = ValueLabel();
        private readonly Label _systemProxy = ValueLabel();
        private readonly Label _winHttp = ValueLabel();
        private readonly Label _warning = ValueLabel();
        private readonly Label _status = ValueLabel();

        private IList<Adapter> _all = new List<Adapter>();

        public MainForm(INetworkSystem system, Func<Report> buildReport)
        {
            _system = system;
            _buildReport = buildReport;

            Text = "NetSwitch " + AppInfo.Version + " — " + AppInfo.Edition;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(960, 520);
            MinimumSize = new Size(640, 400);
            StartPosition = FormStartPosition.CenterScreen;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(BuildHeader(), 0, 0);
            layout.Controls.Add(BuildAdapterList(), 0, 1);
            layout.Controls.Add(BuildProxyBox(), 0, 2);
            layout.Controls.Add(BuildButtons(), 0, 3);
            Controls.Add(layout);

            Load += delegate { Reload(); };
        }

        private Control BuildHeader()
        {
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var title = new Label { Text = "Адаптеры", AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font(Font, FontStyle.Bold) };

            _showVirtual.Text = "Показать виртуальные";
            _showVirtual.AutoSize = true;
            _showVirtual.Anchor = AnchorStyles.Right;
            _showVirtual.CheckedChanged += delegate { ShowAdapters(); };

            header.Controls.Add(title, 0, 0);
            header.Controls.Add(_showVirtual, 1, 0);
            return header;
        }

        private Control BuildAdapterList()
        {
            _adapters.Dock = DockStyle.Fill;
            _adapters.View = View.Details;
            _adapters.FullRowSelect = true;
            _adapters.HideSelection = false;
            _adapters.MultiSelect = false;
            _adapters.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _adapters.ShowItemToolTips = true;
            _adapters.Groups.Add(_physical);
            _adapters.Groups.Add(_virtual);
            foreach (string column in new[] { "Адаптер", "Состояние", "Адрес", "Получение", "Шлюз", "DNS", "Устройство" })
            {
                _adapters.Columns.Add(column);
            }
            return _adapters;
        }

        private Control BuildProxyBox()
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(4) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            AddRow(table, "Пользователь:", _user);
            AddRow(table, "Системный прокси:", _systemProxy);
            AddRow(table, "WinHTTP-прокси:", _winHttp);

            _warning.ForeColor = Color.Firebrick;
            _warning.Visible = false;
            table.Controls.Add(_warning, 0, table.RowCount);
            table.SetColumnSpan(_warning, 2);
            table.RowCount++;

            var box = new GroupBox { Text = "Прокси", Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            box.Controls.Add(table);
            return box;
        }

        private static void AddRow(TableLayoutPanel table, string caption, Label value)
        {
            int row = table.RowCount;
            table.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(3) }, 0, row);
            table.Controls.Add(value, 1, row);
            table.RowCount = row + 1;
        }

        private Control BuildButtons()
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };

            var refresh = new Button { Text = "Обновить", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
            refresh.Click += delegate { Reload(); };

            var save = new Button { Text = "Сохранить отчёт", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
            save.Click += delegate { SaveReport(); };

            _status.Anchor = AnchorStyles.Left;
            _status.ForeColor = SystemColors.GrayText;

            panel.Controls.Add(refresh);
            panel.Controls.Add(save);
            panel.Controls.Add(_status);
            return panel;
        }

        private static Label ValueLabel()
        {
            return new Label { AutoSize = true, Margin = new Padding(3) };
        }

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
                ShowAdapters();

                try
                {
                    ShowProxy(_system.ReadProxy());
                }
                catch (Exception ex)
                {
                    _user.Text = _systemProxy.Text = _winHttp.Text = "—";
                    _warning.Visible = false;
                    failures.Add("прокси — " + ex.Message);
                }

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

            _adapters.BeginUpdate();
            try
            {
                _adapters.Items.Clear();
                foreach (Adapter adapter in visible)
                {
                    var item = new ListViewItem(adapter.Name, adapter.IsPhysical ? _physical : _virtual);
                    item.SubItems.Add(Describe.State(adapter.State));
                    item.SubItems.Add(Describe.Address(adapter));
                    item.SubItems.Add(Describe.Mode(adapter.Configured));
                    item.SubItems.Add(Describe.Gateway(adapter));
                    item.SubItems.Add(Describe.Dns(adapter));
                    item.SubItems.Add(adapter.Description);
                    if (adapter.State == AdapterState.Disabled || adapter.State == AdapterState.Unavailable)
                    {
                        item.ForeColor = SystemColors.GrayText;
                    }
                    _adapters.Items.Add(item);
                }

                // Ширина по содержимому, но не уже заголовка
                foreach (ColumnHeader column in _adapters.Columns) column.Width = -2;
            }
            finally
            {
                _adapters.EndUpdate();
            }
        }

        private void ShowProxy(ProxyState state)
        {
            _user.Text = state.User.IsProcessOwner
                ? state.User.Name
                : state.User.Name + " (инструмент запущен от имени другой учётной записи)";
            _systemProxy.Text = Describe.SystemProxy(state.System);
            _winHttp.Text = Describe.Proxy(state.WinHttp);

            var warnings = new List<string>();
            if (state.Policy.IsManaged) warnings.Add("Прокси на этом компьютере задаётся групповой политикой.");
            if (!state.System.Settings.SameAs(state.WinHttp)) warnings.Add("Системный прокси и WinHTTP-прокси различаются.");

            _warning.Text = string.Join(" ", warnings);
            _warning.Visible = warnings.Count > 0;
        }

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
