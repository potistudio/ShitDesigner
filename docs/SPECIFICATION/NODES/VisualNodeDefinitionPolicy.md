# Visualノード型ごとのポートとパラメーター

## 状態

3D、2D、Shaderおよび動画カテゴリの入力数、追加出力、公開パラメーターおよび保存責務は確定。

## 共通規則

- カテゴリ全体へ固定の入力数やパラメーター一覧を設けない。
- 各登録ノード型が、固定Port ID、型、Required／Optional、既定値、追加出力およびParameterDefinitionを明示する。
- すべてのVisualノードはPort ID `image` の主 `ImageFrame` 出力を持つ。
- 追加出力は初期ポート型のいずれでも定義できる。
- 保存するのは型ID、SchemaVersion、BaseValue、ノード固有状態および共通状態とし、Prefab階層、Materialインスタンス、CameraおよびRenderTextureは保存しない。

## 3D／2D

- 3D／2DのPrefab参照、ポート、パラメーターは `NodeTypeCatalog` の型定義へ含める。
- ノード型はScene生成後に必要なCameraが1つ存在することをFactory検証する。
- 追加出力を定義した場合、要求された出力だけを1回の評価で生成する。

## Shader

- Shaderノード型はGeneratorまたはEffectへ固定する。
- Generatorのカテゴリ表示は `Shader/Generator`、Effectは `Shader/Effect` とする。
- Effectは要求出力解像度で実行し、入力Textureの解像度へ出力を固定しない。

## VideoPlayer

- VideoPlayerは映像入力と追加出力を持たず、Transportパラメーターだけを公開する。
- 将来メタデータ出力が必要な場合はSchemaVersionを上げて固定ポートとして追加する。

## 設計意図

- 表現ごとの差をカテゴリ分岐ではなく登録済み型定義へ閉じ込める。
- 保存対象とUnity実行時オブジェクトを分離する。
- 動的ポート生成を避け、接続保存を安定させる。
