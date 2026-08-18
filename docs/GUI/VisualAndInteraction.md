# 視覚言語と入力操作

## 状態

確定。

## テーマ

- 初期版はDark Themeだけを提供する。Light Themeは初期対象外とする。
- 背景を暗くし、映像と状態色の判別を優先する。純黒を広い操作面へ使わず、階層ごとに明度差を付ける。
- Accent Colorは青とし、選択、フォーカス、主要操作へだけ使用する。
- 色は意味を補助するものとし、状態、ポート型、必須性を色だけで伝えない。

## カラートークン

| 用途 | 色 |
|---|---|
| App Background | `#0D0F12` |
| Panel Background | `#15181D` |
| Elevated Surface | `#1D2229` |
| Border | `#303741` |
| Primary Text | `#E8ECF2` |
| Secondary Text | `#9DA7B4` |
| Disabled Text | `#626B76` |
| Accent／Focus | `#58A6FF` |
| Success | `#4CC38A` |
| Preparing／Info | `#49BFD1` |
| UsingFallback／Clamp | `#E8B44C` |
| Blocked | `#E8873A` |
| Faulted／Error | `#ED5F68` |
| HoldingLastFrame | `#C879E8` |

- 通常文字と背景はWCAG AA相当の4.5:1以上、大きな文字と非文字UIは3:1以上を目標にする。
- Disabled状態でもラベル自体を読める明度を維持し、操作不可はカーソルとアイコンでも示す。

## タイポグラフィ

- `Noto Sans`をUIフォント、`Noto Sans Mono`をID、時刻、数値、診断詳細へ使用する。
- UI表示言語は初期版では英語とし、ローカライズ機構は初期対象外とする。
- プロジェクト名、ノード名、プリセット名、素材名、ユーザーラベルはUnicodeを許可し、日本語を正しく表示する。
- 本文13px、補助11px、パネルタイトル13px Medium、主要数値14px Monoを100%スケール時の基準とする。
- 状態コード、NodeTypeId、ParameterIdは翻訳せず正規名を表示する。

## 寸法

- 4pxを最小単位、8pxを主要スペーシング単位とする。
- 通常コントロール高32px、コンパクト行28px、トップバー40px、ステータスバー24pxとする。
- 角丸は4px、ポップオーバーとモーダルは6pxとする。
- クリック対象は最小28×28px、主要ライブ操作は最小40×40pxとする。
- 境界線は通常1px、選択とキーボードフォーカスは2pxとする。

## ポート型

| 型 | 色 | 中央記号 |
|---|---|---|
| ImageFrame | `#B38CFF` | 四角 |
| Float | `#69D38B` | `F` |
| Int | `#63A9FF` | `I` |
| Bool | `#E6C65C` | 菱形 |
| Vector2 | `#57CFD2` | `2` |
| Vector3 | `#49B9C8` | `3` |
| Vector4 | `#3BA4BE` | `4` |
| Color | `#F07CB4` | 中央ドット |

- 必須ポートは塗りつぶし、任意ポートは同じ形状のアウトラインとする。
- 接続選択時は線幅を2pxから3pxへ上げ、色だけに依存しない。

## アイコンと状態

- アイコンは単色ベクターを使用し、16pxと20pxの2基準サイズを持つ。
- アイコンだけのボタンは必ずTooltipとアクセシブル名を持つ。
- Preparingはスピナー、UsingFallbackは下向き分岐、Blockedは切断記号、Faultedは感嘆符、HoldingLastFrameは一時停止フレームで示す。
- WarningとErrorのアイコン形状を共通化しない。

## アニメーション

- パネル切り替え、ポップオーバー、トーストは120ms～160msのEase Outとする。
- ライブ値の更新に位置アニメーションを使わず、数値とFillだけを直接更新する。
- Faulted、Blocked、プリセット失敗を無限点滅させない。
- Reduce Motion有効時はスピナー以外の遷移を0msにし、スピナーは段階表示へ置き換える。

## ショートカットの規則

- `Primary` はWindows／LinuxのCtrl、macOSのCommandを表す。
- テキスト入力中は編集用ショートカットを優先し、Node Graph操作を発火しない。
- `Tab` のノード追加はNode Graphのキャンバス面にフォーカスがある場合だけ有効とする。検索欄、ノード内コントロール、ツールバーにフォーカスがある場合は通常のフォーカス移動として扱う。
- `Primary + Space` はGraphClock Pause／Resumeとし、フォーカスに依存しない。
- ショートカットはメニューとTooltipへ表示する。
- 初期版でユーザーによるショートカット再割り当ては提供しない。

## グローバルショートカット

| 操作 | Shortcut |
|---|---|
| New Project | `Primary + N` |
| Open Project | `Primary + O` |
| Save Project | `Primary + S` |
| Save Project As | `Primary + Shift + S` |
| Undo | `Primary + Z` |
| Redo | `Primary + Shift + Z` |
| Command Palette | `Primary + K` |
| GraphClock Pause／Resume | `Primary + Space` |
| Close active panel | `Primary + W` |
| Focus Diagnostics | `Primary + Shift + D` |
| Focus Program Monitor | `Primary + Shift + P` |

## Node Graphショートカット

| 操作 | Shortcut |
|---|---|
| Add Node | `Tab` |
| Delete | `Delete`／`Backspace` |
| Copy／Paste | `Primary + C`／`Primary + V` |
| Duplicate | `Primary + D` |
| Select All | `Primary + A` |
| Clear Selection | `Esc` |
| Frame Selection | `F` |
| Frame All | `Home` |
| Toggle Grid Snap | `G` |
| Toggle Minimap | `M` |

## Command Palette

- `Primary + K`でトップバー直下へ開く。
- メニュー操作、パネル表示、レイアウト切り替え、プロジェクト操作、選択ノードの主要コマンドを検索できる。
- プリセット呼び出しは誤発火防止のためCommand Paletteの候補へ含めない。
- 破壊的操作は候補名へ明示し、実行後も通常の確認規則に従う。

## 値操作

- 数値フィールドは直接入力、上下キー、ラベルの水平ドラッグに対応する。
- `Shift`を押しながらドラッグすると10倍、`Alt`を押しながらドラッグすると0.1倍の速度にする。
- 右クリックメニューからDefaultへ戻せる。
- スライダーのダブルクリックは数値入力へフォーカスし、値をリセットしない。
- 操作中の連続値は共通パラメーターAPIへ送り、更新キューの合流規則に従う。
- Queue上限で拒否された場合は操作部品へ赤い境界と短い理由を表示し、Diagnosticsへ記録する。

## フォーカスとTooltip

- Tabでパネル内の操作可能要素を論理順に移動し、パネルタブ、ツールバー、本文、フッターの順とする。ただしNode Graphのキャンバス面にフォーカスがある場合はノード追加を開く。
- キーボードフォーカスは2pxのAccentリングで常に表示する。
- `Esc`は最も内側の一時UIから閉じる。ドラッグ、Learn Key、ポップオーバー、検索、モーダルの順に作用する。
- Tooltipは既定500msで表示し、設定で250ms／500ms／1000msから選択できる。
- 無効な操作のTooltipは、操作名ではなく無効な理由を先に表示する。

## 確認と取り消し

- Undo可能なノード追加、削除、接続、切断、置換には確認を出さない。
- Undo不能または外部ファイルへ影響する素材削除、プロジェクト破棄、レイアウト削除には確認を出す。
- 未保存レイアウト変更の切り替えだけは、確定済み方針に従い確認なしで破棄する。
- Modalの既定フォーカスは安全側の操作へ置き、破壊的ボタンを右端かつ赤色にする。
