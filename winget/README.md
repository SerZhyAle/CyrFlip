# winget manifests

Templates for publishing CyrFlip to [winget-pkgs](https://github.com/microsoft/winget-pkgs).
Package identifier: **`SerZhyAle.CyrFlip`** (portable exe shipped inside the release ZIP).

The placeholders `__VERSION__`, `__URL__`, and `__SHA256__` are filled in per release:

- `__VERSION__` — the release version, e.g. `26.6.11.1700`
- `__URL__` — the `CyrFlip-<version>-windows-x64.zip` asset URL from the GitHub Release
- `__SHA256__` — the hash printed by the release workflow (the `.sha256` sidecar)

## Submitting an update

The easiest path is [`wingetcreate`](https://github.com/microsoft/winget-create):

```powershell
wingetcreate update SerZhyAle.CyrFlip `
  --version <VERSION> `
  --urls <ZIP_URL> `
  --submit
```

`wingetcreate` recomputes the SHA256, updates the manifests, validates them, and opens the
PR against `microsoft/winget-pkgs` for you. Alternatively, copy these files, replace the
placeholders by hand, validate with `winget validate`, and open the PR manually.
