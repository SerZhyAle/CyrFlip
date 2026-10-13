# tools/l10n - the localization-key tool

Ticket S0070. The key of `Localization` is the **English** source text; Russian is the `ru:` column of each
entry, like Ukrainian. This folder holds the tool that moved the table there and the one that keeps it there.

| What | Where |
|---|---|
| Lexer for C# string literals (also compiled into `tests/CyrFlip.Tests`, so the gates and the tool agree on what a literal is) | `CsLexer.cs` |
| Reads the `Add(...)` entries, finds every literal that is a key, writes the reports, applies the migration | `TableModel.cs`, `Migration.cs`, `Program.cs` |
| Renames one key at its entry and every use | `Rename-SourceKey.ps1` |

```powershell
dotnet run --project tools/l10n -c Release              # lists the commands
dotnet run --project tools/l10n -c Release -- inventory # reports only, touches no source file
```

## Everyday use: a key has to change

Reword or fix an English caption with

```powershell
.\tools\l10n\Rename-SourceKey.ps1 -Old 'Old text' -New 'New text'
dotnet build CyrFlip.sln -c Release; dotnet test CyrFlip.sln -c Release
```

It renames the entry and every literal equal to it in `src/` and `tests/`; the twelve translations are
untouched, so re-read them when the meaning changed. `LocalizationSourceGateTests` and
`SettingsLocalizationTests` fail for a use it did not reach.

## The rules the gates hold

* A key is English text and never holds Cyrillic. A row carries `ru:` and `uk:` (an empty `en` is the key).
* A Cyrillic string literal lives only in a `ru:`/`uk:` column, in `Symbols.g.cs`, or in the short declared
  list `DeclaredData` of `LocalizationSourceGateTests` (each entry with its reason).
* Two keys that need one English word in two grammatical forms of another language: the second gets a
  context in parentheses, `Large (tiles)`, and an `en:` display text that keeps the English UI unchanged -
  exactly seven today, listed in `PLAN/S0070_english-source-strings/research/decisions.tsv`.

## How the migration was done (kept for the record)

`inventory` -> `decisions.tsv` -> `snapshot` of the old build -> `apply` -> `snapshot` of the new build ->
`compare` through `key-map.tsv`: 894 keys x 13 languages, no cell different. The tool is deterministic and
re-runnable on the pre-migration shape; on the migrated tree `apply` has nothing to do and refuses.
