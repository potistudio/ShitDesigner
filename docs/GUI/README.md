# GUI設計

## 状態

初期版GUI仕様は確定。機能仕様と競合する場合は `docs/SPECIFICATION/` の個別仕様を優先し、GUI側で差分を明示する。

## 目的

- Standaloneアプリだけで、VJプロジェクトの構築、ライブ操作、監視、保存および復旧を完結させる。
- Program映像の安定性を最優先し、GUIとPreviewの負荷を自動的に抑制する。
- ノード型やパラメーターが増えても、共通パネルとメタデータ駆動UIで操作できるようにする。
- ライブ中に必要な情報を短時間で判別でき、異常をProgram映像へ混入させない。

## 文書

- [ワークスペースとレイアウト](Workspace.md)
- [ノードグラフ](NodeGraph.md)
- [パラメーターとライブ操作](ParametersAndControls.md)
- [ProgramとPreview](OutputMonitoring.md)
- [プリセットと素材](PresetsAndMedia.md)
- [診断とプロジェクト操作](DiagnosticsAndProject.md)
- [視覚言語と入力操作](VisualAndInteraction.md)
- [GUI受け入れ条件](AcceptanceCriteria.md)

## 基本構成

GUIは1つのメインウィンドウ内に、トップバー、ドックワークスペース、ステータスバーを持つ。機能パネルは自由にドッキング、タブ化およびリサイズできるが、独立したOSウィンドウにはしない。

次のパネル種別を初期版で提供する。

| パネル | インスタンス数 | 主な役割 |
|---|---:|---|
| Node Graph | 1 | ノード追加、削除、接続、選択 |
| Node Library | 1 | 登録ノード型の検索と追加 |
| Inspector | 1 | 選択ノードの全パラメーター編集 |
| Live Dashboard | 1 | 複数ノードの任意パラメーターをライブ操作 |
| Logical Controls | 1 | Value／PresetTrigger、Keyboard割り当て、ターゲット式の編集 |
| Program Monitor | 1 | Program映像と出力状態の監視 |
| Preview Viewer Host | 1 | 最大8個のPreviewタブを収容するドック領域 |
| Presets | 1 | ボタン型プリセットの呼び出しと整理 |
| Preset Editor | 1 | 部分プリセットの対象と保存値の編集 |
| Media Library | 1 | 素材のインポート、参照、削除 |
| Diagnostics | 1 | 現在状態、履歴、詳細、書き出し |

## 設計原則

- `Edit` と `Live` は固定モードではなく、通常のユーザーレイアウトプリセットとする。
- レイアウトは表示構成だけを扱い、操作権限やVJプロジェクト固有の内容を持たない。
- ノード編集はどのレイアウトでも常に有効とする。
- プロジェクト変更とレイアウト変更は別々のDirty状態として表示し、別々に保存する。
- 頻繁な操作は非モーダルにし、破壊的操作、未保存プロジェクトの終了、復旧不能エラーだけをモーダル確認にする。
- Blocked、Faulted、Preparing、UsingFallback、HoldingLastFrameを、色だけでなくアイコンと文字でも区別する。
- `BaseValue` と `EffectiveValue` を省略せず、どのパラメーターUIでも意味を統一する。

## 仕様との所有境界

- ユーザー設定: レイアウトプリセット、UIスケール、テーマ設定、最近開いたプロジェクト。
- VJプロジェクト: ノード位置、Live Dashboardの内容、Previewタブの表示対象と順序、パネルが扱うプロジェクト固有データ。
- セッションのみ: Undo／Redo、診断履歴、現在の選択、スクロール位置、一時検索語。
- レイアウトプリセット: パネル種別とインスタンス、配置、サイズ、表示状態、タブ構成だけ。

`VJProjectPersistence` の「UI配置」は、ノード位置とプロジェクト固有パネル内容を指す。ユーザーのワークスペース配置は含めない。

## 主な根拠仕様

| GUI領域 | 根拠 |
|---|---|
| Node Graph | `NodeTypeRegistry.md`、`PortCatalogAndEditing.md`、`UnknownNode.md` |
| Inspector／Dashboard | `ParameterUIContract.md`、`ParameterBaseAndEffectiveValue.md` |
| Logical Controls | `LogicalControlBinding.md`、`LogicalControlExpressionValidation.md`、`PhysicalInputScope.md` |
| Program／Preview | `ProgramRuntimePolicy.md`、`PreviewRuntimePolicy.md`、`PreviewDisplayMode.md` |
| Presets | `PresetIdentityAndEditing.md`、`AtomicPresetApplication.md` |
| Media | `MediaAssetCatalog.md`、`AssetIntegrityHash.md` |
| Diagnostics | `RuntimeDiagnosticsPolicy.md`、`BlockedNodeState.md`、`FaultedNodeState.md` |
| Project | `VJProjectPersistence.md`、`NodeSchemaMigrationAndUnknown.md` |
