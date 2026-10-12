using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using NetSwitch.Model;

namespace NetSwitch.Profiles
{
    /// <summary>Профили в файле рядом с инструментом.</summary>
    internal sealed class ProfileStore
    {
        public const string FileName = "NetSwitch.json";

        private readonly string _path;

        public ProfileStore(string folder)
        {
            _path = Path.Combine(folder, FileName);
            Profiles = new List<Profile>();
        }

        public string FilePath
        {
            get { return _path; }
        }

        public List<Profile> Profiles { get; private set; }

        /// <summary>
        /// Почему файл не удалось прочитать. Пока ошибка не устранена, сохранение запрещено:
        /// иначе пустой список затёр бы файл, который человек ещё может починить.
        /// </summary>
        public string? LoadError { get; private set; }

        public void Load()
        {
            Profiles = new List<Profile>();
            LoadError = null;
            if (!File.Exists(_path)) return;

            try
            {
                Profiles = ProfileJson.Read(File.ReadAllText(_path, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                if (!(ex is FormatException) && !(ex is IOException) && !(ex is UnauthorizedAccessException)) throw;
                LoadError = ex.Message;
            }
        }

        public void Save()
        {
            if (LoadError != null)
                throw new InvalidOperationException("Файл профилей не был прочитан, поэтому не будет перезаписан: " + LoadError);

            // Сначала во временный файл: если флешку выдернут посреди записи, прежние профили уцелеют
            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, ProfileJson.Write(Profiles), new UTF8Encoding(true));
            if (File.Exists(_path)) File.Delete(_path);
            File.Move(temporary, _path);
        }

        /// <summary>Название, которого ещё нет среди профилей: «Имя», «Имя (2)», «Имя (3)»…</summary>
        public string FreeName(string wanted)
        {
            string name = wanted;
            for (int n = 2; Find(name) != null; n++)
            {
                name = wanted + " (" + n + ")";
            }
            return name;
        }

        public Profile? Find(string name)
        {
            foreach (Profile profile in Profiles)
            {
                if (string.Equals(profile.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase)) return profile;
            }
            return null;
        }
    }

    /// <summary>Запись профилей в виде, удобном для чтения и правки руками.</summary>
    internal static class ProfileJson
    {
        private const int FormatVersion = 1;

        public static List<Profile> Read(string text)
        {
            object? root;
            try
            {
                root = new JavaScriptSerializer().DeserializeObject(text);
            }
            catch (Exception ex)
            {
                if (!(ex is ArgumentException) && !(ex is InvalidOperationException)) throw;
                throw new FormatException("Файл профилей повреждён: " + ex.Message);
            }

            var document = root as IDictionary<string, object>;
            if (document == null) throw new FormatException("Файл профилей повреждён: ожидался объект с полем «profiles».");

            var profiles = new List<Profile>();
            foreach (object item in List(document, "profiles"))
            {
                var source = item as IDictionary<string, object>;
                if (source == null) throw new FormatException("Файл профилей повреждён: профиль должен быть объектом.");

                var profile = new Profile { Name = Text(source, "name") };

                IDictionary<string, object> ipv4 = Section(source, "ipv4");
                profile.Ipv4.AutoAddress = Flag(ipv4, "autoAddress", true);
                profile.Ipv4.Address = Text(ipv4, "address");
                profile.AskAddress = Flag(ipv4, "askAddress", false);
                profile.Ipv4.Mask = Text(ipv4, "mask");
                profile.Ipv4.Gateway = Text(ipv4, "gateway");
                profile.Ipv4.AutoDns = Flag(ipv4, "autoDns", true);
                foreach (object server in List(ipv4, "dns")) profile.Ipv4.Dns.Add(Convert.ToString(server, CultureInfo.InvariantCulture) ?? "");

                IDictionary<string, object> proxy = Section(source, "proxy");
                profile.Proxy.Enabled = Flag(proxy, "enabled", false);
                profile.Proxy.Server = Text(proxy, "server");
                profile.Proxy.BypassLocal = Flag(proxy, "bypassLocal", false);
                foreach (object entry in List(proxy, "exceptions")) profile.Proxy.Exceptions.Add(Convert.ToString(entry, CultureInfo.InvariantCulture) ?? "");

                // Файл могли править руками: проверяем так же строго, как ввод в окне
                List<string> errors = ProfileRules.Check(profile);
                if (errors.Count > 0)
                    throw new FormatException("Профиль «" + profile.Name + "» в файле задан неверно. " + string.Join(" ", errors));

                profiles.Add(profile);
            }
            return profiles;
        }

