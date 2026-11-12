# Pointer - `UI-SIZING`

| | |
| --- | --- |
| **Id** | `UI-SIZING` |
| **Version** | 1.1, active (read 2026-10-27; 1.0 sections 2-4 and the 2026-10-19 amendment, section 5) |
| **Home** | `ui-sizing/README.md` in the shared contracts catalog, sections 2-5 |
| **Role here** | consumer - partial: stable pointer/touch profile and bounded interface and text scales are implemented for every `ThemedForm` and the covered components; the product-owned custom-painted tool windows are not yet scaled (ticket S0076) |

## What this repository holds

- `UiProfiles` / `UiProfileManager` / `UiMetrics` resolve the profile once per opening (or on the user's explicit choice on the General page) and compose it with OS DPI once. Nothing follows the pointer, the pen or a touch event.
- The offered range, the smallest targets, the glyph and text bounds and the per-PC storage are recorded in the S0076 UI profile record (size profile record). A stored or hand-edited value that is not an offered step reads as ordinary.
- A change of profile measures the covered components again at their final width (1.1) through `ThemedForm.OnUiProfileApplied`.
- Measured coverage: see the evidence section of the design record. No compact, touch or very large conformance is claimed beyond what it lists.
