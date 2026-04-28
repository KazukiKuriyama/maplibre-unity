# Vector Tiles with Labels Sample

MapLibre Demo Tiles を使ったベクタータイル＋ラベル表示のサンプルです。

## 機能デモ
- **Fill レイヤー**: 水域、公園、建物（minzoom: 14、zoom 依存の opacity）
- **Line レイヤー**: 道路（zoom 依存の line-width）、鉄道、国境
- **Symbol レイヤー**: 都市名、町名、村名、道路名、POI ラベル
- **Filter**: `["==", "class", "city"]`、`["in", "class", ...]` 等
- **Zoom functions**: exponential stops による線幅・テキストサイズの補間
- **minzoom/maxzoom**: ズームレベルに応じたレイヤー表示制御

## セットアップ

1. Unity シーンに空の GameObject を作成
2. `MapLibreMap` コンポーネントを追加
3. **Style File** に `VectorLabelsStyle.json` を TextAsset として設定
4. Play を押す

## スタイル構成

| レイヤー | タイプ | ソースレイヤー | zoom 範囲 |
|---|---|---|---|
| water | fill | water | 全 zoom |
| landuse-park | fill | landuse | 全 zoom |
| building | fill | building | 14+ |
| road-minor | line | transportation | 13+ |
| road-major | line | transportation | 全 zoom |
| road-motorway | line | transportation | 全 zoom |
| railway | line | transportation | 全 zoom |
| boundary-country | line | boundary | 全 zoom |
| label-place-city | symbol | place | 4-14 |
| label-place-town | symbol | place | 8-16 |
| label-place-village | symbol | place | 12+ |
| label-road | symbol | transportation_name | 14+ |
| label-poi | symbol | poi | 15+ |

## データソース

[MapLibre Demo Tiles](https://demotiles.maplibre.org/) (OpenMapTiles schema)
