# Shaderノードの明示的バインド

## 状態

Shader／Material参照、プロパティとポート／パラメーターの対応、解像度および失敗診断は確定。

## 参照

- Shaderノード定義は `NodeTypeCatalog` 内でShaderまたはテンプレートMaterialを直接参照する。
- 各ノードインスタンスは専用Materialを生成し、テンプレートMaterialを変更しない。
- Shaderアセットのパスや名前をプロジェクト保存データへ書かず、NodeTypeIdからカタログ参照を解決する。

## バインド

- `ShaderParameterBinding` はParameterId、Shader Property ID、期待型を明示する。
- `ShaderInputBinding` は入力Port IDとTexture Property IDを明示する。
- 主出力と追加出力のRender Pass／Pass Indexも型定義へ明示する。
- Shader Reflectionから実行時にポートまたはパラメーターを自動生成しない。
- ビルド時にProperty存在、型互換、Pass存在、重複バインドを検証する。

## 実行

- GeneratorとEffectは要求出力解像度のRenderTextureへFullscreen Passを実行する。
- Effect入力は元解像度のままTextureとして受け、Shader内のUVでサンプリングする。
- Optional Image入力がFallback中なら定義済みDefaultImageをバインドする。

## 診断

- 失敗診断はNodeTypeId、NodeInstanceId、Shader名、Pass Index、Property ID、要求解像度、GraphicsFormatおよびGPUメッセージを含める。
- Material生成、Property設定または描画失敗はFaultedとする。

## 設計意図

- Shader変更によって保存済みポート構造が暗黙に変わらないようにする。
- バインド誤りをStandalone実行前のビルド検証で止める。
- 入力解像度と出力要求を分離する。
