# VideoPlayerノード

## 状態

基本登録契約、GraphClock同期、Transport、API Onlyデコード、対応形式、拡縮および診断まで確定。

## 役割

VJプロジェクトへ取り込まれたプリレンダ動画を再生し、共通の `ImageFrame` として出力する。

## 確定事項

- プリレンダ動画は、汎用の `VideoPlayer` ノード型1つで扱う。
- `VideoPlayer` ノード型は、安定した型IDを持つ登録済みノード型とする。
- `VideoPlayer` ノード型は、共通 `NodeTypeRegistry` へ動画カテゴリとして登録する。
- 各 `VideoPlayer` インスタンスは、再生する `MediaAsset` をパラメーターとして選択する。
- `MediaAsset` はVJプロジェクト内へコピーされた動画ファイルとプロジェクト相対パスを管理する。
- ノードはOS上の元ファイルの絶対パスを保存または参照しない。
- 同じ `VideoPlayer` ノード型をグラフ内へ複数配置できる。
- 各インスタンスは、選択素材、再生位置、再生状態、公開パラメーターを個別に持つ。
- VJプロジェクトには、ノード型ID、インスタンスID、`SchemaVersion`、選択した `MediaAsset` の参照、再生状態、パラメーターを保存する。
- 動画ファイル本体はノード状態へ埋め込まず、VJプロジェクトの素材として保持する。
- 主出力ポート `Image` から、デコードした映像を含む `ImageFrame` を出力する。
- デコードした映像は、内部Linear色空間、プロジェクトのHDR／LDR形式、Premultiplied Alphaへ変換する。
- 選択した `MediaAsset` が見つからない、またはデコードできない場合は `Faulted` 状態とする。
- コーデック、コンテナ、動画ファイルごとに別のノード型を登録しない。
- H.264／VP8はUnity Video Backend、Hap Familyは専用Hap Backendへ内部的に振り分ける。
- 再生位置は共通 `GraphClock` とインスタンスの再生状態から計算する。
- 逆引き評価の対象外ではデコードとRenderTexture更新を止めるが、再生中の論理位置は進める。
- 再び評価対象になったとき、現在の論理位置に対応するフレームを取得する。
- 初期Transportパラメーターとして `MediaAsset`、`Playing`、`Playhead`、`Speed`、`Loop` を持つ。
- Transportパラメーターは実行中に変更できる。
- `Playing`、`Playhead`、`Speed`、`Loop` は `Value` 論理コントロールから操作でき、`MediaAsset` はUIまたはプリセットから変更する。
- Transport状態はVJプロジェクトへ保存し、再読込時に復元する。

## 設計意図

- 動画素材を追加するたびに新しいノード型やアプリビルドを必要としないようにする。
- 再生ロジックと素材ファイルを分離し、同じ素材を複数の再生状態で利用できるようにする。
- VJプロジェクトフォルダーを別PCへ移動しても、素材参照を維持できるようにする。
