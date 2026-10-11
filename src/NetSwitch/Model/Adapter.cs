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
        public bool AutoDns { get; set; } = true;
        public List<string> Dns { get; } = new List<string>();
    }

    /// <summary>Адреса, которые адаптер реально имеет в данный момент.</summary>
    internal sealed class ActiveIpv4
    {
        /// <summary>Адреса с длиной префикса: 10.68.12.37/24.</summary>
        public List<string> Addresses { get; } = new List<string>();
        public List<string> Gateways { get; } = new List<string>();
        public List<string> Dns { get; } = new List<string>();
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
}
