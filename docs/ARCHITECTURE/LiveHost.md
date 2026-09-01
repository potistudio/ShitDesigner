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

- ノードグラフは専用スクリプトが `PatchDefinition` の設定から生成する。
- ライブ用ノードグラフのノード、接続および初期パラメーターは `PatchDefinition` に保持する。
- 3Dシーンの構成はライブ用ノードグラフに含める。

### 2026-08-30: Overlayシーケンサーは既存ノードRuntimeでProgramへ合成する

- Overlayシーケンサーの8レーンは、割り当てられたOverlayパッチのRuntimeをレーンごとに保持する。セルがOffでも割り当てが変わるまでRuntimeを維持する。
- Overlayパッチの評価、Scene更新および描画は現在ステップで有効な間だけ行う。Offへ移った時点でSceneを非アクティブ化し、ProgramはMain Textureへ直接戻す。
- 割り当て済みOverlayシーンは、レーン選択ボタンに160×90の低解像度サムネイルを表示する。同じシーンを複数レーンへ割り当てた場合はプレビューRuntimeを共有し、10fpsで更新する。
- 現在ステップで有効なレーンを0から7の順にMain Textureへ合成し、後のレーンを前面として扱う。
- Normal、Add、Multiply、SubtractおよびDifferenceはShader Manifestの既存Blendノードを使用する。Invertは既存Invertノードの結果をNormal Alpha Overで合成する。
- シーケンサーの拍進行はLoaded Patchを切り替えない。合成後の単一TextureをProgram映像として外部DisplayとProgram Monitorへ提示する。

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
- ライブ用ノードグラフは `PatchDefinition` に保存された構成を実行中に維持する。

### 2026-08-26: 固定ライブグラフの `ProgramOutput` をProgram映像の正本にする

- 固定ライブグラフの `ProgramOutput` がProgram映像の唯一の正本となるTextureを生成する。
- `LiveGraphBootstrap` はMainへOverlayを合成してInstant FXを通すOutput 1と、コピー指定されたOverlayレーンだけを黒背景へ合成するOutput 2に独立したRenderTextureを構成する。外部Display専用スクリプトはDisplay 2、Display 3へ順に対応するTextureだけを受け取り提示する。
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
- `ApplicationLiveHost` のライブ実行Tickが `LiveKeyboardInput.Poll()` を呼び、パッチ切替または公開パラメーター変更要求を `LiveParameterQueue` へ投入する。左右矢印はMain／Overlay／FXタブを切り替え、上下矢印は選択中タブのカタログ内を移動する。
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
- `ApplicationLiveHost` は各ライブ実行TickでOutput 1とOutput 2のFrameを外部Display専用スクリプトの `Present` へ渡し、両方の参照を `LiveUiReadModel` へ公開する。
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

- `LiveGraphBootstrap` はTickを持たないMonoBehaviourとし、`PatchDefinition` とShader ManifestなどのAsset参照をシリアライズして保持する。
- `LiveGraphBootstrap.CreateRuntime()` は通常のC#クラスである `LiveGraphRuntime` を作成する。
- `LiveGraphRuntime` は `PatchDefinition` から生成したノードグラフ、3DシーンRuntimeおよびProgram Textureを所有し、`Evaluate`、`SceneUpdate`、`Render` および `Dispose` を提供する。
- `ApplicationLiveHost` はBootでRuntimeを作成し、ライブ実行Tickで各処理を定義した順序で呼び、ShutdownでRuntimeを破棄する。
- ノード構成、接続およびShaderノードの初期パラメーターは `PatchDefinition` のInspectorで設定する。Prefab、`Scene3DDefinition`、ShaderなどのAsset参照をGUIDまたはパスとしてコードへ埋め込まない。

### 2026-08-26: パッチ数はInspectorで設定し、1件だけ事前ロードする

