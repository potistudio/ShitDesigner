# Live Host

## 状態

確定。Mainシーン専用のライブ実行起動構成を定義する。

この文書はMainシーンにだけ適用する。既存の `ApplicationHost` とその起動仕様は変更しない。

## 決定ログ

### 2026-08-26: Mainシーンは `ApplicationLiveHost` を使用する

- `ApplicationHost` は残すが、Mainシーンの起動には使用しない。
- Mainシーンはライブ実行専用の `ApplicationLiveHost` を使用する。

### 2026-08-26: `ApplicationLiveHost` はライブ実行のライフサイクルだけを管理する

- `ApplicationLiveHost` は `ApplicationHost` からエディターに関する依存を取り除く。
- `ApplicationLiveHost` は `Boot`、`Tick`、`Shutdown` を管理する。
- ノードグラフの生成、3Dシーンの生成および操作UIの描画は `ApplicationLiveHost` の責務に含めない。

### 2026-08-26: ライブ用ノードグラフは専用スクリプトで生成する

- ノードグラフは専用スクリプトが生成する。
- ライブ用ノードグラフの構成はそのスクリプト内部にハードコードする。
- 3Dシーンの構成はライブ用ノードグラフに含める。

### 2026-08-26: 操作UIは専用オブジェクトが描画する

- 操作UIは専用GameObjectに配置する。
- 操作UIは専用のUXMLで描画する。

### 2026-08-26: `ApplicationLiveHost` のCompositionはライブ実行に限定する

- `ApplicationLiveHost` のCompositionは、ライブ用ノードグラフ実行、Program出力、MIDIおよび外部Displayを含める。
- 既存のグラフエディターUIとプロジェクト編集機能は含めない。
- 専用操作UIはCompositionの所有物ではなく、専用GameObjectが描画する。

### 2026-08-26: `ApplicationLiveHost` は独立したHandshakeフェーズを持たない

- `ApplicationLiveHost` は `Boot`、`Tick`、`Shutdown` を管理する。
- `ApplicationLiveHost` は `Handshake()` APIを持たない。
- Bootが成功した時点でHostはTick可能になる。MIDIと外部Displayの利用可否は、Hostの状態遷移ではなく個別のCapability状態として公開する。
- MIDIと外部Displayの初回確認および再確認は、同じCapability更新処理へ統合する。
- Capability更新は最初のTickで実行し、以後は一定間隔で実行する。
- 入力取得、ライブ用グラフと3Dシーンの更新・描画、Program出力への提示は毎Tick実行する。

### 2026-08-26: ライブ実行とUIは別ループにする

- MIDIと外部DisplayのCapability確認は、一定間隔で実行する状態確認ループが担当する。
- ライブ実行Tickは毎フレーム、入力更新、パラメーター反映、ノード評価、シーン更新、描画およびProgram出力への提示をこの順序で実行する。
- UIは同じUnity PlayerLoop上の別コンポーネントで更新する。別スレッドは使用しない。
- UIはライブ実行Tickの後に、直近に完了したライブフレームの読み取り状態を専用UXMLへ反映する。
- UI操作は状態を直接変更せず、次のライブ実行Tickで適用する要求としてキューへ投入する。

### 2026-08-26: 外部Displayは専用スクリプトが所有する

- 外部Displayの検出、選択、活性化およびProgramフレームの提示は、外部Display専用スクリプトが所有する。
- `ApplicationLiveHost` は外部Displayの実リソースを所有せず、専用スクリプトの開始と終了をライフサイクルに組み込む。
- 外部Display専用スクリプトはノードグラフ、3Dシーンおよび操作UIを参照しない。
- 外部Display専用スクリプトは、接続済み外部Displayすべてへの出力の開始または停止およびDisplay識別を提供する制御ポートを公開する。

### 2026-08-26: 専用UIはライブ用ノードグラフを編集しない

- 専用UIはシーン選択と公開パラメーターの変更だけを要求できる。
- 専用UIからライブ用ノードグラフのノード構成、接続および有効状態は変更できない。
- ライブ用ノードグラフは専用スクリプト内のハードコードした構成を実行中に維持する。

