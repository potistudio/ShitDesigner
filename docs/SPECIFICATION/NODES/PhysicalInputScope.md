# 物理入力の範囲

## 状態

保証する物理入力と、外部プロトコルの扱いは確定。

## 対応入力

- KeyboardはUnity Input System、Windows MIDI入力はWinMMを使用する。
- Key入力を `Value` または `PresetTrigger` へ割り当てできる。
- Keyを `Value` へ割り当てた場合、離している間を `0.0`、押している間を `1.0` とする。
- MIDIのNote、Control ChangeおよびPitch Bendを `Value` または `PresetTrigger` へ割り当てできる。
- MIDI Learnはデバイス名、メッセージ種別、チャンネルおよび番号を保存する。Note／Control Changeは0～127、Pitch Bendは0～16383を0～1へ正規化する。
- Windows MIDI入力デバイス0を既定で開き、ネイティブコールバックの入力をメインスレッドのフレーム先頭で処理する。
- シーン上の `MidiInputManager` はInspectorでDevice IDとBinding一覧を保持し、各Bindingを実行中のLive Controlへ直接入力できる。Bindingがない入力はProjectのMIDI Learn／ControlMappingへ渡す。
- `PatchDefinition` のMIDI Inputsは、Note／Control Change／Pitch Bendをパッチの公開パラメーターへ割り当てる。メッセージ種別、チャンネル、番号、Raw Minimum、Raw Maximum、Invertおよび公開パラメーターIDをパッチごとに設定し、ロード中パッチの一致する入力だけを `SetParameter` 要求へ変換する。
- 物理入力識別子、Control Path、RawMin、RawMax、InvertをControlMappingへ保存する。
- 接続中デバイスが見つからないMappingはBrokenとして保持し、論理コントロール定義を削除しない。
- 別デバイスのControl Pathへ再割り当てしても、LogicalControlIdから先の割り当てを維持する。

## 初期対象外

- Mouse、Gamepad、OSC、DMX、NDIおよびSpoutは初期版で実装しない。
- 将来の外部プロトコルは、生値またはButton状態をControlMappingへ供給するAdapterとして追加する。
- Adapter追加で論理コントロール、パラメーターおよびプリセット仕様を変更しない。

## 設計意図

- 既に導入済みのInput SystemでAcceptance Criteriaを検証可能にする。
- 外部プロトコルをノードやパラメーターへ直接依存させない。
- 未接続デバイスで演出側設定を失わないようにする。
