# パラメーター型の実装表現

## 状態

初期パラメーター型のC#表現、精度および基本検証規則は確定。

## 型対応

| パラメーター型 | C#表現 | 基本規則 |
|---|---|---|
| `Float` | `System.Single` | NaNとInfinityを禁止 |
| `Int` | `System.Int32` | 32bit符号付き整数 |
| `Bool` | `System.Boolean` | `true`／`false` |
| `Vector2` | `UnityEngine.Vector2` | 各成分は有限な32bit float |
| `Vector3` | `UnityEngine.Vector3` | 各成分は有限な32bit float |
| `Vector4` | `UnityEngine.Vector4` | 各成分は有限な32bit float |
| `Color` | `UnityEngine.Color` | Linear RGBA、各成分は有限な32bit float |
| `String` | `System.String` | UTF-16で最大4096文字、NUL禁止 |
| `Enum` | 安定した選択肢ID文字列 | 定義済みIDだけを許可 |
| `MediaAssetReference` | `MediaAssetId`値型 | 128bit UUIDと素材種別を検証 |

## ID

- `ParameterId` はノード型内で一意な、小文字ASCIIの安定IDとする。
- 形式はドット区切りのlower snake caseとし、例を `transport.playhead_seconds` とする。
- 表示名の変更では `ParameterId` を変更しない。
- `Enum` の選択肢IDも同じ命名規則を使い、表示名と分離する。
- `MediaAssetId`、`NodeInstanceId`、`PresetId`、`LogicalControlId` はランダムなUUID v4とする。

## 範囲と色

- 数値、VectorおよびColorのハード範囲は省略可能とする。
- ハード範囲がない場合も有限値の検証は必須とする。
- `Color` はLinearの非Premultiplied RGBA値として扱い、HDR用途では1を超えるRGBを許可できる。
- Premultiplied Alpha契約は `ImageFrame` に適用し、Colorパラメーター自体には適用しない。

## 設計意図

- 保存形式とC#実装間の型解釈を一意にする。
- 表示名変更や素材移動で参照が壊れないようにする。
- パラメーターのColorと映像ピクセルのAlpha契約を混同しない。
