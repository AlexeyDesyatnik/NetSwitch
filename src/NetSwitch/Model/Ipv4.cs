using System;
using System.Collections.Generic;
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

        /// <summary>Маска в любой записи — 255.255.255.0, /24 или 24 — приводится к виду 255.255.255.0.</summary>
        public static bool TryParseMask(string? text, out string mask)
        {
            mask = "";
            string value = (text ?? "").Trim();
            if (value.StartsWith("/", StringComparison.Ordinal)) value = value.Substring(1).Trim();

            int prefix;
            if (value.IndexOf('.') >= 0)
            {
                if (!TryMaskToPrefix(value, out prefix)) return false;
            }
            else
            {
                if (value.Length == 0 || value.Length > 2) return false;
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out prefix) || prefix > 32) return false;
            }

            mask = PrefixToMask(prefix);
            return true;
        }

        /// <summary>
        /// Начало адреса («10.68.12.») дополняется нулями до полного адреса. Так по уточняемому адресу
        /// определяется подсеть профиля.
        /// </summary>
        public static bool TryCompleteStub(string? stub, out uint value)
        {
            value = 0;
            string text = (stub ?? "").Trim();
            if (text.EndsWith(".", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 1);

            var parts = new List<string>(text.Length == 0 ? new string[0] : text.Split('.'));
            if (parts.Count > 4) return false;
            while (parts.Count < 4) parts.Add("0");

            return TryParse(string.Join(".", parts), out value);
        }

        public static bool InSameSubnet(uint one, uint other, uint mask)
        {
            return (one & mask) == (other & mask);
        }

        /// <summary>Совпадают ли два адреса, даже если записаны по-разному (010.1.1.1 и 10.1.1.1).</summary>
        public static bool Same(string? one, string? other)
        {
            uint a, b;
            if (TryParse(one, out a) && TryParse(other, out b)) return a == b;
            return string.Equals((one ?? "").Trim(), (other ?? "").Trim(), StringComparison.Ordinal);
        }
    }
}
