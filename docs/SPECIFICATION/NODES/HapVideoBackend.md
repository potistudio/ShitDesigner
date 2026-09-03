# Hap動画デコーダBackend

## 状態

Hap Codec Familyを専用デコーダBackendでWindows／macOSへ対応する方針は確定。

## 対応形式

- 初期保証コンテナはQuickTime MOVとする。
- `Hap` (`Hap1`)、`Hap Alpha` (`Hap5`)、`Hap Q` (`HapY`)、`Hap Q Alpha` (`HapM`) を保証する。
- `Hap R`、`Hap HDR`、Alpha OnlyおよびMOV以外のコンテナは初期保証対象外とする。
- 音声トラックはデコードしない。

## Backend

- 動画ノードはCodec Probeによって `UnityVideoBackend` または `HapVideoBackend` を選択する。
- H.264とVP8はUnity VideoPlayer Backendを使い、HapはUnity VideoPlayerへ渡さない。
- `HapVideoBackend` はVidvox Hap仕様に従い、MOV Sample Table、Hap Chunk、Snappy圧縮および各画像Planeを解釈する。
- Windows x64とmacOS arm64へ専用Native Pluginを提供し、同じ管理C# APIへ接続する。
- GraphClock、Prepare、Seek、Frame Ready、Faultedおよび出力変換は共通動画Backend契約へ従う。

## GPU経路

- Graphics APIがBC1／BC3／BC4 TextureをSample可能な場合は、展開したHap Blockを圧縮TextureとしてGPUへ直接転送する。
- `Hap Q` はShaderでScaled YCoCgからLinear RGBへ変換する。
- `Hap Q Alpha` はColor PlaneとAlpha PlaneをShaderで合成する。
- 圧縮Textureを直接利用できない場合はCompute ShaderでRGBAへ展開する。
- Compute Shader経路も利用できない場合だけCPUでRGBAへ展開し、性能低下を操作画面へ表示する。
- 最終的にプロジェクト内部形式へ変換し、Premultiplied AlphaのImageFrameを出力する。

## API別検証

- Direct3D 12、Vulkan、Metalごとに圧縮TextureとCompute Shader経路を起動時にProbeする。
- 選択した経路を動画ノード診断へ表示する。
- 全経路が利用できない場合は素材をFaultedとし、別Codecへ暗黙変換しない。

## 設計意図

- OSネイティブCodecの有無に依存せずHapを保証する。
- HapのGPU向けBlock形式を利用し、高解像度動画のCPU負荷を抑える。
- Graphics API差を動画Backend内へ閉じ込める。
