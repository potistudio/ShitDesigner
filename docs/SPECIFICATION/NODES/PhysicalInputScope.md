# 初期物理入力の範囲

## 状態

初期版で保証する物理入力と、外部プロトコルの扱いは確定。

## 初期対応

- Unity Input Systemを使い、Keyboardだけを初期保証対象とする。
- Key入力を `Value` または `PresetTrigger` へ割り当てできる。
- Keyを `Value` へ割り当てた場合、離している間を `0.0`、押している間を `1.0` とする。
- 物理入力識別子、Control Path、RawMin、RawMax、InvertをControlMappingへ保存する。
- 接続中デバイスが見つからないMappingはBrokenとして保持し、論理コントロール定義を削除しない。
- 別デバイスのControl Pathへ再割り当てしても、LogicalControlIdから先の割り当てを維持する。

## 初期対象外

- Mouse、Gamepad、MIDI、OSC、DMX、NDIおよびSpoutは初期版で実装しない。
- 将来の外部プロトコルは、生値またはButton状態をControlMappingへ供給するAdapterとして追加する。
- Adapter追加で論理コントロール、パラメーターおよびプリセット仕様を変更しない。

## 設計意図

- 既に導入済みのInput SystemでAcceptance Criteriaを検証可能にする。
- 外部プロトコルをノードやパラメーターへ直接依存させない。
- 未接続デバイスで演出側設定を失わないようにする。