### 2026-08-26: 固定ライブグラフの `ProgramOutput` をProgram映像の正本にする

- 固定ライブグラフの `ProgramOutput` がProgram映像の唯一の正本となるTextureを生成する。
- `LiveGraphBootstrap` はProgramOutputごとに独立したRenderTextureを構成する。外部Display専用スクリプトはDisplay 2から順に対応するProgram Textureだけを受け取り提示する。
- `MainLiveOutput` が `OutputSurfaceBridge` へsource overrideを渡す方式は、新構成へ持ち込まない。

### 2026-08-26: Mainのライブ実行Tickは `ApplicationLiveHost` に一本化する

- `ApplicationLiveHost` は、Mainのライブ実行Tickを呼ぶ唯一のMonoBehaviourとする。
- `ApplicationLoopDriver` と `MainLiveSceneBootstrap` はMainシーンから外す。
- 専用グラフ、外部DisplayおよびUIの各スクリプトは、`ApplicationLiveHost` が開始と終了を管理する。

### 2026-08-26: `ApplicationLiveHost.Boot` はライブ実行を開始可能な状態へ組み立てる

- Bootはシリアライズ済み参照と必須Assetを検証する。
- Bootは専用グラフスクリプトを開始し、固定ライブグラフと3Dシーンを生成する。
- BootはMIDI、外部DisplayおよびUIの専用スクリプトを初期化してから、ライブ実行Tickを有効にする。
- Capability確認はBootに含めず、最初の状態確認ループで実行する。
- Bootに失敗した場合は、その時点までに開始した専用スクリプトを逆順で停止する。

### 2026-08-26: `MidiInputManager` をMIDIデバイスの所有者にする

- MIDIデバイスの接続、再接続の実行、停止およびイベント取得は既存の `MidiInputManager` が所有する。
- ライブ用MIDIマッピングは `MidiInputManager.InputReceived` を、シーン選択または公開パラメーター変更要求へ変換して `LiveParameterQueue` へ投入するだけとする。
- ライブ用MIDIマッピングはMIDIデバイスのライフサイクルを所有しない。
- `ApplicationLiveHost` のライブ実行Tickが `MidiInputManager.Poll()` を一度だけ呼ぶ。`MidiInputManager` 自身の `Update()` によるPollは削除する。

### 2026-08-26: キーボード入力はMIDI未接続時のライブ操作手段として残す

- `LiveKeyboardInput` はMonoBehaviourや独自ループを持たない軽量なC#クラスとする。
- `ApplicationLiveHost` のライブ実行Tickが `LiveKeyboardInput.Poll()` を呼び、パッチ切替または公開パラメーター変更要求を `LiveParameterQueue` へ投入する。左右矢印は事前ロード対象を移動する。
- Keyboard、MIDIおよびUIの操作要求は同じ `LiveParameterQueue` に入る。
- グラフ編集、ファイル操作およびウィンドウ操作など、エディターに関するショートカットは提供しない。

### 2026-08-26: Program Textureは固定ライブグラフRuntimeが所有する

- 固定ライブグラフの `ProgramOutput` は、1920×1080、HDR RGBA16FのProgram Textureを正本として保持する。
- Program Textureはライブ用グラフRuntimeが所有する。外部DisplayおよびUIはTextureを破棄しない。
- Program Textureは次の正常なProgramフレームへ置換されるまで、またはShutdownまで有効とする。
- 初回に正常なProgramフレームがない場合は黒を提示し、一度でも正常なフレームを提示した後は、以後の描画失敗時に最後の正常フレームを維持する。
- Program TextureにはFrameNumberを付与し、外部Displayは未提示フレームだけを提示する。
- HDRからDisplay向けLDRへの変換は外部Display専用スクリプトが担当する。

### 2026-08-26: Program Frameは `ApplicationLiveHost` を経由して提示先へ渡す

