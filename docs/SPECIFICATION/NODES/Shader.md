# シェーダーノード

## 状態

確定。`docs/SPECIFICATION/REQUIREMENTS.md` および関連する個別仕様に基づく初期版のシェーダーノード仕様とする。

## 確定事項

- シェーダーによる映像表現を扱うノードである。
- 必須の主出力ポート `Image` から、映像実体の `RenderTexture` を含む `ImageFrame` を出力する。
- `Image` に加えて、任意の名前付き出力ポートを定義できる。
- 3D描画、2D描画、プリレンダ動画の各ノードと同じノードグラフへ配置できる。
- 出力は同一グラフ内で加工・合成できる。
- Standaloneアプリの実行中に扱えるノードである。
- 新しいノード種別を追加するとき、既存グラフや保存形式の全面変更を要求しない拡張構造へ従う。
- シェーダーによる映像処理はLinear色空間で行う。
- Premultiplied Alpha形式で映像を出力する。
- 共通 `NodeTypeRegistry` へShaderカテゴリのノード型として登録する。
- Shaderノード型は、映像入力なしで生成する `Generator` または、1つ以上の映像入力を加工・合成する `Effect` のどちらかへ定義時に固定する。
- `Generator` は `ImageFrame` 型の必須入力を持たず、要求解像度で映像を生成する。
- `Effect` は少なくとも1つの必須 `ImageFrame` 入力を持つ。
- 複数映像を扱う `Effect` は、役割が分かる名前付き入力ポートを必要数だけ定義する。
- 入力の接続状態によって同じノードが `Generator` と `Effect` を切り替えることはない。
- Shader表現ごとに、安定した型ID、ShaderまたはMaterial、固定ポート、公開パラメーター定義を持つ独立ノード型として登録する。
- Shaderプロパティからポートやパラメーターを実行時に自動生成しない。
- 同じShaderノード型を複数配置でき、各インスタンスのMaterial状態とパラメーターを分離する。
- VJプロジェクトには型ID、`SchemaVersion`、インスタンス状態を保存し、ShaderやMaterialアセットそのものは保存しない。
- 登録、保存、バージョニングには3D／2D描画ノードと同じ共通契約を使用する。

## 要求への対応

- `docs/SPECIFICATION/REQUIREMENTS.md` Acceptance Criteria: 「シェーダーノードがRenderTextureを出力できる」
- `docs/SPECIFICATION/REQUIREMENTS.md` Acceptance Criteria: 4種類のノードを同一グラフ内で接続し、加工・合成できる
