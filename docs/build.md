# Сборка

## Что нужно

- Windows.
- **.NET SDK 8 или новее** (проверено на 10). Проекты в SDK-формате (`Microsoft.NET.Sdk`), поэтому одного MSBuild из Visual Studio мало: без SDK сборка падает с `Could not resolve SDK "Microsoft.NET.Sdk"`. Поставить можно любым способом:
  - `winget install Microsoft.DotNet.SDK.10`;
  - или в Visual Studio Installer — нагрузка «Разработка классических приложений .NET» (.NET desktop development).

  После установки откройте новый терминал: `dotnet` появится в `PATH` только в нём.
- Доступ к nuget.org при первой сборке. Ставить targeting pack .NET Framework 4.0 не нужно: эталонные сборки приходят пакетом `Microsoft.NETFramework.ReferenceAssemblies`.
- Для запуска тестов — установленный .NET Framework 4.8: тесты собираются под `net48` (xUnit не работает на `net40`), см. комментарий в [NetSwitch.Tests.csproj](../tests/NetSwitch.Tests/NetSwitch.Tests.csproj).

## Собрать

Из корня репозитория:

```
dotnet build NetSwitch.sln -c Release
```

Готовый инструмент — один файл `dist/NetSwitch.exe` в корне репозитория (около 100 КБ). В Release рядом с ним нет ни `.pdb`, ни `.exe.config`: так и задумано, инструмент носят на флешке одним exe ([ADR 0001](adr/0001-csharp-winforms-net-framework-4.md)). Именно этот файл кладётся на флешку для [ручной проверки](manual-check.md).

Если запустить exe прямо из `dist/`, он создаст рядом `NetSwitch.json` и папку `Отчёты`; `dist/` целиком в `.gitignore`.

Промежуточные файлы сборки обоих проектов (`bin`, `obj`) лежат в `artifacts/` в корне, а не внутри `src/` и `tests/` — это задаёт [Directory.Build.props](../Directory.Build.props). Каталог `artifacts/` можно удалять целиком.

Локальная сборка называет себя версией `0.0.0-dev` — это видно в заголовке окна и в отчёте. Настоящий номер получает только сборка релиза, см. ниже.

## Тесты

```
dotnet test NetSwitch.sln
```

Тесты с категорией `Machine` читают настройки компьютера, на котором запущены (ничего не меняют, прав администратора не требуют). Без них:

```
dotnet test NetSwitch.sln --filter "Category!=Machine"
```

## Visual Studio

`NetSwitch.sln` открывается в Visual Studio 2022 (17.8 или новее) с нагрузкой «Разработка классических приложений .NET»; сборка и тесты — штатные.

## Выпуск релиза

Релизы собирает GitHub Actions ([build.yml](../.github/workflows/build.yml)). На каждый push в `main` он собирает Release и прогоняет тесты без категории `Machine`; собранный exe можно скачать со страницы запуска (раздел Artifacts) — это сборка `0.0.0-dev`.

Чтобы выпустить версию, поставьте на нужный коммит в `main` аннотированный тег `vX.Y.Z` — его текст станет описанием релиза — и отправьте тег на GitHub:

```
git tag -a v0.2.0 -m "Описание изменений"
git push origin v0.2.0
```

Номер версии берётся только из тега, в csproj его нет. Тег не вида `vX.Y.Z` (или `vX.Y.Z-суффикс`) сборка отвергает. Workflow соберёт exe с этой версией, прогонит тесты и создаст **черновик** релиза с файлом `NetSwitch.exe`; SHA-256 файла GitHub показывает рядом с ним. Версии `0.x` и с суффиксом помечаются как пре-релиз. Черновик виден только владельцу репозитория: проверьте его на странице Releases и нажмите «Publish release».

Файл в релизе называется просто `NetSwitch.exe`, без номера версии: новая версия на флешке заменяет старую, а `NetSwitch.json` рядом остаётся. Exe не подписан, поэтому при скачивании и первом запуске Windows SmartScreen предупреждает о неизвестном издателе.

Если сборка по тегу упала, исправьте ошибку, удалите тег (`git push --delete origin v0.2.0`, `git tag -d v0.2.0`) и поставьте его заново.
