# Camera Rail

`Cylinder Flythrough` now uses a Unity Splines path for camera movement. The path is advanced by the existing scene graph clock, so Timeline is not required and the live scene `Motion` parameter still controls playback speed.

## Editing

1. Open `Assets/ShitDesigner/Scenes/Cylinder Flythrough.prefab`.
2. Select the `Camera Rail` child.
3. Edit the knots and Bezier tangents in the `Spline Container` component in the Scene view.
4. Select the prefab root to adjust `Spline Camera Rail > Speed`, `Start Offset`, `Loop`, or `Align To Path`.

The camera follows the spline tangent when `Align To Path` is enabled. The existing `CylindricalObjectFlythrough` remains responsible for generating the object field; its legacy linear camera speed is disabled for this prefab.
