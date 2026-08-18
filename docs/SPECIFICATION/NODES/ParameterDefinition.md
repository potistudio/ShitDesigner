# ParameterDefinition

## 状態

共通パラメーター契約、初期対応型、ID、メタデータ、更新API、標準UIおよびカスタムUIまで確定。

## 役割

ノードの設定値、ライブ操作値、任意入力の既定値を、保存、プリセット、論理コントロールから共通方式で扱う。

## 確定事項

- ノード型は、公開する各パラメーターを `ParameterDefinition` として定義する。
- 各パラメーターは、同じノード型内で安定した `ParameterId` を持つ。
- 各定義は値評価方式として、既定の `Standard` または明示的な `RuntimeStateful` を持つ。
- `ParameterDefinition` は、少なくとも `ParameterId`、表示名、値の型、既定値を定義する。
- 範囲を持つパラメーターでは、`ParameterDefinition` が有効値の最小値と最大値をハード範囲として定義する。
- 各ノードインスタンスは、`ParameterDefinition` に対応する保存対象の `BaseValue` と、ノード評価に使用する `EffectiveValue` を個別に持つ。
- 通常パラメーターと任意入力の既定値は、同じ `ParameterDefinition` 契約を使用する。
- 任意入力ポートは、既定値を提供する `ParameterId` を参照する。
- 任意入力が未接続の場合、その入力の実効値として参照先パラメーターの `EffectiveValue` を使用する。
- 任意入力が接続されている場合、接続値を実効値として使用し、背後のパラメーター値は保持する。
- 接続中に背後のパラメーター値が変更されても接続値を優先し、切断後は保持されていたパラメーター値へ戻る。
- パラメーターの `BaseValue` はVJプロジェクトの保存対象に含め、派生値である `EffectiveValue` は保存しない。
- 部分プリセットは `NodeInstanceId` と `ParameterId` で対象パラメーターを参照する。
- 名前付き論理コントロールは `NodeInstanceId` と `ParameterId` で操作対象を参照する。
- ノード型のパラメーター構造を変更する場合は、ノード型の `SchemaVersion` と状態移行を使用する。
- 初期対応型は `Float`、`Int`、`Bool`、`Vector2`、`Vector3`、`Vector4`、`Color`、`String`、`Enum`、`MediaAssetReference` とする。
- `ImageFrame` と任意のJSONオブジェクトはパラメーター値として扱わない。
- 型とメタデータからStandalone用の標準パラメーターUIを生成する。
- ノード型は必要な場合だけカスタムUIを提供でき、カスタムUIも共通パラメーターAPIを使用する。
- カスタムUIがない、または利用できない場合は標準UIへフォールバックする。
- 1つの名前付き論理コントロールから複数のパラメーターへ割り当てできる。
- 論理コントロールによる変更も共通パラメーターAPIを使用する。
- 1つのパラメーターへ複数の論理コントロールを割り当てでき、値を `AND / Min` または `OR / Max` で合成する。
- 論理コントロール同士の値を相互同期しない。
- 論理コントロールの合成式は `BaseValue` を参照する `Base` 葉を明示的に配置できる。
- 合成式がない場合は `EffectiveValue` を `BaseValue` と同じ値にし、合成式がある場合は式の結果を `EffectiveValue` とする。
- パラメーターUIは `BaseValue` を編集欄、`EffectiveValue` を読み取り専用モニターとして同時に表示する。
- `BaseValue` と `EffectiveValue` は `ParameterDefinition` のハード範囲を常に守る。
- ハード範囲外の入力値は拒否せず、最も近い境界値へクランプしてから `BaseValue` または `EffectiveValue` として扱う。
- 論理コントロール合成式は、ハード範囲の内側へ任意の狭い出力範囲を設定できる。
- VectorおよびColorのハード最小値とハード最大値は成分ごとに定義する。
- 論理コントロールの直接ターゲットにできるのは `Bool`、`Float`、`Int`、`Vector2`、`Vector3`、`Vector4`、`Color` とする。
- `String`、`Enum`、`MediaAssetReference` は論理コントロールの直接ターゲットにせず、UIまたはプリセットから変更する。
- 初期版の `RuntimeStateful` は動画の `transport.playhead_seconds` だけとする。

## 設計意図

- ノード設定、入力既定値、プリセット、ライブ操作で値の保存と検証処理を共有する。
- 任意入力を接続しても、切断後に戻る既定値を失わないようにする。
- 表示名を変更しても、安定した `ParameterId` によって保存データと操作マッピングを維持する。
