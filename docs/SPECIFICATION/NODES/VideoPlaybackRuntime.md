# VideoPlayerノードのデコードと出力

## 状態

ノード型ID、Unity Video Backend、Hap Backend、GraphClock同期、デコードTexture、拡縮、Alphaおよび失敗診断は確定。

## ノード型

- NodeTypeIdは `shitdesigner.video.player` とする。
- 主出力はPort ID `image` の `ImageFrame` とし、追加出力は初期版で持たない。
- 映像入力ポートは持たない。

## デコード

- Codec Probeによって `UnityVideoBackend` または `HapVideoBackend` を選択する。
- H.264／VP8ではUnity `VideoPlayer` を `VideoRenderMode.APIOnly` で使用し、`VideoPlayer.texture` からデコード済みTextureを取得する。
- Hap／Hap Alpha／Hap Q／Hap Q Alphaでは専用 `HapVideoBackend` を使用する。
- Audio Output ModeはNoneとする。
- URLにはプロジェクト内素材の検証済み絶対パスを実行時だけ組み立てて渡す。保存は相対参照のままとする。
- Unity Backendは `Prepare()` と `prepareCompleted`、`seekCompleted`、`errorReceived` を状態遷移へ使用する。
- Hap Backendも同じPrepare／Seek／Frame Ready／Errorの管理契約を実装する。
- 評価対象外ではTexture転送を止める。再び要求されたときは現在のGraphClock位置へSeekする。

## 出力変換

- デコードTextureを要求解像度のプールTextureへBilinearでBlitする。
- 素材アスペクト比を維持する `Fit` を使用し、余白は透明黒とする。
- 色メタデータまたは既定Rec.709からLinearへ変換する。
- 不透明動画はAlpha 1、VP8 WebMのAlphaはStraightとして受け、Premultiplyして内部形式へ書く。
- Hap AlphaとHap Q Alphaは専用BackendでColor／Alpha Planeを合成してからPremultiplyする。
- HDR動画入力は初期保証対象外とし、LDR素材として解釈してHDR内部バッファへ展開する。

## 診断

- 診断にはMediaAssetId、相対パス、コンテナ／Codec、要求時刻、VideoPlayerのエラーメッセージを含める。
- ファイル欠落、Probe不一致、Prepare失敗、Seek失敗およびデコード失敗はFaultedとする。
- 正常なPrepareとSeekはPreparingとする。

## 設計意図

- Codec別Backendと、ノード共通の解像度・色・Alpha変換を分離する。
- デコード元解像度にかかわらず下流要求を満たす。
- 音声やHDR動画など初期要件外の機能を暗黙に扱わない。
