# 実行性能と並列化

## 状態

確定。Burst、C# Job System、GPU処理、Taskおよびメインスレッド処理の使い分けを定める。

## 結論

BurstとC# Job Systemをグラフランタイム全体の実行モデルにはしない。初期実装は `FrameCoordinator` がメインスレッドで決定的に進行し、Profilerで確認したCPUホットスポットだけを、所有モジュール内のバッチ処理としてBurst／Job化できる構成にする。

```text
グラフの調停・Unity API        -> メインスレッド
映像生成・加工・合成          -> Shader / Compute Shader / GPU
ファイルI/O・ハッシュ・Probe  -> Task / 専用Backend
純粋で大量のCPU同型計算       -> 計測後にBurst / Job System
```

Burst／Job Systemは許可された最適化手段であり、ノード実装の共通必須条件にはしない。

## 現在のPackage状態

`Packages/packages-lock.json` にはURPなどの推移的依存としてBurstとCollectionsが存在するが、`Packages/manifest.json` の直接依存には含まれていない。

- 初期実装はこの推移的依存をアプリケーションAPIとして使用しない。
- BurstまたはCollectionsを本番コードで初めて使用する変更と同時に、必要Packageを `manifest.json` の直接依存へ追加する。
- 推移的依存の存在だけを理由としてasmdef参照やNative Containerを導入しない。

## 初期実装の実行モデル

### メインスレッド

次の処理はメインスレッドで行う。

- FrameCoordinatorのPhase 0～9
- GraphEditCommandの確定とProjectDocumentの変更
- EvaluationPlanに従うノード評価順の調停
- GameObject、Scene、Physics、Camera、Material、RenderTexture、VideoPlayerおよびInput System Objectの操作
- RenderTexture Leaseの取得、差し替えおよび返却
- NodeOutputResultの当該フレームへの確定
- Program／Preview提示とApplication Read Modelの公開

Runtime Nodeの `Evaluate` は同期的なメインスレッド契約を維持する。全ノードを1ノード1Jobとして自動Schedulingする仕組みは初期版へ入れない。

### GPU

映像の画素単位処理はCPUへ読戻さず、可能な限りGPU上で完結させる。

- Shader Generator／Effect
- 合成と色変換
- Preview終端の拡縮
- Feedback履歴コピー
- Hap Backendで仕様化された直接圧縮TextureまたはCompute Shader経路

GPU処理はRenderingまたはMediaモジュールが所有し、Burstへ置き換える対象にしない。GPU readbackが仕様上必要になった場合は、同期Readbackを通常評価経路へ入れず、個別に性能と遅延を設計する。

### Taskまたは専用Backend

フレームをまたいでよく、Unity Objectを操作しない処理はTaskまたは専用Backendを使用する。

- project.jsonのファイル読書き
- 素材コピー
- ファイルサイズとXXH3-128計算
- Unity APIを使わない素材Probe
- Native動画Backendのデコード準備

入力は不変Snapshotとし、結果はCompletion Queueへ返す。TaskからProjectDocumentまたはRuntimeSessionを直接変更しない。

## Burst／Job Systemを初期適用しない領域

| 領域 | 理由 |
|---|---|
| GraphEditとEvaluationPlan構築 | グラフ変更時だけ実行し、初期上限4096接続では常時処理ではない |
| ノード単位Scheduling | 小さいJobのSchedule／同期点が増え、依存グラフとUnity API境界が複雑になる |
| Scene／Physics／Camera調停 | Unity Objectとフレーム順序をメインスレッドで所有する必要がある |
| Shader／Texture処理 | 画素並列処理はGPUが本来の実行先である |
| UI、診断、Undo／Redo | データ量が小さく、保守性と正しさが優先される |
| ファイルI/O | CPU演算の並列化ではなく非同期I/Oまたは専用スレッドが適する |
| 1件ごとのPort変換 | 初期暗黙変換が小さく、Job化の固定費に見合わない |

## 将来の適用候補

ProfilerでCPU負荷が確認された場合、次の順で候補を調べる。

