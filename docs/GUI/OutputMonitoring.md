# ProgramとPreview

## 状態

確定。

## Program Monitor

- 専用の単一パネルとし、Preview Viewer Hostと共通化しない。
- 映像領域は常に16:9を保ち、余白を不透明黒で表示する。
- Program映像データ自体へGUIや診断文字を合成しない。
- パネル上部に選択中Unity Display、出力有効状態、1920×1080、実測fpsを表示する。
- パネル下部にCPU Frame Time、GPU Frame Time、Program状態を固定高のステータス行として表示する。
- HoldingLastFrame中は映像領域の外側へ状態、継続時間、原因ノード、Diagnosticsリンクを表示する。
- 正常フレームがない場合の不透明黒もProgram出力として扱い、パネル外側に `No valid frame` を表示する。
- Program Monitorを閉じても外部Display出力とProgram評価は停止しない。

## Program Display

- `LiveGraphBootstrap` はProgramOutputごとに独立したRenderTextureを構成する。`Display 2` は先頭ProgramOutput、`Display 3` は2番目のProgramOutputを表示する。
- 接続済みDisplayはトップバーで一覧表示する。Display数がProgramOutput数を超える場合、対応する出力のないDisplayは不透明黒を表示する。
- 外部Displayが失われた場合は残りの接続済みDisplayへ対応するProgramOutputの出力を継続し、外部Displayがない場合はProgram Monitorへフォールバックする。
- `Identify Displays` は各操作Displayへ番号を3秒表示するが、Program映像には重ねない。

## Preview Viewer Host

- レイアウトプリセットは単一のPreview Viewer Hostの配置、サイズ、表示状態だけを保存する。
- VJプロジェクトは、Host内で表示するPreviewノード、タブ順序、選択中タブを保存する。
- Previewノード1つにつきViewerタブ1つを対応させる。
- Previewノードのダブルクリックは既存タブへフォーカスし、未作成ならHostへ新規タブを開く。
- Viewerタブを閉じる操作とノードの目アイコンは同じプロジェクト固有の表示状態を変更する。
- レイアウト切り替えでHostが非表示になった場合、Previewタブの割り当ては維持したまま全Previewを非表示として扱い、上流評価要求を止める。VJプロジェクトはDirtyにしない。
- Hostが再表示された場合は、VJプロジェクトに保存されたPreviewタブを復元して表示を再開する。
- 同時表示上限8個へ達した場合、9個目を開かず、理由と現在表示中のPreview名を表示する。
- タブタイトルはPreviewノードの表示名とし、NodeInstanceIdをツールチップへ表示する。
- ツールバーにFit／Fill／Stretch、背景Checker／Black、対象ノードへ移動、閉じるを置く。
- Fitを既定とし、表示方式と背景をPreviewノードのプロジェクトデータへ保存する。
- 選択されたViewerタブを品質制御上のフォーカスPreviewとして扱う。

## Preview状態表示

- Blocked: 透明黒の映像領域へ不足入力、原因ノード、Diagnosticsリンクを表示する。
- Faulted: 暗赤色オーバーレイ、DiagnosticCode、短いメッセージを表示する。
- Preparing: 進行スピナーと処理名を表示する。
- UsingFallback: 映像右上へ黄色バッジを表示する。
- 自動品質抑制: 現在解像度と更新fpsを右下へ表示する。
- 状態表示は操作用Previewだけへ重ね、Program映像へ伝播させない。

## Preview品質

- 品質段階は仕様の0～4をそのまま表示し、ユーザーが段階を固定する操作は初期版に設けない。
- ステータスバーの品質表示をクリックすると、各Previewの段階、フォーカス時刻、抑制理由をポップオーバー表示する。
- Preview抑制中もノード評価とProgram品質の優先順位を変更しない。
