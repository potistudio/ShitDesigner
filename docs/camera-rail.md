# Camera Rail

`Cylinder Flythrough` now uses a Unity Splines path for camera movement. The path is advanced by the existing scene graph clock, so Timeline is not required and the live scene `Motion` parameter still controls playback speed.

## Editing

1. Open `Assets/ShitDesigner/Scenes/Cylinder Flythrough.prefab`.
2. Select the `Camera Rail` child.
3. Edit the knots and Bezier tangents in the `Spline Container` component in the Scene view.
4. Select the prefab root to adjust `Spline Camera Rail > Speed`, `Start Offset`, `Loop`, `Align To Path`, or `Target`.

The camera follows `Target` when it is assigned; otherwise it follows the spline tangent when `Align To Path` is enabled. The existing `CylindricalObjectFlythrough` remains responsible for generating the object field; its legacy linear camera speed is disabled for this prefab.

## Stage

The `Stage` scene uses `Stage Random Camera` instead of a spline rail. It keeps the prefab's `Camera Target` in frame and moves in a straight line between deterministic random positions and fields of view. The `Penlight` crowd defines the audience direction, and every camera endpoint is constrained to remain on that side of the stage by the configured minimum distance. Because the valid region is a half-space, the straight path between endpoints also stays on the audience side. Select the Stage prefab root to adjust its shot bounds, duration, field-of-view range, audience-side distance, or random seed. The scene graph clock still drives the camera, so the live scene `Motion` parameter controls its playback speed.
