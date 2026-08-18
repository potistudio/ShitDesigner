# VideoPlayer Transport

## 状態

初期パラメーターのID、型、既定値、秒単位、範囲、終端およびSeek表示まで確定。

## 初期パラメーター

| パラメーター | 役割 |
|---|---|
| `transport.media_asset` | 再生するVJプロジェクト内の動画素材 |
| `transport.playing` | 再生または一時停止の状態 |
| `transport.playhead_seconds` | 秒単位の論理再生位置。値の変更をSeekとして扱う |
| `transport.speed` | `GraphClock` に対する再生速度 |
| `transport.loop` | 動画終端へ到達した後に繰り返すか |

## 確定事項

- `VideoPlayer` ノードは、初期版で上記5種類のTransportパラメーターを持つ。
- 各Transportパラメーターはノードインスタンスごとに個別状態を持つ。
- `Playing` が有効な間、`Playhead` は `GraphClock` と `Speed` に基づいて進む。
- `Playing` が無効な間、`GraphClock` が進んでも `Playhead` を進めない。
- `Playhead` の値を変更した場合、対応する動画位置へのSeekとして扱う。
- `Loop` が有効な場合、動画終端へ到達した後は再生範囲の先頭へ戻る。
- TransportパラメーターはVJプロジェクトの保存対象に含め、再読込時に復元する。
- Transportパラメーターは実行中に変更できる。
- `Playing`、`Playhead`、`Speed`、`Loop` は `Value` 論理コントロールから操作できる。
- `MediaAsset` は論理コントロールの直接対象にせず、UIまたはプリセットから変更する。
- `Playhead` は `RuntimeStateful` とし、通常再生中の `EffectiveValue` をGraphClockから計算する。
- `Playhead` に論理コントロール式がある間はScrub Modeとし、自動進行を止める。
- In点、Out点、Reverse専用操作、フレーム送りは初期必須パラメーターに含めない。
- 将来Transportパラメーターを追加する場合は、ノード型の `SchemaVersion` と状態移行を使用する。
- `MediaAsset`、`Playing`、`Playhead`、`Speed`、`Loop` は、それぞれ個別に部分プリセットの対象へ選択できる。
- プリセットに含まれていないTransportパラメーターは、呼出時の現在値を維持する。

## 設計意図

- 通常の動画再生、停止、Seek、速度変更、ループに必要な機能を初期範囲へ含める。
- 高度な編集機能を初期契約へ固定せず、ノード状態移行によって後から追加できるようにする。
- 物理入力デバイスへ直接依存せず、論理コントロール経由でライブ操作できるようにする。
