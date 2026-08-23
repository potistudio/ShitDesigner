# VJ shader playground

`Assets/ShitDesigner/Scenes/VJShaderPlayground.unity` is a small, inspector-only scene for trying the generated VJ shader pack. It does not modify or depend on the production Bootstrap scene.

## Open and run

1. Open the project in Unity 6000.5.9f1.
2. Open `Assets/ShitDesigner/Scenes/VJShaderPlayground.unity`.
3. Select **VJ Shader Playground Output** in the Hierarchy and press Play.
4. In the Inspector, change **Selected Type Id** to another generated ID, such as `shitdesigner.shader.generator.plasma`, `shitdesigner.shader.color.invert`, or `shitdesigner.shader.transition.crossfade`.
5. Adjust **Output Width/Height**, **Seed**, **Time Speed**, **Paused**, and the common VJ parameters. Use the component context menu **Reset Playground Output** after changing a history/stateful input.

The scene already references the generated `ShaderNodeManifest.asset` and `NodeTypeCatalog.asset`. The selected manifest entry supplies the direct strip-safe Shader reference and SourceVariant; the playground writes the same `_SD_*`, `_VJVariant`, `_Variant`, clock alias, input, and history properties used by the runtime shader bindings.

## Inputs

- **Input Texture** is the primary image. Generators can leave it empty.
- **Secondary Texture** is used for blend/composite experiments.
- **History Texture / 2 / 3** are optional history slots for temporal/stateful experiments.
- **Shader Override** is an optional direct Shader reference for an experimental shader that is not in the generated manifest.

The output is rendered into an owned `RenderTexture` and assigned to the scene quad's `PreviewDisplay` material. The component can therefore be used on another MeshRenderer as a small reusable inspector-only preview without opening a custom window.
