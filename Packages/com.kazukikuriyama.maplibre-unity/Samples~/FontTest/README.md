# Font Test Sample

TextMeshPro (SDF) によるテキストラベル描画と日本語フォントの検証用サンプルです。

## 検証項目

| 項目 | 確認ポイント |
|------|------------|
| SDF テキスト品質 | ズームイン・アウト時にテキストが滑らかにスケーリングされるか |
| テキストハロー | ラベルに白い縁取り（halo）が表示されるか |
| 日本語テキスト | 都市名・町名が日本語で表示されるか（フォント設定時） |
| フォントフォールバック | Symbol Font 未設定時に OS フォントから自動生成されるか |
| Zoom 依存サイズ | ズームレベルに応じてテキストサイズが変化するか |
| 衝突検出 | ラベル同士が重ならないように間引かれるか |

## セットアップ

### 方法 A: 自動セットアップ（推奨）

1. `FontTestScene.unity` を開く
2. **MapLibre > Font Setup** メニューを実行
3. CJK フォント一覧からフォントを選択して **Generate Font Asset & Apply**
4. Play

### 方法 B: フォント未設定で実行

1. `FontTestScene.unity` を開く
2. そのまま Play
3. コンソールに `[MapLibre] No Symbol Font assigned. Using OS font "..." as fallback.` と表示される
4. OS のシステムフォントが自動的に使われる

### 方法 C: 手動設定

1. `FontTestScene.unity` を開く
2. 日本語フォント（.ttf/.otf）をプロジェクトにインポート
3. **Window > TextMeshPro > Font Asset Creator** で Dynamic モードの TMP_FontAsset を生成
4. MapLibreMap の Inspector > **Text > Symbol Font** にアサイン
5. Play

## スタイル構成

| レイヤー | ソースレイヤー | zoom 範囲 | 説明 |
|---|---|---|---|
| osm-tiles | (raster) | 全 zoom | 半透明 OSM ラスター背景 |
| country-labels | centroids | 0-6 | 国名（大きめテキスト + ハロー） |
| city-labels | place | 4-14 | 都市名（日本語テスト主対象） |
| town-labels | place | 8-16 | 町名 |
| village-labels | place | 12+ | 村・地区名 |

## データソース

- ラスター: [OpenStreetMap](https://www.openstreetmap.org/)
- ベクター: [MapLibre Demo Tiles](https://demotiles.maplibre.org/)
