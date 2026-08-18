# パラメーターとライブ操作

## 状態

確定。

## Inspector

- Node Graphで最後に選択した1ノードを表示する。複数選択時も最後の選択を対象とする。
- 未選択時は空状態と、ノード選択方法を表示する。
- ヘッダーに表示名、NodeTypeId、NodeInstanceId、状態、診断へのリンクを表示する。
- ノード型のカスタムUIを既定表示とし、`Standard Parameters` タブから常に標準UIへ切り替えられる。
- カスタムUI生成失敗時は標準UIへ自動フォールバックし、上部へ警告を表示する。
- パラメーター名、ParameterId、説明を対象とする単純フィルターを設ける。

## 標準パラメーター行

- グループごとの折り畳みセクションとし、表示順、次にParameterIdで並べる。
- 各行はLabel、BaseValue編集、EffectiveValue読取、単位、状態、Dashboard追加ボタンを持つ。
- BaseValueとEffectiveValueが同じ場合も両方表示する。EffectiveValueは弱い背景と `E` ラベルで示す。
- Boolはトグル、Float／Intは数値フィールドとスライダー、Vectorは成分フィールド、ColorはスウォッチとRGBA、Enumはドロップダウン、Stringはテキスト、MediaAssetReferenceは素材ピッカーを使う。
- ハード範囲をスライダー範囲へ使用し、刻みがある場合は入力にも適用する。
- Vector／Colorは成分単位の範囲と、一括入力メニューを持つ。
- クランプ時は黄色バッジを表示し、詳細で変換前、変換後、範囲種別を示す。
- Broken、検証失敗、ReadOnlyを行内表示する。Hiddenは通常UIへ表示しない。
- 表示外または折り畳み中のEffectiveValue描画更新を止める。

## Live Dashboard

- 複数ノードの任意パラメーターを、プロジェクト固有の操作面として並べる。
- プロジェクトは複数の名前付きDashboard Pageを持てる。パネル上部のタブで切り替える。
- 新規プロジェクトは `Main` ページを1つ持つ。
- ページは12列グリッドとし、Widgetをドラッグ、リサイズ、複製、削除できる。
- `Arrange` トグルが有効な間だけWidget配置を変更し、無効時はライブ操作に専念する。
- Arrange状態はプロジェクトへ保存せず、パネルのセッション状態とする。
- Inspectorの追加ボタン、パラメーター行のドラッグ、Widgetの複製からDashboardへ追加できる。
- WidgetはNodeInstanceIdとParameterIdを参照し、表示名変更に追従する。
- 参照切れWidgetは削除せずBrokenとして残し、対象ID、理由、`Rebind`、`Remove`を表示する。

## Dashboard Widget

- 共通表示はユーザーラベル、ノード名、BaseValue操作、EffectiveValue読取、論理コントロール状態、診断バッジとする。
- Float／IntはHorizontal Faderを既定とし、Knob、Vertical Fader、Numericへ変更できる。
- BoolはToggleを既定とし、Momentary Buttonへ変更できる。
- Vector／Colorは成分グループ、Enumはドロップダウン、Stringはテキスト、MediaAssetReferenceは素材ボタンを使う。
- Widgetスタイル、ラベル、サイズ、ページ、並び順はVJプロジェクトへ保存する。
- EffectiveValueを直接編集するWidgetは作らない。

## Logical Controls

- 左にLogicalControl一覧、右に選択項目の詳細を置く単一パネルとする。
- 一覧はValueとPresetTriggerで区別し、名前、現在値／発火状態、物理割り当て状態、Broken件数を表示する。
- 追加、名前変更、複製、削除を提供する。削除時は参照中ターゲットを表示して確認する。
- 初期物理入力はKeyboardだけとし、`Learn Key` 中に次に押されたキーを割り当てる。
- EscapeはLearnをキャンセルし、修飾キー単独は割り当て対象外とする。
- Valueは押下中1、解放中0を表示する。PresetTriggerは発火時に短いパルス表示を行う。
- ターゲット一覧はノード、パラメーター、範囲、反転、式内利用箇所を表示する。
- Valueは複数ターゲットを持てる。各ターゲットで出力範囲と反転を編集する。
- PresetTriggerは未割り当てまたは1つのPresetだけを割り当て可能とし、パラメーターやグラフ編集を直接ターゲットにしない。
- PresetTriggerのアナログ入力には0.5発火／0.4未満で再発火可能の閾値を読取表示し、初期版では変更不可とする。

## 合成式エディター

- 対象パラメーター行の `Expression` からインライン展開する。
- Min／Max二分木を括弧構造どおり省略せず表示する。
- 演算変更、左右交換、葉の差し替え、Base葉追加／削除を提供する。
- 編集中はDraftバナーを表示し、`Apply` 成功時だけ旧式と原子的に置換する。
- `Cancel` はDraftを破棄する。
- 不完全、型不一致、循環、Broken葉は該当ノードへ理由を表示する。
- Broken式のEffectiveValueがBaseValueへフォールバック中であることを、InspectorとDashboardの両方へ表示する。

## ノード固有の標準カスタムUI

### VideoPlayer

- MediaAsset、Play／Pause、Playhead、Speed、Loopを1つのTransport領域へまとめる。
- Playheadは秒表示とスクラバーを併記し、BaseValueとEffectiveValueを通常どおり分ける。
- Value式がPlayheadへ割り当てられている間は `Scrub Mode` バッジを表示し、自動進行停止を明示する。
- SeekまたはPrepare中は操作を隠さず、進行状態と要求位置を表示する。
- In／Out、Reverse、フレーム送りは初期版へ表示しない。

### Feedback

- `Reset History`をカスタムUIとノードのコンテキストメニューへ表示する。
- 実行時は次の評価フレーム境界までPending表示し、完了後に短い成功表示を行う。
