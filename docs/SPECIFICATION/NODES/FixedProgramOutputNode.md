# 固定ProgramOutputノード

## 状態

確定。

## 確定事項

- ノードグラフには、システムが所有する `ProgramOutput` ノードを必ず1つ配置する。
- `ProgramOutput` ノードはProgram映像を受け取る必須の `ImageFrame` 入力を持つ。
- `ProgramOutput` ノードは映像をグラフ外のProgram出力へ渡す終端ノードであり、出力ポートを持たない。
- ユーザーは `ProgramOutput` ノードを削除できない。
- ユーザーは `ProgramOutput` ノードを複製または追加できない。
- グラフ内に存在できる `ProgramOutput` ノードは1つだけとする。
- `ProgramOutput` ノードの入力接続は、Standaloneアプリの実行中に接続、切断、置換できる。
- `ProgramOutput` ノードの入力接続はVJプロジェクトの保存対象に含め、再読込時に復元する。
- 入力が未接続、または上流から有効な映像を取得できない場合は、Program出力のフォールバック規則を適用する。
- 型IDは `system.program_output`、入力Port IDは `image`、表示名は `Image` とする。
- ノード位置は移動可能とし、新規プロジェクトではグラフ右側中央へ配置する。
- 読込データに存在しない場合は自動作成し、複数ある場合は最初の1つを採用して余分なものを `UnknownNode` へ変換する。

## 設計意図

- Program映像の出口をグラフ内で明示する。
- 出力先を失う削除操作や、複数Program出力による初期仕様外の状態を防ぐ。
- Programへの接続経路は、通常のノード接続として編集および保存できるようにする。
