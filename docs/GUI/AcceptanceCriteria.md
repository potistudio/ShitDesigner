# GUI受け入れ条件

## 状態

確定。初期版GUIの受け入れ判定に使用する。

## ワークスペース

- [ ] 1280×720以上でトップバー、ドック領域、ステータスバーが重ならず操作できる。
- [ ] パネルを上下左右へ分割し、中央へタブ化し、リサイズできる。
- [ ] パネルをメインウィンドウ外へドロップしても独立OSウィンドウを作らない。
- [ ] Layout DirtyとProject Dirtyを別々に識別できる。
- [ ] 未保存レイアウトから別プリセットへ切り替えると、確認なしで変更を破棄する。
- [ ] Edit／Liveを含むレイアウトを作成、上書き、名前変更、複製、削除できる。
- [ ] レイアウトを切り替えてもノード編集権限が変化しない。

## Node Graph

- [ ] 右クリックとTabの両方からノード検索を開き、開いた位置へ追加できる。
- [ ] Node Libraryからダブルクリックまたはドラッグでノードを追加できる。
- [ ] ノード追加、削除、接続、切断、置換をUndo／Redoできる。
- [ ] 非互換接続をドロップ時に拒否し、正常な既存接続を維持できる。
- [ ] 暗黙変換を破線と変換バッジで判別できる。
- [ ] 必須／任意ポートを色なしでも判別できる。
- [ ] Blocked、Faulted、Preparing、UsingFallback、UnknownNodeをノード上で判別できる。

## パラメーターとライブ操作

- [ ] Inspectorで選択ノードの全公開パラメーターを標準UIから編集できる。
- [ ] BaseValueとEffectiveValueを同時に確認でき、EffectiveValueを編集できない。
- [ ] 複数ノードのパラメーターをLive Dashboardへ配置できる。
- [ ] Dashboard PageとWidgetの配置、表示形式、参照を保存して復元できる。
- [ ] Broken Widgetを削除せず表示し、RebindまたはRemoveできる。
- [ ] KeyboardをValueまたはPresetTriggerへLearn Keyで割り当てられる。
- [ ] Min／Max式のDraftがApply成功前に実行値へ反映されない。

## Program／Preview

- [ ] Program Monitorを閉じてもProgram評価と外部Display出力が継続する。
- [ ] Program映像へGUI診断文字が合成されない。
- [ ] PreviewノードのダブルクリックでPreview Viewer Host内の対応タブを開ける。
- [ ] 8個表示中に9個目のPreviewを開く操作を理由付きで拒否する。
- [ ] Hostをレイアウトで非表示にしてもPreviewタブ割り当てを失わず、VJプロジェクトをDirtyにしない。
- [ ] PreviewごとにFit／Fill／StretchとChecker／Blackを保存して復元できる。
- [ ] Previewの状態と自動品質段階を映像上で確認できる。

## Presets／Media

- [ ] プリセットボタンの1回押下で呼び出し要求が発行される。
- [ ] Broken項目を含むプリセットの呼び出しが全体失敗し、部分適用されない。
- [ ] 素材を複数同時にインポートし、コピー、検証、Probeの進捗を確認できる。
- [ ] 素材削除前に参照中ノードとプリセットを確認できる。
- [ ] 素材削除後の参照がBrokenとして残り、別素材へ差し替えられる。

## Diagnostics／Project

- [ ] DiagnosticsをSeverity、ノード、DiagnosticCodeでフィルターできる。
- [ ] 同一継続障害が新規行を増やさず集約件数を更新する。
- [ ] 診断履歴をTextとJSONで書き出せる。
- [ ] Dirtyプロジェクトを閉じる際にSave／Discard／Cancelを選択できる。
- [ ] Save失敗時に既存project.jsonとDirty状態を維持する。
- [ ] `.bak` 復旧時にRecoveredバナーとDirty表示を行い、自動上書きしない。
- [ ] 読込不能プロジェクトを開こうとしても、現在開いているプロジェクトを維持する。

## 視認性と入力

- [ ] 状態とポート型を色だけに依存せず判別できる。
- [ ] キーボードフォーカスが常に視認できる。
- [ ] 100%、125%、150%のUIスケールで文字と操作部品が欠けない。
- [ ] Reduce Motion有効時に不要な遷移と点滅を停止できる。
- [ ] テキスト入力中にNode Graphの単一キーショートカットが発火しない。
