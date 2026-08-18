# Zone rule files

These are **not** NinjaScript. They are read at runtime by `AlightenMirrorV0041` and must be
placed in the NinjaTrader user data directory — `Documents\NinjaTrader 8\` — *not* in
`bin\Custom\`. The indicator resolves a bare filename against that folder.

| file | indicator setting | rules |
|---|---|---|
| `MirrorGroupsV0040.txt` | `12. Signal Groups` → Groups File | primary (confluence stacks) |
| `MirrorInsideZonesV0040.txt` | `13. Inside Zones` → Inside Zones File | inside (pairs) |

If the filename in the settings does not resolve, the loader does **not** error — it silently
creates a stub file containing two example rules. A load banner reading `rules=2` in
`MirrorZonesV0041_PRI.log` means exactly that.
