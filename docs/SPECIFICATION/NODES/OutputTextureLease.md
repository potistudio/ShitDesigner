# 出力RenderTextureの貸出期間

## 状態

確定。

## 確定事項

- 共通プールは、映像ノードの出力ポート単位でRenderTextureを貸し出す。
- 貸し出されたRenderTextureは、複数の評価フレームにわたって同じ出力ポートが継続利用する。
- 毎フレームの評価では、原則としてRenderTextureの新規生成、返却、再貸し出しを行わない。
- ノードが削除されたとき、そのノードの出力ポートに貸し出されたRenderTextureをプールへ返却する。
- 出力ポートの解像度または形式が変更されたときは新しいRenderTextureを先に確保し、正常フレーム生成後に原子的に差し替えてから旧RenderTextureを返却する。
- 新しいRenderTextureの確保または初回描画に失敗した場合は旧RenderTextureを維持する。
- 出力ポートの初回Leaseは、その出力がProgramまたは表示中Previewから初めて要求された評価フレーム境界で取得する。
- 接続されていない、または有効な出力から到達しない出力ポートには初回Leaseを割り当てない。
- 一度取得したLeaseはノードが評価対象外または無効になっても維持する。
- VJプロジェクトを閉じるとき、プロジェクトの出力ポートへ貸し出されたRenderTextureをプールへ返却する。
- 同じRenderTextureを継続利用しても、`ImageFrame` のフレーム番号は映像が生成された評価フレームに合わせて更新する。

## 設計意図

- 毎フレームのRenderTexture生成と破棄を避ける。
- CameraやVideoPlayerなど、継続した出力先を必要とするUnity機能へ安定したRenderTextureを提供する。
- RenderTextureの参照は安定させつつ、`ImageFrame` によって映像の更新状態を識別できるようにする。
