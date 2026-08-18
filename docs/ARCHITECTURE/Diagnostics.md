# 診断アーキテクチャ

## 状態

確定。現在状態、履歴、抑制、回復、例外境界、スレッド境界および診断書き出しを定める。

## 目的

- BlockedやPreparingのような正常状態と、Faultedを混同しない。
- 同じ障害が毎フレーム発生しても履歴とUIを埋めない。
- Program映像へ診断を混入させず、操作画面では原因を追跡できるようにする。
- ModuleがUnity Consoleへばらばらに書き込まず、同じ構造と抑制規則を通す。
- 例外を握りつぶさず、独立した枝または次フレームの再試行を継続する。

## Diagnostic Hub

Application Lifetimeで1つの `DiagnosticHub` を持つ。RuntimeSessionはHub内にSession Scopeを作り、そのScope Handleを所有する。

- Application起動、Catalog、Project読込・保存などはApplication Scopeへ記録する。
- Node、Graph、Rendering、Scene、MediaおよびInputはCurrent Runtime Session Scopeへ記録する。
- Project終了時はSession Scopeの現在状態を閉じる。
- 履歴はアプリケーション実行中の最新1000件を保持し、Projectへ保存しない。
- PresentationはDiagnostic Read Modelだけを参照し、Hubの可変状態へ触れない。

## 2種類の診断状態

### Current Condition

現在のNode、Port、Parameter、ProgramまたはSubsystemの状態を表す。

- Blocked
- Preparing
- UsingFallback
- HoldingLastFrame
- 現在継続中のFaulted
- VRAM使用率、Preview抑制、性能警告などの現在状態

Current ConditionはKey単位で置換・解除する。UIはここから現在のBadge、色、文字および原因リンクを表示する。

### Diagnostic History

ユーザーが後から確認または書き出すべき出来事を表す。

- Faulted開始
- Faulted継続集約
- Faulted回復
- Command拒否
- Preset発火成功または失敗
- 保存、読込、復旧および移行
- 起動Capability Probeと非対応
- VRAM警告、性能警告および回復
- Frame全体の予期しない例外

Blocked、Preparing、UsingFallbackおよびHoldingLastFrameの通常遷移は、Current Conditionだけを更新して履歴へ追加しない。ただし、それらの原因が履歴対象Faultを発生させた場合は原因Faultだけを記録する。

## Diagnosticデータ

Diagnosticは作成後に変更しない値として次を持つ。

- DiagnosticCode
- Severity
- Scope ID
- NodeInstanceId、NodeTypeIdおよび実行時Generation ID
- PortIdまたはParameterId
- 短いMessage
- 構造化Detail
- FrameNumber
- GraphClock時刻
- 発生元Module
- Exception情報
- 関連Diagnosticへの参照

Generation IDはRuntime Nodeに属する診断だけが持ち、Project保存へ含めない。対象が存在しない項目は明示的なOptionalとして扱い、空UUIDまたは空文字を有効値の代わりに使わない。

### Severity

| 値 | 意味 |
|---|---|
| Info | 成功、復旧、選択Backendなど運用情報 |
| Warning | 動作は継続するが、性能低下、補正またはユーザー確認が必要 |
| Error | Node、操作または部分機能が失敗したがアプリケーションは継続可能 |
| Fatal | Catalog不正、内部Format非対応など、Projectを安全に実行できない |

Fatalはアプリケーション終了を必ず意味せず、Projectを開かない、またはCurrent Sessionを停止する判断をApplicationが行う。Diagnostic Hub自身は制御フローを決めない。

### DiagnosticCode

- 小文字ASCIIの `module.category.reason` 形式とする。
- 公開後の意味を変更しない。
- 表示Messageや翻訳文を識別子に使わない。
- 同じCodeでDetail構造の意味を変える場合はCodeを追加する。
- 例: `rendering.texture_pool.budget_exceeded`、`media.video.seek_failed`、`graph.connection.cycle_rejected`。

### 構造化Detail

