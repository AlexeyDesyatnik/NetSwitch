using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;

namespace NetSwitch.Windows
{
    /// <summary>Строка результата WMI-запроса: имя свойства → значение.</summary>
    internal sealed class WmiRow : Dictionary<string, object?>
    {
        public WmiRow() : base(StringComparer.OrdinalIgnoreCase)
        {
        }

        public string Text(string name)
        {
            object? value;
            return TryGetValue(name, out value) && value != null
                ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
                : "";
        }

        public int? Number(string name)
        {
            object? value;
            if (!TryGetValue(name, out value) || value == null) return null;
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        public bool? Flag(string name)
        {
            object? value;
            if (!TryGetValue(name, out value) || value == null) return null;
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }
    }

    internal static class Wmi
    {
        public static List<WmiRow> Query(string wql, string scope = @"root\cimv2")
        {
            var rows = new List<WmiRow>();
            using (var searcher = new ManagementObjectSearcher(scope, wql))
            using (ManagementObjectCollection found = searcher.Get())
            {
                foreach (ManagementBaseObject item in found)
                {
                    using (item)
                    {
                        var row = new WmiRow();
                        foreach (PropertyData property in item.Properties)
                        {
                            row[property.Name] = property.Value;
                        }
                        rows.Add(row);
                    }
                }
            }
            return rows;
        }
    }
}