- `LiveGraphBootstrap` は任意数の `PatchDefinition` をシリアライズして保持する。
- `PatchDefinition` はInspectorでProgram graphのノード、接続およびノードパラメーターを単一の可変長リストとして設定する。`Scene3DDefinition` はSceneノードのペイロードとして参照し、ライブUIへ公開するパラメーターをグラフノードIDとパラメーターIDで明示的に束ねる。Shaderパラメーターは必要なものだけを個別に追加し、未追加の値はManifestの既定値を使用する。これはUnityのSceneとは別の論理的なパッチである。
- Inspectorの可変長要素はUnity標準のリスト描画を使用し、各要素とその子要素を再帰的に表示する。追加・削除は各リストの標準操作から行う。
- `PatchDefinition` のInspectorは生成済みShader Manifestとプロジェクト内の `Scene3DDefinition` を選択肢として表示する。ノード型、接続元/接続先、画像ポート、ShaderパラメーターおよびScene公開パラメーターのIDは、候補選択または定義からの自動生成を基本とし、手入力を要求しない。
- `PatchDefinition` のグラフノードIDおよび公開パラメーターIDは、空欄の追加時にInspectorが一意な値を生成する。Sceneノードは `Scene3DDefinition` を選択し、すべてのノードを同じノードID・接続・`image` ポートの契約で扱う。
- `PatchDefinition` はMIDI入力一覧を持ち、MIDIのメッセージ種別、チャンネル、番号、生値範囲、反転および対象となる公開パラメーターIDを設定する。`ApplicationLiveHost` はロード中パッチの設定だけを `LiveParameterQueue` の `SetParameter` 要求へ変換する。
- `PatchDefinition` はKeyboard入力一覧を持ち、Input SystemのKeyおよび対象となる公開パラメーターIDを設定する。押下時だけ1.0を送り、離上時は要求を生成しない。`ApplicationLiveHost` はロード中パッチの設定だけを `LiveParameterQueue` の `SetParameter` 要求へ変換する。
- Program graphのLook調整は `PatchDefinition` Assetの変更として保存し、C#スクリプトのコンパイルを必要としない。
- Bootは最初のパッチだけを事前ロードする。
- パッチの事前ロード時に対象のUnityシーンノードRuntimeと描画リソースを生成する。
- 事前ロード対象は常に1件だけ保持する。別のパッチを事前ロードすると、表示中ではない前の事前ロード対象を破棄する。
- ロード要求は事前ロード済みのパッチIDと一致する場合だけ受理する。表示中のパッチはロード要求まで切り替わらない。
- 表示中のパッチに含まれるノードだけを更新および描画する。表示中および事前ロード対象のRuntimeはShutdownで破棄する。

### 2026-08-30: ライブUIからCueを除去する

- パッチスロットとCue表示は持たず、パッチブラウザの選択を直接Launch要求へ変換する。
- Overlayレーンを選択している間は、Overlayパッチの選択をLaunchではなく対象レーンへの割り当てとして扱う。
- Launch内部で必要なRuntime生成と切替を同一ライブTickで完結させる。

### 2026-08-27: BPMクロックはライブグラフ全体で共有する

- BPM値と累積ビートは `LiveGraphRuntime` が保持し、パッチ固有の公開パラメーターにはしない。
- BPM変更とTap入力はグローバル要求として処理し、パッチを切り替えてもテンポと拍位置を維持する。
- BPMに追従するSceneは、更新ごとに共有クロック状態を受け取る。SceneごとのMotion倍率はBPMクロックに影響しない。

### 2026-08-29: BPMクロックのビート位置をボタンで合わせ直せる

- UIの`ALIGN BEAT`ボタンは現在の調整後ビート位置を最寄りの整数ビートへ合わせるグローバル要求を生成する。
- 合わせ直しは現在のビート位相だけを補正し、累積ビートは変更せず、調整後位置を最寄りの整数ビートへ移す。後からBPMを変更しても合わせ直し位置は動かない。
- `BeatClockFrame` は累積ビートと調整後のビート位置を分離して公開し、位相とビート境界を使うSceneの両方へ同じ補正を適用する。

### 2026-08-27: ライブ用キーボードは全体操作とパッチ入力を扱う

- 左右矢印はMain／Overlay／FXタブを切り替え、上下矢印はタブ内のカタログ選択を移動する。`1`から`8`は対応するOverlayレーンを押している間だけTakeするPianoモードとする。恒常キーの`Shift+1`から`Shift+8`は、押した時点のビートに対応するセルだけをONにし、他のビートやレーンは変更しない。MainまたはOverlayタブではEnterで選択パッチを直接Launchする。FXタブの選択ノードはLaunchしない。
- 同じOverlayシーンを複数レーンへ割り当てた場合、フル解像度のScene Runtimeは共有し、評価・物理更新・描画をフレームごとに1回だけ行う。各レーンの合成モードは共有出力へ独立して適用する。
- Overlayレーンは既定でOutput 1だけへ合成する。Ctrl/Cmdを押しながらシーケンサー行をクリックすると、そのレーンの共有出力をOutput 2へもコピーする状態を切り替える。
- SpaceはUIのTAPボタンと同じBPM Tap入力とする。
- 通常操作中の`Q`から`P`または対応するCueボタンは、押下時点では発火せず、共有BPMクロックの次の整数拍へ予約する。同じCueが発火前に複数回押された場合は1回へまとめる。
- `PatchDefinition` のKeyboard Inputsは、キー押下時だけ1.0をロード中パッチの公開パラメーターへ送る。離上では要求を生成せず、キーボードの全体操作はこの設定とは別に固定される。
- Motion、Scaleなどの連続値はMIDI入力またはUIの操作面から要求する。

