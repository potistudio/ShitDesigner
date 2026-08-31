# Camera Rail

`Cylinder Flythrough` now uses a Unity Splines path for camera movement. The path is advanced by the existing scene graph clock, so Timeline is not required and the live scene `Motion` parameter still controls playback speed.

## Editing

1. Open `Assets/ShitDesigner/Scenes/Cylinder Flythrough.prefab`.
2. Select the `Camera Rail` child.
3. Edit the knots and Bezier tangents in the `Spline Container` component in the Scene view.
4. Select the prefab root to adjust `Spline Camera Rail > Speed`, `Start Offset`, `Loop`, `Align To Path`, or `Target`.

The camera follows `Target` when it is assigned; otherwise it follows the spline tangent when `Align To Path` is enabled. The existing `CylindricalObjectFlythrough` remains responsible for generating the object field; its legacy linear camera speed is disabled for this prefab.

## Stage

The `Stage` scene uses `Stage Random Camera` instead of a spline rail. It keeps the prefab's `Camera Target` in frame and continues moving in one fixed direction at one fixed speed until the next manual jump; there is no timed or endpoint-based camera change. The `Penlight` crowd defines the audience direction. Movement is selected from horizontal directions that run sideways or farther into the audience side, so it never travels behind the stage.

During Play Mode, select the Stage prefab root and press `飛び` to choose a new audience-side position, movement direction, speed, and field of view. The Inspector exposes the jump bounds, field-of-view range, minimum audience-side distance, movement-speed range, and random seed. The scene graph clock still drives the camera, so the live scene `Motion` parameter controls its playback speed without changing the selected direction.
