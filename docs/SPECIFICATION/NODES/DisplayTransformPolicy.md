# 表示色変換とAlpha境界

## 状態

Program／Previewの表示色空間、トーンマッピング、外部素材の既定解釈、Alpha変換およびGPU形式検証は確定。

## 内部形式

- プロジェクト内部はLinear色空間とPremultiplied Alphaへ固定する。
- HDRプロジェクトは `R16G16B16A16_SFloat`、LDRプロジェクトは `R8G8B8A8_UNorm` を使用する。
- HDR／LDRはプロジェクト読込時に確定し、実行中には変更しない。
- 設定変更はプロジェクト再読込を要求する。

## 表示変換

- ProgramとPreviewは同じ表示変換を使用する。
- HDR内部映像はURPのACES Tone Mappingを適用してLDRへ変換する。
- LDR内部映像はトーンマッピングを行わない。
- 表示出力はRec.709色域とsRGB Transfer Functionを使用する。
- Wide GamutおよびHDRディスプレイ直接出力は初期版の対象外とする。

## 外部素材

- 色プロファイルを持たない静止画はsRGB／Rec.709として解釈する。
- 動画はデコーダが提供する色メタデータを優先し、存在しない場合はRec.709として解釈する。
- Alphaを持たない素材はAlpha `1.0` とする。
- Alphaを持つ静止画はStraight Alphaを既定とし、インポート設定で上書きできる。
- 入力境界でLinear化してPremultiplyし、内部形式へ変換する。

## Alpha出力

- 通常のProgram画面表示はPremultiplied映像を不透明黒へ合成する。
- 内部Program TextureはPremultiplied Alphaを保持する。
- Straight Alphaが必要な将来出力では、Alphaが `1e-6` 以下の画素をRGBゼロとし、それ以外をAlphaで除算する。
- 初期版には外部Alpha出力APIを実装しない。

## GPU対応

- 起動時に `SystemInfo.IsFormatSupported` で内部GraphicsFormatのRender／Sample対応を検証する。
- HDRのRGBA16FをサポートしないGPUではLDRへ暗黙フォールバックせず、プロジェクトを開かない。
- LDRのRGBA8をサポートしないGPUでも同様に起動診断を出す。
- Alphaや精度を変える別形式への自動置換は行わない。

## 設計意図

- ProgramとPreviewの見え方を一致させる。
- GPU差によって保存済みプロジェクトのDynamic RangeやAlpha契約が暗黙に変わることを防ぐ。
- 外部素材の不明メタデータへ再現可能な既定解釈を与える。
