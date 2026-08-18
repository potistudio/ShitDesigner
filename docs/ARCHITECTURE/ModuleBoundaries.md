# モジュール境界

## 状態

確定。モジュール分割、asmdef構成、参照関係および公開API方針を定める。

## 分割方針

- モジュールは機能上の所有権を表し、単なるフォルダー分類にしない。
- モジュール間参照はasmdefで明示し、循環参照を許可しない。
- UnityのシーンまたはPrefabに直接配置するMonoBehaviourは、Bootstrapまたは該当Unity連携モジュールへ限定する。
- Project、GraphおよびRuntimeの中心ロジックは、MonoBehaviourのライフサイクルへ直接依存させない。
- Editor専用コードをStandalone用Assemblyから参照しない。
- 外部I/OとUnity Objectの生成・破棄は、所有モジュールが一元管理する。

## モジュール責務

### Core

アプリケーション全体で意味が変わらない最小の契約を所有する。

- `NodeInstanceId`、`MediaAssetId`、`PresetId`、`LogicalControlId` などの安定ID
- `NodeTypeId`、`ParameterId`、`PortId` などの安定文字列ID
- 成功または回復可能な失敗を表すResult契約
- Diagnostic、Severity、DiagnosticCodeの共通表現
- 他モジュール固有のサービス実装を含めない

### Project

VJプロジェクトとして保存可能な宣言状態を所有する。

- `ProjectDocument`
- ノード、接続、BaseValue、論理コントロール、プリセット、素材メタデータ、プロジェクト固有UI状態
- Project Dirtyの追跡
- Undo／Redo履歴と編集結果
- プロジェクト全体に対する整合性検証
- RenderTexture、Scene、GameObjectおよび実行時診断を所有しない

### Graph

ノードグラフの構造と、評価可能な計画への変換を所有する。

- GraphEditCommandの検証と適用
- ポート互換性、暗黙変換および接続上限の検証
- Feedbackを時間境界として扱う循環検証
- 評価起点から必要な上流を求める `EvaluationPlan`
- グラフ構造の変更を検知するバージョン
- 個別ノードの描画処理を所有しない

### Runtime

1フレームの進行と実行時状態を所有する。

- `RuntimeSession`
- `FrameCoordinator`
- `FrameSnapshot`
- `GraphClock`
- コマンドおよびイベントのフレーム境界での切り離し
- EvaluationPlanに従うノード評価の調停
- 永続化形式およびGUI部品を所有しない

### Rendering

映像フレームとGPU Textureの所有権を管理する。

- `ImageFrame` と `NodeOutputResult`
- 共通RenderTexture PoolとLease
- 解像度要求の統合
- Program提示、最後の正常フレームおよびPreview品質制御
- Display Transformと出力境界

### Nodes

ノード型の定義、生成および組み込み実装を所有する。

- `INodeTypeDefinition`、`INodeFactory`、`IRuntimeNode`
- ノード型ごとのPortおよびParameter定義
- Shader、Video、Feedback、変換などの組み込みノード
- ノードごとのUnity Objectと一時状態
- 共通Texture Pool、Scene Poolまたは永続化サービス自体は所有しない

### Scene

3D／2D Sceneノードが使用するUnity Scene境界を所有する。

- Additive Sceneの生成とアンロード
- Local Physicsの手動更新
- User Layer 8～31の貸出
- Camera Render Request
- ノード削除後の非同期破棄完了管理

### Media

素材ファイルの意味と再生Backend境界を所有する。

- 素材形式のProbe結果
- 静止画および動画のデコード準備
- Unity VideoPlayer Backend
- Hap Native Backend
- GraphClockへ同期するTransport
- プロジェクトフォルダーへのコピー処理自体はPersistenceと協調する

### Input

物理入力を論理操作へ変換する経路を所有する。

- Unity Input SystemのKeyboard Adapter
- SequenceNumberの発行
- Valueの正規化
- PresetTriggerの立ち上がりとヒステリシス
- パラメーター更新キューへの要求投入
- NodeまたはProjectDocumentへ直接書き込まない

### Persistence

VJプロジェクトを外部ファイルへ安全に保存・復元する。

- `project.json` DTOとUTF-8 JSON変換
- ProjectFormatVersionとノードSchemaVersion移行
- 原子的保存、`.bak`、`Backups`
- 素材コピー、サイズおよびXXH3-128検証
- UnknownNodeの生状態保持
- 読み込んだDTOを実行状態へ直接採用せず、Projectの検証を通す

### Presentation

Standalone操作画面とユーザー操作の翻訳を所有する。

