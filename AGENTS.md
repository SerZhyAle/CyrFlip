# AGENTS.md

Read CLAUDE.md in full; it is the authoritative agent contract and carries the canon pointer; the
stricter file wins. If you are not Claude Code, the canon plugin is not loaded: read
rules/INVARIANTS.md at https://github.com/SerZhyAle/sza-unified-rules first.

**Area rules.** The detail behind `CLAUDE.md` lives in `.claude/rules/*.md`. Only Claude Code loads those files by itself. Every other agent opens them by hand: each file starts with a `paths:` list, and before the first edit, review or command in a matching area you read the whole file. The table in `CLAUDE.md` names them.
