using System.Collections.Generic;
using NetSwitch.Model;

namespace NetSwitch.UI
{
    /// <summary>Как адаптеры, настройки и прокси называются на экране.</summary>
    internal static class Describe
    {
        private const string Nothing = "—";
        private const string Automatic = "автоматически";

        public static string State(Adapter adapter)
        {
            if (adapter.Active.HasConflict) return "конфликт адресов";

            switch (adapter.State)
            {
                case AdapterState.Connected: return "подключён";
                case AdapterState.NoLink: return "нет соединения";
                case AdapterState.Connecting: return "подключается";
                case AdapterState.Disabled: return "выключен";
                default: return "недоступен";
            }
        }

        /// <summary>Адрес, который адаптер имеет сейчас; пока соединения нет — тот, что задан вручную.</summary>
        public static string Address(Adapter adapter)
        {
            if (adapter.Active.Addresses.Count > 0) return string.Join(", ", adapter.Active.Addresses);

            Ipv4Settings configured = adapter.Configured;
            return configured.AutoAddress || configured.Address.Length == 0 ? Nothing : WithPrefix(configured);
        }

        public static string Mode(Ipv4Settings settings)
        {
            if (!settings.AutoAddress) return "вручную";
            return settings.AutoDns ? "авто" : "авто, DNS вручную";
        }

        public static string Gateway(Adapter adapter)
        {
            if (adapter.Active.Gateways.Count > 0) return string.Join(", ", adapter.Active.Gateways);
            return adapter.Configured.Gateway.Length > 0 ? adapter.Configured.Gateway : Nothing;
        }

        public static string Dns(Adapter adapter)
        {
            List<string> servers = adapter.Active.Dns.Count > 0 ? adapter.Active.Dns : adapter.Configured.Dns;
            return servers.Count > 0 ? string.Join(", ", servers) : Nothing;
        }

        // Настройки как они заданы, без оглядки на то, что адаптер имеет сейчас

        public static string ConfiguredAddress(Ipv4Settings settings)
        {
            if (settings.AutoAddress) return Automatic;
            return settings.Address.Length > 0 ? WithPrefix(settings) : "не задан";
        }

        public static string ConfiguredGateway(Ipv4Settings settings)
        {
            if (settings.AutoAddress) return Automatic;
            return settings.Gateway.Length > 0 ? settings.Gateway : "нет";
        }

        public static string ConfiguredDns(Ipv4Settings settings)
        {
            if (settings.AutoDns) return Automatic;
            return settings.Dns.Count > 0 ? string.Join(", ", settings.Dns) : "нет";
        }

        public static string Proxy(ProxySettings settings)
        {
            if (!settings.Enabled) return "выключен";

            string text = settings.Server.Length > 0 ? settings.Server : "включён, сервер не задан";
            if (settings.Exceptions.Count > 0) text += "; исключения: " + string.Join(", ", settings.Exceptions);
            if (settings.BypassLocal) text += "; не для локальных адресов";
            return text;
        }

        public static string SystemProxy(SystemProxyState state)
        {
            string text = Proxy(state.Settings);
            if (state.AutoDetect) text += "; автоопределение включено";
            if (state.AutoConfigUrl.Length > 0) text += "; сценарий: " + state.AutoConfigUrl;
            return text;
        }

        /// <summary>Содержимое профиля, по строке на каждую настройку.</summary>
        public static string Profile(Profile profile)
        {
            string address = ConfiguredAddress(profile.Ipv4);
            if (profile.AskAddress) address += " (уточняется)";

            return "Адрес: " + address
                + "\nШлюз: " + ConfiguredGateway(profile.Ipv4)
                + "\nDNS: " + ConfiguredDns(profile.Ipv4)
                + "\nПрокси: " + Proxy(profile.Proxy);
        }

        private static string WithPrefix(Ipv4Settings settings)
        {
            int prefix;
            return Ipv4.TryMaskToPrefix(settings.Mask, out prefix)
                ? settings.Address + "/" + prefix
                : (settings.Address + " " + settings.Mask).Trim();
        }
    }
}
