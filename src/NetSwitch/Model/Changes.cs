using System;
using System.Collections.Generic;
using System.Globalization;

namespace NetSwitch.Model
{
    /// <summary>Что нужно применить. Части, равные null, не затрагиваются.</summary>
    internal sealed class ChangeRequest
    {
        public string Title { get; set; } = "";
        public Adapter? Adapter { get; set; }
        public Ipv4Settings? Ipv4 { get; set; }
        public SystemProxyState? SystemProxy { get; set; }
        public ProxySettings? WinHttp { get; set; }

        /// <summary>
        /// Применение профиля или ручной правки: одни настройки прокси идут и в системный прокси,
        /// и в WinHTTP, а автоопределение и сценарий выключаются, чтобы результат был однозначным.
        /// </summary>
        /// <param name="current">Нужен, чтобы при выключении прокси не стереть запомненный адрес сервера.</param>
        public static ChangeRequest For(string title, Adapter? adapter, Ipv4Settings? ipv4, ProxySettings? proxy, ProxyState? current)
        {
            var request = new ChangeRequest { Title = title };

            if (adapter != null && ipv4 != null)
            {
                request.Adapter = adapter;
                request.Ipv4 = ipv4.Clone();
            }

            if (proxy != null)
            {
                ProxySettings system;
                if (proxy.Enabled || current == null)
                {
                    system = proxy.Clone();
                }
                else
                {
                    system = current.System.Settings.Clone();
                    system.Enabled = false;
                }

                request.SystemProxy = new SystemProxyState { Settings = system };
                request.WinHttp = proxy.Enabled ? proxy.Clone() : new ProxySettings();
            }

            return request;
        }
    }

    internal sealed class StepResult
    {
        public StepResult(string title, string? error)
        {
            Title = title;
            Error = error;
        }

        public string Title { get; private set; }
        public string? Error { get; private set; }

        public bool Ok
        {
            get { return Error == null; }
        }
    }

    /// <summary>
    /// Применение настроек по шагам: включить адаптер → IPv4 → системный прокси → WinHTTP-прокси.
    /// Сбой шага не останавливает следующие. Помнит, что было до последнего применения, для отката.
    /// </summary>
    internal sealed class Applier
    {
        private readonly INetworkSystem _system;
        private ChangeRequest? _undo;

        public Applier(INetworkSystem system)
        {
            _system = system;
            Log = new List<string>();
        }

        /// <summary>Все выполненные шаги с результатами — для отчёта.</summary>
        public List<string> Log { get; private set; }

        public bool CanRollback
        {
            get { return _undo != null; }
        }

        /// <summary>То, что вернёт откат; null, если откатывать нечего.</summary>
        public ChangeRequest? Undo
        {
            get { return _undo; }
        }

        /// <summary>Если прежние настройки прочитать не удалось, ничего не меняется и исключение уходит наружу.</summary>
        public IList<StepResult> Apply(ChangeRequest request)
        {
            ChangeRequest undo = Snapshot(request);
            IList<StepResult> results = Run(request);
            _undo = undo;
            return results;
        }

        /// <summary>Включение и выключение адаптера откату не подлежит и прежние настройки не затирает.</summary>
        public StepResult SetAdapterEnabled(Adapter adapter, bool enabled)
        {
            return Step((enabled ? "Включение" : "Выключение") + " адаптера «" + adapter.Name + "»", () => _system.SetAdapterEnabled(adapter, enabled));
        }

        public IList<StepResult> Rollback()
        {
            if (_undo == null) throw new InvalidOperationException("Откатывать нечего.");

            ChangeRequest undo = _undo;
            _undo = null;
            return Run(undo);
        }

        private ChangeRequest Snapshot(ChangeRequest request)
        {
            var undo = new ChangeRequest { Title = "Откат: " + request.Title };

            if (request.Adapter != null && request.Ipv4 != null)
            {
                Adapter adapter = Fresh(request.Adapter);
                undo.Adapter = adapter;
                undo.Ipv4 = adapter.Configured.Clone();
            }

            if (request.SystemProxy != null || request.WinHttp != null)
            {
                ProxyState proxy = _system.ReadProxy();
                if (request.SystemProxy != null) undo.SystemProxy = proxy.System.Clone();
                if (request.WinHttp != null) undo.WinHttp = proxy.WinHttp.Clone();
            }

            return undo;
        }

        private IList<StepResult> Run(ChangeRequest request)
        {
            var results = new List<StepResult>();
            Log.Add(Now() + " | " + request.Title);

            if (request.Adapter != null && request.Ipv4 != null)
            {
                Adapter adapter = Fresh(request.Adapter);
                Ipv4Settings ipv4 = request.Ipv4;

                if (adapter.State == AdapterState.Disabled)
                {
                    results.Add(Step("Включение адаптера «" + adapter.Name + "»", () => _system.SetAdapterEnabled(adapter, true)));
                }
                results.Add(Step("Настройки IPv4 адаптера «" + adapter.Name + "»", () => _system.ApplyIpv4(adapter, ipv4)));
            }

            if (request.SystemProxy != null)
            {
                SystemProxyState state = request.SystemProxy;
                results.Add(Step("Системный прокси", () => _system.ApplySystemProxy(state)));
            }

            if (request.WinHttp != null)
            {
                ProxySettings settings = request.WinHttp;
                results.Add(Step("WinHTTP-прокси", () => _system.ApplyWinHttpProxy(settings)));
            }

            return results;
        }

        private StepResult Step(string title, Action action)
        {
            string? error = null;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            Log.Add(Now() + " |   " + title + " — " + (error == null ? "выполнено" : "ОШИБКА: " + error));
            return new StepResult(title, error);
        }

        // Между чтением и применением состояние адаптера могло измениться
        private Adapter Fresh(Adapter adapter)
        {
            foreach (Adapter candidate in _system.ReadAdapters())
            {
                if (string.Equals(candidate.Id, adapter.Id, StringComparison.OrdinalIgnoreCase)) return candidate;
            }
            throw new InvalidOperationException("Адаптер «" + adapter.Name + "» больше не найден.");
        }

        private static string Now()
        {
            return DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }
    }
}
