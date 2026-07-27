# Publishing a Windows desktop app to the Microsoft Store (MSIX) - reusable playbook

A step-by-step, reusable guide distilled from publishing **CyrFlip**. For a second product,
work top to bottom: copy the scripts, swap the app-specific values, reuse the text templates.

## Why this path (Path A: MSIX)

- **Developer account is free** (individuals since late 2025, companies since May 2026).
- **Microsoft re-signs the MSIX during certification** - you do **not** need to buy a code-signing
  certificate. (The alternative "unpackaged exe/MSI" path *does* require a paid cert chaining to a
  Microsoft-trusted root.)
- A Store-signed, Store-distributed build also defuses antivirus heuristic false positives
  (e.g. Avast/AVG `IDP.Generic`) better than anything else.

---

## Phase 1 - Make the app MSIX-ready (code)

MSIX runs a desktop app in a light container with **file/registry virtualization**. The same exe
ships packaged and unpackaged, so detect which at runtime and branch where it matters.

| Concern | Why it breaks under MSIX | Fix (CyrFlip reference) |
| --- | --- | --- |
| "Am I packaged?" | need to branch behaviour | `GetCurrentPackageFullName` → `src/CyrFlip/PackageInfo.cs` |
| Autostart | a packaged `HKCU\..\Run` write is **virtualized and ignored** at sign-in | declare a manifest `windows.startupTask`; the checkbox opens `ms-settings:startupapps` and **reads** the task state from `..\AppModel\SystemAppData\<PFN>\<TaskId>\State` so it isn't stuck on "off" (`Autostart.cs`, `PackageInfo.FamilyName`) |
| Files read by *other* processes | `%LOCALAPPDATA%` is redirected into the package container | write to `%ProgramData%` when packaged (`LayoutPublisher.cs`); have the reader check both paths |

**Rule of thumb:** anything that writes to `%LOCALAPPDATA%` / `HKCU` and must be visible outside the
process (or survive across the real logon) needs an MSIX-aware path.

`runFullTrust` (declared in the manifest) keeps every Win32 API working - global hooks, `SendInput`,
clipboard, `SetSystemCursor`, etc. - so the desktop app behaves exactly as unpackaged.

---

## Phase 2 - Packaging artifacts

| File | Role |
| --- | --- |
| `msix/AppxManifest.xml` | Package manifest: Identity placeholders, `runFullTrust`, `startupTask`, visual assets. |
| `msix/build-msix.ps1` | build → version remap → stage payload → generate logos → fill manifest → `makeappx pack` → optional self-sign. |
| `msix/README.md` | Build/submit instructions. |

**Version gotcha (important):** the Store requires a 4-part version with the **revision = 0**
(`Major.Minor.Build.0`), and each part ≤ 65535. The build script remaps the app's `YY.M.D.HHmm`
stamp to `YY.(M*100+D).HHmm.0` - monotonic over time and unique per minute.

Tooling needed: the Windows SDK (provides `makeappx.exe` + `signtool.exe`):
```powershell
winget install Microsoft.WindowsSDK.10.0.26100
```

---

## Phase 3 - Verify locally before uploading

```powershell
.\msix\build-msix.ps1 -SelfSign
# prints two commands: Import-Certificate (run as admin) + Add-AppxPackage
```
Pitfalls hit in practice:
- `Square310x310Logo` requires a paired `Wide310x150Logo` - drop the large tile if you don't have a
  wide one.
- `Add-AppxPackage` **installs but does not launch** - start it from the Start menu, or
  `explorer.exe "shell:AppsFolder\<PackageFamilyName>!<AppId>"`.
- A path-independent single-instance mutex makes the packaged copy exit if a dev copy is already
  running. Close the dev copy when testing the package.

---

## Phase 4 - Partner Center: account + identity

1. **Account settings → Programs → Windows → Get started** (NOT "Windows Desktop Applications" - that
   one is telemetry for EV-signed Win32 apps). Registration is free (choose **Individual**).
2. **Create a new product → MSIX or PWA app** → reserve the app name.
3. **Product ▸ Product identity** → copy three values:
   - `Package/Identity/Name` → `-IdentityName`
   - `Package/Identity/Publisher` (e.g. `CN=..`) → `-Publisher`
   - `Package/Properties/PublisherDisplayName` → `-PublisherDisplayName`
4. Build the Store package (no `-SelfSign`) with those values and upload the **unsigned** `.msix`:
   ```powershell
   .\msix\build-msix.ps1 -IdentityName "<Name>" -Publisher "<CN=..>" -PublisherDisplayName "<..>"
   ```

---

## Phase 5 - Listing materials

