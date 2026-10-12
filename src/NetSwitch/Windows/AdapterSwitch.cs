using System;
using System.Globalization;
using System.Management;
using System.Threading;

namespace NetSwitch.Windows
{
    internal static class AdapterSwitch
    {
        private const int TimeoutMs = 15000;
        private const int PollMs = 300;

        public static void SetEnabled(string adapterId, string adapterName, bool enabled)
        {
            // Идентификатор уходит в текст запроса, поэтому сначала убеждаемся, что это именно GUID
            string id = new Guid(adapterId).ToString("B").ToUpperInvariant();

            bool found = false;
            using (var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT * FROM Win32_NetworkAdapter WHERE GUID = '" + id + "'"))
            using (ManagementObjectCollection items = searcher.Get())
            {
                foreach (ManagementObject item in items)
                {
                    using (item)
                    {
                        found = true;
                        int code = Convert.ToInt32(item.InvokeMethod(enabled ? "Enable" : "Disable", null), CultureInfo.InvariantCulture);
                        if (code != 0)
                        {
                            throw new InvalidOperationException("Windows не смогла " + (enabled ? "включить" : "выключить")
                                + " адаптер «" + adapterName + "», код ошибки " + code + ".");
                        }
                    }
                }
            }
            if (!found) throw new InvalidOperationException("Адаптер «" + adapterName + "» не найден.");

            // Настраивать IPv4 можно только после того, как адаптер появился в сетевом стеке
            for (int waited = 0; AdapterReader.IsInStack(adapterId) != enabled; waited += PollMs)
            {
                if (waited >= TimeoutMs)
                {
                    throw new InvalidOperationException("Адаптер «" + adapterName + "» не успел "
                        + (enabled ? "включиться" : "выключиться") + " за " + TimeoutMs / 1000 + " секунд.");
                }
                Thread.Sleep(PollMs);
            }
        }
    }
}
