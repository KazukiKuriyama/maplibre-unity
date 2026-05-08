# Symbol Translate Demo

Shows `text-translate` / `text-translate-anchor` (and the same machinery
that drives `icon-translate` / `icon-translate-anchor`).

Three layers reference the same four-station GeoJSON source:

1. **anchor-dot** — red circle at the feature's actual coordinate.
2. **label-baseline** — black label, no translate. Sits on the dot.
3. **label-translated** — blue label with `text-translate: [40, -30]`,
   floating up-and-right.

The constant `TranslateAnchor` in `SymbolTranslateDemo.cs` toggles between
`"map"` (default — offset rotates with the world) and `"viewport"` (offset
stays screen-aligned). Right-drag to rotate the map and watch the
difference.
