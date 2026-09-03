# Program出力のAlpha

## 状態

確定。

## 確定事項

- Program終端のLDR映像データはPremultiplied Alphaを保持する。
- Program終端でAlphaを常に1へ固定しない。
- HDRからLDRへトーンマッピングする場合もAlpha値を保持する。
- トーンマッピング後のRGBもPremultiplied Alphaの契約を満たす。
- Alphaが0の出力画素はRGBも0とする。
- 通常の画面またはディスプレイへProgram映像を表示するときは、黒背景へ合成して不透明映像として提示する。
- 黒背景への合成は画面提示時だけ行い、Alphaを保持するProgram映像データ自体は変更しない。
- 将来Alpha対応の外部出力を追加する場合は、保持したProgramのAlphaを利用できる。

## 設計意図

- 初期の画面表示を単純な不透明映像として提供する。
- 将来のAlpha対応出力で、グラフやProgram出力契約の変更を不要にする。
- 内部の多段合成で生成したAlpha情報を、Program終端まで失わないようにする。
