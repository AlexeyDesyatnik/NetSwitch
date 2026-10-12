using System;
using System.Collections.Generic;

namespace NetSwitch.Model
{
    /// <summary>Именованный набор из настроек IPv4 и настроек прокси. Не привязан ни к какому адаптеру.</summary>
    internal sealed class Profile
    {
        public string Name { get; set; } = "";
        public Ipv4Settings Ipv4 { get; set; } = new Ipv4Settings();
        /// <summary>Адрес в профиле задан не полностью и дополняется при применении.</summary>
        public bool AskAddress { get; set; }
        public ProxySettings Proxy { get; set; } = new ProxySettings();

        public Profile Clone()
        {
            return new Profile { Name = Name, Ipv4 = Ipv4.Clone(), AskAddress = AskAddress, Proxy = Proxy.Clone() };
        }
    }

    /// <summary>
    /// Проверка введённых настроек. Заодно приводит их к единому виду: маску к 255.255.255.0,
    /// адреса — без ведущих нулей, которые Windows понимает как восьмеричную запись.
    /// </summary>
    internal static class ProfileRules
    {
        public static List<string> Check(Profile profile)
        {
            var errors = new List<string>();

            profile.Name = profile.Name.Trim();
            if (profile.Name.Length == 0) errors.Add("Укажите название профиля.");

            if (profile.Ipv4.AutoAddress) profile.AskAddress = false;
            errors.AddRange(CheckIpv4(profile.Ipv4, profile.AskAddress));
            errors.AddRange(CheckProxy(profile.Proxy));
            return errors;
        }

        /// <param name="askAddress">Адрес — начало, которое дополнят при применении.</param>
        public static List<string> CheckIpv4(Ipv4Settings settings, bool askAddress)
        {
            var errors = new List<string>();

            if (settings.AutoAddress)
            {
                settings.Address = settings.Mask = settings.Gateway = "";
            }
            else
            {
                settings.AutoDns = false;
                settings.Address = settings.Address.Trim();
                settings.Gateway = settings.Gateway.Trim();

                if (askAddress)
                {
                    if (!Ipv4.TryCompleteStub(settings.Address, out _))
                        errors.Add("Начало адреса задано неверно. Пример: 10.68.12.");
                }
                else if (!Normalize(settings.Address, out string address))
                {
                    errors.Add("Адрес IPv4 задан неверно. Пример: 10.68.12.37");
                }
                else
                {
                    settings.Address = address;
                }

                if (Ipv4.TryParseMask(settings.Mask, out string mask) && mask != "0.0.0.0")
                    settings.Mask = mask;
                else
                    errors.Add("Маска задана неверно. Примеры: 255.255.255.0 или /24");

                if (settings.Gateway.Length > 0)
                {
                    if (Normalize(settings.Gateway, out string gateway))
                        settings.Gateway = gateway;
                    else
                        errors.Add("Шлюз задан неверно. Пример: 10.68.12.1");
                }
            }

            if (settings.AutoDns)
            {
                settings.Dns.Clear();
                return errors;
            }

            var servers = new List<string>();
            foreach (string entry in settings.Dns)
            {
                if (entry.Trim().Length == 0) continue;

                if (Normalize(entry, out string server))
                    servers.Add(server);
                else
                    errors.Add("DNS-сервер «" + entry.Trim() + "» задан неверно. Пример: 10.0.68.10");
            }
            settings.Dns.Clear();
            settings.Dns.AddRange(servers);

            if (servers.Count > 2) errors.Add("DNS-серверов может быть не больше двух.");
            if (settings.AutoAddress && servers.Count == 0 && errors.Count == 0)
                errors.Add("Укажите DNS-сервер или выберите автоматическое получение DNS.");

            return errors;
        }

        public static List<string> CheckProxy(ProxySettings settings)
        {
            var errors = new List<string>();
            settings.Server = settings.Server.Trim();

            if (settings.Enabled)
            {
                if (settings.Server.Length == 0)
                    errors.Add("Укажите прокси-сервер. Пример: 10.0.68.52:3128");
                else if (settings.Server.IndexOfAny(new[] { ' ', '\t' }) >= 0)
                    errors.Add("В адресе прокси-сервера не должно быть пробелов.");
                else if (!HasPort(settings.Server))
                    errors.Add("Укажите порт прокси-сервера. Пример: 10.0.68.52:3128");
            }
            return errors;
        }

        /// <summary>
        /// Полный адрес для уточняемого адреса профиля. Ошибка — когда введённое адресом не является;
        /// выход за пределы подсети профиля ошибкой не считается и сообщается отдельно.
        /// </summary>
        public static bool TryResolveAddress(string entered, out string address)
        {
            return Normalize(entered, out address);
        }

        /// <summary>
        /// Чем заполнить поле уточняемого адреса: адресом, который адаптер уже имеет в подсети профиля,
        /// а если такого нет — началом адреса из профиля.
        /// </summary>
        public static string SuggestAddress(Ipv4Settings profile, Adapter adapter)
        {
            var candidates = new List<string>();
            if (!adapter.Configured.AutoAddress) candidates.Add(adapter.Configured.Address);
            foreach (string active in adapter.Active.Addresses) candidates.Add(active.Split('/')[0]);

            foreach (string candidate in candidates)
            {
                if (IsInProfileSubnet(candidate, profile.Address, profile.Mask)) return candidate;
            }
            return profile.Address;
        }

        /// <summary>Лежит ли адрес в подсети, заданной началом адреса и маской профиля.</summary>
        public static bool IsInProfileSubnet(string address, string stub, string mask)
        {
            uint value, network, maskValue;
            if (!Ipv4.TryParse(address, out value) || !Ipv4.TryCompleteStub(stub, out network) || !Ipv4.TryParse(mask, out maskValue)) return false;
            return Ipv4.InSameSubnet(value, network, maskValue);
        }

        private static bool Normalize(string text, out string address)
        {
            address = "";
            uint value;
            if (!Ipv4.TryParse(text, out value)) return false;

            address = Ipv4.Format(value);
            return true;
        }

        private static bool HasPort(string server)
        {
            int colon = server.LastIndexOf(':');
            if (colon <= 0 || colon == server.Length - 1) return false;

            int port;
            return int.TryParse(server.Substring(colon + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out port)
                && port >= 1 && port <= 65535;
        }
    }
}