- 文字列1本ではなく、安定したField名と値の集合を基本とする。
- MediaAssetId、相対パス、Codec、要求時刻、Texture Descriptorなど原因追跡に必要な値を持てる。
- 保存Project外の絶対パスは通常Detailと書き出しへ含めず、Project相対パスへ正規化する。
- UI向けMessageと抑制判定用Detailを分離する。
- 無制限な巨大文字列またはバイナリを保持しない。

### Exception情報

- Exception型の完全名
- Message
- Stack Trace
- Inner Exception Chainの型とMessage

Exception Object自体を履歴へ保持せず、境界で不変データへ変換する。これにより、例外が参照するRuntime NodeまたはUnity Objectの寿命を延長しない。

## Current Condition Key

現在状態を次の組み合わせで識別する。

```text
Scope ID + Subject Kind + Subject ID + Generation ID + DiagnosticCode + Port/Parameter ID
```

- 同じKeyの更新は既存Conditionを置き換える。
- Node削除時は同じNode GenerationのConditionを閉じる。
- Undoで同じNodeInstanceIdが復元されてもGeneration IDが異なるConditionを引き継がない。
- Condition解除は原因が回復した評価フレーム境界で行う。

## Fault集約

継続障害の識別Keyは次とする。

```text
Scope ID + Subject + DiagnosticCode + Canonical Detail Hash
```

Canonical DetailはField名順に正規化し、FrameNumber、時刻、集約件数など毎回変化する値を除外する。

### 開始

- 最初のFaultを履歴へ1件追加する。
- First Frame、Last Frame、Countを持つActive Fault Trackerを作る。
- Current ConditionをFaultedへ設定する。

### 継続

- 同じKeyが次フレーム以降も発生した場合はCountとLast Frameを更新する。
- 新しい履歴行を追加しない。
- 300フレームごとに最初の履歴Entryの集約CountとLast Frameを更新する。
- 対象Entryがリングバッファから既に失われていても、新しい継続行を作らない。

### 回復

- 以前のActive FaultがそのNodeの要求フレームで発生しなくなった場合、回復Entryを1件追加する。
- 回復Entryは元DiagnosticCode、継続Frame数、Countおよび回復Frameを持つ。
- Current Fault Conditionを解除する。
- Nodeが評価対象外になっただけでは回復とみなさず、Active Faultを休止状態にする。
- Node削除またはSession終了では回復Entryを作らず、終了理由をTrackerへ設定して閉じる。

## Node状態との対応

| Node状態 | Current Condition | History |
|---|---|---|
| Available | 通常状態、必要なら以前のFaultを回復 | 回復時だけ1件 |
| Blocked | 不足入力と根本原因への参照 | 追加しない |
| Preparing | 処理名、開始Frame、進捗 | 追加しない |
| Faulted | Active Fault | 開始、300Frame集約、回復 |
| UsingFallback | Input Portごとの既定値と原因 | 追加しない |

下流Blockedは上流Faultの履歴を複製しない。Blocked Conditionから根本Fault Diagnosticへたどれる参照を持つ。

## ProgramとPreview

- ProgramがAvailableでない場合、Program Presenterは `HoldingLastFrame` Current Conditionを設定する。
- Conditionは原因Node、根本Diagnostic、開始Frameおよび継続時間を持つ。
- Program映像TextureへMessage、Iconまたは色を合成しない。
- Program Monitorは映像領域外へHoldingLastFrameを表示する。
- Previewは操作用SurfaceにBlocked、Faulted、Preparing、UsingFallbackおよび品質段階をOverlayできる。
- Program復旧時はHoldingLastFrame Conditionを解除し、通常の履歴行は追加しない。

## 例外境界

### Resultを使う失敗

入力検証、接続拒否、Lease確保失敗、ファイル欠落など予測可能な失敗はResultとして返し、呼出側がDiagnosticへ変換する。通常制御に例外を使用しない。

### 捕捉する境界

