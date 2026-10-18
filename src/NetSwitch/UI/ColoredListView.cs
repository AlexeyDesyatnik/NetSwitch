using System.Drawing;
using System.Windows.Forms;

namespace NetSwitch.UI
{
    /// <summary>
    /// Список, в котором выделенная строка не теряет свой цвет текста. Windows рисует выделение
    /// цветами системы, и красная строка адаптера с конфликтом адресов, а это почти всегда
    /// выбранный адаптер, выглядела обычной (#10).
    /// </summary>
    internal sealed class ColoredListView : ListView
    {
        private const TextFormatFlags TextFlags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        private const int TextPadding = 6;
        private const int FirstColumnPadding = 2;

        public ColoredListView()
        {
            OwnerDraw = true;
            DoubleBuffered = true;
        }

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            e.DrawDefault = true;
        }

        protected override void OnDrawItem(DrawListViewItemEventArgs e)
        {
            // Всё, кроме выделенных цветных строк, рисует Windows; для них ниже рисуется каждая ячейка
            e.DrawDefault = !Colored(e.Item);
        }

        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
        {
            if (!Colored(e.Item))
            {
                e.DrawDefault = true;
                return;
            }

            // Для первого столбца Windows отдаёт границы всей строки
            Rectangle bounds = e.Bounds;
            if (e.ColumnIndex == 0) bounds.Width = Columns[0].Width;

            // Фон как у выделения в списке без фокуса: на нём цвет текста читается и на синем бы не читался
            e.Graphics.FillRectangle(SystemBrushes.Control, bounds);

            // Текст там же, где его рисует сама Windows
            int left = e.ColumnIndex == 0 ? e.Item.GetBounds(ItemBoundsPortion.Label).X + FirstColumnPadding : bounds.X + TextPadding;
            Rectangle text = Rectangle.FromLTRB(left, bounds.Top, bounds.Right, bounds.Bottom);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, text, e.Item.ForeColor, TextFlags);
        }

        private bool Colored(ListViewItem item)
        {
            return item.Selected && item.ForeColor != ForeColor;
        }
    }
}
