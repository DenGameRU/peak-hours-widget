# Peak Hours — виджет Windows 11 (Win+W)

Нативный виджет панели **Widgets** (`Win + W`), который показывает окна пиковой нагрузки

```
Weekdays 01:00–04:00, 06:00–10:00 UTC
```

**в часовом поясе, настроенном в Windows**. Часовой пояс определяется автоматически через
`TimeZoneInfo.Local` — ничего настраивать не нужно.

## Что показывает

- Локальное время окон с учётом смещения и переноса через полночь/другой день недели.
- Подписи дней недели: `Weekdays`, `Daily`, `Sun–Thu`, `Mon–Fri` и т.п.
- Крупную жирную надпись состояния:
  - **`PEAK`** — красная,
  - **`NOT PEAK`** — зелёная.
- Строку деталей: `Peak now — ends HH:mm` или `Next peak in 2h 15m · Mon 09:00`.
- Название часового пояса и смещение, например `Russian Standard Time · UTC+03:00`.
- Блоки **CHINA** и **NewYork** с их текущим временем (UTC+8 и US Eastern с учётом DST).
- Кнопки **Refresh**, **CHINA …**, **NewYork …** — любая обновляет данные сразу, не дожидаясь таймера.

Автообновление — **раз в 10 минут** (плюс сразу при открытии панели и по кнопке Refresh).
Пока виджет не виден, обновления не отправляются.

Пример для UTC+3: `Weekdays 04:00–07:00` и `Weekdays 09:00–13:00`.
Для UTC−5: `Sun–Thu 20:00–23:00` и `Mon–Fri 01:00–05:00`.

## Требования

- Windows 11 22H2 или новее (панель Widgets).
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — только для сборки из исходников.
- Windows App SDK runtime `Microsoft.WindowsAppRuntime.1.6` — на Windows 11 обычно уже установлен.
  Проверить: `Get-AppxPackage Microsoft.WindowsAppRuntime.1.6`.
  Если нет — [Windows App SDK downloads](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).
- Visual Studio **не обязательна**, сборка идёт через `dotnet`.
- Права администратора — только для установки self-signed сертификата и sideload'а MSIX.

> В проекте зафиксирован `Microsoft.WindowsAppSDK 1.6.241114003` — стабильная версия с API Widgets.
> При наличии интернета можно поднять версию в `src/PeakHoursWidget/PeakHoursWidget.csproj`.

---

## Вариант A. Установить готовый MSIX из Releases

1. Скачать из раздела **Releases** два файла в одну папку:
   - `PeakHoursWidget_1.0.6.0_x64.msix`
   - `PeakHoursWidget.cer`
2. Открыть **PowerShell от имени администратора** в этой папке и выполнить:

```powershell
Import-Certificate .\PeakHoursWidget.cer -CertStoreLocation Cert:\LocalMachine\Root
Import-Certificate .\PeakHoursWidget.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage .\PeakHoursWidget_1.0.6.0_x64.msix
```

