using UnityEngine;

namespace MapLibre.Unity.Pool
{
    public class TileObjectPool
    {
        private readonly ObjectPool<GameObject> _pool;
        private readonly Transform _parent;
        private readonly Material _sharedMaterial;
        private readonly Mesh _quadMesh;

        public TileObjectPool(Transform parent, Material material, int initialSize = 32)
        {
            _parent = parent;
            _sharedMaterial = material;
            _quadMesh = CreateQuadMesh();

            _pool = new ObjectPool<GameObject>(
                factory: CreateTileObject,
                onGet: go => go.SetActive(true),
                onRelease: go =>
                {
                    go.SetActive(false);
                    // Clear per-instance texture
                    var mr = go.GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        var block = new MaterialPropertyBlock();
                        mr.SetPropertyBlock(block);
                    }
                },
                initialSize: initialSize,
                maxSize: 64
            );
        }

        private GameObject CreateTileObject()
        {
            var go = new GameObject("Tile");
            go.transform.SetParent(_parent, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _quadMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _sharedMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            return go;
        }

        public GameObject Get() => _pool.Get();

        public void Release(GameObject go)
        {
            if (go != null)
                _pool.Release(go);
        }

        public void Clear() => _pool.Clear();

        /// <summary>
        /// Creates a unit quad on the XZ plane, centered at origin. Y = 0.
        /// UV: (0,1) at top-left (north-west), (1,0) at bottom-right (south-east).
        /// </summary>
        private static Mesh CreateQuadMesh()
        {
            var mesh = new Mesh { name = "TileQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0.5f),  // top-left (NW)
                new Vector3(0.5f, 0f, 0.5f),   // top-right (NE)
                new Vector3(-0.5f, 0f, -0.5f),  // bottom-left (SW)
                new Vector3(0.5f, 0f, -0.5f)    // bottom-right (SE)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 1f), // NW
                new Vector2(1f, 1f), // NE
                new Vector2(0f, 0f), // SW
                new Vector2(1f, 0f)  // SE
            };
            mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 };
            mesh.normals = new[]
            {
                Vector3.up, Vector3.up, Vector3.up, Vector3.up
            };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
