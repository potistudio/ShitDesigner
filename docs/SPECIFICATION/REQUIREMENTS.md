# REQUIREMENTS

## 状態

確定。本書を初期版の要求に関する正本とし、具体的な実行規則は `NODES/` の各個別仕様、操作と表示は `../GUI/` の各個別仕様が所有する。

## Metadata

- Interview ID: `afc4a065-2637-4fd1-84b0-b337d2814671`
- Rounds: 10
- Final Ambiguity Score: 16.9%
- Type: brownfield
- Generated: 2026-08-16T12:01:23.5809821+09:00
- Threshold: 20%
- Threshold Source: `default`
- Initial Context Summarized: no
- Interview Result: `PASSED`
- Document Status: `CONFIRMED`
- Approval: confirmed

## Clarity Breakdown

| Dimension | Score | Weight | Weighted |
|---|---:|---:|---:|
| Goal Clarity | 87% | 35% | 30.5% |
| Constraint Clarity | 77% | 25% | 19.3% |
| Success Criteria | 79% | 25% | 19.8% |
| Context Clarity | 91% | 15% | 13.7% |
| **Total Clarity** |  |  | **83.1%** |
| **Ambiguity** |  |  | **16.9%** |

## Topology

| Component | Status | Description | Coverage |
|---|---|---|---|
| 表現モジュール | active | 3D、2D、シェーダー、プリレンダ動画を共通方式で扱う | 全種類がRenderTextureを出力するノードとして動作する |
| 合成・演出 | active | 映像の生成、加工、合成をノードグラフで構成する | Standaloneアプリ内でノード追加・削除・接続変更ができる |
| ライブ操作 | active | パラメーター操作とプリセット切り替えを扱う | 物理入力から独立した論理コントロールを介して操作する |
| 出力・運用 | active | Program映像と操作用Previewを提供する | Program出力はFHD・60fpsを最低基準とする |
| 拡張・保存 | active | VJプロジェクト全体を保存・移動できるようにする | 外部素材をプロジェクト内へコピーする |

## Goal

Unity上で継続的に拡張できる個人用汎用VJシステムを構築する。3D、2D、シェーダー、プリレンダ動画をRenderTextureベースのノードとして共通化し、Standaloneアプリ内のノードエディターでリアルタイムに構成・操作・出力できるようにする。

## Constraints

- Unity `6000.5.9f1`とURP `17.5.0`を使用する。
- 各表現モジュールの共通映像出力はRenderTextureとする。
- 映像の生成、加工、合成はノードグラフ方式とする。
- Unity Editor自体をビルドへ含めない。
- Standaloneアプリ内に独自のランタイムノードエディターを搭載する。
- ランタイム中にノードの追加、削除、接続変更ができるようにする。
- ライブ操作はノードパラメーターの連続操作と、グラフ状態／プリセットの切り替えを両方扱う。
- 物理入力と操作対象の間に名前付き論理コントロール層を設ける。
- 初期必須出力はProgram映像1系統と操作用Previewとする。
- Program出力の最低基準は1920×1080・60fpsとする。
- 保存単位はノードグラフ、パラメーター、操作マッピング、プリセット、素材を含むVJプロジェクト全体とする。
- 外部の動画や画像はVJプロジェクト内へコピーし、プロジェクト相対パスで参照する。
- 特定の演出機能へ過度に特化せず、新しいノード種別を追加できる構造を優先する。

## Non-Goals

- 初期完成版では複数の独立したProgram映像出力を必須としない。
- Unity Editorを実行環境として要求しない。
- 物理デバイス入力をノードパラメーターへ直接結合する方式を中心設計としない。
- 個別ノードグラフを独立した再利用アセットとして保存する機能は初期必須範囲に含めない。
- MIDI、OSC、DMX、NDI、Spoutなど、特定の外部プロトコルをこの仕様段階では必須としない。
- モバイル環境を初期の本番対象としない。

## Acceptance Criteria

- [ ] 3D描画ノードがRenderTextureを出力できる。
- [ ] 2D描画ノードがRenderTextureを出力できる。
- [ ] シェーダーノードがRenderTextureを出力できる。
- [ ] プリレンダ動画ノードがRenderTextureを出力できる。
- [ ] 上記4種類のノードを同一グラフ内で接続し、加工・合成できる。
- [ ] Standaloneアプリ内でノードを追加、削除、接続、切断できる。
- [ ] ノードパラメーターを実行中に連続操作できる。
- [ ] グラフ状態またはプリセットを実行中に切り替えられる。
- [ ] 物理入力を名前付き論理コントロールへ割り当て、論理コントロールからパラメーターまたはプリセットを操作できる。
- [ ] 物理入力デバイスの割り当てを変更しても、論理コントロールからグラフへの関係を維持できる。
- [ ] Program映像と操作用Previewを同時に表示できる。
- [ ] `PerformanceBaseline.md`で定義したWindows／macOS基準PC上で、Program映像を1920×1080・60fpsで維持できる。
- [ ] VJプロジェクトを保存し直しても、ノード、接続、パラメーター、論理コントロール、入力割り当て、プリセットが復元される。
- [ ] 外部素材がVJプロジェクト内へコピーされ、元ファイルを参照せず読み込める。
- [ ] VJプロジェクトフォルダーを別PCへコピーし、素材を含めて開ける。
- [ ] 新しいノード種別を既存ノードグラフや保存形式の全面変更なしに追加できる。