1. 大量ParameterのValue Mapping、Min／Max式およびEffectiveValue計算
2. 多数ノードに共通する純粋な数値Kernel
3. 大量の診断・Demand用データに対する純粋な集計
4. Native Backend内でCPU処理が必要な動画Block変換

Graph全体をJob化する前に、同じ型の入力をまとめて処理できるバッチ境界を探す。単発ノードや数十件程度の小さな処理へJobを作らない。

## 導入条件

BurstまたはJob Systemを追加する変更は、次をすべて満たす場合だけ採用する。

1. 基準グラフと基準PCで再現できるProfiler計測がある。
2. 対象処理がp95で0.5ms以上を消費するか、Programの16.67ms超過へ直接寄与している。
3. 対象データをUnity Object参照なしの固定Layoutへ変換できる。
4. 複数要素をまとめて処理でき、Job ScheduleとCompleteの固定費を上回る。
5. 変更前後Benchmarkで対象処理が20%以上改善し、Programのp99 Frame Timeを悪化させない。
6. Managed実装との結果一致、破棄、キャンセルおよびプロジェクト切り替えをテストできる。

閾値を満たさない最適化は導入せず、Profiler Captureと判断理由を設計またはテスト記録へ残す。

## Jobを導入する場合の境界

### Scheduling単位

- Runtime Nodeそのものではなく、所有モジュール内の同型データをまとめたKernelをJob単位とする。
- JobはFrameCoordinatorの明示されたPhaseからScheduleする。
- その結果を必要とする最初のPhaseより前に依存JobHandleをCompleteする。
- 初期版ではJobを次の評価フレームへ持ち越さない。
- Main ThreadがJob待ちだけで停止する時間をProfiler Markerで計測する。

### データ所有

- Job入力は当該フレーム中に不変とする。
- Job出力用Native Containerは所有モジュールが一元管理する。
- Runtime Node、UIおよびProjectDocumentへNative Containerの可変参照を渡さない。
- Native Containerをノードごと・フレームごとに生成しない。
- Project終了、Node削除または容量変更時は、依存Job完了後にだけDisposeする。
- Dispose漏れとJob実行中Disposeをテストで検出する。

### 結果の適用

- Job結果はメインスレッドで型、Generation ID、Document Revisionおよび有限値を検証してから適用する。
- 古いGenerationまたはRevisionの結果は現在状態へ適用しない。
- Job例外または不正結果は対象処理のFaulted診断へ変換する。
- Burst無効時も同じ契約を満たすManaged経路を維持する。

## Runtime Nodeとの関係

`IRuntimeNode` に `ScheduleJob`、`JobHandle` またはNative Containerを最初から公開しない。

- Jobを使うNodeが1種類だけ現れた場合は、そのNodeまたは注入Serviceの内部実装へ閉じ込める。
- 3種類以上のNodeで同じScheduling契約が必要になり、計測上の効果も確認された時点で共通契約を検討する。
- 共通契約追加時も、Job非対応Nodeを同じグラフで実行可能にする。
- 非同期準備をJob Systemへ置き換えても、外側からはPreparingとCompletion Queueの既存契約を維持する。

## asmdefと依存

- 現時点ではBurst専用asmdefまたは共通Kernel Assemblyを追加しない。
- 初回導入時は、最適化対象を所有する既存asmdefだけへBurst／Collections参照を追加する。
- 3つ以上のモジュールから共有される実証済みKernelが生まれた場合だけ、`ShitDesigner.Kernels` の分離を検討する。
- Presentation、ProjectおよびPersistenceの公開モデルへNative型を漏らさない。

## 検証

Burst／Jobを導入した処理には次を要求する。

- Managed経路とBurst経路の結果比較
- 必要な数値許容差を明示した境界値テスト
- NaN、Infinity、空入力、最大要素数のテスト
- プロジェクト切り替え、Undo復元およびNode削除中の寿命テスト
- Burst有効Player Buildでの性能試験
- Windows D3D12／VulkanとmacOS Metalの基準試験でProgram結果が変わらないこと
- Profiler MarkerによるSchedule、Worker実行、Complete待機時間の分離