- Runtime UI ToolkitのView
- Node Graph、Dock Workspace、Inspector、Dashboard、Diagnostics
- Application APIへの操作要求
- ProjectとRuntimeの読取モデル表示
- Runtime Node、RenderTexture Poolまたはファイルへ直接書き込まない

### Bootstrap／Editor

アプリケーションの組み立てとビルド時処理を所有する。

- 起動用Composition Root
- Unity PlayerLoopからFrameCoordinatorへの呼び出し
- アプリケーション終了時の破棄順序
- NodeTypeCatalog ScriptableObjectのビルド前生成
- 型ID、Port、ParameterおよびShader Bindingのビルド時検証
- Editor APIを使用するコードをEditor専用Assemblyへ隔離する

## 境界を越える基本規則

- PresentationおよびInputからの変更はApplication APIを経由する。
- バックグラウンド処理の完了通知はスレッドセーフなキューを経由し、Unity Objectをバックグラウンドスレッドで変更しない。
- RuntimeはProjectDocumentを評価中に直接変更しない。
- NodesはRenderTextureを生成・破棄せず、RenderingからLeaseを借用する。
- NodesはAdditive SceneまたはLayerを直接管理せず、Sceneモジュールから借用する。
- Persistenceは読込データを検証なしで現在のProjectDocumentへ置き換えない。
- Presentationは実行状態を表示できるが、EffectiveValueおよび診断状態を保存状態へ書き戻さない。

## asmdef構成

概念上のモジュールをそのまま巨大な単一Assemblyへまとめず、次のRuntime用asmdefへ分割する。Applicationは独立asmdefとし、PresentationとInputが使用する変更要求および読取APIを集約する。

| asmdef | 直接参照できるプロジェクトAssembly | 備考 |
|---|---|---|
| `ShitDesigner.Core` | なし | 安定ID、共通Result、診断契約 |
| `ShitDesigner.Project` | Core | 保存可能モデルと編集履歴 |
| `ShitDesigner.Graph` | Core、Project | グラフ検証とEvaluationPlan |
| `ShitDesigner.Runtime` | Core、Project、Graph | フレーム進行とノード実行契約 |
| `ShitDesigner.Rendering` | Core、Runtime | Texture Pool、Program、Preview |
| `ShitDesigner.Scene` | Core、Runtime | Additive SceneとLayer管理 |
| `ShitDesigner.Media` | Core、Runtime | 素材Probeと動画Backend |
| `ShitDesigner.Nodes` | Core、Runtime | 組み込みノードとカタログ実体 |
| `ShitDesigner.Persistence` | Core、Project、Runtime | DTO、JSON、移行、ファイルI/O |
| `ShitDesigner.Application` | Core、Project、Graph、Runtime、Persistence | ユースケースと外部向けFacade |
| `ShitDesigner.Input` | Core、Application | Input System Adapter |
| `ShitDesigner.Presentation` | Core、Application | Runtime UI Toolkit |
| `ShitDesigner.Bootstrap` | 上記Runtime用Assembly | Composition Rootだけを所有 |
| `ShitDesigner.Editor` | 必要なRuntime用Assembly | Editor限定、Standaloneへ含めない |

### 参照規則

- 表の直接参照にないAssemblyを、推移的依存を利用して直接使用しない。
- Rendering、Scene、Media、Nodesは相互の具象型を参照しない。共有に必要な最小契約はRuntimeに置き、Bootstrapが具象実装を注入する。
- `ShitDesigner.Application` はRendering、Scene、Media、Nodesの具象型を参照しない。
- `ShitDesigner.Input` と `ShitDesigner.Presentation` はApplicationへ要求を送り、ProjectまたはRuntimeへ直接書き込まない。
- `ShitDesigner.Bootstrap` 以外のRuntime用Assembly同士で循環参照を作らない。
- `ShitDesigner.Editor` はEditorプラットフォームだけを対象とし、Runtime用Assemblyから参照しない。
- テストAssemblyは対象asmdefごとに分け、テストのためだけに本番Assembly間の参照を増やさない。

## 公開API方針

- Assembly外から使う型だけを `public` とし、実装型は原則 `internal` とする。
- 各Assemblyの公開入口は少数のFacade、契約、値型に限定する。
- `InternalsVisibleTo` は対応するテストAssemblyだけに許可する。
- Runtime Nodeが使用するサービス群は、巨大なService Locatorではなく用途別のContextとして渡す。
- 読取APIと変更要求APIを分け、Presentationへ可変コレクションを返さない。
