# Технический план v0.2
## Полное поглощение OneClickRunner функциональностью CyrFlip

**Статус:** Утверждён (2026-07-25). Спецификация
[OneClickRunner_Absorption_Spec_Idea_v0.1.md](OneClickRunner_Absorption_Spec_Idea_v0.1.md)
подтверждена как v0.2; решения по её §14 внесены сюда (см. правки в §2.3, §7 и добавленный
хоткей-пункт в §3). Сверх исходного объёма добавлен один пункт: **необязательный глобальный хоткей
на сценарий** (решение пользователя; спецификация §5.1/§13).

**Источник:** `P:\WINDOWS\OneClickRunner\` на 2026-07-25.

**Цель аудита:** ни одна пользовательская функция, контракт данных, визуальный asset, документ или
проверяемое решение OneClickRunner не должны потеряться молча. Для каждого артефакта ниже указано
назначение: перенос, адаптация, архивирование или сознательное исключение с причиной.

---

## 1. Зафиксированная модель результата

CyrFlip остаётся одним `net48` WinForms EXE и владельцем своего `Local\CyrFlipSingleInstance` mutex,
единственного tray icon, автозапуска, версии, MSIX/Store и winget. Внутри него появляется отключённый
по умолчанию модуль **Launcher** с полным функциональным эквивалентом OneClickRunner.

```text
OneClickRunner.exe (WPF/.NET 8)       CyrFlip.exe (WinForms/.NET Framework 4.8)
───────────────────────────────       ─────────────────────────────────────────
свой tray icon                         существующий tray → подменю Launcher
свой Settings window                   SettingsForm → вкладка Launcher
свой mutex + named pipe                mutex CyrFlip + launcher named pipe
свой Jump List                         Jump List CyrFlip
%APPDATA%\OneClickRunner               %APPDATA%\CyrFlip\Scenarios
```

Не создаются второй EXE, второй процесс в фоне, второй запуск с Windows, отдельная версия,
отдельный GitHub Release или отдельный сайт. Это не потеря функции: соответствующие сервисы уже есть
у CyrFlip и должны быть единственными источниками этого поведения.

## 2. Полный инвентарь источника и решение по нему

### 2.1 Рабочий код

| Исходный артефакт | Что в нём проверено | Решение в CyrFlip |
| --- | --- | --- |
| `OneClickRunner/App.xaml`, `App.xaml.cs` | ручной startup, mutex, `/run:{guid}` / `/settings` / `/exit`, pipe dispatch, one-shot launch, refresh обоих surfaces, disposal | Адаптировать в `Program.cs`, `CyrFlipContext.cs` и `LauncherIpcService.cs`; никакого WPF `Application`. |
| `Models/AppItem.cs` | XML-контракт, Guid, порядок, `Executable`/`YtDlp`, clone | Перенести как независимую `LauncherScenario.cs`, сохранив имена сериализуемых полей. |
| `Services/ConfigurationService.cs` | файл на сценарий, first-run marker, seed, legacy sentinel, order normalization | Перенести как `LauncherScenarioStore.cs`, сменив лишь владельца данных на CyrFlip и добавив миграцию. |
| `Services/ScenarioLauncher.cs` | единый launch path, валидация файла/URL/PATH, UAC, yt-dlp, результат запуска | Перенести по смыслу в `LauncherExecutionService.cs`; его вызывают все новые UI/IPC entry points. |
| `Services/ScriptInterpreter.cs` | `.ps1`, `.bat`, `.cmd`, PowerShell 7 preference и корректное quoting | Перенести в `LauncherScriptInterpreter.cs` без ослабления quoting. |
| `Services/ScenarioIconResolver.cs` | иконка EXE, интерпретатора или fallback; исключает пустые Jump List tasks | Перенести в `LauncherIconResolver.cs`; fallback — текущая иконка CyrFlip. |
| `Services/JumpListService.cs` | упорядоченные Jump Tasks, icon resource, отсутствие Recent/Frequent | Переписать под WinForms/net48 в `LauncherJumpList.cs` через поддерживаемый Shell API; функциональный контракт сохраняется. |
| `Services/TrayIconService.cs` | сценарии в том же порядке, Settings/Exit, disposal старого menu | Адаптировать в подменю существующего `_tray` внутри `CyrFlipContext`; второй `NotifyIcon` не переносить. |
| `Services/PipeIpcService.cs` | изолированная ошибка одного подключения, cancellation, timeout, UI dispatch | Перенести как `LauncherIpcService.cs`, не блокируя UI или keyboard hook. |
| `Services/AutostartService.cs` | Run key и путь текущего EXE | Не дублировать: `Autostart.cs` CyrFlip уже покрывает это и учитывает MSIX. В документации объяснить эквивалент. |
| `Services/ThemeService.cs`, `Themes/*` | OS light/dark, live refresh, стили WPF controls | Не переносить как отдельную функцию: WPF theme не переносима и не является поведением launcher. Новый WinForms UI обязан следовать системным цветам и не ухудшать существующий `SettingsForm`; отдельная UI-тема — отдельная будущая задача CyrFlip. |
| `Services/LoggingService.cs` | serialised `%APPDATA%` activity log | Адаптировать в `LauncherLog.cs` под каталог CyrFlip; не писать ссылку yt-dlp и данные clipboard. |
| `Services/VersionService.cs` | build-stamped version в заголовке | Не дублировать: использовать уже штампуемую версию CyrFlip. |
| `MainWindow.xaml`, `.xaml.cs` | список, search, CRUD, hotkeys, context menu, autostart, lifecycle | Полностью перенести поведение в `LauncherSettingsPanel.cs` внутри `SettingsForm`; autostart строку заменить пояснением о существующем общем autostart. |
| `Windows/AppItemDialog.*` | оба типа сценария, выбор файлов/папок, validation, edit-copy semantics | Перенести как `LauncherScenarioDialog.cs`. |
| `Windows/LinkInputDialog.*` | обязательная ссылка, cancel | Перенести как `YtDlpLinkDialog.cs` на WinForms. |
| `Windows/NotificationWindow.*` | 7-second, dismissible failure notice, non-blocking live process | Адаптировать в `LauncherFailureNotice.cs` либо в существующий notification primitive CyrFlip, если он появится раньше; не заменять ошибку молчанием. |
| `Converters/PathToIconConverter.cs` | кэш извлечённых file icons в таблице | Перенести логику в `LauncherIconCache.cs`/owner-draw список; `ImageSource` и WPF converter не переносить. |
| `AssemblyInfo.cs` | WPF resource lookup | Не нужен в WinForms. |

### 2.2 Сценарии, данные и тестовые образцы

| Источник | Содержимое | Решение |
| --- | --- | --- |
| `%APPDATA%\OneClickRunner\Scenarios\*.xml` (runtime contract) | реальные пользовательские сценарии | Одноразовый безопасный import в `%APPDATA%\CyrFlip\Scenarios`; source read-only, никогда не удаляется. |
| `OneClickRunner/Scenarios/*.xml` (9 repo samples) | Calculator, RDP, Hyper-V, facefusion, legacy `SPECIAL_YTDLP`; в части файлов есть персональные пути/названия | Побайтно сохранить в `tests/CyrFlip.Tests/Fixtures/OneClickRunner/` как migration fixtures. Не устанавливать пользователям и не показывать как default список. |
| `run_windows_calculator.xml` | нейтральный пример первого запуска | Использовать только как логический шаблон seed-сценария; новый XML создаёт CyrFlip, а не копируется с зашитым Guid. |
| `ytdlp_download.xml` | legacy magic path | Обязательный migration regression test: sentinel превращается в `YtDlp` type. |

### 2.3 Иконки и другие визуальные assets

**Решение v0.2 (пользователь, 2026-07-25):** исходный репозиторий OneClickRunner публичен и жив —
полный архив сайта внутри CyrFlip не нужен. Копируются **только две иконки**; всё остальное
фиксируется манифестом (SHA-256 + ссылка на github.com/SerZhyAle/OneClickRunner).

| Исходный файл | Проверенное состояние | Решение |
| --- | --- | --- |
| `OneClickRunner/Assets/app.ico` | blue circle + white check; 30,567 bytes; SHA-256 prefix `ED283C87B666E1A1` | Не заменяет бренд CyrFlip. Сохранить exact copy в `assets/oneclickrunner-source/app.ico`; использовать только в archival/migration documentation. |
| `OneClickRunner/Assets/icon.png` | 256×256; 14,674 bytes; prefix `07B07D502A49CDEB` | Сохранить exact copy рядом с `app.ico`. |
| `Resources/GenerateIcon.ps1` | воспроизводит blue-check PNG; ICO делается внешним convert step | Не копировать (v0.2): зафиксировать в манифесте. Скрипт не становится генератором основной иконки CyrFlip. |
| `docs/assets/favicon.ico` | byte-identical `Assets/app.ico` | Не копировать (v0.2): дубликат `app.ico`, фиксируется манифестом. |
| `docs/assets/icon-{16,32,48,256,512}.png` | web variants; 256 is byte-identical `Assets/icon.png` | Не копировать (v0.2): фиксируются манифестом с хешами. |
| `docs/assets/social-preview.png` | 1200×630, 101,303 bytes; prefix `D643781B5743E31E` | Не копировать (v0.2): фиксируется манифестом. Для страниц CyrFlip — собственный social preview с её брендом. |
| `docs/assets/sza-kit.css` | общий SZA web kit | Не копировать (v0.2): фиксируется манифестом; источник остаётся в живом репозитории OneClickRunner. |

Текущий `assets/cyrflip.ico` и все существующие CyrFlip icons сохраняют назначение. Launcher в трее
и Jump List использует иконку CyrFlip как fallback, поэтому старый OneClickRunner badge не выдаётся
за главный бренд другого приложения.

### 2.4 Документация, лицензия, сборка и workflow

| Источник | Решение |
| --- | --- |
| `README.md` | Перенести все пользовательские возможности в README EN/RU/UK CyrFlip: включение модуля, сценарии, surfaces, import, storage, security, yt-dlp, troubleshooting. Старые команды сборки/релиза OneClickRunner не копировать как инструкции CyrFlip. |
| `docs/index.html`, `docs/guide.html`, `docs/style.css`, `.nojekyll`, web assets | Не копировать (v0.2): страницы фиксируются манифестом; источник — живой публичный репозиторий. Создать/обновить равнозначный раздел Launcher на трёх страницах CyrFlip (`docs/index.html`, `docs/ru/index.html`, `docs/uk/index.html`) и трёх guide pages. Все ссылки должны вести на CyrFlip, не на скачивание OneClickRunner.exe. |
| `CHANGELOG.md` | Перенести функциональные пункты в release notes CyrFlip как «Launcher integrated»; исторический OneClickRunner changelog сохранить в archive с явной пометкой, что это не журнал CyrFlip. |
| `LICENSE` | Обе лицензии MIT. Перед началом сверить тексты/hashes; если идентичны, второй `LICENSE` не создавать. Если отличаются, legal attribution добавить в `NOTICE` и release docs. |
| `build.ps1`, `release.ps1`, `.sln`, `.csproj` | Не переносить .NET 8 single-file pipeline. Проверить, что CyrFlip build/release/MSIX/winget включают новые `.cs` и archival files корректно. Никакой .NET 8 runtime requirement не появляется. |
| `AGENTS.md`, `CLAUDE.md`, `.claude/agents/*`, `.claude/commands/*`, `doc/{SPEC_LIFECYCLE,VALIDATION,CODE_QUALITY,COST,RESEARCH_INDEX,AGENT_MEMORY}.md` | Это development-process documentation, а не функция приложения. Не дублировать конфликтующие правила в CyrFlip. Все инженерные решения, важные для launcher, фиксируются в этой спецификации, техническом плане и тестах; первичный источник остаётся нетронутым. |
| `PLAN/INDEX.md`, `PLAN/Done/T0001…T0024` | Не копировать 24 завершённых билета как активные задачи. Их решения перечислены в §3; в план добавить traceability test, который гарантирует, что ни один билет не остался без приемочного критерия. |
| `.git`, `.vs`, `bin`, `obj`, `temp/*` и `temp` release assets | Сгенерированные/локальные artefacts, не являются исходниками или документацией. Не переносить. |

## 3. Матрица функциональной трассируемости

Все 24 завершённых билета OneClickRunner покрываются следующими работами. Это контрольный список
перед закрытием, а не ориентировочный список идей.

| Source ticket / capability | Требование к реализации CyrFlip | Проверка |
| --- | --- | --- |
| T0001 elevation | `RunAsAdmin` — единственный источник UAC на каждом entry point | EXE non-admin / admin из panel, tray, Jump List. |
| T0002 resilient pipe, T0019 cleanup | ошибки connection не убивают listener; cancellation clean; нет старого dead server | искусственная неудачная connection, затем успешная команда. |
| T0003 safe yt-dlp, T0013 type | first-class type, link prompt, PATH check, persistent console, safe env transfer | ссылки с `&`, `|`, quote rejection, cancellation, output folder. |
| T0004 validation | file, URL, working dir and PATH validation до `Process.Start` | missing/relative/bare command cases. |
| T0005 ordering, T0018 zero scenarios | stored contiguous order; empty list stays empty after deleting all | reorder/restart/delete-all. |
| T0006 failure notice | visible short error from one-shot and live command without blocking settings | missing target from Jump List and tray. |
| T0007 run interaction | double click row runs only a row, not blank list/header | UI manual test. |
| T0008 export, T0015 clone, T0016 menu/keys, T0022 search | complete CRUD, XML round-trip, new id on import, context menu, Enter/F2/Del, filter by name/path | automated store tests + UI matrix. |
| T0009 tray | same ordered scenario list in tray and Jump List; settings/exit affordances | compare menus after each mutation. |
| T0010 user docs | README and guide match shipped behavior | documentation checklist / link test. |
| T0011 list fields, T0012 dialog layout | admin + working-dir visibility; resizable/usable editor | visual manual test at 100%/150% DPI. |
| T0014 service split, T0020 logging facade | UI stays thin; store/launch/IPC/jump list separate; serialized diagnostic log | static ownership review + log test. |
| T0017 autostart path | existing CyrFlip autostart uses actual executable path and remains the only setting | portable + MSIX regression. |
| T0021 icon resolver | no blank Jump List icons for scripts or unresolved targets | EXE, ps1, cmd, py, vbs, yt-dlp matrix. |
| T0023 theme | launcher UI readable in existing CyrFlip WinForms visual context | Windows light/dark manual matrix. |
| T0024 portable deploy | resulting CyrFlip portable output still starts as one EXE, no .NET 8 dependency | build/release package smoke test. |
| *(v0.2, сверх OneClickRunner)* per-scenario hotkey | optional `Hotkey` per scenario; hook snapshot identical to conversion table; 4-way conflict check (case / history / conversions / scenarios); active only while launcher + master switch on | hotkey-binding unit tests + manual chord launch. |

## 4. Порядок реализации

Каждая фаза производит то, что потребляет следующая; переход запрещён, пока её статические и
автоматические проверки не зелёные.

### Фаза 0 — зафиксировать исходник и archival contracts

**Производит:** `PLAN/OneClickRunner_Source_Manifest_v0.1.md`, exact-copy subtree для assets/web
archive и fixtures. Манифест содержит source relative path, SHA-256, classification, target path and
decision; не содержит персональных содержимых XML в prose.

**Действия:**

1. Зафиксировать hash всех source code, docs, assets, 9 XML fixtures, license и public metadata.
2. Скопировать exact web/icon assets и generator script в оговорённые archival paths.
3. Скопировать scenario XML только в test fixtures, пометив их private-path fixtures.
4. Сохранить HTML/CSS website source в archive без включения старых external release links в
   опубликованную навигацию CyrFlip.

**Проверка:** manifest содержит каждую versioned source file, кроме явно перечисленных generated/local
исключений; hashes archived binary/text copies совпадают с source; `git diff --check` чист.

### Фаза 1 — модель, хранилище, миграция и конфигурация

**Производит:** `LauncherScenario.cs`, `LauncherScenarioStore.cs`, `LauncherScenarioMigration.cs`,
`LauncherLog.cs`, расширенный `AppConfig` (`EnableScenarioLauncher` плюс marker migration) и unit tests.

**Действия:**

1. Реализовать XML model без сериализации UI/image/runtime полей.
2. Создать хранилище `%APPDATA%\CyrFlip\Scenarios`, isolated corrupted-file handling, normalized
   contiguous order, CRUD, clone and atomic per-file replacement.
3. Реализовать import/export: fresh Guid for ordinary import; preserved Guid iff no collision for
   OneClickRunner migration.
4. Реализовать explicit, non-destructive first-enable migration and import summary.
5. Не seed личные source examples; Calculator only through clean first-enable rule.

**Проверка:** tests execute every XML fixture, including `SPECIAL_YTDLP`, corrupt XML and duplicate ids;
all store operations leave items in deterministic order; test process uses disposable directory rather
than the developer's AppData.

### Фаза 2 — единый запуск и безопасность

**Производит:** `LauncherExecutionService.cs`, `LauncherScriptInterpreter.cs`,
`LauncherIconResolver.cs`, `LauncherLaunchResult.cs`, `YtDlpLinkDialog.cs` and unit tests.

**Действия:**

1. Port source target validation exactly: existing local file, http(s), working-directory-relative,
   PATH/PATHEXT; no speculative shell invocation.
2. Port interpreter resolution and command quoting exactly; explicitly retain PowerShell 7 lookup,
   Windows PowerShell fallback and `cmd /s /c` shape.
3. Model success, user cancellation and failure separately; `RunAsAdmin` alone chooses `runas`.
4. Port yt-dlp handling with `cmd /k`, isolated environment-variable link transfer, control/quote
   rejection, `PATH` detection and default Downloads folder.
5. Cache display icons separately from persisted scenario state; never require an icon extraction to
   launch a scenario.

**Проверка:** parameterized tests cover every type in source ticket matrix; an `&`-bearing URL reaches
the prepared launch structure as data, never as concatenated shell syntax; tests assert that private
link value is absent from log text.

### Фаза 3 — command lifecycle, IPC и native Jump List spike

**Производит:** `LauncherIpcService.cs`, command parser/routing seam in `Program.cs` and
`CyrFlipContext.cs`, `LauncherJumpList.cs`, a small proof-of-concept before bulk UI work.

**Действия:**

1. Reserve a CyrFlip-private protocol such as `/launcher-run:{guid}`, `/launcher-settings` and
   `/exit`; parse no other arguments as commands.
2. Modify the existing second-instance branch: it forwards only an accepted command over named pipe
   and exits. A first-instance `/launcher-run` performs OneClickRunner-equivalent one-shot launch
   without creating hook/tray; regular startup creates the normal context.
3. Listener dispatches onto the WinForms UI synchronization context, survives one malformed/failed
   connection and stops during `CyrFlipContext.Dispose`.
4. Implement a native Win32/Shell Jump List bridge appropriate for WinForms/net48; prove add, update
   and clear of one test task before wiring all scenarios. Do not add WPF just for `JumpList`.
5. Explicitly set task title, argument, application path, icon path/index and disable Recent/Frequent;
   include Manage scenarios and Exit tasks.
6. Rebuild on enable/disable, every store mutation, app start and graceful exit. Disabled state clears
   previous launcher tasks.

**Verification gate:** run a compiled proof on Windows 10/11 with a pinned CyrFlip icon: the task runs
the intended test scenario, a repeated task dispatches to the live instance, then disable removes the
task. If the OS API requires a stable AppUserModelID that conflicts with MSIX identity, stop here and
record the observed contract before changing production code.

### Фаза 4 — tray and settings surfaces

**Производит:** `LauncherSettingsPanel.cs`, `LauncherScenarioDialog.cs`, `LauncherFailureNotice.cs`
and small, explicit integration edits to `SettingsForm.cs`/`CyrFlipContext.cs`.

**Действия:**

1. Add the opt-in switch and a scalable Launcher tab, keeping `SettingsForm` as host rather than
   turning it into another large code-behind.
2. Port all list columns, search, CRUD buttons, keyboard/mouse/context actions and resizable add/edit
   dialog. Wire UI through store/service, never directly to XML or `Process.Start`.
3. Add a Launcher submenu to the existing tray, with scenario order identical to Jump List, Manage
   scenarios and migration action. Dispose/rebuild menu safely.
4. Localize all strings EN/RU/UK and make the new controls readable at normal and high DPI Windows
   light/dark settings.
5. Surface failed live launches non-modally; one-shot failures remain visible long enough to read.

**Проверка:** UI automation where practical plus manual matrix: 100%/150% DPI, all three languages,
no item / one item / many items, context actions, disabled mode and settings reopen.

### Фаза 5 — documentation, assets, metadata and delivery

**Производит:** updated README trio, six CyrFlip site pages, release/store/winget copy, migration
guide, archive manifest and archived source docs/assets.

**Действия:**

1. Write an end-user migration guide: enable module, choose source migration, locate CyrFlip XML,
   import/export, disable without loss, remove scenarios, troubleshooting log and yt-dlp prerequisite.
2. Update all trilingual public surfaces to use CyrFlip product name, current package links and exact
   storage paths. Explain that Launcher is optional and its old OneClickRunner source files remain
   untouched.
3. Add screenshots only after final UI is stable; regenerate CyrFlip social/store images rather than
   presenting OneClickRunner art as CyrFlip.
4. Update `CLAUDE.md` architecture, `msix/store-listing-export.csv`, derived store listing files,
   winget release notes and `CHANGELOG`/release content according to current CyrFlip release rules.
5. Check that `assets/oneclickrunner-source` and `docs/oneclickrunner-source` are not accidentally
   packaged into the distributable EXE/MSIX unless intentionally required as source archive.

**Проверка:** link checker on `docs/`; source page hashes match manifest; user-facing pages contain no
instruction to download `OneClickRunner.exe`, require .NET 8 or start a separate tray application.

### Фаза 6 — final verification and release regression

**Производит:** build/test evidence plus manual verification record (`expected | actual`).

**Действия:**

1. Run `dotnet build CyrFlip.sln -c Release` and `dotnet test CyrFlip.sln`.
2. Perform source traceability review against §3 and archive manifest review against §2.
3. Run portable and MSIX manual smoke tests, including existing CyrFlip hook, cursor restore,
   clipboard history and language settings so launcher integration cannot regress them.
4. Test first install, upgrade from an existing CyrFlip profile, OneClickRunner migration and full
   disable/re-enable lifecycle on a non-developer Windows account.

**Проверка:** no warning/error in build, all tests pass, all 24 traceability rows have evidence, and
manual Jump List delivery works in a real taskbar environment.

## 5. Required test inventory

| Area | Automated | Hands-on |
| --- | --- | --- |
| Store & migration | XML round trip, legacy fields, malformed files, Guid collision, seed marker, ordering | source app's existing scenarios import without source modification. |
| Execution | target resolution, command formation, interpreter choice, UAC decision, yt-dlp validation | calc/URL/cmd/ps1/python/vbs and one elevated scenario. |
| IPC | valid/invalid command, timeout, bad connection then good connection, disabled rejection | second-process Jump List dispatch and first-process one-shot dispatch. |
| Jump List | task specification/clear logic as pure tests where possible | icon appearance, script icons, task ordering, pinned/unpinned app, enable/disable. |
| UI | store/service behavior and localization key completeness | search, keyboard shortcuts, dialogs, DPI, light/dark, three languages. |
| Existing CyrFlip | current xUnit suite untouched and green | global hotkeys, caret overlay, history, autostart and MSIX startup task still work. |
| Docs/assets | hash manifest, internal link/forbidden obsolete-link checks | visual review of screenshots/social cards. |

## 6. Explicit non-regression rules

1. Never copy `OneClickRunner.exe` or target `net8.0-windows` into CyrFlip.
2. Never create two tray icons, two Run-key values or two keyboard hooks.
3. Never delete, rename or write a file in `%APPDATA%\OneClickRunner` during discovery or migration.
4. Never seed private RDP/Hyper-V/facefusion scenarios into a release build.
5. Never log the full yt-dlp link, clipboard content or keyboard events as a side effect of Launcher.
6. Never leave stale Jump List tasks when launcher is disabled or an item is removed.
7. Never replace the CyrFlip icon/package identity with OneClickRunner branding.
8. Never claim that a build validates Jump List; that surface requires a real Windows taskbar check.

## 7. Approval gates — resolved (v0.2, 2026-07-25)

The product decisions are fixed: full functional parity, opt-in default, one CyrFlip process,
non-destructive data migration. The three open implementation details are resolved:

1. **Shell COM wrapper** — hand-rolled `ICustomDestinationList` + `IShellLinkW` + `IObjectCollection`
   (`LauncherJumpList.cs`), following the `UiaCaretCom`/`Ia2Caret` pattern; no WPF reference. No
   explicit AppUserModelID is set: portable inherits the exe-path identity (exactly what shipped in
   OneClickRunner), MSIX gets the package identity automatically — so no conflict is possible. The
   live-taskbar observation remains part of Phase 6 manual verification.
2. **One-shot `/launcher-run`** — confirmed: one-shot-and-exit, no hook/tray. The one-shot path
   releases the single-instance mutex before doing its work, so a yt-dlp link prompt can never block
   a real CyrFlip launch.
3. **Archive location** — `assets/oneclickrunner-source/` holds exactly `app.ico` + `icon.png`; every
   other source file is recorded in `PLAN/OneClickRunner_Source_Manifest_v0.1.md` (SHA-256 + decision)
   with the live public repository as the canonical source. Nothing lands in `docs/`.

Additional v0.2 decisions: launcher ships in **both** portable and MSIX builds (package verified
separately); all UI strings localized into **13 languages** at once; optional **per-scenario global
hotkey**; single paid release after Phase 6 (`/build` + `[skip ci]` in between).