3. Перейти к шагу [«Закрепление виджета»](#закрепление-виджета-обязательный-разовый-шаг).

Если пакет уже установлен и нужно обновить — просто удалите старый перед установкой:

```powershell
Get-AppxPackage PeakHoursWidget | Remove-AppxPackage
```

---

## Вариант B. Сборка из исходников (рекомендуется)

Из корня репозитория:

```powershell
# 1) картинки уже лежат в репозитории; перегенерировать их можно так:
python tools\generate_assets.py

# 2) собрать, подписать и установить
powershell -ExecutionPolicy Bypass -File .\build-and-install.ps1
```

Скрипт `build-and-install.ps1` делает всё сам:

1. Создаёт self-signed сертификат `CN=PeakHoursWidget` (если ещё нет) в `Cert:\CurrentUser\My`.
2. Добавляет его в `Cert:\LocalMachine\Root`, `Cert:\LocalMachine\TrustedPeople`
   и `Cert:\CurrentUser\TrustedPeople` — без этого Windows не разрешит sideload.
3. Выполняет `dotnet publish` с `-p:PublishAppxPackage=true` и подписывает пакет.
4. Удаляет предыдущую версию (если есть) и ставит новую через `Add-AppxPackage`.

Готовый пакет: `artifacts\PeakHoursWidget_<версия>_x64_Test\PeakHoursWidget_<версия>_x64.msix`.

Только собрать, не устанавливая:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-and-install.ps1 -NoInstall
```

Сборка без скрипта (если сертификат уже доверен):

```powershell
dotnet publish src\PeakHoursWidget\PeakHoursWidget.csproj `
  -c Release -r win-x64 --self-contained false `
  -p:PublishAppxPackage=true `
  -p:AppxPackageDir=artifacts\ `
  -p:AppxPackageSigningEnabled=true `
  -p:PackageCertificateThumbprint=<THUMBPRINT>
```

### Частые ошибки сборки

| Симптом | Лечение |
|---|---|
| `NU1301: Не удалось загрузить индекс службы` | Нет интернета/NuGet недоступен. Пакеты должны быть в `%USERPROFILE%\.nuget\packages`, либо restore с доступом в сеть. |
| `0x800B0109` при `Add-AppxPackage` | Сертификат не в `LocalMachine\Root`. Запустите скрипт от администратора. |
| Скрипт не запускается | `powershell -ExecutionPolicy Bypass -File .\build-and-install.ps1`. |
| Нет `mspdbcmf.exe` | Предупреждение о symbols-пакете, на работоспособность не влияет. |

## Закрепление виджета (обязательный разовый шаг)

Windows не позволяет приложению закрепить себя в панели программно — это делает пользователь:

1. Нажмите `Win + W` — откроется панель Widgets.
2. **Add widgets** / **Добавить мини-приложения**.
3. Найдите **Peak Hours (local time)** → **Pin**.
4. Готово: теперь `Win + W` показывает виджет.

> После обновления версии пакета закрепление может сброситься — повторите шаги выше.
> Если `Pin` не реагирует, перезапустите панель: в диспетчере задач завершите
> `Widgets` и `WidgetService`, затем снова нажмите `Win + W`.

## Обновление и удаление

```powershell
# обновить (собрать новую версию и поставить поверх)
powershell -ExecutionPolicy Bypass -File .\build-and-install.ps1

# удалить
Get-AppxPackage PeakHoursWidget | Remove-AppxPackage

# удалить доверенный сертификат (по желанию)
Get-ChildItem Cert:\LocalMachine\Root | Where-Object Subject -eq 'CN=PeakHoursWidget' | Remove-Item
Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=PeakHoursWidget' | Remove-Item
```

При изменении кода обязательно увеличьте `Version` в `Package.appxmanifest`
(например `1.0.6.0` → `1.0.7.0`) — иначе `Add-AppxPackage` посчитает пакет тем же.

## Настройка расписания

В `src\PeakHoursWidget\PeakHoursWidget.cs`:

```csharp
private static readonly (int StartHour, int EndHour)[] UtcWindows =
{
    (1, 4),   // 01:00–04:00 UTC
    (6, 10),  // 06:00–10:00 UTC
};
```

Часы задаются **в UTC**, дни — будни (Пн–Пт UTC). После изменения — пересоберите
(см. «Обновление и удаление»).

Интервал автообновления задаётся в `src\PeakHoursWidget\WidgetProvider.cs`:

```csharp
_refreshTimer ??= new Timer(
    _ => RefreshAll(),
    null,
    TimeSpan.FromSeconds(15),   // первый запуск
    TimeSpan.FromMinutes(10));  // период
```

## Как это работает

COM-провайдер виджетов на Windows App SDK (`IWidgetProvider`):

- `Package.appxmanifest` регистрирует:
  - `windows.comServer` → `PeakHoursWidget.exe -RegisterProcessAsComServer`;
  - `windows.appExtension` с именем `com.microsoft.windows.widgets`,
    definition `PeakHoursWidget.Definition`.
- При закреплении хост вызывает `CreateWidget`; провайдер отправляет Adaptive Card
  из `Templates\PeakHoursTemplate.json` и JSON с данными через `WidgetManager.UpdateWidget`.
- Процесс запускается хостом по требованию (COM activation) и завершается, когда COM-класс отзывается.
- Приложение собрано как `WinExe` и вызывает `FreeConsole()` — чёрное окно консоли не появляется.
- Шаблон отправляется один раз, дальше только данные; расписание кешируется на день.
- Включён `InvariantGlobalization`: не грузится ICU. Working Set ~34 МБ,
  приватная память ~7.6 МБ (было 55 / 11 МБ).

## Структура репозитория

```
.
├── build-and-install.ps1              # сборка + подпись + установка
├── PeakHoursWidget.sln
├── README.md
├── tools/
│   └── generate_assets.py             # генератор PNG-иконок (pure stdlib)
└── src/PeakHoursWidget/
    ├── PeakHoursWidget.csproj
    ├── Package.appxmanifest
    ├── Program.cs                     # точка входа COM-сервера
    ├── WidgetProvider.cs              # IWidgetProvider, таймер
    ├── PeakHoursWidget.cs             # расчёт локального времени и данные карточки
    ├── WidgetImplBase.cs
    ├── Diag.cs                        # лог в %TEMP%\PeakHoursWidget.log
    ├── WidgetHelper/                  # COM class factory / регистрация
    ├── Templates/PeakHoursTemplate.json
    ├── Assets/                        # иконки пакета
    └── ProviderAssets/                # иконка и скриншот виджета
```

## Диагностика

Лог пишется в `%TEMP%\PeakHoursWidget.log`:

```powershell
Get-Content "$env:TEMP\PeakHoursWidget.log" -Tail 50
```

Там видны запуск провайдера, `CreateWidget`, `Activate`, `OnActionInvoked` и ошибки.

| Симптом | Что проверить |
|---|---|
| Виджет не появляется в Add widgets | Панель перезапущена? `Widgets`/`WidgetService` в диспетчере задач. |
| `Pin` не реагирует | Удалите старый экземпляр виджета, перезапустите `Widgets`/`WidgetService`. |
| Карточка пустая | Откройте `%TEMP%\PeakHoursWidget.log`, проверьте `CreateWidget` и ошибки `UpdateWidget`. |
| Неверное время | `Get-TimeZone` — какой пояс видит система. |

## Известные нюансы

- Иконки в `Assets/` и `ProviderAssets/` — заглушки из `tools/generate_assets.py`;
  при желании замените своими (StoreLogo 50×50, Square44 44×44 / 88×88 / 24×24,
  Square150 300×300, Wide310 620×300, SplashScreen 1240×600, иконка виджета 100×100,
  скриншот 400×200).
- Подпись self-signed сертификатом пригодна только для локального sideloading'а.
- DST учитывается через `TimeZoneInfo.ConvertTimeFromUtc`; нестандартные пояса с несколькими
  сменами смещения в неделю могут требовать доработки группировки дней.

## Лицензия

MIT — см. [LICENSE](LICENSE).
