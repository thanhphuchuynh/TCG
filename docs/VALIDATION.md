# Local validation record

These results describe local testing on the supplied reconstruction, not a guarantee for other players or mod versions.

| Area | Observed result |
|---|---|
| Responsive menu geometry | 1,650 assertions passed |
| Pinned ShopOS drag mapping | All 15 float operands matched in order |
| Credit selection scrolling | Five regressions passed; pointer selection does not recenter a clicked row |
| Menu runtime | 431 checks passed: bounds, drag targets, compact folder transitions, footer and app open/return at windowed, 720p and 1080p sizes |
| Credits runtime | 32 checks passed: public registration, screen lifecycle, scrolling and reopening without duplicate UI |
| Loading-tip runtime | 12 checks passed; new text visible and normal percentage update verified |
| Saves during UI tests | Nine base save files matched their pre-test backups |
| Physical controllers | Not tested |
| Synthetic mouse click-through to websites | Not verified; callback bindings and HTTPS destinations reviewed |
| Full-game save compatibility | A reported load failure remains unconfirmed; two controlled reloads passed |
| Switch | No console test; experimental compatibility approach only |

No private save backups, raw game data, runtime logs, or computer-specific validation exports are included.


## macOS plugin installer v0.1.0

The native AppKit installer passed 30 assertions using dummy temporary installations: install/update/restore, idempotence, preservation of the original DLL, payload and backup tampering, required dependency checks, arm64 and universal Mach-O validation, x86-only rejection, symlink escape refusal, running-game refusal, write-failure rollback and restore-rename failure rollback/retry. A save sentinel remained unchanged.

The packaged arm64 executable was compiled for macOS 13+, ad-hoc signed and verified with `codesign --verify --deep --strict`. Its native folder picker recognized the local compatible game setup; that check did not install into the live game. Release packaging contains only the installer and our plugin payload. Developer ID signing/notarization and other Mac/game/mod configurations were not tested.