## Assumptions Exposed & Resolved

| Assumption | Challenge | Resolution |
|---|---|---|
| 異なる表現方式は別々の仕組みが必要 | 共通の操作・合成単位を確認 | すべてRenderTexture出力ノードとして共通化する |
| 汎用VJにはレイヤー方式が必要 | 合成の基本モデルを確認 | ノードグラフ方式を採用する |
| ライブ操作はパラメーター操作だけでよい | 状態切り替えの必要性を確認 | パラメーターとプリセットの両方を扱う |
| 汎用機なら複数Program出力が必要 | 初期必須範囲を反証 | Program 1系統と操作用Previewに限定する |
| 保存対象は個別アセットである | 保存の基本単位を確認 | VJプロジェクト全体を保存する |
| 一部の表現方式だけで共通設計を証明できる | 最小完成条件を確認 | 3D、2D、シェーダー、プリレンダ動画の全種類を要求する |
| ノード編集はUnity Editorだけでよい | 本番中の構成変更を確認 | Standalone内にランタイムノードエディターを搭載する |
| 物理デバイスを直接ノードへ結べばよい | デバイス交換時の再利用性を確認 | 論理コントロール層を設ける |
| 性能は実装後に判断すればよい | 出力成功条件を確認 | FHD・60fpsを最低基準とする |
| 外部素材は元パス参照でよい | プロジェクトの可搬性を確認 | 素材をVJプロジェクト内へコピーする |

## Resolved Decisions

| Topic | Resolution | Owning Specification |
|---|---|---|
| FHD・60fps基準PC | WindowsはRyzen 5 5600X／RTX 3060 12GB／32GB、macOSはMacBook Pro M4／16GBを基準とする | [PerformanceBaseline](NODES/PerformanceBaseline.md) |
| 物理入力 | Unity Input SystemのKeyboardとWindows WinMMのMIDI入力に対応し、Mouse、Gamepad、OSC、DMX、NDI、Spoutは初期対象外とする | [PhysicalInputScope](NODES/PhysicalInputScope.md) |
| プロジェクト形式と復旧 | UTF-8 `project.json`、ProjectFormatVersion、原子的置換、`.bak`、移行前バックアップおよびUnknownNode復旧を使用する | [VJProjectPersistence](NODES/VJProjectPersistence.md)、[NodeSchemaMigrationAndUnknown](NODES/NodeSchemaMigrationAndUnknown.md) |
| グラフ循環 | `Feedback` ノードを経由するフレーム間循環だけを許可し、同一フレームの評価グラフは非循環に保つ | [Feedback](NODES/Feedback.md)、[GraphEvaluation](NODES/GraphEvaluation.md) |
| 映像共通形式 | Linear、HDR RGBA16F、Premultiplied Alphaを内部形式とし、Programは固定1920×1080、表示時にACESでLDRへ変換する | [InternalColorSpace](NODES/InternalColorSpace.md)、[InternalDynamicRange](NODES/InternalDynamicRange.md)、[InternalAlpha](NODES/InternalAlpha.md)、[ResolutionAndOutputPolicy](NODES/ResolutionAndOutputPolicy.md) |
| 外部連携 | 音声解析、録画、配信、NDI、SpoutおよびHDRディスプレイ直接出力は初期対象外として確定し、導入時に別要求として定義する | [Nodes設計インデックス](NODES/README.md) |

## Technical Context

- `ProjectSettings/ProjectVersion.txt`: Unity `6000.5.9f1`。
- `Packages/manifest.json`: URP `17.5.0`、Input System `1.20.0`、Test Framework `1.7.0`。
- `Assets/Scenes/SampleScene.unity`: Main Camera、Directional Light、Global Volumeだけのテンプレートシーン。
- `Assets/Settings/PC_RPAsset.asset`: PC向けForward+、HDR、Depth/Opaque Texture有効。
- `Assets/Settings/Mobile_RPAsset.asset`: Mobile向けForward構成。モバイルは初期本番対象外。
- 独自C#、Shader、Prefab、Material、VFX Graph、RenderTexture、テストは現時点で存在しない。
- MIDI入力はWindows WinMMで導入済み。OSC、DMX、NDI、Spoutなどの外部連携は未導入。
- `ProjectSettings/ProjectSettings.asset`は現在1024×768、`runInBackground: 0`、`resizableWindow: 0`であり、本仕様へ合わせた変更が必要。