        public static string Write(IList<Profile> profiles)
        {
            var text = new StringBuilder();
            text.AppendLine("{");
            text.AppendLine("  \"version\": " + FormatVersion + ",");
            text.AppendLine("  \"profiles\": [");

            for (int i = 0; i < profiles.Count; i++)
            {
                Profile profile = profiles[i];
                text.AppendLine("    {");
                text.AppendLine("      \"name\": " + Quote(profile.Name) + ",");
                text.AppendLine("      \"ipv4\": {");
                text.AppendLine("        \"autoAddress\": " + Bool(profile.Ipv4.AutoAddress) + ",");
                if (!profile.Ipv4.AutoAddress)
                {
                    text.AppendLine("        \"address\": " + Quote(profile.Ipv4.Address) + ",");
                    text.AppendLine("        \"askAddress\": " + Bool(profile.AskAddress) + ",");
                    text.AppendLine("        \"mask\": " + Quote(profile.Ipv4.Mask) + ",");
                    text.AppendLine("        \"gateway\": " + Quote(profile.Ipv4.Gateway) + ",");
                }
                text.AppendLine("        \"autoDns\": " + Bool(profile.Ipv4.AutoDns) + ",");
                text.AppendLine("        \"dns\": " + StringList(profile.Ipv4.Dns));
                text.AppendLine("      },");
                text.AppendLine("      \"proxy\": {");
                text.AppendLine("        \"enabled\": " + Bool(profile.Proxy.Enabled) + ",");
                text.AppendLine("        \"server\": " + Quote(profile.Proxy.Server) + ",");
                text.AppendLine("        \"exceptions\": " + StringList(profile.Proxy.Exceptions) + ",");
                text.AppendLine("        \"bypassLocal\": " + Bool(profile.Proxy.BypassLocal));
                text.AppendLine("      }");
                text.AppendLine(i < profiles.Count - 1 ? "    }," : "    }");
            }

            text.AppendLine("  ]");
            text.AppendLine("}");
            return text.ToString();
        }

        private static string Text(IDictionary<string, object> source, string name)
        {
            object? value;
            return source.TryGetValue(name, out value) && value != null
                ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
                : "";
        }

        private static bool Flag(IDictionary<string, object> source, string name, bool fallback)
        {
            object? value;
            if (!source.TryGetValue(name, out value) || value == null) return fallback;
            if (value is bool) return (bool)value;
            throw new FormatException("Файл профилей повреждён: поле «" + name + "» должно быть true или false.");
        }

        private static IDictionary<string, object> Section(IDictionary<string, object> source, string name)
        {
            object? value;
            if (!source.TryGetValue(name, out value) || value == null) return new Dictionary<string, object>();

            var section = value as IDictionary<string, object>;
            if (section == null) throw new FormatException("Файл профилей повреждён: поле «" + name + "» должно быть объектом.");
            return section;
        }

        private static IEnumerable List(IDictionary<string, object> source, string name)
        {
            object? value;
            if (!source.TryGetValue(name, out value) || value == null) return new object[0];

            var list = value as object[];
            if (list == null) throw new FormatException("Файл профилей повреждён: поле «" + name + "» должно быть списком.");
            return list;
        }

        private static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        private static string StringList(IList<string> items)
        {
            var quoted = new List<string>();
            foreach (string item in items) quoted.Add(Quote(item));
            return "[" + string.Join(", ", quoted) + "]";
        }

        private static string Quote(string value)
        {
            var text = new StringBuilder("\"");
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            text.Append(c);
                        break;
                }
            }
            return text.Append('"').ToString();
        }
    }
}
