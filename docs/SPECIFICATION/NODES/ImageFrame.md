# ImageFrame

## 状態

確定。

## 役割

`ImageFrame` は、ノード間で映像とその付随情報を受け渡すための共通値である。

## 確定事項

- `ImageFrame` は映像実体として `RenderTexture` を保持する。
- `ImageFrame` はRenderTextureの解像度情報を保持する。
- `ImageFrame` はRenderTextureの形式情報を保持する。
- `ImageFrame` は、その映像がどの評価フレームで生成されたかを示すフレーム番号を保持する。
- `ImageFrame` は、RenderTextureの所有者である共通プールと貸出状態を判断するための所有権情報を保持する。
- 映像ノードの主出力ポート `Image` は、RenderTextureを直接ではなく `ImageFrame` として受け渡す。
- `ImageFrame` で包んでも、共通映像出力の実体がRenderTextureであるという要求は維持する。
- `ImageFrame` を受け取ったノードは、内部のRenderTextureを借用参照として扱い、直接回収または破棄しない。
- `ImageFrame` を受け取ったノードは、共有された入力RenderTextureへ書き込まない。
- 出力ポートは同じRenderTextureを複数フレームにわたって利用できるが、`ImageFrame` のフレーム番号は映像を生成した評価フレームに合わせて更新する。
- 同一フレーム内で生成済みのノード出力を識別し、複数の接続先で評価結果を共有するためにフレーム番号を使用する。
- 出力ターゲットから伝播された要求に基づいて実際に生成した幅と高さを、解像度情報として保持する。
- 映像実体の色空間はノードグラフ内でLinearへ統一する。
- 形式情報として `R16G16B16A16_SFloat` または `R8G8B8A8_UNorm` のGraphicsFormatを保持する。
- 映像実体のアルファ形式はPremultiplied Alphaへ統一する。
- Alphaが0の画素はRGBも0とする。

## 設計意図

- 映像と、その映像を正しく扱うための情報を同じ接続で渡す。
- 解像度や形式の伝播規則を、ノードごとの暗黙的な判断にしない。
- RenderTextureの再利用や破棄を実装するとき、所有権を追跡できるようにする。
- フレーム番号により、未更新の出力や同一フレーム内の結果を識別できるようにする。
