# 内部アルファ形式

## 状態

内部Premultiplied Alpha統一、素材境界、Program終端での保持およびStraight変換規則まで確定。

## 確定事項

- ノードグラフ内で受け渡す映像はPremultiplied Alphaへ統一する。
- `ImageFrame` 内のRGB値は、Alpha値を乗算済みの値として扱う。
- Alphaが0の画素はRGBも0とする。
- Straight Alphaの外部素材は、ノードグラフへ映像を出力する前にPremultiplied Alphaへ変換する。
- 3D描画、2D描画、シェーダー、動画、加工、合成の各ノードはPremultiplied Alpha形式で映像を出力する。
- 映像入力を使用するノードは、入力がPremultiplied Alphaであることを共通契約として扱う。
- リサイズやフィルタリングはPremultiplied Alphaの値に対して行う。
- ノード間接続でStraight AlphaとPremultiplied Alphaを混在させない。
- Straight Alphaへの変換が必要な外部出力では、ノードグラフの終端側で変換する。
- Program終端のLDR映像データでもPremultiplied Alphaを保持する。
- 通常の画面表示ではProgram映像を黒背景へ合成し、Alphaを保持した元データは変更しない。

## 設計意図

- 半透明映像の合成、補間、リサイズで透明境界に不要な色が現れることを防ぐ。
- 各合成ノードが入力ごとにアルファ形式を判定する必要をなくす。
- 入力と出力の境界へアルファ変換責任をまとめる。
