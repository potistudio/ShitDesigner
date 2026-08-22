using UnityEngine;

namespace ShitDesigner.Bootstrap
{
    /// <summary>
    /// Supplies the small deterministic preview mesh at runtime.  Scene
    /// prefabs deliberately keep their authored MeshFilter/Renderer so the
    /// catalog can validate the production asset, but a builtin resource mesh
    /// is not a stable serialized dependency for an instantiated additive
    /// Scene.  Replacing that one preview mesh with owned vertices keeps the
    /// visible geometry inside the Scene node and makes the render contract
    /// independent of builtin-resource lookup for both 3D and 2D prefabs.
    /// </summary>
    public sealed class SceneDeterministicGeometry : MonoBehaviour
    {
        private Mesh _runtimeMesh;

        private void Awake()
        {
            var filter = FindBackdropFilter();
            if (filter == null) return;
            _runtimeMesh = BuildQuadMesh();
            filter.sharedMesh = _runtimeMesh;
        }

        private void OnDestroy()
        {
            if (_runtimeMesh != null) Destroy(_runtimeMesh);
            _runtimeMesh = null;
        }

        private MeshFilter FindBackdropFilter()
        {
            foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.gameObject.name == "DeterministicBackdrop" || filter.gameObject.name == "DeterministicQuad") return filter;
            }
            return GetComponentInChildren<MeshFilter>(true);
        }

        private static Mesh BuildQuadMesh()
        {
            var mesh = new Mesh { name = "ShitDesigner.Scene3D.DeterministicQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f)
            };
            // Keep both windings so the deterministic preview is visible from
            // either side of the dedicated camera without changing culling
            // policy for user-authored Scene geometry.
            mesh.triangles = new[]
            {
                0, 1, 2, 2, 3, 0,
                2, 1, 0, 0, 3, 2
            };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
