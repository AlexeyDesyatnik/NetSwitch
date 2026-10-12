using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using NetSwitch.Model;
using static NetSwitch.UI.Fields;

namespace NetSwitch.UI
{
    /// <summary>Создание и правка профиля.</summary>
    internal sealed class ProfileForm : Form
    {
        private readonly Func<string, bool> _isNameTaken;
        private readonly TextBox _name = Box();
        private readonly Ipv4Editor _ipv4 = new Ipv4Editor { ShowAskAddress = true };
        private readonly ProxyEditor _proxy = new ProxyEditor();

        /// <param name="isNameTaken">Занято ли название другим профилем.</param>
        public ProfileForm(string title, Profile profile, Func<string, bool> isNameTaken)
        {
            _isNameTaken = isNameTaken;
            Result = profile;

            Text = title;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(700, 420);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            TableLayoutPanel name = Table();
            Row(name, Caption("Название:"), _name);

            var parts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            parts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            parts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            parts.Controls.Add(Section("IPv4", _ipv4), 0, 0);
            parts.Controls.Add(Section("Прокси", _proxy), 1, 0);

            Button ok = ActionButton("Сохранить", delegate { Accept(); });
            Button cancel = ActionButton("Отмена", delegate { });
            cancel.DialogResult = DialogResult.Cancel;
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(name, 0, 0);
            layout.Controls.Add(parts, 0, 1);
            layout.Controls.Add(buttons, 0, 2);
            Controls.Add(layout);

            AcceptButton = ok;
            CancelButton = cancel;

            _name.Text = profile.Name;
            _ipv4.Value = profile.Ipv4;
            _ipv4.AskAddress = profile.AskAddress;
            _proxy.Value = profile.Proxy;
        }

        /// <summary>Проверенный профиль; имеет смысл после закрытия окна кнопкой «Сохранить».</summary>
        public Profile Result { get; private set; }

        private static GroupBox Section(string title, Control content)
        {
            var box = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };
            box.Controls.Add(content);
            return box;
        }

        private void Accept()
        {
            var profile = new Profile { Name = _name.Text, Ipv4 = _ipv4.Value, AskAddress = _ipv4.AskAddress, Proxy = _proxy.Value };

            List<string> errors = ProfileRules.Check(profile);
            if (errors.Count == 0 && _isNameTaken(profile.Name)) errors.Add("Профиль с таким названием уже есть.");

            if (errors.Count > 0)
            {
                MessageBox.Show(this, string.Join("\n", errors), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Result = profile;
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>Подтверждение применения: что было и что станет, и поле для уточняемого адреса.</summary>
    internal sealed class ConfirmForm : Form
    {
        private readonly ChangeRequest _request;
        private readonly ProxyState? _current;
        private readonly string? _addressStub;
        private readonly TextBox _address = Fields.Box();
        private readonly TableLayoutPanel _lines = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(2) };

        /// <param name="request">Адрес в нём уже заполнен предложением; окно впишет то, что введёт человек.</param>
        /// <param name="addressStub">Начало адреса из профиля, если адрес уточняемый; иначе null.</param>
        public ConfirmForm(ChangeRequest request, ProxyState? current, string? addressStub, string acceptText)
        {
            _request = request;
            _current = current;
            _addressStub = addressStub;

            Text = "NetSwitch";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(760, 400);
            MinimumSize = new Size(560, 320);
            MaximizeBox = MinimizeBox = ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            string title = request.Title;
            if (request.Adapter != null && request.Ipv4 != null) title += " → адаптер «" + request.Adapter.Name + "»";
            Add(layout, new Label { Text = title, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 3, 3, 8) }, SizeType.AutoSize);

            if (addressStub != null && request.Ipv4 != null)
            {
                TableLayoutPanel ask = Table();
                Row(ask, Caption("Адрес этого компьютера:"), _address);
                Add(layout, ask, SizeType.AutoSize);

                _address.Text = request.Ipv4.Address;
                _address.TextChanged += delegate
                {
                    _request.Ipv4!.Address = _address.Text.Trim();
                    ShowLines();
                };
            }

            // Таблица из надписей, а не список: длинные описания прокси переносятся по строкам и видны целиком
            _lines.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _lines.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            _lines.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            var lines = new Panel { AutoScroll = true, BorderStyle = BorderStyle.FixedSingle, BackColor = SystemColors.Window };
            lines.Controls.Add(_lines);
            Add(layout, lines, SizeType.Percent);

            IList<string> notes = ChangeSummary.Notes(request, current);
            if (notes.Count > 0)
            {
                Add(layout, new Label { Text = string.Join("\n", notes), AutoSize = true, ForeColor = Color.Firebrick, Margin = new Padding(3, 6, 3, 3) }, SizeType.AutoSize);
            }

            Button ok = ActionButton(acceptText, delegate { Accept(); });
            Button cancel = ActionButton("Отмена", delegate { });
            cancel.DialogResult = DialogResult.Cancel;
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            Add(layout, buttons, SizeType.AutoSize);

            Controls.Add(layout);
            AcceptButton = ok;
            CancelButton = cancel;

            Shown += delegate
            {
                ShowLines();
                if (_addressStub != null)
                {
                    // Курсор в конец: обычно остаётся дописать последний октет
                    _address.Focus();
                    _address.SelectionStart = _address.Text.Length;
                }
            };
        }

        private static void Add(TableLayoutPanel layout, Control control, SizeType size)
        {
            if (!(control is Label)) control.Dock = DockStyle.Fill;
            layout.RowStyles.Add(size == SizeType.Percent ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(control, 0, layout.RowStyles.Count - 1);
        }

        private void ShowLines()
        {
            _lines.SuspendLayout();
            try
            {
                while (_lines.Controls.Count > 0) _lines.Controls[0].Dispose();
                _lines.RowStyles.Clear();
                _lines.RowCount = 0;

                var bold = new Font(Font, FontStyle.Bold);
                AddLine("Параметр", "Было", "Станет", SystemColors.ControlText, bold);
                foreach (ChangeLine line in ChangeSummary.Lines(_request, _current))
                {
                    AddLine(line.What, line.Before, line.Changed ? line.After : "без изменений",
                        line.Changed ? SystemColors.ControlText : SystemColors.GrayText, Font);
                }
            }
            finally
            {
                _lines.ResumeLayout();
            }
        }

        private void AddLine(string what, string before, string after, Color color, Font font)
        {
            int row = _lines.RowCount;
            _lines.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            foreach (string text in new[] { what, before, after })
            {
                var cell = new Label { Text = text, AutoSize = true, Dock = DockStyle.Fill, ForeColor = color, Font = font, Margin = new Padding(4, 3, 10, 3) };
                _lines.Controls.Add(cell, _lines.Controls.Count % 3, row);
            }
            _lines.RowCount = row + 1;
        }

        private void Accept()
        {
            if (_addressStub != null && _request.Ipv4 != null)
            {
                string address;
                if (!ProfileRules.TryResolveAddress(_address.Text, out address))
                {
                    MessageBox.Show(this, "Адрес задан не полностью или неверно. Пример: 10.68.12.37", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _address.Focus();
                    return;
                }

                if (_addressStub.Trim().Length > 0 && !ProfileRules.IsInProfileSubnet(address, _addressStub, _request.Ipv4.Mask))
                {
                    DialogResult answer = MessageBox.Show(
                        this, "Адрес " + address + " не входит в подсеть профиля. Всё равно применить?",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                    if (answer != DialogResult.Yes) return;
                }

                _request.Ipv4.Address = address;
            }

            DialogResult = DialogResult.OK;
        }
    }
}
