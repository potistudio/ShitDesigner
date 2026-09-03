# ImageFrame実装契約

## 状態

`ImageFrame` のC#表現、フィールド、所有権および映像未生成状態の表現は確定。

## C#表現

- `ImageFrame` は `readonly struct` とする。
- フィールドは `RenderTexture Texture`、`Vector2Int Size`、`GraphicsFormat ColorFormat`、`ulong FrameNumber`、`OutputLeaseId LeaseId` とする。
- `Texture`、幅、高さおよび形式が有効なフレームだけを `ImageFrame` として生成する。
- `ImageFrame` のコピーはRenderTexture所有権を移動しない。
- `LeaseId` は診断用であり、受信ノードへ返却または破棄権限を与えない。

## 未生成状態

- 映像未生成、Blocked、FaultedおよびPreparingを、null Textureを持つ `ImageFrame` で表さない。
- ノード出力は `Available(ImageFrame)`、`Blocked(Diagnostic)`、`Faulted(Diagnostic)`、`Preparing(Diagnostic)` の判別可能な `NodeOutputResult` を返す。
- 下流は `Available` の場合だけ `ImageFrame` を参照する。

## フィールド規則

- `Size.x` と `Size.y` は1以上とし、実際のTexture寸法と一致させる。
- `ColorFormat` はTextureのgraphicsFormatと一致させる。
- `FrameNumber` はグラフ評価フレーム番号を使用する。
- `ImageFrame` 自体に色空間やAlphaモードの可変フラグを持たせず、プロジェクト共通のLinear／Premultiplied契約へ固定する。

## 設計意図

- 無効なTextureを通常フレームとして渡す状態を作らない。
- 値型コピーとGPUリソース所有権を分離する。
- 下流の状態分岐を明示的なResult型で強制する。