- `LiveGraphRuntime.Render()` はTexture参照、FrameNumber、解像度および形式だけを持つ読み取り専用の `LiveProgramFrame` を返す。
- `ApplicationLiveHost` は各ライブ実行TickでProgramOutput群のFrameを外部Display専用スクリプトの `Present` へ渡し、先頭ProgramOutputの参照を `LiveUiReadModel` へ公開する。
- 外部Display専用スクリプトは同じFrameNumberを再提示しない。
- 外部DisplayとUIは `LiveGraphRuntime` の内部状態を取得または監視せず、受け取ったFrameを表示するだけとする。

### 2026-08-26: `LiveParameterQueue` は受付順に要求を適用する

- `LiveParameterQueue` の容量は4,096件とする。満杯時は新規要求を拒否し、UIには `Rejected` を返す。
- Sequence番号は `LiveParameterQueue` が発行する。UI、MIDIおよびKeyboardの要求に優先順位は設けない。
- ライブ実行Tickの開始時にキューを取り出し、すべての要求を受付順に適用する。初期実装では連続値を合流しない。
- `SelectScene` は受付順を維持する。
- `SetParameter(SceneId, ParameterId, Value)` は、選択中かどうかにかかわらず指定したSceneIdの `LiveSceneRoot` APIへ適用する。
- 存在しないSceneIdまたはParameterId、あるいは `LiveSceneRoot` が受け付けない要求は `Rejected` とし、理由をUIへ返す。

### 2026-08-26: ライブ用PlayerLoopの実行順を固定する

- `LiveCapabilityMonitor.Update()` は初回および1秒間隔の状態確認と再接続依頼を行う。
- `ApplicationLiveHost.LateUpdate()` は、Keyboard入力、MIDI入力、要求キューの適用、グラフ評価、シーン更新、描画、外部DisplayへのProgram Frame提示、`LiveUiReadModel` の公開をこの順序で実行する。
- `LiveUiController.LateUpdate()` は `DefaultExecutionOrder` により `ApplicationLiveHost` の後で実行し、公開済みRead ModelをUXMLへ反映する。
- Unity UIの描画は `LiveUiController.LateUpdate()` の後に行う。

### 2026-08-26: Tick中の失敗では最後の正常なProgram映像を維持する

- パラメーター適用失敗は対象要求だけを `Rejected` とし、同じTickの後続処理を継続する。
- `Evaluate`、`SceneUpdate` または `Render` が失敗した場合は、そのTickの後続処理を中止して最後の正常なProgram Textureを維持する。
- Tick中の失敗は `LiveUiReadModel` の診断として公開し、次のTickで通常どおり再試行する。
- Tick中の失敗を理由に `Shutdown` は実行しない。Boot失敗だけは起動を中止して開始済みの専用スクリプトを逆順で停止する。
- `Shutdown` は明示停止、シーン破棄またはアプリケーション終了時だけに実行する。

### 2026-08-26: 専用グラフは `LiveGraphBootstrap` と `LiveGraphRuntime` に分ける

- `LiveGraphBootstrap` はTickを持たないMonoBehaviourとし、`Scene3DDefinition` などのAsset参照をシリアライズして保持する。
- `LiveGraphBootstrap.CreateRuntime()` は通常のC#クラスである `LiveGraphRuntime` を作成する。
- `LiveGraphRuntime` は固定ノードグラフ、3DシーンRuntimeおよびProgram Textureを所有し、`Evaluate`、`SceneUpdate`、`Render` および `Dispose` を提供する。
- `ApplicationLiveHost` はBootでRuntimeを作成し、ライブ実行Tickで各処理を定義した順序で呼び、ShutdownでRuntimeを破棄する。
- コードへハードコードするのはノード構成、接続および公開パラメーター対応だけとする。Prefab、`Scene3DDefinition`、ShaderなどのAsset参照をGUIDまたはパスとしてコードへ埋め込まない。

### 2026-08-26: パッチ数はInspectorで設定し、1件だけ事前ロードする