### 2026-09-02: Main TakeとMain Composite Takeを分離する

- `A`は反対側のMainを押している間だけ表示し、離すと元のMainへ戻すMomentary Takeとする。`S`は反対側のMainへ完全に切り替えるPermanent Takeとする。
- `Shift+A`は反対側のMainを現在のMainへ通常のアルファ合成で重ね、離すと元の合成状態へ戻すMomentary Composite Takeとする。
- `Shift+S`は同じMain合成をキーから手を離した後も維持するPermanent Composite Takeとする。通常のPermanent TakeまたはMainパッチのLaunchは合成状態を解除する。
- `S`または`Shift+S`と`[`／`]`を同時に押した場合は、反対側のMainへHot Cueを先に適用し、その後にPermanent TakeまたはPermanent Composite Takeを1回だけ実行する。

### 2026-08-31: Main Cueの完全切替とフェーダー変化を分離する

- `Shift+C` は、現在のフェーダー合成で優勢なCueとは反対側を100%表示し、その時点の物理フェーダー位置を新しい基準にする。
- Main CueフェーダーはCue A/Bへ絶対位置を割り当てない。基準位置では基準Cueを100%表示し、基準から片側の端まで動かした割合に応じて反対Cueの不透明度を0から1へ変化させる。
- 最初のMIDIフェーダー値は基準位置の取得だけに使用し、Program映像を切り替えない。基準位置が1なら1→0、0なら0→1を反対Cueの不透明度0→1へ正規化し、中間位置では動かした側の端までの距離を使用する。Cue切替後は最後に受信した物理位置を新しい基準として再ラッチする。正規化後の値には`ApplicationLiveHost`のMain Cue Fader Curveを適用し、カーブ出力を0から1へ制限する。既定入力はLaunch Control XL 3のMode 16における左端フェーダー（MIDI Channel 16 / CC 5）とし、Channel、CCおよびカーブは`ApplicationLiveHost`のInspectorで変更できる。

### 2026-08-30: FXカタログは画像処理ノードを表示する

- FXカタログはパッチ一覧ではなく、Shader Manifestに登録されたノード定義を正本とする。
- `UserAddable` かつ履歴以外の`ImageFrame`入力を持つノードを、カテゴリ、表示名、Type IDの順でFXカタログへ登録する。
- FXカタログはノード型の選択とINSTANT FX Cueへの割り当てに使用する。Cue割り当てが、選択したFXノードをINSTANT FX用の実行グラフへ追加する操作となる。
- Cueへ割り当てたFXノードは割り当て時にRuntimeインスタンスを生成する。BPM補正後の発火拍ではOverlay合成済みProgram映像をPrimary入力と必須ImageFrame入力へ配線し、次の整数拍までFX出力をProgram出力とする。同じ拍の複数CueはCue番号順に直列評価する。
- INSTANT FXの初期パラメーターはShaderが定義するMaterial既定値から取得する。Amount／Mixは適用度として1に設定するが、BlurのRadiusなどFX固有の強度はShader既定値を保持し、0で効果が消える状態を作らない。
- 最後に割り当てた、またはパラメーターフォーカスしたCueをライブパラメーターの対象とし、そのFXノードが持つ操作可能なパラメーターを定義順で最大8本公開する。変更値は対象CueのRuntimeインスタンスに保持し、別Cueの同型FXとは共有しない。

### 2026-09-01: Hot CueはMain Cueから独立したパッチ状態とする

- `PatchDefinition` はProgram Graphパラメーター値の組み合わせとしてHot Cueを最大2つ保持する。別ファイルやMain Cueスロットには保存しない。
- Hot Cue値のIDが空欄なら未設定として扱い、パッチ検証とHot Cue呼び出しの両方で無視する。
- `[`はHot Cue 1、`]`はHot Cue 2を現在のMainパッチへ適用する。未設定のHot Cueは適用しない。
- `Shift+[`と`Shift+]`は同じ番号のHot Cueを反対側のMain Cueスロットに読み込まれたパッチへ適用する。反対側が空なら何もしない。
- VideoPlayerの`Playing`、`Playhead`、`Speed`、`Loop`は公開パラメーターにしなくてもHot Cueから直接変更でき、保存済みPlayheadへのSeekを同じライブTickで行う。
- Hot Cueの呼び出しはMain Cueのパッチ割り当て、優勢Cue、基準Cueおよびフェーダー位置を変更しない。MainパッチのLaunchやMain Cue切替もHot Cueを発火しない。
- Hot Cueはビートクオンタイズせず、入力要求を処理する同じライブTickで適用する。トリガー型の公開SceneパラメーターはHot Cue値を適用した直後に最小値へ自動解放し、AssetFlushなどのワンショット発火を保持状態にしない。AssetFlushの解放後不透明度は、InspectorのFade Out SecondsとFade Out Curveで設定する。

