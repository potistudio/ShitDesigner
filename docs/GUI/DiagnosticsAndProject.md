# 診断とプロジェクト操作

## 状態

確定。

## 通知の使い分け

- Toast: 保存成功、Display切り替え、拒否された非破壊操作など、応答だけ伝える事象。
- Inline: パラメーター検証、Broken参照、入力不足など、対象箇所と結び付く事象。
- Banner: Recovered、移行後Dirty、Program HoldingLastFrameなど、継続して注意が必要な事象。
- Modal: 未保存プロジェクト終了、素材削除、プリセット削除、復旧不能読込、上書き保存。
- 同じ事象をToastとModalで重複表示しない。

## Diagnosticsパネル

- `Current` と `History` の2タブを持つ。
- CurrentはBlocked、Faulted、Preparing、UsingFallback、HoldingLastFrameの現在状態をノード単位で表示する。
- Historyは最大1000件のリングバッファを表形式で表示する。
- Severity、ノード、DiagnosticCodeのフィルターと単純テキスト検索を提供する。
- 行は時刻、Severity、状態、ノード名、コード、短いメッセージ、集約件数を表示する。
- 行選択時は詳細ペインへID、Port／Parameter、FrameNumber、GraphClock時刻、詳細、例外型、スタックトレースを表示する。
- ノード名クリックでNode Graphを開き対象へフォーカスする。
- `Export Text`／`Export JSON` を提供し、現在フィルターではなく全履歴を書き出す。書き出し前に件数を表示する。
- 同一障害の集約件数は同じ行を更新し、新しい行を点滅追加しない。

## プロジェクトメニュー

- `New`、`Open`、`Open Recent`、`Save`、`Save As`、`Close Project`、`Exit`を提供する。
- ショートカットは `Ctrl + N`、`Ctrl + O`、`Ctrl + S`、`Ctrl + Shift + S`とする。
- 通常編集は手動保存を基本とし、定期自動保存は行わない。
- 未保存変更はトップバーのプロジェクト名へ `*` を表示する。
- Dirty状態でClose／Open／New／Exitを実行した場合は `Save`／`Discard`／`Cancel` を表示する。
- Save失敗時はDirtyを維持し、既存project.jsonを変更せず、詳細をDiagnosticsへ表示する。
- Save中は同じプロジェクトへのSave操作だけを無効化し、映像評価とライブ操作を止めない。

## New／Open

- Newはプロジェクト名と保存フォルダーを最初に指定し、作成成功後に空グラフを開く。
- 空グラフにはProgramOutputとMain Dashboard Pageを生成する。
- Openはフォルダーまたはproject.jsonを選択でき、選択後に構文、形式、参照、素材整合性を段階表示する。
- 読込中は現在のプロジェクトを閉じず、検証成功後に切り替える。
- 最近開いたプロジェクトはユーザー設定へ最大10件保持し、見つからない項目は選択時に削除確認を出す。

## Recovered／Migration

- `.bak` から開いた場合はトップバー直下へ `Recovered from backup` バナーを固定表示する。
- RecoveredはDirtyとし、`Save As`を主要操作、`Overwrite Main File`を副操作として提示する。
- 主ファイルを自動上書きしない。
- ノードスキーマ移行前バックアップの作成、移行、検証を進捗表示する。
- 移行成功後もDirtyとし、自動保存しない。
- UnknownNodeがある場合は読込完了後にDiagnosticsを開き、`Try Restore All`を提示する。
- project.jsonと.bakの両方が無効な場合は現在プロジェクトを維持し、両方の診断をモーダルで表示する。

## 設定

- General: UI Scale、Reduce Motion、Tooltip Delay。
- Graphics: Program Display、RenderTexture予算、Graphics API情報。
- Layouts: レイアウトプリセット管理、既定レイアウト再作成。
- Diagnostics: 書き出し先の初期フォルダー。
- 変更を即時適用できない項目だけ `Restart Required` を表示する。
