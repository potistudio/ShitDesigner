# 3D／2D Sceneノードの実行時分離

## 状態

3D／2DノードのScene、Physics、描画Layer、Canvas、生成および破棄方式は確定。

## SceneとPhysics

- 各3Dノードは `SceneManager.CreateScene` で専用のAdditive Sceneを生成し、`LocalPhysicsMode.Physics3D` を使用する。
- 各2Dノードも専用Additive Sceneを生成し、`LocalPhysicsMode.Physics2D` を使用する。
- ノードのPrefab、Camera、Light、Renderer、Colliderおよびアニメーション要素は専用Sceneへ移動する。
- 3D／2DのローカルPhysicsはGraphClockの固定ステップ `1/60秒` で手動シミュレーションする。
- フレーム落ち時は1評価フレームに最大4ステップまで追いつき、それを超える遅延は次フレームへ持ち越す。

## 描画分離

- UnityのUser Layer `8～31` をSceneノード専用プールとして使い、アクティブな3D／2Dノードへ1つずつ貸し出す。
- Scene系ノードの同時存在上限は3Dと2Dの合計24個とする。
- Layerが空いていない場合はノード追加を拒否し、理由をUIへ表示する。
- ノード生成時にPrefab配下の全GameObjectを貸し出されたLayerへ設定する。
- CameraのCulling Mask、LightのCulling Maskおよび関連Rendererを同じLayerへ限定する。
- 各CameraはURP Base Cameraとし、Camera Stackを使用しない。
- 描画時は `RenderPipeline.SupportsRenderRequest` を確認し、対応する `StandardRequest` で貸し出しRenderTextureへ出力する。

## 2D Canvas

- 2DノードはSpriteRenderer、World Space Canvas、Screen Space - Camera Canvasを使用できる。
- Screen Space - Overlay CanvasはRenderTexture分離ができないため禁止する。
- CanvasのWorld Cameraはノード専用Cameraへ固定する。
- 2Dの描画順はノード内のSorting Layer／Orderで決める。異なるノード間の描画順はグラフ接続で決める。

## 生成と破棄

- ノードFactoryはカタログ内のPrefabを専用Sceneへインスタンス化し、必要コンポーネントとLayerを検証する。
- ノード削除は評価フレーム境界で実行対象から外し、Cameraを停止し、出力Leaseを返却してから専用Sceneをアンロードする。
- Sceneアンロード完了後にLayerをプールへ返す。
- 生成または破棄途中のノードは `Preparing` とし、評価しない。

## 描画順序

- 複数3D／2DノードのCamera描画順に映像上の意味を持たせない。
- グラフの依存順で各ノードを評価し、合成結果だけを下流へ渡す。

## 設計意図

- GameObject、Light、CameraおよびPhysicsの影響をノード外へ漏らさない。
- Unityの有限Layer数を隠さず、生成時に検証して不正状態を防ぐ。
- Camera StackやOverlay Canvasによる暗黙の共有を避ける。
