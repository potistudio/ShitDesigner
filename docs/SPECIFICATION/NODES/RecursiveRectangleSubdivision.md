# 再帰的矩形分割画像ノード

## 状態

提案。画像出力を目的とした、BSPベースの再帰的矩形分割ノードの初期仕様案を定める。

## 目的

- 画面全体を二分木として再帰的に分割し、矩形パターンを1枚の画像として出力する。
- Seedと構造パラメーターが同じ場合に、フレーム間で同じ分割木を再現する。
- 分割の構造変更と、分割中のイージングを分離する。

## 対象外

- 矩形リストまたはBSP木をデータPortへ出力すること
- 矩形ごとの動的Output Portを生成すること
- 入力画像を矩形ごとに貼り付けること
- 4分割、任意のN分割および矩形ごとの個別パラメーター
- 分割木をフレームごとに再生成する時間変化

入力画像を矩形へ割り当てる用途は、後続のEffectノードで別途定義する。

## ノード定義案

| 項目 | 定義 |
|---|---|
| 表示名 | Recursive Rectangles |
| NodeTypeId候補 | `shitdesigner.shader.generator.recursive-rectangles` |
| Category | `Shader/Generator` |
| 入力 | なし |
| 出力 | `image : ImageFrame` |
| SchemaVersion | `1` |
| 状態 | ステートレス |

`image` は全画面のImageFrameを返す。矩形の境界と塗りは同じ出力画像へ合成する。

## 分割木

ルート領域は正規化UVの `[0, 1] x [0, 1]` とする。各内部ノードは、親矩形を水平または垂直に2つの子矩形へ分割する。

分割を行わずLeafとして確定する条件は次のいずれかとする。

- `max_depth` に到達した
- 子矩形が `min_leaf_size` を満たせない
- `split_probability` により分割しないと決定された
- 内部上限のLeaf数に到達した

分割軸、分割位置および分割判定は、Seedとツリーパスから得た決定的ハッシュを使用する。乱数列の消費順序には依存しない。

### 分割パラメーター

| Parameter | 型 | 内容 |
|---|---|---|
| `max_depth` | Int | 最大分割深度 |
| `min_leaf_size` | Float | Leafの最小幅・高さの基準 |
| `split_probability` | Float | 各矩形を分割する確率 |
| `axis_mode` | Enum | `longer_side`、`horizontal`、`vertical`、`random` |
| `ratio_min` | Float | 分割位置の下限 |
| `ratio_max` | Float | 分割位置の上限 |
| `seed` | Int | 分割木とLeaf表現を決定するSeed |

`ratio_min` と `ratio_max` は、子矩形が最小サイズを満たす範囲へ制限する。合法な分割位置がない場合はLeafとして扱う。

## 分割アニメーション

分割木は構造パラメーターの変更時に確定し、分割イベントの進行中に再生成しない。各内部ノードは1つの分割イベントを持つ。

### イベントの状態

- 開始前：親矩形だけを表示する。
- 進行中：2つの子矩形を分割線から最終境界へ展開する。
- 完了後：子矩形を最終サイズで表示する。

子矩形は分割線上の幅または高さ0の状態から、最終的な矩形境界まで展開する。これにより、親矩形の表示から子矩形の表示へ不連続に切り替えずに分割を表現できる。

```text
t = clamp((progress - start) / duration, 0, 1)
e = Ease(t)
childBounds = ExpandFromSplit(finalChildBounds, e)
```

親の分割が完了するまで、その子孫の分割イベントは開始しない。初期のイベント順序は深度単位の幅優先とし、同じ深度の分割は同時に開始できる。

### アニメーションパラメーター

| Parameter | 型 | 内容 |
|---|---|---|
| `beat_sync` | Bool | 共有BPMクロックが利用可能な場合、1拍を分割全体の進行度として使用する |
| `reveal_progress` | Float | 分割全体の進行度 |
| `split_duration` | Float | 1回の分割に要する進行時間 |
| `split_stagger` | Float | 親の完了後から子の開始までの間隔 |
| `easing` | Enum | `linear`、`smooth_step`、`ease_in`、`ease_out`、`ease_in_out` |

`beat_sync` が有効で共有BPMクロックを利用できる場合、拍の先頭を進行度0、次の拍の直前を進行度1として分割を繰り返す。共有BPMクロックがない場合、または `beat_sync` が無効な場合は `reveal_progress` を使用する。`reveal_progress` は通常のEffectiveValueとして扱い、論理コントロールまたは既存のフレーム同期経路から更新する。ノード内部でUnity Timeを直接参照しない。

### Easing

`easing` は分割イベントの局所進行度へ適用する。初期実装の関数は次の定義とする。

| Mode | Function |
|---|---|
| `linear` | `t` |
| `smooth_step` | `t * t * (3 - 2 * t)` |
| `ease_in` | `t * t` |
| `ease_out` | `1 - (1 - t) * (1 - t)` |
| `ease_in_out` | `t < 0.5 ? 2 * t * t : 1 - 2 * (1 - t) * (1 - t)` |

`gutter` を描画する場合、境界線の幅または不透明度にも同じ `e` を適用する。親領域を背景として残すため、分割途中に未定義の空白領域を作らない。

## 外観

初期案では、Leafおよび分割途中の親領域を決定的な色で塗り、内部境界へ線を重ねる。

| Parameter | 型 | 内容 |
|---|---|---|
| `color_a` | Color | 色生成の基準色A |
| `color_b` | Color | 色生成の基準色B |
| `gutter` | Float | 内部境界の幅 |
| `line_color` | Color | 内部境界の色 |

Leafごとの色は、`seed` とツリーパスから得た値で `color_a` と `color_b` の間を決定的に補間する。

## 実行契約

- 分割木の構造変更は、`seed`、`max_depth`、`min_leaf_size`、`split_probability`、`axis_mode`、`ratio_min` または `ratio_max` の変更時にだけ行う。
- `reveal_progress`、`split_duration`、`split_stagger`、`easing` および外観パラメーターの変更では分割木を再生成しない。
- 同一の構造パラメーターとSeedからは、同一の分割木と同一のイベント順序を生成する。
- ノードは履歴を保持せず、同じフレーム内で同じ分割木を参照して画像を生成する。
- 出力は要求された解像度のImageFrameとし、分割木は解像度に依存しない。
- ImageFrameの色空間、Dynamic Range、AlphaおよびSurface所有権は共通のRendering契約へ従う。

## 検証項目

- 同一のSeedと構造パラメーターから同一の分割木が得られる。
- 各子矩形が親矩形の内部に収まる。
- 子矩形が互いに重ならず、分割完了時に親領域全体を覆う。
- 最小サイズを満たせない領域が分割されない。
- `max_depth` とLeaf数の上限を超えない。
- `reveal_progress = 0` で親領域だけが表示される。
- `reveal_progress` が完了値に達すると、確定済みのLeaf全体が表示される。
- Easingの変更が分割木および完了時の矩形境界を変更しない。
- 異なる出力解像度でも、正規化された分割境界が一致する。
- 進行中に子孫が親の未完了分割を越えて表示されない。

## 後続検討

- 分割イベントを深度順ではなくツリーパス順またはハッシュ順にするモード
- 分割線が移動するWipe型トランジション
- 入力ImageFrameを各LeafへマッピングするEffectノード
- `RectPartition` を出力するデータノード
