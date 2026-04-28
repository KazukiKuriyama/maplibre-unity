# Sprite Icon Test

スプライトアイコン機能の検証用サンプルです。

## 検証ポイント

| 項目 | 検証内容 |
|------|----------|
| スプライトアトラス読み込み | `sprite` URL から PNG + JSON を取得 |
| icon-image (Expression) | `["match", ["get", "CONTINENT"], ...]` で大陸ごとに異なるアイコン |
| icon-image (固定値) | `"airport_11"` で全フィーチャーに同じアイコン |
| icon-size (zoom stops) | ズームレベルに応じたアイコンサイズの補間 |
| icon-opacity | 半透明アイコン表示 |
| アイコン + テキスト併存 | 国名ラベルとアイコンを同時に表示 |
| アイコンのみ | テキストなしでアイコンだけの表示 |
| 衝突判定 | アイコン同士の重なり防止 |

## セットアップ

1. Unity シーンに空の GameObject を作成
2. `MapLibreMap` コンポーネントを追加
3. **Style File** に `SpriteIconTestStyle.json` を TextAsset として設定
4. Play を押す

## スタイル構成

- **sprite**: `https://demotiles.maplibre.org/styles/osm-bright-gl-style/sprite`
  - 100種類以上のアイコン（airport, restaurant, park, hospital 等）
- **ソース**: MapLibre Demo Tiles (centroids レイヤー)
- **country-icons**: 大陸ごとに異なるアイコン + 国名テキスト
  - Asia → star, Europe → castle, Africa → mountain, etc.
- **icons-only-layer**: ヨーロッパ・アジアの国にairportアイコンのみ（opacity: 0.6）

## 期待される表示

- zoom 2: 各国の位置に小さなアイコン + 国名が表示される
- zoom 3+: アイコンが徐々に大きくなり、ヨーロッパ・アジアに半透明の空港アイコンが追加で表示される
- アイコンが重なる密集地域では自動的に間引かれる