- Runtime NodeのEvaluate
- Port Converter
- Node FactoryとCleanup
- Scene生成、Render RequestおよびPhysics
- Video Backend CallbackとNative境界
- PersistenceのSerialize、File I/OおよびMigration
- FrameCoordinator Tick全体
- PresentationのCustom UI Factory

各境界は `System.Exception` を捕捉し、Contextを追加したDiagnosticへ変換する。捕捉後の継続可否は所有モジュールがResultまたはFaulted状態で返す。

### 継続しない状態

次のように内部不変条件を保証できない場合はFatalとし、新しい評価を停止する。

- NodeTypeRegistryまたはConversion Registryが不正
- 内部GraphicsFormatを安全に作成・Sampleできない
- Current EvaluationPlanとGraph Revisionが一致せず再構築もできない
- Ownership Registryに同一Textureの複数Ownerが検出された

Fatalでも現在のProgram Hold Textureを破棄せず、操作画面でProjectを閉じるまたは再読込するための経路を維持する。

## Unity Console

- Moduleから `Debug.LogError` を直接呼ぶことを標準経路にしない。
- Diagnostic HubのUnity Console Adapterが、新しいWarning、Error、Fatal履歴EntryだけをConsoleへMirrorする。
- 継続集約Updateを毎回Consoleへ出さない。
- Development BuildではContextとStack Traceを含め、Release BuildでもDiagnosticCodeと短いMessageを残す。
- Unity自身またはNative Pluginが直接出すLogは完全には抑止せず、可能な範囲で関連Diagnosticへ紐付ける。

## スレッド境界

- Diagnostic HubのCurrent ConditionとHistoryはメインスレッドだけで変更する。
- Background Task、Native CallbackまたはJobはDiagnosticを直接追加しない。
- Background側は構造化されたCompletion ResultをCompletion Queueへ入れる。
- FrameCoordinatorのPhase 0でGeneration、Revisionおよび対象の有効性を確認してからDiagnosticへ変換する。
- Queue投入自体の失敗はThread SafeなEmergency Counterへ記録し、次のPhase 0で1件の過負荷Diagnosticにする。

## History Ring Buffer

- 容量は1000 Entry固定とする。
- 新規Entry追加時に最古Entryを上書きする。
- Entry IDはアプリケーション実行中に再利用しない。
- Diagnostic Occurrenceは不変とする。集約CountとLast Frameを更新するときは、同じEntry IDを持つ新しいHistory Entry Snapshotへ置き換える。
- Active Fault TrackerとCurrent ConditionはRing Buffer外に持ち、履歴Entry上書きで現在状態を失わない。
- UIはEntry IDをKeyに差分更新し、上書きで消えたEntryを選択中なら選択解除と理由を表示する。
- FilterはSeverity、NodeInstanceIdおよびDiagnosticCodeで行う。

## 書き出し

ユーザー操作で現在のDiagnostic SnapshotをTextまたはJSONへ書き出す。

- SnapshotにはHistory、Current Condition、Active Fault集約、性能概要、Graphics APIおよびアプリVersionを含める。
- Project保存データそのもの、素材内容および物理入力の現在値は含めない。
- 絶対素材パスを含めず、MediaAssetIdとProject相対パスを使う。
- JSONは安定Field名を持つ診断専用DTOとし、project.jsonのVersionとは分ける。
- File I/OはBackground Taskで行い、成功または失敗をCompletion Queueへ返す。
- 書き出し失敗は既存Historyを変更せず、新しいPersistence系Diagnosticを追加する。

## Read Model更新

- Phase 9でCurrent Conditionと新規／更新HistoryのChange Setを1回公開する。
- 同一フレーム中の一時状態をPresentationへ逐次通知しない。
- Read ModelはNode Badge用現在状態、Diagnostics一覧、Program状態および件数Summaryを分ける。
- UI Filter変更は診断Storeを変更せず、Presentation側の投影だけを更新する。
- 履歴が更新されてもProgram／Graph評価を再実行しない。
