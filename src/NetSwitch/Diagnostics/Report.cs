using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NetSwitch.Diagnostics
{
    /// <summary>
    /// Отчёт об одном управляемом компьютере. Сбой при сборе одного раздела записывается в отчёт
    /// и не мешает остальным разделам.
    /// </summary>
    internal sealed class Report
    {
        private readonly List<string> _lines = new List<string>();
        private int _depth;

        public IList<string> Lines
        {
            get { return _lines; }
        }

        public bool HasErrors { get; private set; }

        public string Text
        {
            get { return string.Join(Environment.NewLine, _lines) + Environment.NewLine; }
        }

        public void Line(string text)
        {
            _lines.Add(new string(' ', _depth * 2) + text);
        }

        public void Value(string name, object? value)
        {
            Line(name + ": " + Format(value));
        }

        public void Section(string title, Action body)
        {
            if (_depth == 0)
            {
                if (_lines.Count > 0) _lines.Add("");
                Line("== " + title + " ==");
            }
            else
            {
                Line("[" + title + "]");
            }

            _depth++;
            try
            {
                body();
            }
            catch (Exception ex)
            {
                Error(ex);
            }
            finally
            {
                _depth--;
            }
        }

        public void Error(Exception ex)
        {
            HasErrors = true;
            Line("ОШИБКА: " + ex.GetType().FullName + ": " + ex.Message);
            foreach (string frame in (ex.StackTrace ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Line("  " + frame.Trim());
            }
            if (ex.InnerException != null)
            {
                Line("Вызвано:");
                Error(ex.InnerException);
            }
        }

        internal static string Format(object? value)
        {
            if (value == null) return "(нет)";

            var text = value as string;
            if (text != null) return text.Length == 0 ? "(пусто)" : text;

            var bytes = value as byte[];
            if (bytes != null) return bytes.Length == 0 ? "(пусто)" : BitConverter.ToString(bytes).Replace('-', ' ');

            var array = value as Array;
            if (array != null)
            {
                if (array.Length == 0) return "(пусто)";
                var items = new List<string>();
                foreach (object? item in array) items.Add(Format(item));
                return string.Join(", ", items);
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }
    }

    /// <summary>Хранение отчётов рядом с инструментом: по файлу на каждый снятый отчёт.</summary>
    internal static class ReportFile
    {
        public const string FolderName = "Отчёты";

        public static string NameFor(string computer, DateTime when)
        {
            var name = new StringBuilder();
            foreach (char c in computer.Trim())
            {
                name.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            }
            if (name.Length == 0) name.Append("компьютер");

            return name + "_" + when.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + ".txt";
        }

        /// <summary>Сохраняет отчёт, не затирая уже существующие, и возвращает путь к файлу.</summary>
        public static string Save(string baseFolder, string computer, DateTime when, string text)
        {
            string folder = Path.Combine(baseFolder, FolderName);
            Directory.CreateDirectory(folder);

            string name = NameFor(computer, when);
            string path = Path.Combine(folder, name);
            for (int n = 2; File.Exists(path); n++)
            {
                path = Path.Combine(folder, Path.GetFileNameWithoutExtension(name) + "_" + n + ".txt");
            }

            // С BOM, чтобы Блокнот на старых Windows не принял файл за ANSI
            File.WriteAllText(path, text, new UTF8Encoding(true));
            return path;
        }
    }
}
