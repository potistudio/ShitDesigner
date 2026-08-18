# ポート型カタログと接続編集

## 状態

初期ポート型ID、C#表現、ポートID、固定スキーマ、暗黙変換表示および編集反映方式は確定。

## 型対応

| PortTypeId | C#表現 |
|---|---|
| `core.image_frame` | `ImageFrame` |
| `core.float32` | `System.Single` |
| `core.int32` | `System.Int32` |
| `core.bool` | `System.Boolean` |
| `core.vector2f` | `UnityEngine.Vector2` |
| `core.vector3f` | `UnityEngine.Vector3` |
| `core.vector4f` | `UnityEngine.Vector4` |
| `core.color_linear` | `UnityEngine.Color` |

- FloatとVectorの全成分は32bit floatとする。
- `core.color_linear` はLinearの非Premultiplied RGBA値とする。
- 新しいポート型はビルド時カタログへ明示登録する。

## ポート定義

- Port IDはノード型内の入力／出力を通して一意なlower snake caseとする。
- 主映像出力のPort IDは `image`、表示名は `Image` とする。
- Port IDの `system_` 接頭辞はシステム所有ポート用に予約する。
- 初期版のポート数と型はノード型定義で固定し、実行中に増減しない。
- 3D、2D、Shaderなどカテゴリ単位の共通入力数は設けず、各登録ノード型が明示する。
- 追加出力には初期ポート型のすべてを使用できる。

## 接続編集

- 接続、切断、置換、ノード追加、削除はGraphEditCommandとしてキューへ積む。
- コマンドは次の評価フレーム境界で検証後に原子的に適用する。
- 接続置換は旧接続の切断と新接続の作成を1つのUndo操作として扱う。
- Undo／Redoは直近200コマンドをメモリ上に保持し、プロジェクトへ保存しない。
- 接続順序に評価上の意味を持たせず、保存時は送信元と接続先IDで正規化する。
- 出力ごとの固定Fan-out上限は設けない。プロジェクト全体の接続数は4096を安全上限とする。

## 表示

- 暗黙変換を含む接続は破線と変換バッジで表示し、変換IDを確認できるようにする。
- 必須入力と任意入力はソケット形状とラベルで区別する。
- 多数の接続は選択時だけ接続先一覧を展開し、通常時は束ねて描画できる。

## 設計意図

- 実行中にポート形状が変わる複雑さを初期版へ持ち込まない。
- グラフ変更を評価途中へ混ぜず、Undo可能な単位へ揃える。
- 暗黙変換を見えない処理にしない。
