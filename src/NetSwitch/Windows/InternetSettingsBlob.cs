using System;
using System.Collections.Generic;
using System.Text;
using NetSwitch.Model;

namespace NetSwitch.Windows
{
    /// <summary>
    /// Двоичное значение, в котором Windows хранит параметры прокси подключения
    /// (DefaultConnectionSettings у пользователя, WinHttpSettings у компьютера).
    /// «Автоматическое определение параметров» хранится только здесь.
    /// </summary>
    internal sealed class InternetSettingsBlob
    {
        private const int CurrentVersion = 0x46;
        private const int FlagDirect = 0x1;
        private const int FlagProxy = 0x2;
        private const int FlagAutoConfig = 0x4;
        private const int FlagAutoDetect = 0x8;

        public int Version { get; private set; }
        public int Counter { get; private set; }
        public int Flags { get; private set; }
        public string Proxy { get; private set; } = "";
        public string Bypass { get; private set; } = "";
        public string AutoConfigUrl { get; private set; } = "";
        /// <summary>Служебные данные Windows после строк; при перезаписи сохраняются как были.</summary>
        public byte[] Tail { get; private set; } = new byte[0];

        public bool ProxyEnabled
        {
            get { return (Flags & FlagProxy) != 0; }
        }

        public bool AutoConfigEnabled
        {
            get { return (Flags & FlagAutoConfig) != 0; }
        }

        public bool AutoDetect
        {
            get { return (Flags & FlagAutoDetect) != 0; }
        }

        /// <summary>WinHttpSettings короче: в нём нет сценария автонастройки, поэтому хвост необязателен.</summary>
        public static InternetSettingsBlob Parse(byte[] data)
        {
            if (data.Length < 12) throw new FormatException("Параметры подключения короче заголовка: " + data.Length + " байт.");

            var blob = new InternetSettingsBlob
            {
                Version = BitConverter.ToInt32(data, 0),
                Counter = BitConverter.ToInt32(data, 4),
                Flags = BitConverter.ToInt32(data, 8)
            };

            int offset = 12;
            blob.Proxy = ReadString(data, ref offset);
            blob.Bypass = ReadString(data, ref offset);
            blob.AutoConfigUrl = ReadString(data, ref offset);

            blob.Tail = new byte[data.Length - offset];
            Array.Copy(data, offset, blob.Tail, 0, blob.Tail.Length);
            return blob;
        }

        /// <summary>
        /// Значение для записи нового состояния системного прокси. Счётчик изменений растёт,
        /// чтобы Windows заметила перемену; всё, чего инструмент не понимает, переносится из прежнего значения.
        /// </summary>
        public static byte[] Build(InternetSettingsBlob? previous, SystemProxyState state)
        {
            int flags = FlagDirect;
            if (state.Settings.Enabled) flags |= FlagProxy;
            if (state.AutoConfigUrl.Length > 0) flags |= FlagAutoConfig;
            if (state.AutoDetect) flags |= FlagAutoDetect;

            var bytes = new List<byte>();
            bytes.AddRange(BitConverter.GetBytes(previous != null ? previous.Version : CurrentVersion));
            bytes.AddRange(BitConverter.GetBytes(previous != null ? unchecked(previous.Counter + 1) : 1));
            bytes.AddRange(BitConverter.GetBytes(flags));
            WriteString(bytes, state.Settings.Server);
            WriteString(bytes, state.Settings.ToBypassList());
            WriteString(bytes, state.AutoConfigUrl);
            bytes.AddRange(previous != null && previous.Tail.Length > 0 ? previous.Tail : new byte[32]);
            return bytes.ToArray();
        }

        private static string ReadString(byte[] data, ref int offset)
        {
            if (offset + 4 > data.Length) return "";

            int length = BitConverter.ToInt32(data, offset);
            offset += 4;
            if (length < 0 || offset + length > data.Length)
                throw new FormatException("Строка в параметрах подключения выходит за границы значения.");

            string text = Encoding.ASCII.GetString(data, offset, length);
            offset += length;
            return text;
        }

        private static void WriteString(List<byte> bytes, string text)
        {
            byte[] encoded = Encoding.ASCII.GetBytes(text);
            bytes.AddRange(BitConverter.GetBytes(encoded.Length));
            bytes.AddRange(encoded);
        }
    }
}
