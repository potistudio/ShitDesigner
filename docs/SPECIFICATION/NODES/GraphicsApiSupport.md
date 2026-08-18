# Graphics API対応

## 状態

WindowsのDirect3D 12／Vulkanと、macOSのMetalを初期対応Graphics APIとして確定。

## プラットフォーム

- Windows x64ビルドはDirect3D 12を第1Graphics API、Vulkanを第2Graphics APIとして含める。
- 通常起動はDirect3D 12を使用し、検証時はVulkanを明示選択して同じプロジェクトを試験する。
- macOS Apple SiliconビルドはMetalを使用する。
- Windows用Native PluginとmacOS用Native Pluginは別バイナリとして配布する。

## 共通描画契約

- ノード、ImageFrame、RenderTexture Lease、URP Shaderおよび保存形式はGraphics APIへ依存させない。
- 起動時に内部GraphicsFormatのRender、Sample、Load／Store対応を `SystemInfo.IsFormatSupported` で検証する。
- Compute Shaderまたは圧縮Textureを使うBackendは、必要機能を個別に検証して対応経路を選ぶ。
- API固有機能が使えない場合は、同じ映像契約を保てるフォールバックだけを許可する。
- 色精度、AlphaまたはDynamic Rangeが変わる暗黙フォールバックは行わない。

## 検証対象

- Windows基準PCではDirect3D 12とVulkanの両方で機能試験を行う。
- WindowsのFHD・60fps合格判定はDirect3D 12を主基準とし、Vulkan結果も別記録する。
- MacBook Pro M4ではMetalで同じ機能試験と性能試験を行う。
- ShaderのコンパイルエラーとGraphics API別Variant欠落をビルド検証で失敗させる。

## 設計意図

- Unityが抽象化する通常描画と、HapなどAPI依存のTexture経路を分ける。
- Vulkan／Metal対応によってノード保存形式やグラフ構造を分岐させない。
- 対応していないGPU形式を実行中に推測で置き換えない。
