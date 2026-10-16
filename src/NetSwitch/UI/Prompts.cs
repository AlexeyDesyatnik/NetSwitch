using System.Windows.Forms;
using NetSwitch.Model;

namespace NetSwitch.UI
{
    /// <summary>Модальные окна, через которые главное окно спрашивает и сообщает; в тестах подменяются.</summary>
    internal interface IPrompts
    {
        /// <summary>Окно «было → станет». Уточняемый адрес, введённый в нём, записывается в сам запрос.</summary>
        bool Confirm(IWin32Window owner, ChangeRequest request, ProxyState? current, string? addressStub, string acceptText);

        void Tell(IWin32Window owner, string text, string caption, MessageBoxIcon icon);
    }

    internal sealed class WindowPrompts : IPrompts
    {
        public bool Confirm(IWin32Window owner, ChangeRequest request, ProxyState? current, string? addressStub, string acceptText)
        {
            using (var dialog = new ConfirmForm(request, current, addressStub, acceptText))
            {
                return dialog.ShowDialog(owner) == DialogResult.OK;
            }
        }

        public void Tell(IWin32Window owner, string text, string caption, MessageBoxIcon icon)
        {
            MessageBox.Show(owner, text, caption, MessageBoxButtons.OK, icon);
        }
    }
}
