# Event Demo

MapLibre GL JS の `map.on()` / `map.off()` / `map.once()` に対応するイベントシステムのデモ。

## セットアップ

1. 既存のサンプルシーン（例: BasicRasterMapScene）を複製
2. MapLibreMap が付いた GameObject に `MapEventDemo` コンポーネントを追加
3. Play して Console を確認

## 確認できるイベント

| イベント | 発火タイミング |
|---------|---------------|
| `Load` | マップ初期化完了時 |
| `Idle` | 移動停止後 |
| `MoveStart` / `Move` / `MoveEnd` | パン・ズーム・回転など任意の状態変化 |
| `ZoomStart` / `Zoom` / `ZoomEnd` | ズーム変化 |
| `RotateStart` / `Rotate` / `RotateEnd` | ベアリング変化 |
| `PitchStart` / `Pitch` / `PitchEnd` | ピッチ変化 |
| `Click` | 左クリック（地理座標付き） |
| `SourceAdd` / `SourceRemove` | ソース追加・削除時 |
| `LayerAdd` / `LayerRemove` | レイヤー追加・削除時 |

## 操作

- **左ドラッグ**: パン → `movestart` / `move` / `moveend`
- **スクロール**: ズーム → `zoomstart` / `zoom` / `zoomend`
- **右ドラッグ**: 回転 → `rotatestart` / `rotate` / `rotateend`
- **中ドラッグ**: ピッチ → `pitchstart` / `pitch` / `pitchend`
- **左クリック**: → `click` (Console に緯度経度が表示される)
