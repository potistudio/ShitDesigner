# Shaderノードの役割

## 状態

GeneratorとEffectの分離、カテゴリ、カタログ参照、明示的Propertyバインドおよび診断まで確定。

## 共通契約

- Shaderによる映像生成と映像加工を、接続状態で切り替わる1種類のノードにまとめない。
- Shaderノード型は、定義時に `Generator` または `Effect` の役割を固定する。
- どちらの役割も主出力ポート `Image` から `ImageFrame` を出力する。
- どちらの役割も内部Linear色空間、プロジェクトのHDR／LDR形式、Premultiplied Alpha契約に従う。
- 同じノードインスタンスが、入力の接続または切断によってGeneratorとEffectの間を切り替わることはない。
- Shader表現ごとに独立ノード型を作り、3D／2D描画ノードと同じ登録、保存、`SchemaVersion` 契約を使用する。
- Generatorのカテゴリ表示は `Shader/Generator`、Effectは `Shader/Effect` とする。
- Shader、Material、Property、Port、ParameterおよびPassの対応はカタログへ明示し、ビルド時に検証する。
- Effectは入力Textureの寸法ではなく、伝播された要求出力解像度で実行する。

## Shader Generator

- `Generator` は、映像入力を必要とせず映像を生成する。
- `Generator` は `ImageFrame` 型の必須入力を持たない。
- `Generator` は数値、Vector、Colorなど、定義済みポート型の入力を持つことができる。
- `Generator` は出力ターゲットから伝播した要求解像度で映像を生成する。

## Shader Effect

- `Effect` は、1つ以上の映像入力を使用して映像を加工または合成する。
- `Effect` は少なくとも1つの必須 `ImageFrame` 入力を持つ。
- 複数映像を使用する `Effect` は、`A`、`B`など役割が分かる名前付き入力ポートを個別に定義する。
- 任意のマスク映像などは、任意入力ポートと既定値の共通契約を使用できる。
- 必須映像入力が利用できない場合は `Blocked` 状態となる。

## 設計意図

- ノードの役割と必要入力を、接続前からノードエディターで判断できるようにする。
- 接続を外しただけでノードの生成内容や保存上の意味が変わることを防ぐ。
- 生成Shader、単入力Effect、複数入力合成を、同じポートと評価契約で拡張できるようにする。
