# 登録型3D描画ノード

## 状態

基本登録契約、型ID、カタログ、Factory、SchemaVersionおよび配布方式まで確定。

## 確定事項

- 3D描画ノード型は共通のポートおよびパラメーター契約を持ち、表現の選択はノードインスタンスが参照する `Scene3DDefinition` が担う。
- 各 `Scene3DDefinition` は保存後も変わらない安定したUUIDを持つ。
- 各 `Scene3DDefinition` は専用3D空間を生成するPrefabを持つ。
- 共通3D描画ノード型は、Standaloneアプリから追加可能なノードとして登録する。
- 共通3D描画ノード型をグラフ内へ複数インスタンス配置し、インスタンスごとにDefinitionを選択できる。
- 各インスタンスは専用3D空間とパラメーター状態を個別に持つ。
- VJプロジェクトには、3D描画ノードの型ID、インスタンスID、Definition UUID、パラメーター、ポート状態を保存する。
- VJプロジェクトへPrefabやGameObject階層そのものを直列化しない。
- VJプロジェクト読込時はDefinition UUIDをBootstrapに明示された `Scene3DDefinitionCatalog` で解決し、そのPrefabから専用3D空間を復元する。
- 新しい3D表現を追加しても、共通3Dノード型や既存プロジェクトの保存構造を変更しない。
- 3D描画ノード型は、他の種類と同じ共通 `NodeTypeRegistry` へ登録する。
- 3Dはノード追加UI上のカテゴリであり、専用レジストリを持たない。
- 共通3Dノード型は `SchemaVersion` と段階的な状態移行処理を持つ。Definition固有状態を追加する場合はDefinition側の状態契約を版管理する。

## 設計意図

- 3D表現のUnityアセット差し替えと、グラフの共通ポート契約を分離する。
- Unityオブジェクト階層全体をVJプロジェクトへ保存する複雑さを避ける。
- 同じ表現を複数配置し、それぞれ独立した状態で動かせるようにする。
- 共通型IDとDefinition UUIDによって保存データと実装を安定して対応付ける。
