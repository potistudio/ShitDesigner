# 2D描画ノードの空間分離

## 状態

Additive Scene、ローカル2D Physics、専用Unity LayerおよびCamera対象Canvasを使う具体的な分離実装まで確定。

## 確定事項

- 各2D描画ノードは、他の2D描画ノードから独立した2D空間を所有する。
- 各2D描画ノードは、専用のルート、Camera、SpriteまたはCanvas要素を持つ。
- 各2D描画ノードの描画順、アニメーション、表示状態は、そのノードが所有する2D空間だけへ影響する。
- 他の2D描画ノードが所有するSprite、Canvas要素、Cameraを暗黙的に共有しない。
- 1つの2D描画ノードを追加、削除、複製しても、別の2D描画ノードが所有する空間を変更しない。
- 2D描画ノードは、自身のCameraで自身の空間を描画し、出力ポートへ貸し出されたRenderTextureへ結果を書き込む。
- 2D空間の状態と公開パラメーターは、ノードインスタンスごとに分離する。
- 単一Spriteだけでなく、複数要素、Canvas、アニメーションを含む2D表現を構成できる。
- 専用Additive Sceneを `LocalPhysicsMode.Physics2D` で生成し、User Layer 8～31のプールから1つを割り当てる。
- CanvasはWorld SpaceまたはScreen Space - Cameraだけを許可し、Screen Space - Overlayは禁止する。
- 3D／2D Sceneノードの合計上限は24個とする。

## 設計意図

- 2D表現モジュールを自己完結させ、別ノードの描画順や状態に依存しないようにする。
- 同じ2D表現を複数配置し、それぞれ異なる状態で動かせるようにする。
- 単一画像の表示に限定せず、複数要素からなる2D演出を扱えるようにする。
