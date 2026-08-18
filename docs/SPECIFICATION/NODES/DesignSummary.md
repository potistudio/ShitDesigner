# 初期ノード設計サマリー

## 状態

初期版の全ノード設計判断をレビュー用に集約済み。各判断は個別仕様として確定。

## 全体

- Program 1系統と複数Previewを起点に、必要な上流だけをフレーム単位でPull評価する。
- グラフ編集、パラメーター、論理入力、プリセットを評価フレーム境界で確定し、同一フレーム中は変更しない。
- 映像はLinear、Premultiplied Alpha。プロジェクト既定はHDR RGBA16F、最終表示はACESでLDR Rec.709／sRGBへ変換する。
- GPU Textureはアプリ共通プールが所有し、出力ポートが継続Leaseする。

## ノード

- 3D、2D、Shader表現は、表現ごとに独立した登録ノード型を作る。
- NodeTypeIdは `vendor.category.name`、SchemaVersionは1開始、登録一覧はビルド生成ScriptableObjectカタログとする。
- 新規ノード型の追加にはStandalone再ビルドが必要。ランタイムプラグインは初期対象外。
- 未知型、新しいSchemaVersion、移行失敗はUnknownNodeとして生データと接続を保持する。
- ポートは型定義で固定し、実行中に増減しない。

## 3D／2D

- ノードごとにAdditive SceneとローカルPhysicsを所有する。
- User Layer 8～31を1ノード1Layerで貸し出し、3D／2D合計24ノードを上限とする。
- CameraはURP Render Requestで専用RenderTextureへ描画する。
- 2D CanvasはWorld SpaceまたはScreen Space - Cameraだけを許可する。

## パラメーターと論理入力

- 保存する `BaseValue` と、ノードが使う `EffectiveValue` を分離して両方表示する。
- `Value` 論理コントロールは `0～1` のFloat。ターゲット側でBool、数値、Vector、Colorへ線形変換する。
- 複数Valueは二項 `Min`／`Max` 式ツリーで合成する。Base葉は最大1つ、定数葉は持たない。
- `String`、`Enum`、`MediaAssetReference` は論理コントロールの直接対象外。
- `PresetTrigger` はプリセット呼び出し専用で、1つにつき割り当て先は最大1つ。
- Buttonの立ち上がり、またはアナログの0.5到達で発火し、0.4未満へ戻ると再発火可能になる。
- 初期物理入力はKeyboardだけとし、KeyをValueの0／1またはPresetTriggerへ割り当てる。

## プリセット

- プリセットは選択したBaseValueだけを保存する部分スナップショット。
- ノード、接続、出力、Preview、Enabled状態は変更しない。
- 事前検証後、次の評価境界で補間せず原子的に適用する。
- 複数プリセットが同じフレームに来た場合は入力シーケンス順、同じパラメーターは後勝ちとする。
- 参照切れ項目を含むプリセットは全体を適用しない。

## Program／Preview

- Programは1920×1080固定、60fps目標。選択Unity Displayへ全画面表示する。
- 障害時は `HoldingLastFrame`。正常フレームがなければ不透明黒を表示する。
- Previewはノード内サムネイルとドッキングパネルへ表示し、同時表示は最大8個。
- 通常Previewは640×360・30fps。負荷時は5段階で160×90・5fpsまで下げる。
- Fit余白は透明黒、Fillは中央基準、Stretchは縦横独立拡縮。

## Feedback

- 任意入力 `Input` と出力 `Image` を持つ。
- 2枚のTextureで前フレームを出し、グラフ評価後に現在入力を次履歴へコミットする。
- 初回、Reset、解像度／形式変更時は透明黒へ戻す。

## 動画

- `shitdesigner.video.player` 1型で全動画素材を扱う。
- H.264／VP8はUnity VideoPlayerをAPI Onlyで使い、Hapは専用Backendを使う。どちらもGraphClockへ同期する。
- Playheadは秒、Speedは0～4、負数なし。Loop無効の終端ではPlayingをfalseにする。
- PlayheadへValue式がある間はScrub Modeとなり、自動進行を止める。
- 初期保証はH.264 MP4、VP8 WebM Alpha、およびMOVのHap／Hap Alpha／Hap Q／Hap Q Alpha。

## 保存

- プロジェクトはフォルダー単位、UTF-8 `project.json` と `Assets/{MediaAssetId}` で構成する。
- 保存は一時ファイル、読戻し検証、原子的置換、`.bak` 1世代の順で行う。
- ノード移行前バックアップは `Backups` に5世代保持する。
- 素材参照はUUIDと相対パス、整合性はXXH3-128で検証する。

## 性能と安全上限

- RenderTexture予算は専用GPUでVRAMの50%を基本とし、最低1.5GiBを他用途へ残す。M4のUnified Memoryでは4GiBを既定とする。
- 接続数はプロジェクト全体で4096まで。出力ポート単位のFan-out上限は設けない。
- Windows基準PCはRyzen 5 5600X、RTX 3060 12GB、Memory 32GB、Windows 10。D3D12を主基準、Vulkanも検証する。
- macOS基準PCはMacBook Pro M4、Unified Memory 16GB、Metalとする。
- 基準グラフを10分動かし、99%以上のProgramフレームが16.67ms以内であることを合格条件とする。

## 初期対象外

- Mouse、Gamepad、MIDI、OSC、DMX、NDI、Spout、録画、動画音声、HDRディスプレイ直接出力。
- 複数Program、可変Program解像度、ランタイムノードプラグイン、動的ポート数。
- 負の動画再生速度、動画In／Out点、フレーム送り。

## レビュー時に特に確認する判断

- Unity Layer制約による3D／2D合計24ノード上限。
- Program固定1920×1080とPreview最大8個。
- Preview自動品質の具体的な閾値と5段階。
- 専用GPU 50%／Unified Memory 4GiBのVRAM予算式と確保失敗時のFaulted動作。
- PlayheadのRuntimeStateful／Scrub Mode。
- 同一フレームに複数プリセットが来た場合の後勝ち規則。
- Hap専用Native Backendと、Graphics API別の直接圧縮Texture／Compute Shader経路。
- 基準PCと性能試験の99%判定。
