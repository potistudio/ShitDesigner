# ワークスペースとレイアウト

## 状態

確定。

## ウィンドウ

- 初期ウィンドウサイズは1600×900、最小サイズは1280×720とする。
- ウィンドウはリサイズ可能、バックグラウンド実行を有効にする。
- 推奨操作解像度は1920×1080以上とする。
- UIスケールは100%、125%、150%から選択し、ユーザー設定へ保存する。
- パネルをメインウィンドウ外へ切り離す機能は設けない。
- ProgramのUnity Display全画面出力は、メインウィンドウのレイアウトとは独立して扱う。

## 共通シェル

### トップバー

高さ40 logical pxの固定領域とし、左から次を並べる。

1. アプリメニュー
2. プロジェクト名とProject Dirty表示
3. Save
4. Undo／Redo
5. GraphClock Pause／Resume
6. レイアウトプリセットのドロップダウン
7. Layout Save／Save As／Manage
8. Program Display選択
9. Diagnostics状態ボタン

- Project Dirtyはプロジェクト名の末尾へ `*` を表示する。
- Layout Dirtyはレイアウト名の末尾へ `*` を表示する。
- 2つのDirty状態を1つの印へ統合しない。
- トップバーはレイアウトプリセットの対象外とし、常に表示する。

### ドックワークスペース

- パネルは水平／垂直分割、タブ化、リサイズ、表示／非表示を行える。
- ドロップ先は上下左右、中央タブの5領域をガイド表示する。
- パネルの最小幅は240 logical px、最小高さは160 logical pxを既定とし、Program MonitorとPreview Viewer Hostは最小16:9表示領域を優先する。
- タブを閉じてもパネル固有のプロジェクトデータは削除しない。
- `Window` メニューから閉じたパネルを再表示できる。
- 同じ単一インスタンスパネルを再度開く操作は、既存タブへフォーカスする。

### ステータスバー

高さ24 logical pxの固定領域とし、次を常時表示する。

- GraphClock時刻とPause状態
- Program実測fps
- CPU Frame Time
- GPU Frame Time
- Preview品質段階と抑制中件数
- 最新のWarning／Error件数

Program性能警告中は該当値を強調するが、ステータスバーの高さや配置は変えない。

## レイアウトプリセット

- ユーザー設定として全VJプロジェクトから共通利用する。
- すべて通常プリセットとして扱い、作成、名前変更、上書き、複製、削除を許可する。
- 切り替えの主要UIはトップバーのドロップダウンとする。
- レイアウト変更は自動保存せず、`Layout Save` で明示的に上書きする。
- 未保存変更中はレイアウト名へ `*` を表示する。
- 未保存変更中に別レイアウトを選択した場合は、確認なしで変更を破棄して切り替える。
- レイアウト削除だけは確認ダイアログを表示する。
- 最後の1件は削除できない。削除操作が最後の1件を対象とする場合は理由を表示して拒否する。
- 初回起動時に `Edit` と `Live` を作成する。どちらも通常プリセットであり、保護しない。
- `Manage Layouts` から `Recreate Defaults` を実行すると、同名衝突時は `Edit (Default)`／`Live (Default)` として追加する。

## 初期レイアウト

### Edit

- 左20%: Node Library、Media Libraryのタブ。
- 中央55%: Node Graph。
- 右25%: Inspector、Logical Controls、Diagnosticsのタブ。
- 右領域の下40%: Program Monitor。
- Presets、Preset Editor、Live Dashboardは閉じた状態で開始する。

### Live

- 左65%上: Live Dashboard。
- 左65%下25%: Presets。
- 右35%上: Program Monitor。
- 右35%下: Preview Viewer Host。
- Diagnosticsは右下グループのタブとして置き、通常は非選択とする。
- Node Graph、Node Library、Inspector、Preset Editorは閉じた状態で開始するが、編集権限は無効化しない。

## レイアウト適用失敗

- 存在しないパネル種別は無視せず、`Unknown Panel` のプレースホルダーとして位置と生データを保持する。
- 画面サイズが保存時より小さい場合は比率を保って縮小し、最小サイズを満たせないパネルをタブへ畳む。
- レイアウト全体を復元できない場合は現在状態を破壊せず、`Edit (Default)` 相当の一時レイアウトを開いてDiagnosticsへ理由を記録する。
