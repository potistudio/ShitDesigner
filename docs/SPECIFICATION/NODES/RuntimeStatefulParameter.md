# ランタイム状態パラメーター

## 状態

GraphClockなどから時間変化するパラメーターを、保存用BaseValueと実行時EffectiveValueへ分ける方式は確定。

## 宣言

- `ParameterDefinition` は `Standard` または `RuntimeStateful` のValue Modeを持つ。
- 既定は `Standard` とする。
- `RuntimeStateful` はノード型が明示したパラメーターだけに使用できる。
- 初期版では `transport.playhead_seconds` だけを `RuntimeStateful` とする。

## 値

- `BaseValue` はUI、プリセットまたはValue論理コントロールが指定した基準値として保存する。
- `EffectiveValue` はフレーム境界で、基準値、GraphClockスナップショットおよびノードの保存済みランタイム状態から純粋計算する。
- EffectiveValueの時間進行をBaseValueへ毎フレーム書き戻さず、プロジェクトDirtyイベントも発生させない。
- プロジェクト保存時は、その瞬間のEffectiveValueを次回読込用BaseValueとしてスナップショットする。

## Playhead

- 論理コントロール合成式がない再生中Playheadは、GraphClockからEffectiveValueを進める。
- UIまたはプリセットでBaseValueが変わった場合はSeekし、その位置でGraphClock Anchorを更新する。
- PlayheadへValue論理コントロール合成式がある間は、式結果を直接EffectiveValueとして扱うScrub Modeにし、GraphClockによる自動進行を止める。
- 合成式を解除した場合は、最後のEffectiveValueを新しいAnchorとして通常再生へ戻る。

## 制約

- RuntimeStateful計算はGPU処理、非同期処理および他ノード評価を実行しない。
- 計算例外はパラメーター検証失敗としてノードをFaultedにする。
- カスタムUIはBaseValueとEffectiveValueを通常どおり併記する。

## 設計意図

- 動画再生位置の進行だけでプロジェクトを毎フレームDirtyにしない。
- 保存位置、操作位置および現在位置を既存のBase／Effective表示へ統合する。
- 論理コントロールによるPlayhead操作を明示的なScrubとして扱う。