- `LiveGraphBootstrap` は任意数の `PatchDefinition` をシリアライズして保持する。
- `PatchDefinition` はUnityの `Scene3DDefinition` をノードグループとして参照し、ライブUIへ公開するパラメーターを明示的に束ねる。これはUnityのSceneとは別の論理的なパッチである。
- `PatchDefinition` は任意の `PatchFlashDefinition` を持ち、発火時に表示する画像と表示時間をパッチ単位で定義する。未設定のパッチは画像フラッシュを行わず、共通の白フラッシュだけを行う。
- Bootは最初のパッチだけを事前ロードする。
- パッチの事前ロード時に対象のUnityシーンノードRuntimeと描画リソースを生成する。
- 事前ロード対象は常に1件だけ保持する。別のパッチを事前ロードすると、表示中ではない前の事前ロード対象を破棄する。
- ロード要求は事前ロード済みのパッチIDと一致する場合だけ受理する。表示中のパッチはロード要求まで切り替わらない。
- 表示中のパッチに含まれるノードだけを更新および描画する。表示中および事前ロード対象のRuntimeはShutdownで破棄する。

### 2026-08-27: ライブUIは4つの固定パッチスロットを持つ

- パッチブラウザで選んだパッチは、先頭の空きスロットへ追加する。スロットは4件で固定し、満杯時は追加を拒否する。
- スロットの選択、Cue、PlayおよびClearはUIの読み取り状態と限定したHost APIを通じて操作する。スロット内容はPatchDefinitionを変更しない。
- Cueは選択スロットのパッチを事前ロードする。PlayはCue済みならロード要求を、未Cueなら単一のLaunch要求を投入し、同一ライブTickで作成と切替を完結させる。
- 物理的に事前ロードするPatch Runtimeは従来どおり1件だけとする。4スロットは出演順を保持する操作状態であり、4つのRuntimeを保持しない。

### 2026-08-27: BPMクロックはライブグラフ全体で共有する

- BPM値と累積ビートは `LiveGraphRuntime` が保持し、パッチ固有の公開パラメーターにはしない。
- BPM変更とTap入力はグローバル要求として処理し、パッチを切り替えてもテンポと拍位置を維持する。
- BPMに追従するSceneは、更新ごとに共有クロック状態を受け取る。SceneごとのMotion倍率はBPMクロックに影響しない。

### 2026-08-27: ライブ用キーボードは離散操作だけを扱う

- 左右矢印はパッチカタログの選択を移動し、Enterは選択パッチを先頭の空きスロットへQueueする。`1`から`4`は対応するパッチスロットをPlayし、`Shift + 1`から`4`は対応スロットをClearする。
- SpaceはUIのTAPボタンと同じBPM Tap入力とする。Fは現在のパッチにFlashを発火する。
- Motion、Scaleなどの連続値はKeyboardへ割り当てず、MIDI入力またはUIの操作面から要求する。

### 2026-08-26: 各3Dシーンのルートが公開パラメーターAPIを提供する

- すべての対象3Dシーンのルートには、公開パラメーター一覧の取得と変更適用を提供する `LiveSceneRoot` を配置する。
- `LiveSceneRoot` は同じGameObject上の `ILiveSceneParameter` を集約して公開し、各パラメーターコンポーネントがシーン固有の反映方法と対象参照を所有する。
- UIは選択中シーンの公開パラメーター一覧から操作部品を描画する。
- MIDI、KeyboardおよびUIの変更要求はSceneIdとParameterIdを指定して `LiveParameterQueue` へ投入する。
- `LiveGraphRuntime` はTransformやCameraを直接変更せず、選択中シーンの `LiveSceneRoot` APIを通じて変更を適用する。
- Bootはすべての対象シーンに `LiveSceneRoot` が配置されていることを検証する。

### 2026-08-26: Capability確認は専用ループで実行する

- `LiveCapabilityMonitor` は専用MonoBehaviourとし、MIDIと外部Displayの状態確認を担当する。
- `LiveCapabilityMonitor` はBoot後の最初のPlayer Frameで、`ApplicationLiveHost` の最初のライブ実行Tickより前に状態確認を実行する。
- 以後の状態確認は1秒間隔で実行する。`LiveCapabilityMonitor` はMIDI未接続時に `MidiInputManager.TryReconnect()` を呼び、外部Displayは利用可能状態への復帰を検出する。
- MIDIまたは外部Displayが利用できなくても、ライブ実行およびProgram Textureの生成は継続する。
- `ApplicationLiveHost` は `Degraded` 状態を持たない。`LiveCapabilityMonitor` は個別Capabilityの状態を `LiveCapabilitySnapshot` として公開する。

