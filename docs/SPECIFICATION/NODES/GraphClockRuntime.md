# GraphClock実行仕様

## 状態

時刻源、精度、フォーカス喪失、フレーム落ち、初期時刻および動画同期方式は確定。

## 時刻

- GraphClockは秒単位の `System.Double` とする。
- 時刻源は `Time.realtimeSinceStartupAsDouble` の差分を使用する。
- プロジェクト読込時のGraphClock時刻を `0.0` とする。
- `Time.timeScale` の影響を受けない。
- Standaloneは `runInBackground` を有効にし、フォーカス喪失中も進める。
- OSサスペンドまたは大きなフレーム落ちの後は、経過した単調時刻へ追いつく。
- UIの一時停止操作だけがGraphClockの進行を停止する。

## 動画同期

- 各VideoPlayerノードは再生開始時のGraphClock時刻とPlayheadをAnchorとして保持する。
- 論理再生位置は `AnchorPlayhead + (GraphClock - AnchorClock) * Speed` で求める。
- Unity VideoPlayerが対応する場合は `VideoTimeReference.ExternalTime` と `externalReferenceTime` へ論理位置を渡す。
- 内部VideoPlayerの時計をGraphClockの正としない。
- デコード済みフレームが論理位置と一致しない場合は最も近いPresentation Timestampを選び、同距離なら過去側を選ぶ。
- フレーム落ち時は古い全フレームを順番に表示せず、現在の論理位置へ追いつく。

## 将来同期

- 外部タイムコードはGraphClockの時刻供給実装を差し替える拡張点とし、ノード側の計算契約は変更しない。
- 初期版では外部タイムコード入力を実装しない。

## 設計意図

- 表示されていない動画も論理時刻だけは一貫して進める。
- Unityの内部再生時計とグラフ評価のずれをGraphClock側で補正する。
- フォーカス喪失やフレーム落ちで演出全体の時間関係を崩さない。
