# RenderTextureプール

## 状態

確定。

## 役割

ノードグラフで使用するRenderTextureの生成、貸し出し、回収、再利用、破棄を一元管理する。

## 確定事項

- RenderTextureは、ノードグラフ側の共通プールが所有する。
- ノードは必要な条件を指定し、共通プールからRenderTextureを借りて使用する。
- ノードは借りたRenderTextureを直接破棄しない。
- 接続先ノードは、入力された `ImageFrame` 内のRenderTextureを借用参照として扱う。
- 接続先ノードは、入力されたRenderTextureを回収または破棄しない。
- RenderTextureの生成、再利用、回収、破棄は共通プールが担当する。
- VRAM使用量は共通プールを通して一元的に管理できるようにする。
- RenderTextureは映像ノードの出力ポート単位で貸し出す。
- 貸し出しは複数フレームにわたって継続し、ノード削除、出力仕様変更、またはプロジェクト終了時に返却する。
- 伝播された要求解像度が変わった場合、出力ポートの貸し出しを新しい条件に合うRenderTextureへ切り替える。
- プロジェクトの内部HDR／LDR設定に応じて、`R16G16B16A16_SFloat` または `R8G8B8A8_UNorm` のRenderTextureを貸し出す。
- 内部形式が変更された場合、出力ポートの貸し出しを新しい形式へ切り替える。
- 切り替え時は新しいTextureを先に確保し、正常出力への差し替え後に旧Textureを返却する。
- プールのVRAM予算、LRU破棄および確保失敗時のFaulted規則は `RenderTexturePoolPolicy.md` へ従う。

## 設計意図

- RenderTextureの寿命管理を各ノードへ分散させない。
- 同じ条件のRenderTextureを再利用し、不要な生成と破棄を減らす。
- ノードの削除や接続変更が実行中に起きても、RenderTextureの回収責任を明確にする。
- 将来、VRAM使用量の計測や上限管理を共通箇所へ追加できるようにする。
