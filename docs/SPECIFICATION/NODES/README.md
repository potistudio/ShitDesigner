# Nodes設計インデックス

## 状態

`docs/SPECIFICATION/REQUIREMENTS.md` を満たす初期版ノード設計は確定。本文書は個別仕様への索引であり、競合時は各個別仕様の具体的な規則を優先する。

- [初期ノード設計サマリー](DesignSummary.md)

## 映像共通契約

- [ImageFrame実装契約](ImageFrameRuntimeContract.md)
- [内部色空間](InternalColorSpace.md)
- [内部Dynamic Range](InternalDynamicRange.md)
- [内部Alpha](InternalAlpha.md)
- [表示色変換とAlpha境界](DisplayTransformPolicy.md)
- [Graphics API対応](GraphicsApiSupport.md)
- [RenderTextureプール運用](RenderTexturePoolPolicy.md)
- [解像度要求と出力サイズ](ResolutionAndOutputPolicy.md)

## ノード登録と保存互換

- [ノード型定義とランタイムカタログ](NodeDefinitionRuntimeCatalog.md)
- [ノードスキーマ移行とUnknownNode復旧](NodeSchemaMigrationAndUnknown.md)
- [Visualノード型ごとのポートとパラメーター](VisualNodeDefinitionPolicy.md)
- [Shaderノードの明示的バインド](ShaderBindingContract.md)
- [3D／2D Sceneノードの実行時分離](SceneNodeRuntimeIsolation.md)

## グラフと評価

- [ノード評価フレームのライフサイクル](NodeEvaluationLifecycle.md)
- [ポート型カタログと接続編集](PortCatalogAndEditing.md)
- [ポート変換カタログ](PortConversionCatalog.md)
- [明示的な非可逆変換ノード](LossyConversionNodes.md)
- [任意入力の既定値](OptionalInputDefaults.md)
- [Feedbackノードの履歴運用](FeedbackRuntimePolicy.md)
- [ランタイム診断ポリシー](RuntimeDiagnosticsPolicy.md)

## パラメーターとライブ操作

- [パラメーター型の実装表現](ParameterTypeRepresentation.md)
- [パラメーター更新キュー](ParameterUpdateQueue.md)
- [パラメーターUI契約](ParameterUIContract.md)
- [ランタイム状態パラメーター](RuntimeStatefulParameter.md)
- [Value論理コントロールのマッピング](LogicalControlValueMapping.md)
- [論理コントロール合成式の検証](LogicalControlExpressionValidation.md)
- [PresetTriggerの発火と適用](PresetTriggerRuntime.md)
- [初期物理入力の範囲](PhysicalInputScope.md)

## プリセットとプロジェクト

- [プリセット](Preset.md)
- [プリセットの識別と編集](PresetIdentityAndEditing.md)
- [VJプロジェクトの保存と復旧](VJProjectPersistence.md)
- [MediaAssetカタログ](MediaAssetCatalog.md)
- [素材整合性ハッシュ](AssetIntegrityHash.md)

## 動画

- [GraphClock実行仕様](GraphClockRuntime.md)
- [VideoPlayer Transport契約](VideoTransportContract.md)
- [VideoPlayerノードのデコードと出力](VideoPlaybackRuntime.md)
- [Hap動画デコーダBackend](HapVideoBackend.md)

## 出力

- [Program出力の提示と運用](ProgramRuntimePolicy.md)
- [Previewの表示と自動品質制御](PreviewRuntimePolicy.md)
- [FHD・60fps性能試験基準](PerformanceBaseline.md)

## 初期版の明示的な上限と対象外

- Programは1系統、1920×1080・60fps固定。
- 同時表示Previewは最大8個。
- 3D／2D Sceneノードは専用Unity Layer数に合わせて合計24個まで。
- プロジェクト全体の接続数は4096まで。
- `Value` と `PresetTrigger` の初期物理入力はKeyboardだけとする。
- `PresetTrigger` はプリセット呼び出し専用。
- Mouse、Gamepad、MIDI、OSC、DMX、NDI、Spout、録画、音声再生、ランタイムノードプラグイン、HDRディスプレイ直接出力は初期版の対象外。

## 設計原則

- 保存値と実効値を分離し、フレーム境界で状態を原子的に確定する。
- 不明型、参照切れ、変換欠落を削除せず、修復可能なBroken／Unknown状態で保持する。
- Program品質をPreviewより優先する。
- GPUリソースを共通プールが所有し、ノードは借用する。
- 情報を失う型変換はノードとしてグラフ上に表示する。
