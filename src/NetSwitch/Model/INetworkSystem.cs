using System.Collections.Generic;

namespace NetSwitch.Model
{
    /// <summary>
    /// Сетевые настройки управляемого компьютера. Единственное место, через которое инструмент
    /// обращается к системе; в тестах подменяется.
    /// </summary>
    internal interface INetworkSystem
    {
        /// <summary>Все адаптеры, видимые в «Сетевых подключениях», физические и виртуальные.</summary>
        IList<Adapter> ReadAdapters();

        /// <summary>Системный прокси интерактивного пользователя и WinHTTP-прокси.</summary>
        ProxyState ReadProxy();
    }
}
