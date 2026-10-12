using System.Collections.Generic;
using NetSwitch.Model;

namespace NetSwitch.UI
{
    internal sealed class ChangeLine
    {
        public ChangeLine(string what, string before, string after)
        {
            What = what;
            Before = before;
            After = after;
        }

        public string What { get; private set; }
        public string Before { get; private set; }
        public string After { get; private set; }

        public bool Changed
        {
            get { return Before != After; }
        }
    }

    /// <summary>То, что человек видит в окне подтверждения: «было → станет» и оговорки.</summary>
    internal static class ChangeSummary
    {
        private const string Unknown = "неизвестно";

        /// <param name="current">null, если прокси управляемого компьютера прочитать не удалось.</param>
        public static IList<ChangeLine> Lines(ChangeRequest request, ProxyState? current)
        {
            var lines = new List<ChangeLine>();

            if (request.Adapter != null && request.Ipv4 != null)
            {
                Ipv4Settings before = request.Adapter.Configured;
                Ipv4Settings after = request.Ipv4;

                if (request.Adapter.State == AdapterState.Disabled)
                {
                    lines.Add(new ChangeLine("Адаптер «" + request.Adapter.Name + "»", "выключен", "включён"));
                }
                lines.Add(new ChangeLine("Адрес", Describe.ConfiguredAddress(before), Describe.ConfiguredAddress(after)));
                lines.Add(new ChangeLine("Шлюз", Describe.ConfiguredGateway(before), Describe.ConfiguredGateway(after)));
                lines.Add(new ChangeLine("DNS", Describe.ConfiguredDns(before), Describe.ConfiguredDns(after)));
            }

            if (request.SystemProxy != null)
            {
                lines.Add(new ChangeLine(
                    "Системный прокси",
                    current != null ? Describe.SystemProxy(current.System) : Unknown,
                    Describe.SystemProxy(request.SystemProxy)));
            }
            if (request.WinHttp != null)
            {
                lines.Add(new ChangeLine(
                    "WinHTTP-прокси",
                    current != null ? Describe.Proxy(current.WinHttp) : Unknown,
                    Describe.Proxy(request.WinHttp)));
            }

            return lines;
        }

        public static IList<string> Notes(ChangeRequest request, ProxyState? current)
        {
            var notes = new List<string>();

            if (request.Adapter != null && request.Ipv4 != null && request.Adapter.State == AdapterState.NoLink)
            {
                notes.Add("На адаптере сейчас нет соединения — настройки подействуют, когда оно появится.");
            }

            if (request.SystemProxy != null && current != null)
            {
                if (!current.User.IsProcessOwner)
                {
                    notes.Add("Системный прокси будет изменён для пользователя " + current.User.Name + ".");
                }
                if (current.Policy.IsManaged)
                {
                    notes.Add("Прокси на этом компьютере задаётся групповой политикой — изменение может не подействовать.");
                }
            }

            return notes;
        }
    }
}
