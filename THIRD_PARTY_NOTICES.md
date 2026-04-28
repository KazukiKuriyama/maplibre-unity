# Third-Party Notices

This file lists third-party software and assets used by or recommended for MapLibre Unity.

---

## Runtime Dependencies

### Newtonsoft.Json (Json.NET)

- **Package**: `com.unity.nuget.newtonsoft-json` (3.2.1)
- **License**: MIT License
- **Copyright**: Copyright (c) 2007 James Newton-King
- **URL**: https://www.newtonsoft.com/json

### Unity Input System

- **Package**: `com.unity.inputsystem` (1.18.0)
- **License**: Unity Companion License
- **URL**: https://docs.unity3d.com/Packages/com.unity.inputsystem@1.18/

### TextMeshPro

- **Package**: `com.unity.textmeshpro` (Unity 6 built-in)
- **License**: Unity Companion License
- **URL**: https://docs.unity3d.com/Packages/com.unity.textmeshpro@latest

---

## Fonts (User-Provided)

MapLibre Unity does **not** bundle any font files. Users must provide their own
`TMP_FontAsset` via the Inspector (`MapLibreMap > Text > Symbol Font`).

When selecting a font for your project, ensure the license is compatible with
your use case. Below are recommended open-source fonts suitable for map labels.

### Recommended Fonts for CJK (Japanese, Chinese, Korean)

| Font | License | URL |
|------|---------|-----|
| Noto Sans JP | SIL Open Font License 1.1 | https://fonts.google.com/noto/specimen/Noto+Sans+JP |
| Noto Sans CJK | SIL Open Font License 1.1 | https://github.com/notofonts/noto-cjk |
| M PLUS 1p | SIL Open Font License 1.1 | https://fonts.google.com/specimen/M+PLUS+1p |
| Source Han Sans | SIL Open Font License 1.1 | https://github.com/adobe-fonts/source-han-sans |
| BIZ UDGothic | SIL Open Font License 1.1 | https://fonts.google.com/specimen/BIZ+UDGothic |

### Recommended Fonts for Latin / General

| Font | License | URL |
|------|---------|-----|
| Noto Sans | SIL Open Font License 1.1 | https://fonts.google.com/noto/specimen/Noto+Sans |
| Open Sans | SIL Open Font License 1.1 | https://fonts.google.com/specimen/Open+Sans |
| Roboto | Apache License 2.0 | https://fonts.google.com/specimen/Roboto |

### SIL Open Font License 1.1 (Summary)

Fonts under SIL OFL 1.1 can be:
- Used freely in any project (commercial or non-commercial)
- Bundled and redistributed with your application
- Modified and redistributed under the same license

The only restriction is that the fonts cannot be sold by themselves.
Full license text: https://openfontlicense.org/

### Font Setup

**Automatic (recommended):** Use the **MapLibre > Font Setup** menu in the Unity Editor.
This generates a TMP_FontAsset from OS system fonts and auto-assigns it to MapLibreMap.

**Manual:** If you prefer to use a specific font file:

1. Import the `.ttf` or `.otf` file into your Unity project
2. Open **Window > TextMeshPro > Font Asset Creator**
3. Select the source font and configure atlas settings
4. For CJK fonts, use **Dynamic** rendering mode to generate glyphs on demand
   (this avoids creating a massive static atlas for thousands of characters)
5. Assign the generated `TMP_FontAsset` to `MapLibreMap > Text > Symbol Font`

**Runtime fallback (development only):**
If no font is assigned, MapLibre Unity reads fonts already installed on the OS
(Hiragino on macOS, Yu Gothic on Windows, etc.) and uses them at runtime.
This is the same as a web browser or word processor displaying text with OS fonts —
no font files are copied or redistributed. This feature exists so that developers
can test without configuring a font first.

> **When distributing your application:**
> The runtime fallback depends on fonts being installed on the end user's machine.
> For reliable text display in a distributed app:
>
> 1. Use **MapLibre > Font Setup** to generate a font asset, or
> 2. Import an OFL-licensed font (e.g. Noto Sans JP) and create a TMP_FontAsset manually
>
> Then assign it to `MapLibreMap > Text > Symbol Font`.
> If you include a font file in your build, make sure its license allows redistribution.
> All fonts listed above under SIL OFL 1.1 permit this.
