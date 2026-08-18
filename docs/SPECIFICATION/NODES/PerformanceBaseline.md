# FHD・60fps性能試験基準

## 状態

初期版の基準PC、テスト条件および合格判定は確定。

## Windows基準PC

- OS: Windows 10 64bit
- CPU: AMD Ryzen 5 5600X
- GPU: NVIDIA GeForce RTX 3060 12GB
- Memory: 32GB
- Graphics API: Direct3D 12を主基準、Vulkanを追加検証

## macOS基準PC

- Device: MacBook Pro
- SoC: Apple M4
- Unified Memory: 16GB
- Graphics API: Metal
- macOSの具体的なVersionは試験結果へ記録する。
- Display: 1920×1080、60Hz

## 基準シナリオ

- 3D Generator、2D Generator、Shader Effect、VideoPlayer、2入力合成Shader、Feedback、ProgramOutputを1本のProgram経路へ含める。
- 640×360・30fpsのPreviewを2つ同時表示する。
- H.264 1920×1080・60fps動画を1本再生する試験と、Hap 1920×1080・60fps動画を1本再生する試験を分けて行う。
- 論理コントロールを毎秒120更新、PresetTriggerを10秒に1回発火する。
- 10分間連続実行する。

## 合格条件

- Windows基準PCとmacOS基準PCの両方で、Program内部解像度が全期間1920×1080である。
- 各Codec試験の10分区間で、99%以上のフレームが16.67ms以内に提示される。
- 連続3フレーム以上のProgram欠落がない。
- Faulted、VRAM予算超過、未回収Sceneおよび未回収Textureがない。
- Preview品質低下は許可するがProgram品質低下は許可しない。

## 設計意図

- 「FHD・60fps」を再現可能なシーンと測定条件へ落とす。
- 単一ノードだけでなく、要求された4表現と合成を同時に検証する。
- 基準PC変更時もテストシナリオを維持できるようにする。
