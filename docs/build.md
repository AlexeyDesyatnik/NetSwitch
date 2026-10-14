# Сборка

## Что нужно

- Windows.
- **.NET SDK** любой современной версии (проверено на 9). Проекты в SDK-формате (`Microsoft.NET.Sdk`), поэтому одного MSBuild из Visual Studio мало: без SDK сборка падает с `Could not resolve SDK "Microsoft.NET.Sdk"`. Поставить можно любым способом:
  - `winget install Microsoft.DotNet.SDK.9`;
  - или в Visual Studio Installer — нагрузка «Разработка классических приложений .NET» (.NET desktop development).

  После установки откройте новый терминал: `dotnet` появится в `PATH` только в нём.
- Доступ к nuget.org при первой сборке. Ставить targeting pack .NET Framework 4.0 не нужно: эталонные сборки приходят пакетом `Microsoft.NETFramework.ReferenceAssemblies`.
- Для запуска тестов — установленный .NET Framework 4.8: тесты собираются под `net48` (xUnit не работает на `net40`), см. комментарий в [NetSwitch.Tests.csproj](../tests/NetSwitch.Tests/NetSwitch.Tests.csproj).

## Собрать

Из корня репозитория:

```
dotnet build NetSwitch.sln -c Release
```

Готовый инструмент — один файл `src/NetSwitch/bin/Release/net40/NetSwitch.exe` (около 100 КБ). В Release рядом с ним нет ни `.pdb`, ни `.exe.config`: так и задумано, инструмент носят на флешке одним exe ([ADR 0001](adr/0001-csharp-winforms-net-framework-4.md)). Именно этот файл кладётся на флешку для [ручной проверки](manual-check.md).

Если запустить exe прямо из каталога сборки, он создаст рядом `NetSwitch.json` и папку `Отчёты` — папка уже в `.gitignore`.

## Тесты

```
dotnet test NetSwitch.sln
```

Тесты с категорией `Machine` читают настройки компьютера, на котором запущены (ничего не меняют, прав администратора не требуют). Без них:

```
dotnet test NetSwitch.sln --filter "Category!=Machine"
```

## Visual Studio

`NetSwitch.sln` открывается в Visual Studio 2022 с нагрузкой «Разработка классических приложений .NET»; сборка и тесты — штатные.
