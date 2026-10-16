using System.Collections.Generic;
using System.Windows.Forms;
using NetSwitch.Model;
using NetSwitch.UI;

namespace NetSwitch.Tests
{
    /// <summary>Человек у окна, который всё подтверждает; сообщения запоминаются, а не показываются.</summary>
    internal sealed class FakePrompts : IPrompts
    {
        public readonly List<string> Told = new List<string>();

        public bool Confirm(IWin32Window owner, ChangeRequest request, ProxyState? current, string? addressStub, string acceptText)
        {
            return true;
        }

        public void Tell(IWin32Window owner, string text, string caption, MessageBoxIcon icon)
        {
            Told.Add(caption + ": " + text);
        }
    }
}