### 2026-08-31: Shift+TabでINSTANT FX素材の編集モードを切り替える

- `Shift+Tab` は通常操作と編集モードを切り替える。編集モードへ入るとFXカタログを選択し、上下矢印はFXカタログ内だけを移動する。
- FXカタログはカテゴリ見出しと子ノードのツリーで表示する。開けるカテゴリは1つだけとし、別カテゴリを開くと以前のカテゴリを閉じる。編集モード中の上下キーは表示中のカテゴリ見出しとFX項目を移動し、`Space`は選択中のカテゴリ見出しを開閉する。
- 編集モード中の`QWERTYUIOP`または対応するCueボタンは、選択中のFXノード型を対象Cueへ割り当てる。通常時のINSTANT FX発火には使用しない。
- `Shift+QWERTYUIOP`はモードにかかわらず対象Cueを発火・再割り当てせず、そのCueのFXパラメーターへライブパラメーター表示を切り替える。未割り当てCueでは変更しない。
- 通常操作中の`ZXCVBNM`は、表示中のライブパラメーターを左から7本まで発火する。押下時に最大値、離上時に最小値を送り、同じキーに設定されたパッチ固有Keyboard入力より優先する。
- 編集モード中はMain／Overlayタブ、シーケンサー、公開パラメーターおよびBPM操作を無効にする。Program映像の評価と出力は継続する。
- 編集モードは画面外周の黄色い枠で表示する。

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

- `PatchDefinition` の公開パラメーターは、グラフノードIDとそのパラメーターIDへ対応付ける。Sceneノードは `Scene3DDefinition` の `LiveSceneRoot` が提供するパラメーターへ、ShaderおよびVideoノードは各ノードRuntimeへ委譲する。
- `SetParameter` はパッチの公開IDだけを受け付け、対応するグラフノードへ委譲する。
- Sceneノードが提供していないパラメーター、またはグラフノードが提供していないパラメーターはBootを失敗させる。UIおよび入力側は下位ノードの内部パラメーターを直接指定しない。

### 2026-08-26: Mainシーンは既存の起動・ライブ実行コンポーネントをLive Host構成へ置き換える

- Mainシーンから `ApplicationHost`、`StartupSceneGraphBootstrap`、`Scene3DNode`、`MainLiveSceneBootstrap`、`MainLiveInput`、`MainLiveMidiInput`、`MainLiveOutput` および `SimpleExternalDisplayOutput` を外す。
- `ApplicationLoopDriver` は `ApplicationHost` が実行時に追加するため、Mainから `ApplicationHost` を外すことで除外する。
- `MidiInputManager` と既存の `Scene3DDefinition`、Prefabなどのライブ用AssetはMainで継続利用する。
- Mainへ `ApplicationLiveHost`、`LiveGraphBootstrap`、`LiveCapabilityMonitor`、外部Display専用スクリプト、`LiveUiController` および専用UXMLを追加する。
- `BootstrapAssets` 内のライブに必要なAsset参照は `LiveGraphBootstrap` または外部Display専用スクリプトへ移す。

### 2026-08-26: ライブ用Assetは使用する専用スクリプトが明示参照する

- `LiveGraphBootstrap` はInspectorで設定する `PatchDefinition[]` を保持する。各DefinitionはSceneノードを含むProgram graphと、その依存Assetを参照する。
- 外部Display専用スクリプトは、HDRのProgram TextureをDisplay向けLDRへ変換する `DisplayTransform.shader` を保持する。
- `LiveUiController` または `UIDocument` は、専用UXML、必要なUSSおよびPanelSettingsを保持する。
- `BootstrapAssets` 全体はMainへ移さない。固定ライブグラフがShaderまたはVideoノードを使用する場合だけ、そのノードに必要なShader、MaterialまたはPrefabを `LiveGraphBootstrap` へ追加する。
- 汎用の2D、Video、Hap、Generator、Effect、Blend、NodeTypeCatalogおよびShader ManifestのAssetは、固定ライブグラフが使用しない限りMainへ移さない。