## Ontology (Key Entities)

| Entity | Type | Fields | Relationships |
|---|---|---|---|
| VJSystem | core domain | runtime topology | VJProjectを読み書きし、RuntimeNodeEditorを提供する |
| VJProject | core domain | graph, parameters, mappings, presets, assets | 構成と素材をまとめて保持する |
| MediaAsset | core domain | copied file, relative path | VJProjectに属し、VisualModuleから参照される |
| RuntimeNodeEditor | core domain | add, remove, connect, disconnect | 実行中のNodeGraphを編集する |
| VisualModule | core domain | type, parameters | RenderTextureを出力する |
| RenderTexture | shared contract | image output | Node間を流れ、ProgramOutputへ到達する |
| CompositionEngine | core domain | graph evaluator | NodeGraphを評価する |
| NodeGraph | core domain | nodes, connections, state | VJProjectに属する |
| Node | core domain | inputs, outputs, parameters | NodeGraphに属する |
| Connection | core domain | source port, destination port | Node同士を接続する |
| Parameter | core domain | value | Nodeに属し、LogicalControlから変更される |
| GraphState | core domain | parameter values, graph configuration | Presetから復元される |
| LogicalControl | core domain | name, value or trigger | 物理入力を抽象化し、ParameterやPresetを操作する |
| ControlMapping | supporting | physical input, logical control | 物理入力をLogicalControlへ割り当てる |
| OutputTarget | supporting | role | ProgramOutputまたはPreviewOutputとして機能する |
| ProgramOutput | core domain | 1920×1080, 60fps | 最終映像を出力する |
| PreviewOutput | supporting | operator view | 操作画面へPreviewを表示する |
| Preset | supporting | graph state | GraphStateを保存・復元する |

## Ontology Convergence

| Round | Entity Count | New | Changed | Stable | Stability Ratio |
|---:|---:|---:|---:|---:|---:|
| 1 | 7 | 7 | - | - | - |
| 2 | 10 | 3 | 0 | 7 | 70% |
| 3 | 12 | 2 | 0 | 10 | 83% |
| 4 | 14 | 2 | 0 | 12 | 86% |
| 5 | 15 | 1 | 0 | 14 | 93% |
| 6 | 15 | 0 | 0 | 15 | 100% |
| 7 | 16 | 1 | 0 | 15 | 94% |
| 8 | 17 | 1 | 0 | 16 | 94% |
| 9 | 17 | 0 | 0 | 17 | 100% |
| 10 | 18 | 1 | 0 | 17 | 94% |

## Interview Transcript

<details>
<summary>全10ラウンド</summary>

### Round 1

**Q:** 表現の基本単位は何にするか。
**A:** RenderTexture。
**Ambiguity:** 74.5%

### Round 2

**Q:** RenderTexture同士を合成する基本モデルは何にするか。
**A:** ノード。
**Ambiguity:** 66.8%

### Round 3

**Q:** ライブ中に直接操作する基本単位は何にするか。
**A:** パラメーター操作とプリセット切り替えの両方。
**Ambiguity:** 59.3%

### Round 4

**Q:** 最初から保証する出力構成はどこまでにするか。
**A:** Program映像＋操作用Preview。
**Ambiguity:** 49.7%

### Round 5

**Q:** 保存・再利用の基本単位は何にするか。
**A:** プロジェクト全体。
**Ambiguity:** 43.9%

### Round 6

**Q:** 共通RenderTextureノード設計を証明する最低限の表現種類は何か。
**A:** 3D、2D、シェーダー、プリレンダ動画の全種類。
**Ambiguity:** 38.9%

### Round 7

**Q:** Standaloneアプリ内に独自ノードエディターが必要か。
**A:** 必要。
**Ambiguity:** 32.9%

### Round 8

**Q:** 操作マッピングの中心を物理入力と論理コントロールのどちらにするか。
**A:** 論理コントロール。
**Ambiguity:** 27.8%

### Round 9

**Q:** Program出力の最低性能基準は何か。
**A:** FHD・60fps。
**Ambiguity:** 23.8%

### Round 10

**Q:** 外部素材をVJプロジェクト保存時にどう扱うか。
**A:** プロジェクト内へコピー。
**Ambiguity:** 16.9%

</details>
