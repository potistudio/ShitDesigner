# RenderTextureプール運用

## 状態

プール範囲、同一条件、VRAM予算、未使用破棄および仕様切り替え方式は確定。

## 所有範囲

- Standaloneアプリ内に1つの共通RenderTextureプールを持ち、開いているVJプロジェクトのグラフで共有する。
- 複数プロジェクトを同時に開く機能は初期版で提供しない。

## 同一条件

- 幅、高さ、`GraphicsFormat`、Depth／Stencil形式、MSAAサンプル数、MipMap有無、RandomWrite、Texture Dimension、Volume Depthが一致するTextureを再利用候補とする。
- 内部Color TextureはsRGBフラグを無効にし、Linearデータとして扱う。
- 3D／2Dノードが必要とするDepth Textureも同じプールが別Leaseとして管理する。

## VRAM予算

- Texture記述子から推定したバイト数で予算を管理する。
- 専用GPUの既定予算は `min(VRAMの50%, VRAM - 1.5GiB)` とする。
- 上記で512MiBを確保できないGPUは初期動作対象外とし、起動診断を表示する。
- Apple SiliconなどUnified Memory環境の既定予算は、物理Memoryの25%または4GiBの小さい方とする。
- WindowsでVRAM容量を取得できない場合は2GiB、macOSで共有Memory量を取得できない場合は2GiBを使う。
- 使用量が予算の85%へ到達した時点で警告し、ユーザーが確保失敗前に調整できるようにする。
- ユーザーは設定画面で予算を変更できるが、既存Leaseを下回る値や、専用GPUの `VRAM - 1GiB`、Unified Memoryの40%を超える値にはできない。
- 予算到達時は未貸出のLRU Textureを先に破棄する。
- それでも確保できない場合は新規Leaseを失敗させ、要求ノードをFaultedにする。Programは最後の正常フレームを維持する。

## 寿命

- 出力ポートのLeaseは初回要求時に取得し、ノード削除、出力仕様変更またはプロジェクト終了まで維持する。
- ノード無効化、接続切断またはPreview非表示だけでは取得済みLeaseを返却しない。
- 返却後10秒間使われなかったTextureはLRU順で破棄できる。

## 仕様切り替え

- 解像度または形式変更は評価フレーム境界で行う。
- 新しいTextureを先に確保し、最初の正常フレーム生成に成功してからLeaseを原子的に差し替える。
- 差し替え完了まで旧Textureと旧フレームを有効に保つ。
- 新規確保または描画に失敗した場合は旧Leaseを維持し、診断を表示する。

## 設計意図

- 描画中のTextureを回収せず、切り替え時の黒フレームを防ぐ。
- VRAM不足をOS任せのクラッシュではなく、予測可能なノード障害として扱う。
- 仕様が同じTextureだけを安全に再利用する。
