using System.Collections.Generic;

namespace NetSwitch.Model
{
    /// <summary>
    /// Сетевые настройки управляемого компьютера. Единственное место, через которое инструмент
    /// обращается к системе; в тестах подменяется. Методы записи при неудаче бросают исключение
    /// с объяснением, понятным человеку.
    /// </summary>
    internal interface INetworkSystem
    {
        /// <summary>Все адаптеры, видимые в «Сетевых подключениях», физические и виртуальные.</summary>
        IList<Adapter> ReadAdapters();

        /// <summary>Системный прокси интерактивного пользователя и WinHTTP-прокси.</summary>
        ProxyState ReadProxy();

        /// <summary>Возвращается, когда адаптер действительно включился или выключился.</summary>
        void SetAdapterEnabled(Adapter adapter, bool enabled);

        /// <summary>Работает и без соединения; адаптер должен быть включён.</summary>
        void ApplyIpv4(Adapter adapter, Ipv4Settings settings);

        /// <summary>Записывает системный прокси интерактивному пользователю целиком, вместе с автоопределением и сценарием.</summary>
        void ApplySystemProxy(SystemProxyState state);

        void ApplyWinHttpProxy(ProxySettings settings);
    }
}
