# Font Test Sample

Sample for verifying TextMeshPro (SDF) text label rendering, including non-Latin (CJK) fonts.

## What this validates

| Item | Checkpoint |
|------|------------|
| SDF text quality | Text scales smoothly when zooming in and out |
| Text halo | A white outline (halo) appears around labels |
| Non-Latin text | City and town names render correctly when a CJK font is assigned |
| Font fallback | When Symbol Font is empty, an OS font is auto-promoted |
| Zoom-dependent size | Text size changes with zoom level |
| Collision detection | Overlapping labels are culled |

## Setup

### Option A: Auto setup (recommended)

1. Open `FontTestScene.unity`
2. Run **MapLibre > Font Setup** from the menu
3. Pick a font from the CJK font list and click **Generate Font Asset & Apply**
4. Press Play

### Option B: Run without an assigned font

1. Open `FontTestScene.unity`
2. Just press Play
3. The console prints `[MapLibre] No Symbol Font assigned. Using OS font "..." as fallback.`
4. An OS system font is used automatically

### Option C: Manual setup

1. Open `FontTestScene.unity`
2. Import a font (.ttf/.otf), e.g. a CJK font, into the project
3. Generate a Dynamic-mode TMP_FontAsset via **Window > TextMeshPro > Font Asset Creator**
4. Assign it under MapLibreMap > Inspector > **Text > Symbol Font**
5. Press Play

## Style layout

| Layer | Source layer | Zoom range | Description |
|---|---|---|---|
| osm-tiles | (raster) | all zooms | Translucent OSM raster background |
| country-labels | centroids | 0-6 | Country names (large text + halo) |
| city-labels | place | 4-14 | City names (primary CJK target) |
| town-labels | place | 8-16 | Town names |
| village-labels | place | 12+ | Village / district names |

## Data sources

- Raster: [OpenStreetMap](https://www.openstreetmap.org/)
- Vector: [MapLibre Demo Tiles](https://demotiles.maplibre.org/)
