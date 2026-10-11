using System.Collections.Generic;
using NetSwitch.Model;

namespace NetSwitch.UI
{
    /// <summary>Как адаптеры и прокси называются на экране.</summary>
    internal static class Describe
    {
        private const string Nothing = "—";

        public static string State(AdapterState state)
        {
            switch (state)
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
            if (configured.AutoAddress || configured.Address.Length == 0) return Nothing;

            int prefix;
            return Ipv4.TryMaskToPrefix(configured.Mask, out prefix)
                ? configured.Address + "/" + prefix
                : (configured.Address + " " + configured.Mask).Trim();
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
    }
}
