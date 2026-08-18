# 明示的な非可逆変換ノード

## 状態

初期版で提供する非可逆変換ノードと主要パラメーターは確定。

## 初期ノード

- `Float To Int`: 丸め方式を `Round`、`Floor`、`Ceil`、`Truncate` から選ぶ。
- `Int To Float`: Int32をFloat32へ明示変換する。
- `Float To Bool`: 閾値と比較方向を持つ。
- `Bool To Float`: false値とtrue値を持つ。
- `Compose Vector2`、`Compose Vector3`、`Compose Vector4`: Float入力からVectorを構成する。
- `Split Vector2`、`Split Vector3`、`Split Vector4`: 各成分を名前付きFloat出力へ分解する。
- `Vector Component`: Vector型と成分を選び、Floatを出力する。
- `Color To Luminance`: Linear RGBからRec.709係数で輝度を求める。
- `Float To Color`: FloatをLinear RGBへ複製し、Alphaをパラメーターで指定する。

## 共通規則

- すべて通常の登録済みノード型としてグラフ上に表示する。
- 非可逆な選択、丸め、閾値およびAlpha値はパラメーターとして保存する。
- 数値のNaNとInfinityはFaultedとする。
- 出力は対象ポート型の有効範囲へクランプする。
- 変換ノードの追加は通常のNodeTypeRegistryとSchemaVersion契約へ従う。

## 設計意図

- 情報を失う処理を接続線の裏へ隠さない。
- 丸めや成分選択をプロジェクトに再現可能な設定として残す。
- 初期版で頻出する型変換だけを提供し、汎用式言語は導入しない。
