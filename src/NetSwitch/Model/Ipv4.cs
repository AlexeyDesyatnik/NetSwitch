using System;
using System.Globalization;

namespace NetSwitch.Model
{
    /// <summary>Разбор и запись адресов и масок IPv4.</summary>
    internal static class Ipv4
    {
        public static bool TryParse(string? text, out uint value)
        {
            value = 0;
            if (text == null) return false;

            string[] parts = text.Trim().Split('.');
            if (parts.Length != 4) return false;

            uint result = 0;
            foreach (string part in parts)
            {
                if (part.Length == 0 || part.Length > 3) return false;

                int octet = 0;
                foreach (char c in part)
                {
                    if (c < '0' || c > '9') return false;
                    octet = octet * 10 + (c - '0');
                }
                if (octet > 255) return false;

                result = (result << 8) | (uint)octet;
            }

            value = result;
            return true;
        }

        public static string Format(uint value)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}.{1}.{2}.{3}",
                (value >> 24) & 255, (value >> 16) & 255, (value >> 8) & 255, value & 255);
        }

        /// <summary>Маска вида 255.255.255.0 → длина префикса 24. Маски с «дырами» не принимаются.</summary>
        public static bool TryMaskToPrefix(string? mask, out int prefix)
        {
            prefix = 0;
            if (!TryParse(mask, out uint value)) return false;

            uint inverted = ~value;
            if ((inverted & unchecked(inverted + 1)) != 0) return false;

            int count = 0;
            while (value != 0)
            {
                count++;
                value <<= 1;
            }

            prefix = count;
            return true;
        }

        public static string PrefixToMask(int prefix)
        {
            if (prefix < 0 || prefix > 32) throw new ArgumentOutOfRangeException("prefix");
            return Format(prefix == 0 ? 0 : uint.MaxValue << (32 - prefix));
        }
    }
}
