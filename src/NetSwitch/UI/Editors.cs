using System;
using System.Windows.Forms;
using NetSwitch.Model;
using static NetSwitch.UI.Fields;

namespace NetSwitch.UI
{
    /// <summary>Поля настроек IPv4. Используется и в ручной правке, и в окне профиля.</summary>
    internal sealed class Ipv4Editor : UserControl
    {
        private const string DefaultMask = "255.255.255.0";

        private readonly RadioButton _autoAddress = Radio("Получать адрес автоматически");
        private readonly RadioButton _manualAddress = Radio("Задать адрес вручную:");
        private readonly Label _addressLabel = Caption("Адрес:");
        private readonly TextBox _address = Box();
        private readonly TextBox _mask = Box();
        private readonly TextBox _gateway = Box();
        private readonly CheckBox _askAddress = new CheckBox { Text = "Уточнять адрес при применении", AutoSize = true, Visible = false };
        private readonly RadioButton _autoDns = Radio("Получать DNS автоматически");
        private readonly RadioButton _manualDns = Radio("Задать DNS вручную:");
        private readonly TextBox _dns1 = Box();
        private readonly TextBox _dns2 = Box();

        public Ipv4Editor()
        {
            Dock = DockStyle.Top;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // Переключатели одной группы должны лежать в общем контейнере
            Span(table, Group(_autoAddress, _manualAddress));
            Row(table, _addressLabel, _address);
            Row(table, Caption("Маска:"), _mask);
            Row(table, Caption("Шлюз:"), _gateway);
            Span(table, _askAddress);
            Span(table, Group(_autoDns, _manualDns));
            Row(table, Caption("DNS 1:"), _dns1);
            Row(table, Caption("DNS 2:"), _dns2);
            Controls.Add(table);

            _autoAddress.Checked = true;
            _autoDns.Checked = true;
            _manualAddress.CheckedChanged += delegate
            {
                if (_manualAddress.Checked && _mask.Text.Trim().Length == 0) _mask.Text = DefaultMask;
                UpdateState();
            };
            _manualDns.CheckedChanged += delegate { UpdateState(); };
            _askAddress.CheckedChanged += delegate { UpdateState(); };
            UpdateState();
        }

        /// <summary>Флажок уточняемого адреса есть только у профиля.</summary>
        public bool ShowAskAddress
        {
            get { return _askAddress.Visible; }
            set { _askAddress.Visible = value; }
        }

        public bool AskAddress
        {
            get { return _manualAddress.Checked && _askAddress.Checked; }
            set { _askAddress.Checked = value; }
        }

        public Ipv4Settings Value
        {
            get
            {
                var settings = new Ipv4Settings
                {
                    AutoAddress = _autoAddress.Checked,
                    Address = _address.Text.Trim(),
                    Mask = _mask.Text.Trim(),
                    Gateway = _gateway.Text.Trim(),
                    AutoDns = _autoDns.Checked
                };
                if (!settings.AutoDns)
                {
                    foreach (TextBox box in new[] { _dns1, _dns2 })
                    {
                        if (box.Text.Trim().Length > 0) settings.Dns.Add(box.Text.Trim());
                    }
                }
                return settings;
            }
            set
            {
                _address.Text = value.Address;
                _mask.Text = value.Mask;
                _gateway.Text = value.Gateway;
                _dns1.Text = value.Dns.Count > 0 ? value.Dns[0] : "";
                _dns2.Text = value.Dns.Count > 1 ? value.Dns[1] : "";

                _autoAddress.Checked = value.AutoAddress;
                _manualAddress.Checked = !value.AutoAddress;
                _autoDns.Checked = value.AutoAddress && value.AutoDns;
                _manualDns.Checked = !_autoDns.Checked;
                UpdateState();
            }
        }

        private void UpdateState()
        {
            bool manual = _manualAddress.Checked;

            // При адресе вручную Windows не получает DNS автоматически
            if (manual && !_manualDns.Checked) _manualDns.Checked = true;
            _autoDns.Enabled = !manual;

            _address.Enabled = _mask.Enabled = _gateway.Enabled = _askAddress.Enabled = manual;
            _dns1.Enabled = _dns2.Enabled = _manualDns.Checked;
            _addressLabel.Text = AskAddress ? "Начало адреса:" : "Адрес:";
        }
    }

    /// <summary>Заготовки элементов и строк формы, одинаковые во всех окнах.</summary>
    internal static class Fields
    {
        internal static RadioButton Radio(string text)
        {
            return new RadioButton { Text = text, AutoSize = true, Margin = new Padding(3, 1, 3, 1) };
        }

        internal static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 0, 3, 0) };
        }

        internal static TextBox Box()
        {
            return new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(3, 2, 3, 2) };
        }

        internal static Control Group(params Control[] controls)
        {
            var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
            panel.Controls.AddRange(controls);
            return panel;
        }

        internal static void Row(TableLayoutPanel table, Control caption, Control field)
        {
            int row = table.RowCount;
            table.Controls.Add(caption, 0, row);
            table.Controls.Add(field, 1, row);
            table.RowCount = row + 1;
        }

        internal static void Span(TableLayoutPanel table, Control control)
        {
            int row = table.RowCount;
            table.Controls.Add(control, 0, row);
            table.SetColumnSpan(control, 2);
            table.RowCount = row + 1;
        }

        internal static Button ActionButton(string text, EventHandler onClick)
        {
            var button = new Button { Text = text, AutoSize = true, Padding = new Padding(6, 1, 6, 1), Margin = new Padding(3) };
            button.Click += onClick;
            return button;
        }

        internal static TableLayoutPanel Table()
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return table;
        }
    }

    /// <summary>Поля настроек прокси. Используется и в ручной правке, и в окне профиля.</summary>
    internal sealed class ProxyEditor : UserControl
    {
        private readonly CheckBox _enabled = new CheckBox { Text = "Использовать прокси-сервер", AutoSize = true };
        private readonly TextBox _server = Box();
        private readonly TextBox _exceptions = Box();
        private readonly CheckBox _bypassLocal = new CheckBox { Text = "Не использовать для локальных адресов", AutoSize = true };

        public ProxyEditor()
        {
            Dock = DockStyle.Top;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            Span(table, _enabled);
            Row(table, Caption("Сервер (адрес:порт):"), _server);
            Row(table, Caption("Исключения (через ;):"), _exceptions);
            Span(table, _bypassLocal);
            Controls.Add(table);

            _enabled.CheckedChanged += delegate { UpdateState(); };
            UpdateState();
        }

        public ProxySettings Value
        {
            get
            {
                ProxySettings settings = ProxySettings.FromRaw(_enabled.Checked, _server.Text, _exceptions.Text);
                settings.BypassLocal = settings.BypassLocal || _bypassLocal.Checked;
                return settings;
            }
            set
            {
                _server.Text = value.Server;
                _exceptions.Text = string.Join("; ", value.Exceptions);
                _bypassLocal.Checked = value.BypassLocal;
                _enabled.Checked = value.Enabled;
                UpdateState();
            }
        }

        private void UpdateState()
        {
            _server.Enabled = _exceptions.Enabled = _bypassLocal.Enabled = _enabled.Checked;
        }
    }
}
