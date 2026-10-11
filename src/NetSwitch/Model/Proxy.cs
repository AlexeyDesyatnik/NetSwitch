using System;
using System.Collections.Generic;

namespace NetSwitch.Model
{
    /// <summary>Настройки прокси: включён или выключен, сервер, исключения.</summary>
    internal sealed class ProxySettings
    {
        private const string LocalMarker = "<local>";

        public bool Enabled { get; set; }
        /// <summary>Сервер в виде адрес:порт.</summary>
        public string Server { get; set; } = "";
        public List<string> Exceptions { get; } = new List<string>();
        /// <summary>Не использовать прокси для локальных адресов.</summary>
        public bool BypassLocal { get; set; }

        /// <summary>Собирает настройки из того вида, в котором их хранит Windows: исключения одной строкой через «;».</summary>
        public static ProxySettings FromRaw(bool enabled, string? server, string? bypassList)
        {
            var settings = new ProxySettings { Enabled = enabled, Server = (server ?? "").Trim() };

            string[] entries = (bypassList ?? "").Split(new[] { ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string entry in entries)
            {
                if (string.Equals(entry, LocalMarker, StringComparison.OrdinalIgnoreCase))
                    settings.BypassLocal = true;
                else
                    settings.Exceptions.Add(entry);
            }
            return settings;
        }

        public string ToBypassList()
        {
            var entries = new List<string>(Exceptions);
            if (BypassLocal) entries.Add(LocalMarker);
            return string.Join(";", entries);
        }

        /// <summary>Два выключенных прокси одинаковы независимо от того, что осталось в полях сервера и исключений.</summary>
        public bool SameAs(ProxySettings other)
        {
            if (Enabled != other.Enabled) return false;
            if (!Enabled) return true;

            return string.Equals(Server, other.Server, StringComparison.OrdinalIgnoreCase)
                && BypassLocal == other.BypassLocal
                && new HashSet<string>(Exceptions, StringComparer.OrdinalIgnoreCase).SetEquals(other.Exceptions);
        }
    }

    /// <summary>Системный прокси как он есть на управляемом компьютере, вместе с тем, чем инструмент не управляет.</summary>
    internal sealed class SystemProxyState
    {
        public ProxySettings Settings { get; set; } = new ProxySettings();
        /// <summary>«Автоматическое определение параметров».</summary>
        public bool AutoDetect { get; set; }
        /// <summary>Адрес сценария автоматической настройки; пусто, если сценарий не задан.</summary>
        public string AutoConfigUrl { get; set; } = "";
    }

    /// <summary>Признаки того, что прокси на управляемом компьютере задаётся групповой политикой.</summary>
    internal sealed class ProxyPolicy
    {
        /// <summary>Политика «прокси один на компьютер, а не на пользователя».</summary>
        public bool PerMachine { get; set; }
        /// <summary>Политика сама задаёт значения прокси пользователю.</summary>
        public bool SetsValues { get; set; }
        /// <summary>Политика запрещает менять параметры прокси.</summary>
        public bool ChangeLocked { get; set; }

        public bool IsManaged
        {
            get { return PerMachine || SetsValues || ChangeLocked; }
        }
    }

    /// <summary>Пользователь, вошедший в сеанс на управляемом компьютере.</summary>
    internal sealed class InteractiveUser
    {
        public InteractiveUser(string name, string sid, bool isProcessOwner)
        {
            Name = name;
            Sid = sid;
            IsProcessOwner = isProcessOwner;
        }

        public string Name { get; private set; }
        public string Sid { get; private set; }
        /// <summary>Ложно, если права администратора получены под другой учётной записью.</summary>
        public bool IsProcessOwner { get; private set; }
    }

    internal sealed class ProxyState
    {
        public ProxyState(InteractiveUser user, SystemProxyState system, ProxySettings winHttp, ProxyPolicy policy)
        {
            User = user;
            System = system;
            WinHttp = winHttp;
            Policy = policy;
        }

        public InteractiveUser User { get; private set; }
        public SystemProxyState System { get; private set; }
        public ProxySettings WinHttp { get; private set; }
        public ProxyPolicy Policy { get; private set; }
    }
}
