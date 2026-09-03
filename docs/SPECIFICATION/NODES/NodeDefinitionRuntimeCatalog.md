# ノード型定義とランタイムカタログ

## 状態

ノード型ID、登録方式、Factory契約、システム所有フラグおよび配布単位は確定。

## NodeTypeId

- `NodeTypeId` は全カテゴリを通して一意な小文字ASCII文字列とする。
- 形式は `vendor.category.name` とし、例を `shitdesigner.shader.crossfade` とする。
- 公開後の `NodeTypeId` は変更しない。
- `ParameterId` とPort IDはノード型内で一意とし、lower snake caseを使用する。

## 定義契約

- `INodeTypeDefinition` は `NodeTypeId`、`SchemaVersion`、表示名、カテゴリ、ポート定義、パラメーター定義、Factory、所有区分を公開する。
- `INodeFactory` は検証済み状態から `IRuntimeNode` を生成し、破棄時に所有リソースを解放する。
- 3D／2D定義は生成Prefabを、Shader定義はShaderまたはテンプレートMaterialと明示的バインドを参照できる。
- インスタンス固有のMaterialはテンプレートから複製し、共有Materialを変更しない。

## カタログ構築

- Unity Editor上のビルド前処理で `NodeTypeCatalog` ScriptableObjectを生成する。
- Standaloneは起動時にカタログを読み、共通 `NodeTypeRegistry` を構築する。
- 実行時Reflection検索、フォルダー走査および外部Assemblyのホットロードは初期版で行わない。
- 型ID重複、Port ID重複、Parameter ID重複、参照欠落、Shaderバインド不一致はビルドを失敗させる。
- 起動時にも同じ検証を行い、不正カタログではプロジェクトを開かず診断を表示する。

## 所有区分と配布

- 所有区分は `UserAddable` または `SystemOwned` とする。
- `SystemOwned` ノードは追加メニューへ表示せず、削除と複製を禁止する。
- 新しいノード型はカタログへ追加し、Standaloneアプリを再ビルドして配布する。
- ランタイムプラグインによる型追加は初期版の対象外とする。

## 設計意図

- 不正なノード定義を本番中ではなくビルド時に検出する。
- カテゴリ別の登録方式を増やさず、同じ保存と生成契約へ揃える。
- 現在必要のないプラグイン基盤を初期版へ持ち込まない。
