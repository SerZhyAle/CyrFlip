# MSIX package (Microsoft Store)

Packages the unpackaged `CyrFlip.exe` as a **full-trust MSIX** for the Microsoft Store (Path A).

**Why the Store path is attractive:** the developer account is now **free** (individuals since late
2025, companies since May 2026), and **Microsoft re-signs the package** during certification - so you
get a trusted signature and reputation **without buying a code-signing certificate**. A Store-signed,
Store-distributed build is also the most effective answer to the Avast/AVG `IDP.Generic` heuristic.

## Files

| File | Purpose |
| --- | --- |
| [AppxManifest.xml](AppxManifest.xml) | Package manifest (full-trust app, `runFullTrust`, `startupTask`). Has `__PLACEHOLDERS__`. |
| [build-msix.ps1](build-msix.ps1) | Builds Release, stages payload, generates logo PNGs, fills the manifest, packs the `.msix`. |

The `stage/` and `dist/` folders are produced by the script (git-ignore them).

## How the app adapts under MSIX

The **same `CyrFlip.exe`** ships packaged and unpackaged; it detects which at runtime
(`PackageInfo.IsPackaged` → `GetCurrentPackageFullName`) and adjusts two things:

1. **Autostart.** Unpackaged uses `HKCU\..\Run`. Under MSIX that write is virtualized and ignored at
   sign-in, so autostart is declared in the manifest as a **`windows.startupTask`** (off by default).
   The tray menu item becomes **"Start with Windows.."** and opens *Settings ▸ Apps ▸ Startup*, where
   the user toggles it.
2. **`layout.txt` for the VS Code extension.** Unpackaged writes `%LOCALAPPDATA%\CyrFlip\layout.txt`.
   Under MSIX `%LOCALAPPDATA%` is virtualized into the package container - invisible to the (unpackaged)
   extension - so the packaged app writes **`%ProgramData%\CyrFlip\layout.txt`** instead. The extension
   checks both locations.

The global keyboard hook, `SendInput`, `SetSystemCursor` and clipboard all keep working because the
package declares the `runFullTrust` restricted capability.

## One-time setup in Partner Center

1. Create a **free** developer account and sign in to [Partner Center](https://partner.microsoft.com/dashboard).
2. **Reserve the app name** "CyrFlip" (Apps and games ▸ New product ▸ MSIX or PWA app).
3. Open **Product ▸ Product identity** and copy the three values Microsoft assigned you:
   - **Package/Identity/Name** → pass as `-IdentityName`
   - **Package/Identity/Publisher** (e.g. `CN=ABCD1234-..`) → pass as `-Publisher`
   - **Package/Properties/PublisherDisplayName** → pass as `-PublisherDisplayName`

These **must match exactly**, or the Store rejects the upload.

## Build a Store-ready package

Requires the Windows SDK (`makeappx`): `winget install Microsoft.WindowsSDK`.

```powershell
.\build-msix.ps1 `
  -IdentityName        "<Package/Identity/Name from Partner Center>" `
  -Publisher           "<Package/Identity/Publisher from Partner Center>" `
  -PublisherDisplayName "<PublisherDisplayName from Partner Center>"
```

Output: `msix/dist/CyrFlip-<version>-x64.msix`, **unsigned** - that's correct, upload it as-is.
The internal package version is derived from the exe's `YY.M.D.HHmm` stamp and remapped to a
Store-legal `Major.Minor.Build.0` (the revision must be 0; the build script handles this).

Then in Partner Center: create a submission, upload the `.msix`, fill the listing (you can reuse the
EN/RU/UK copy from the app READMEs), set the age rating, add screenshots, and submit. Certification
typically takes a few business days.

> **Heads-up on certification:** a global keyboard hook + clipboard access can draw extra review (it
> looks like a keylogger to automated checks). Describe the layout-indicator/transliterator purpose
> plainly in the listing; be ready to justify it if asked.

## Test locally before submitting (self-signed)

To sideload and run the package on your own machine, sign it with a throwaway cert (its subject must
equal `-Publisher`, so keep the default or pass a matching `CN=`):

```powershell
.\build-msix.ps1 -SelfSign
```

The script signs the package and prints the two commands to (1) trust the test cert in
`LocalMachine\TrustedPeople` (run as admin) and (2) `Add-AppxPackage` the `.msix`. Test that the tray
app launches, the layout indicator works, and *Settings ▸ Apps ▸ Startup* lists **CyrFlip**.

> Self-signed packages are for local testing only. **Do not** sign the package you upload to the
> Store - Microsoft signs that one.
