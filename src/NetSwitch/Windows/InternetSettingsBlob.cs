using System;
using System.Text;

namespace NetSwitch.Windows
{
    /// <summary>
    /// Двоичное значение, в котором Windows хранит параметры прокси подключения
    /// (DefaultConnectionSettings у пользователя, WinHttpSettings у компьютера).
    /// «Автоматическое определение параметров» хранится только здесь.
    /// </summary>
    internal sealed class InternetSettingsBlob
    {
        private const int FlagProxy = 0x2;
        private const int FlagAutoConfig = 0x4;
        private const int FlagAutoDetect = 0x8;

        public int Version { get; private set; }
        public int Counter { get; private set; }
        public int Flags { get; private set; }
        public string Proxy { get; private set; } = "";
        public string Bypass { get; private set; } = "";
        public string AutoConfigUrl { get; private set; } = "";

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
            return blob;
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
    }
}