### 2026-08-26: 専用UIは `LiveUiReadModel` と限定した操作契約を使用する

- 専用GameObject上の `LiveUiController` が専用UXMLへ `LiveUiReadModel` を反映する。
- `LiveUiReadModel` はFrameNumber、シーン一覧、選択中SceneId、選択中シーンの公開パラメーター定義と確定値、Program TextureとProgram FrameNumber、外部Displayの接続数・選択・有効状態・エラー、`LiveCapabilitySnapshot`、直近の診断および要求ごとの適用結果を含む。
- `LiveParameterQueue` へUIが投入できる要求は `SelectScene(SceneId)` と `SetParameter(SceneId, ParameterId, Value)` だけとする。
- UIは要求投入時にSequence番号を受け取り、次のライブ実行Tickが公開する `Applied` または `Rejected` の結果を読む。
- `LiveUiController` はライブ実行Tickの後にRead Modelを更新し、RuntimeまたはTextureの所有権を持たない。
- UIはProgram Monitorを表示する。外部Display出力の開始または停止およびDisplay識別は `LiveParameterQueue` を経由せず、外部Display専用スクリプトの制御ポートへ直接要求する。

### 2026-08-26: 公開パラメーターはパッチ境界で解決する

- `PatchDefinition` の公開パラメーターは、Scene3DDefinitionのIDとその `LiveSceneRoot` が提供するパラメーターIDへ対応付ける。
- `SetParameter` はパッチの公開IDだけを受け付け、対応するUnityシーンノードの `LiveSceneRoot` へ委譲する。
- Unityシーンノードが提供していないパラメーターはBootを失敗させる。UIおよび入力側は下位ノードの内部パラメーターを直接指定しない。

### 2026-08-26: Mainシーンは既存の起動・ライブ実行コンポーネントをLive Host構成へ置き換える

- Mainシーンから `ApplicationHost`、`StartupSceneGraphBootstrap`、`Scene3DNode`、`MainLiveSceneBootstrap`、`MainLiveInput`、`MainLiveMidiInput`、`MainLiveOutput` および `SimpleExternalDisplayOutput` を外す。
- `ApplicationLoopDriver` は `ApplicationHost` が実行時に追加するため、Mainから `ApplicationHost` を外すことで除外する。
- `MidiInputManager` と既存の `Scene3DDefinition`、Prefabなどのライブ用AssetはMainで継続利用する。
- Mainへ `ApplicationLiveHost`、`LiveGraphBootstrap`、`LiveCapabilityMonitor`、外部Display専用スクリプト、`LiveUiController` および専用UXMLを追加する。
- `BootstrapAssets` 内のライブに必要なAsset参照は `LiveGraphBootstrap` または外部Display専用スクリプトへ移す。

### 2026-08-26: ライブ用Assetは使用する専用スクリプトが明示参照する

- `LiveGraphBootstrap` はInspectorで設定する `Scene3DDefinition[]` を保持する。各Definitionは対象Prefabとその依存Assetを参照する。
- 外部Display専用スクリプトは、HDRのProgram TextureをDisplay向けLDRへ変換する `DisplayTransform.shader` を保持する。
- `LiveUiController` または `UIDocument` は、専用UXML、必要なUSSおよびPanelSettingsを保持する。
- `BootstrapAssets` 全体はMainへ移さない。固定ライブグラフがShaderまたはVideoノードを使用する場合だけ、そのノードに必要なShader、MaterialまたはPrefabを `LiveGraphBootstrap` へ追加する。
- 汎用の2D、Video、Hap、Generator、Effect、Blend、NodeTypeCatalogおよびShader ManifestのAssetは、固定ライブグラフが使用しない限りMainへ移さない。
