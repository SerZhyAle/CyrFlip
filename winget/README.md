# winget manifests

Templates for publishing CyrFlip to [winget-pkgs](https://github.com/microsoft/winget-pkgs).
Package identifier: **`SerZhyAle.CyrFlip`** (portable exe shipped inside the release ZIP).
These templates currently target winget manifest schema **`1.12.0`**.

The placeholders `__VERSION__`, `__URL__`, and `__SHA256__` are filled in per release:

- `__VERSION__` - the release version, e.g. `26.6.11.1700`
- `__URL__` - the `CyrFlip-<version>-windows-x64.zip` asset URL from the GitHub Release
- `__SHA256__` - the hash printed by the release workflow (the `.sha256` sidecar)

## Submitting an update

**Fill these templates and `wingetcreate submit` them.** Do *not* use `wingetcreate update
SerZhyAle.CyrFlip --submit`: it rebuilds the manifests from what is already published, so the
listing copy maintained here - the ru-RU and uk-UA locale files, the `ReleaseNotes`, the
`PrivacyUrl` - is silently dropped, and the PR arrives carrying only what the previous release
happened to contain.

```powershell
$ver = '<VERSION>'
$url = "https://github.com/SerZhyAle/CyrFlip/releases/download/v$ver/CyrFlip-$ver-windows-x64.zip"
$sha = (Get-Content <the .sha256 sidecar> -Raw -split '\s+')[0]

$stage = Join-Path $env:TEMP "winget-SerZhyAle.CyrFlip-$ver"
New-Item -ItemType Directory -Force $stage | Out-Null
Get-ChildItem winget/*.yaml | ForEach-Object {
  (Get-Content $_.FullName -Raw).Replace('__VERSION__',$ver).Replace('__URL__',$url).Replace('__SHA256__',$sha) |
    Set-Content (Join-Path $stage $_.Name) -NoNewline
}

winget validate --manifest $stage          # must pass before anything is pushed
wingetcreate submit --no-open --prtitle "New version: SerZhyAle.CyrFlip version $ver" `
  --token (gh auth token) $stage
```

Check the resulting PR lists **all five** files - the two package manifests plus en-US, ru-RU and
uk-UA. Four files means a locale went missing.
