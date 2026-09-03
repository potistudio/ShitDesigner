# 任意入力の既定値

## 状態

各ポート型の既定値と `ImageFrame` 任意入力の扱いは確定。

## 値型

- `Float`: `0.0`
- `Int`: `0`
- `Bool`: `false`
- `Vector2`、`Vector3`、`Vector4`: 全成分 `0.0`
- `Color`: Linear RGBAの透明黒 `(0, 0, 0, 0)`

- 値型の任意入力は、対応する `ParameterId` の `EffectiveValue` を既定値として使用する。
- ノード型はパラメーター既定値を変更できるため、上記は新規定義時の標準値とする。

## ImageFrame

- `ImageFrame` はパラメーター型ではないため、任意入力は `DefaultImage` をポート定義へ持つ。
- `DefaultImage` は `TransparentBlack`、`OpaqueBlack`、`OpaqueWhite` の3種類とする。
- 標準既定値は `TransparentBlack` とする。
- マスク用途などではノード型が `OpaqueWhite` を明示できる。
- 既定映像は要求解像度と内部形式でシステムが生成し、共有の読み取り専用フレームとして渡す。

## 状態表示

- 任意入力が接続元のBlocked、Faulted、PreparingまたはBrokenConnectionにより既定値を使用中の場合、状態を `UsingFallback` とする。
- ノードエディターでは入力ソケットを黄色にし、使用中の既定値と原因をツールチップへ表示する。
- 開始と復帰を通常ログへ記録せず、現在状態だけを表示する。

## 設計意図

- `ImageFrame` を無理にパラメーター値へ入れない。
- マスクのように透明黒では意味が異なる入力でも、明示的な既定映像を選べるようにする。
- フォールバックを正常値と見分けられるようにする。
