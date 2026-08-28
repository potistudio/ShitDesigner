# VideoPlayer Transport契約

## 状態

初期Transportの型、ID、既定値、単位、範囲、終端およびSeek中表示は確定。

## パラメーター

| ParameterId | 型 | 既定値 | 規則 |
|---|---|---:|---|
| `transport.media_asset` | `MediaAssetReference` | null | Video素材だけを許可 |
| `transport.playing` | `Bool` | `true` | 再生／一時停止 |
| `transport.playhead_seconds` | `Float` | `0.0` | 秒、最小0、素材長で動的クランプ |
| `transport.speed` | `Float` | `1.0` | `0.0～4.0` |
| `transport.loop` | `Bool` | `true` | 終端ループ |

- 負のSpeedと逆再生は初期版で許可しない。
- `Speed = 0` は再生状態を保ったまま論理位置を停止する。
- Loop無効で終端へ到達した場合はPlayheadを素材終端へ固定し、`Playing` を `false` にする。
- 素材変更時はPlayheadを `0.0` へ戻し、Playingの値は維持する。

## Playhead状態

- Playheadの `BaseValue` はUI、Value論理コントロールまたはプリセットから要求されたSeek位置を表す。
- 再生中の `EffectiveValue` はGraphClockから計算した現在の論理位置を表示する。
- ノード内部の時間進行はユーザー編集としてDirtyイベントを毎フレーム発生させない。
- プロジェクト保存時は、その時点の現在論理位置を保存Playheadとしてスナップショットする。

## Seekと準備

- Seek開始から対象フレーム取得までは `Preparing` とする。
- 既に正常フレームがあればSeek完了までそのフレームを保持する。
- 初回準備で正常フレームがない場合は透明黒を出力終端の保持規則へ渡す。
- Seek失敗はFaultedとし、最後の正常フレームを保持する。

## 設計意図

- 秒単位で素材長と現在位置を理解しやすくする。
- 毎フレームの再生進行でプロジェクトDirty状態を発生させない。
- 非同期待ちを黒フレームや障害として扱わない。
