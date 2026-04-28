# Circle Layer Demo

Circle レイヤーのデモ。ベクタータイルの Point フィーチャーを SDF サークルとして描画します。

## セットアップ

1. シーンに空の GameObject を作成し、`MapLibreMap` コンポーネントを追加
2. `Style File` に `CircleDemoStyle.json` を割り当て
3. Play

## 対応プロパティ

- `circle-radius` — 半径（CSS ピクセル）。Expression（zoom interpolate など）対応
- `circle-color` — 塗りつぶし色
- `circle-opacity` — 塗りつぶし透明度
- `circle-blur` — ぼかし（0 = シャープ、1 = 完全にフェード）
- `circle-stroke-width` — ストローク幅（CSS ピクセル）
- `circle-stroke-color` — ストローク色
- `circle-stroke-opacity` — ストローク透明度