| Item | Requirement / gotcha | How CyrFlip did it |
| --- | --- | --- |
| **Privacy policy** | Required when the app touches keyboard/clipboard/personal data | `docs/privacy.html` on GitHub Pages → URL (text variant also accepted) |
| **Screenshots** | At least 1, PNG **≥ 1366×768** | `tools/store/make-screenshot.ps1` composes a promo image from brand assets |
| **Store logos** | Optional (Store falls back to package logos), but Box art improves the page | `dotnet run --project tools/IconGen -- store` → branded Box art + tile icons + 9:16 poster |
| **Description** | Required | see template below |
| **Product features** | Bullet list, each ≤ 200 chars | see template below |
| **Pricing** | "Free" = pick it in the **Retail price** dropdown (base price) | - |
| **runFullTrust justification** | Required for every desktop MSIX; **has a ~1000-char limit** | see template below (long + short) |
| **Age rating** | Short questionnaire | fill it in |

---

## Phase 6 - Submit → certification (~ a few business days)

A global keyboard hook + clipboard can draw extra review (looks like a keylogger). The runFullTrust
justification + a clear description pre-empt most questions.

---

## Phase 7 - Publishing an update (existing app)

The identity is fixed once the app is reserved, so an update is just a new submission:

1. Rebuild the package with the **same** `-IdentityName` / `-Publisher` / `-PublisherDisplayName`.
   The version remap (`YY.(M*100+D).HHmm.0`) is monotonic, so the new package is automatically newer
   - no manual bump. Verify it exceeds the currently published version.
2. Partner Center ▸ **Create new submission** ▸ replace the package ▸ refresh the listing(s) ▸ submit.
3. **Localized listings** (e.g. Russian): *Store listings ▸ Manage additional languages* ▸ add the
   locale ▸ paste its copy. CyrFlip keeps ready EN/RU/UK text in `msix/store-listings.md`. A listing
   language is independent of the package's `<Resource Language>` set, so an English-UI app can still
   have a Russian product page.

> **`msstore` CLI caveat:** automation needs an Azure AD **service principal**
> (`msstore reconfigure --tenantId .. --sellerId .. --clientId .. --clientSecret ..`). The
> interactive `msstore reconfigure` **fails on an individual developer account** ("Error while
> retrieving Organization" - there is no Azure AD org behind a personal MSA). For individual
> accounts, the Partner Center web submission is the reliable path.

---

## Reuse checklist for the next product

1. Copy **`msix/`** → edit `AppxManifest.xml` (`DisplayName`, `Description`, `Application Id`) and the
   `build-msix.ps1` parameter defaults.
2. Copy **`tools/store/make-screenshot.ps1`** and the **store branch of `tools/IconGen`** → point at the
   new app's assets.
3. Copy **`docs/privacy.html`** → rewrite the "what the app accesses" section for the new product.
4. If it's also a tray/desktop app, reuse the **code patterns**: `PackageInfo`, `startupTask` instead of
   `HKCU\Run`, `%ProgramData%` instead of `%LOCALAPPDATA%` for externally-read files.
5. Rewrite the **text templates** below for the new product (same structure).
6. The **Partner Center account already exists** - just "Create a new product" + new identity.

Not needed again: account registration, buying a certificate (Store signs), re-learning the
version/virtualization pitfalls (already encoded in the scripts).

---

## Text templates (rewrite per product)

### Description
```
<App> is a tiny Windows app that <one-sentence value prop>.

<2-4 sentences on the main feature(s) and how to use them.>

It runs in the system tray, uses little memory, and needs nothing extra installed on Windows 10/11.

Note: <App> uses <list any sensitive access, e.g. a keyboard hook / clipboard> only to <purpose>. It does not log keystrokes, collect data, or use the network. It is open source: <repo URL>
```

### Product features (one per line, ≤200 chars each)
```
<feature 1 - the headline capability>
<feature 2>
<feature 3>
Configurable global hotkey (if any)
Runs quietly in the tray, low memory, nothing extra to install
Open source - no telemetry, no network, no data collection
```

### runFullTrust justification (keep under ~1000 chars)
```
<App> is a full-trust Win32 desktop app (<framework>), not a UWP app, so runFullTrust is required to run as a normal desktop process and to call the Win32 APIs its core features depend on:
- <API/capability 1>: <why>. <privacy reassurance if it touches keyboard/clipboard>.
- <API/capability 2>: <why>.
These APIs are available only to full-trust desktop apps. The app runs entirely locally, makes no network connections, and collects no user data. Open source: <repo URL>
```

### Privacy policy (host as a page; key points)
```
<App> does not collect, store, log, or transmit any personal data. It runs entirely on your device,
has no servers, makes no network requests, and contains no telemetry/analytics/ads/accounts.

What it accesses and why: <list each sensitive access and its sole purpose>.
Local files it writes: <list, note they never leave the device>.
Data sharing: none. Children: no data collected. Open source: <repo URL>. Contact: <email>.
```

---

_Generated while publishing CyrFlip. See `msix/README.md` for the packaging detail and `docs/privacy.html`
for the live privacy-policy page._
