# Сборка и Релиз - два понятия проекта

Чтобы тестовая итерация не стоила денег, а публикация ничего не забывала, в проекте есть
два чётко разделённых процесса.

| | **СБОРКА** (build) | **РЕЛИЗ** (release) |
| --- | --- | --- |
| Что это | Собрать exe, протестировать локально, при успехе - закоммитить | Документация/сайт + сборка на GitHub + winget + Microsoft Store + расширение VS Code |
| Где выполняется | **Локально** на машине разработчика | GitHub Actions (платно) + внешние площадки |
| Сколько стоит | **0 минут GitHub** | Платные минуты GitHub + ручные публикации |
| Чем запускается | `build.ps1` | `release.ps1` (+ ручные шаги по чек-листу) |
| Как часто | Постоянно, на каждую правку | Редко, осознанно |

Главное правило: **частая работа = сборка (бесплатно), редкая публикация = релиз (платно).**

---

## СБОРКА - `build.ps1`

Полностью локальная: build Release → тесты → стейджинг single-exe → деплой в синк-папки.
GitHub-минуты **не тратятся**.

```powershell
.\build.ps1                                   # собрать + протестировать + разложить локально
.\build.ps1 -Commit -Message "fix caret"      # то же + git commit (с маркером [skip ci])
.\build.ps1 -Commit -Message "fix caret" -Push # ещё и push в main
```

Почему пуш сборки не платный: коммит получает `[skip ci]` в сообщении, и GitHub **нативно
пропускает** все воркфлоу для такого коммита. То есть «тестовая сборка», закоммиченная в main,
проходит бесплатно - мы её уже проверили локально.

> Если коммитишь вручную (не через `build.ps1`) и не хочешь платной CI-сборки - добавь
> `[skip ci]` в текст коммита сам.

---

## РЕЛИЗ - `release.ps1` + чек-лист

Релиз - единственное место, где осознанно тратятся платные минуты GitHub (подпись + упаковка
в `release.yml`) и делаются внешние публикации.

```powershell
.\release.ps1            # PREFLIGHT: проверки + локальный build/test + печать чек-листа. Без изменений в git.
.\release.ps1 -Push      # после зелёного preflight: создать тег vX и запустить сборку на GitHub
```

### Что делает `-Push`
1. Создаёт **пустой коммит** `release: vX.Y.Z` (якорь без `[skip ci]`).
2. Ставит тег `vX.Y.Z` и пушит ветку + тег.
3. Тег запускает `release.yml` → подписанный ZIP + `.sha256` + GitHub Release.

### Защита от двойной оплаты
- Пуш релизного коммита в `main` **не** запускает `ci.yml`: джоб пропускает коммиты с префиксом
  `release:` (`if:` в `ci.yml`). Платит только `release.yml` по тегу.
- Тег (`refs/tags/v*`) не триггерит `ci.yml` - тот слушает только ветку `main`.

### Полный чек-лист релиза (его же печатает `release.ps1`)
1. **GitHub-сборка зелёная** - `release.yml` собрал `CyrFlip-<ver>-windows-x64.zip` + `.sha256`
   и создал GitHub Release. Скопировать URL ZIP-ассета и SHA256 из лога.
2. **Сайт/доки** - деплоятся из `/docs` автоматически на пуше выше. Проверить GitHub Pages;
   обновить версии/чейнджлог в `docs/`, если менялось поведение для пользователя.
3. **winget** (`SerZhyAle.CyrFlip`) - **не** `wingetcreate update`: та команда пересобирает манифест из
   уже опубликованного в winget-pkgs и меняет только версию с URL, так что `Description`,
   `ShortDescription`, `Tags` и `ReleaseNotes` этого репозитория до магазина не доезжают. Собирать из
   шаблонов `winget/*.yaml`: скопировать их в отдельную папку, подставить `__VERSION__` / `__URL__` /
   `__SHA256__`, нацелить `ReleaseNotesUrl` на `/releases/tag/v<ver>`, затем `winget validate` +
   `winget install --manifest <dir>` и `wingetcreate submit --prtitle "SerZhyAle.CyrFlip version <ver>"`.
   **Тело PR заполнить руками** (`gh pr edit`): `wingetcreate` отправляет шаблон Microsoft нетронутым,
   с пустым описанием и снятыми галочками, и такой PR читается как «ничего не проверено».
4. **Microsoft Store (MSIX)** - `msix\build-msix.ps1` с реальной identity (Store ID `9NB4W41NGQJ4`),
   затем Partner Center → Create new submission → заменить `.msix` → *Store listings → Import* из
   `msix/store-listing-export.csv` (источник истины, все 13 языков; `msix/store-listings.md` и
   `store/listing-*.txt` **генерируются** из него скриптом `msix/render-listing-mirrors.ps1` и годятся
   только как ручной фолбэк) → Submit. Детали: [msix/README.md](msix/README.md), [STORE_PUBLISHING.md](STORE_PUBLISHING.md).
5. **Расширение VS Code** (только если менялся `vscode-extension/`) - поднять `version` в
   `vscode-extension/package.json`, затем `npm install ; npm run compile ; npx @vscode/vsce publish`.
6. **Smoke-тест** опубликованного: `winget install` / установка из Store после прохождения сертификации.

> Шаги 3-5 требуют внешних кредов/интерактива и оставлены ручными намеренно - `release.ps1`
> их не выполняет, но печатает как чек-лист, чтобы ничего не забыть.

---

## Шпаргалка

```
правка кода        ->  .\build.ps1 -Commit -Message "..."        (бесплатно, [skip ci])
готов публиковать  ->  .\release.ps1            (preflight, проверить чек-лист)
                   ->  .\release.ps1 -Push      (тег -> платная сборка GitHub -> Release)
                   ->  пройти пункты 2-6 чек-листа
```
