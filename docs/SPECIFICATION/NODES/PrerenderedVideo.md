# プリレンダ動画ノード

## 状態

確定。`docs/SPECIFICATION/REQUIREMENTS.md` および関連する個別仕様に基づく初期版のプリレンダ動画ノード仕様とする。

## 確定事項

- プリレンダ動画を再生して映像を生成するノードである。
- 必須の主出力ポート `Image` から、映像実体の `RenderTexture` を含む `ImageFrame` を出力する。
- `Image` に加えて、任意の名前付き出力ポートを定義できる。
- 3D描画、2D描画、シェーダーの各ノードと同じノードグラフへ配置できる。
- 出力は同一グラフ内で加工・合成できる。
- Standaloneアプリの実行中に扱えるノードである。
- 外部動画はVJプロジェクト内へコピーし、プロジェクト相対パスで参照する。
- VJプロジェクトフォルダーを別PCへコピーした後も、同梱された動画を読み込めることを前提とする。
- 新しいノード種別を追加するとき、既存グラフや保存形式の全面変更を要求しない拡張構造へ従う。
- 動画素材をLinear色空間へ変換してから `ImageFrame` として出力する。
- 動画素材をPremultiplied Alphaへ変換してから `ImageFrame` として出力する。
- 共通 `NodeTypeRegistry` へ動画カテゴリのノード型として登録する。
- プリレンダ動画は、汎用の登録済み `VideoPlayer` ノード型1つで扱う。
- 各インスタンスは、VJプロジェクト内の `MediaAsset` をパラメーターとして選択する。
- 同じ `VideoPlayer` ノード型を複数配置でき、選択素材と再生状態をインスタンスごとに保持する。
- コーデック、コンテナ、動画ファイルごとに別のノード型を登録しない。
- 選択素材が見つからない、またはデコードできない場合は `Faulted` 状態とする。
- 再生位置は共通 `GraphClock` を基準に計算する。
- 評価対象外ではデコードを止めるが、再生中の論理位置は進め、再評価時に現在位置へ追いつく。
- 初期Transportパラメーターとして `MediaAsset`、`Playing`、`Playhead`、`Speed`、`Loop` を持つ。

## 要求への対応

- `docs/SPECIFICATION/REQUIREMENTS.md` Acceptance Criteria: 「プリレンダ動画ノードがRenderTextureを出力できる」
- `docs/SPECIFICATION/REQUIREMENTS.md` Acceptance Criteria: 外部素材をプロジェクト内へコピーし、元ファイルを参照せず読み込める
- `docs/SPECIFICATION/REQUIREMENTS.md` Acceptance Criteria: 4種類のノードを同一グラフ内で接続し、加工・合成できる
