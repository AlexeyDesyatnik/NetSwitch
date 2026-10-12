using System;
using System.Collections.Generic;

namespace NetSwitch.Model
{
    internal enum AdapterState
    {
        /// <summary>Включён, соединение есть.</summary>
        Connected,
        /// <summary>Включён, но кабель не подключён или Wi-Fi не в сети.</summary>
        NoLink,
        /// <summary>Включён и находится в процессе подключения.</summary>
        Connecting,
        /// <summary>Выключен.</summary>
        Disabled,
        /// <summary>Устройство отсутствует или неисправно.</summary>
        Unavailable
    }

    /// <summary>Адаптер управляемого компьютера вместе с его настройками IPv4.</summary>
    internal sealed class Adapter
    {
        /// <summary>GUID адаптера в фигурных скобках.</summary>
        public string Id { get; set; } = "";
        /// <summary>Имя, под которым адаптер виден в «Сетевых подключениях».</summary>
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string PnpDeviceId { get; set; } = "";
        public string MacAddress { get; set; } = "";
        public bool IsPhysical { get; set; }
        /// <summary>Известно только для включённого адаптера.</summary>
        public bool IsWireless { get; set; }
        public AdapterState State { get; set; }

        /// <summary>То, что на адаптере настроено (действует и когда соединения нет).</summary>
        public Ipv4Settings Configured { get; set; } = new Ipv4Settings();
        /// <summary>То, что адаптер имеет прямо сейчас, включая полученное автоматически.</summary>
        public ActiveIpv4 Active { get; set; } = new ActiveIpv4();
    }

    /// <summary>Настройки IPv4: способ получения адреса и DNS и заданные вручную значения.</summary>
    internal sealed class Ipv4Settings
    {
        public bool AutoAddress { get; set; } = true;
        public string Address { get; set; } = "";
        public string Mask { get; set; } = "";
        public string Gateway { get; set; } = "";
        /// <summary>При адресе, заданном вручную, DNS всегда задаётся вручную (возможно, пустым списком).</summary>
        public bool AutoDns { get; set; } = true;
        public List<string> Dns { get; } = new List<string>();

        public Ipv4Settings Clone()
        {
            var copy = new Ipv4Settings { AutoAddress = AutoAddress, Address = Address, Mask = Mask, Gateway = Gateway, AutoDns = AutoDns };
            copy.Dns.AddRange(Dns);
            return copy;
        }

        /// <summary>Значения, не действующие в текущем режиме (адрес при автополучении), в сравнении не участвуют.</summary>
        public bool SameAs(Ipv4Settings other)
        {
            if (AutoAddress != other.AutoAddress || AutoDns != other.AutoDns) return false;

            if (!AutoAddress)
            {
                if (!Ipv4.Same(Address, other.Address) || !Ipv4.Same(Mask, other.Mask) || !Ipv4.Same(Gateway, other.Gateway)) return false;
            }
            if (!AutoDns)
            {
                if (Dns.Count != other.Dns.Count) return false;
                for (int i = 0; i < Dns.Count; i++)
                {
                    if (!Ipv4.Same(Dns[i], other.Dns[i])) return false;
                }
            }
            return true;
        }
    }

    /// <summary>Адреса, которые адаптер реально имеет в данный момент.</summary>
    internal sealed class ActiveIpv4
    {
        /// <summary>Адреса с длиной префикса: 10.68.12.37/24.</summary>
        public List<string> Addresses { get; } = new List<string>();
        public List<string> Gateways { get; } = new List<string>();
        public List<string> Dns { get; } = new List<string>();
        /// <summary>Windows обнаружила в сети другой компьютер с таким же адресом.</summary>
        public bool HasConflict { get; set; }
    }

    internal static class AdapterClassifier
    {
        // Шины, на которых не бывает настоящих сетевых устройств: программные адаптеры
        // (Hyper-V, VPN, виртуальные машины), Bluetooth, Wi-Fi Direct.
        private static readonly string[] VirtualPrefixes = { @"ROOT\", @"SWD\", @"BTH\", @"BTHENUM\", "{" };

        /// <summary>
        /// Физический адаптер определяется по шине устройства. Незнакомая шина считается физической:
        /// лучше показать лишний адаптер, чем спрятать настоящий.
        /// </summary>
        public static bool IsPhysical(string? pnpDeviceId)
        {
            if (string.IsNullOrEmpty(pnpDeviceId)) return false;

            foreach (string prefix in VirtualPrefixes)
            {
                if (pnpDeviceId!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }
    }

    internal static class AdapterChoice
    {
        /// <summary>
        /// Адаптер, к которому настройки применяются по умолчанию: единственный подключённый проводной,
        /// а если такого нет — единственный проводной. В остальных случаях выбор за человеком.
        /// </summary>
        public static Adapter? Default(IEnumerable<Adapter> adapters)
        {
            var wired = new List<Adapter>();
            var connected = new List<Adapter>();
            foreach (Adapter adapter in adapters)
            {
                if (!adapter.IsPhysical || adapter.IsWireless || adapter.State == AdapterState.Unavailable) continue;

                wired.Add(adapter);
                if (adapter.State == AdapterState.Connected) connected.Add(adapter);
            }

            if (connected.Count == 1) return connected[0];
            if (connected.Count == 0 && wired.Count == 1) return wired[0];
            return null;
        }
    }
}
