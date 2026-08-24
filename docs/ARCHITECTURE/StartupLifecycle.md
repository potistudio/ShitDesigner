# 起動ライフサイクル

## 状態

確定。Unity Playerの起動、外部機器接続、通常稼働および終了順を定める。

## 起動順

起動は `ProductionStartupSequence` が次の境界を順番に実行する。

```text
Cold → Preflight → Composing → Handshaking → Activating → Online | Degraded
```

- `Preflight`: シリアライズ済みアセット、NodeTypeCatalog、Shader参照を検証する。機器、GPUリソースおよびRuntime Objectを所有しない。
- `Composing`: Presentation Hostを用意し、`CompositionFactory` がComposition Root、Runtime Sessionおよび既定Projectを生成する。外部機器へは接続しない。
- `Handshaking`: MIDI入力とUnity Displayを検出・接続し、`HandshakeReport` に各機能の状態を記録する。
- `Activating`: Window制約、Presentationおよび `ApplicationLoopDriver` を有効にする。
- `Online`: 通常のPlayer Loopで稼働する。
- `Degraded`: 必須機能は起動済みだが、MIDIまたは外部Displayが利用できない。外部Displayがない場合はProgram Monitorへ縮退する。

必須境界が失敗した場合は後続境界を実行せず `Faulted` へ移る。MIDIと外部Displayは任意機能なので、不在だけでは起動失敗にしない。

`Awake` と外部からCompositionを渡す `Configure` は、どちらも同じ `ProductionStartupSequence` を通る。別の起動経路は持たない。

## 終了順

```text
Online → Draining → Stopping → Teardown → Offline
```

- `Draining`: `ApplicationLoopDriver` を停止し、新しいフレーム入力を遮断する。
- `Stopping`: Composition Rootを破棄し、入力機器、Runtime Session、Display出力を停止する。
- `Teardown`: Runtime用PanelSettingsなどUnity Host所有物を破棄する。

各所有物は生成直後に `Drain`、`Stop`、`Teardown` のいずれかへ解放処理を登録する。正常終了と起動失敗時のロールバックは同じ登録を逆順で実行し、一段階の例外で後続の解放を中止しない。

## 所有規則

- Hostは起動順と終了順だけを所有し、具象Compositionの生成は `CompositionFactory` に委譲する。
- MIDIサービスはデバイス接続とポーリングを所有する。
- Program DisplayサービスはDisplay検出、Cameraおよび映像提示を所有する。
- Composition Rootは依存関係を組み立てるが、Player Loopを直接駆動しない。
- `Poll` とDisplayの `Sync` は、専用Host以外から利用するテスト用Compositionとの互換性のため、未実施のHandshakeを一度だけ補完できる。
