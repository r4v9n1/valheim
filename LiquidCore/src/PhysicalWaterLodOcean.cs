using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicalWater
{
    /// <summary>
    /// Camera-centred concentric ocean LOD renderer.
    /// The old PhysicalWater heightfield remains physics/shore-mask data only.
    /// Visible ocean geometry is rendered here so a single finite grid can never
    /// become the giant jelly sheet again.
    /// </summary>
    internal sealed class PhysicalWaterLodOcean : MonoBehaviour
    {
        private sealed class LodPatch
        {
            internal GameObject Object;
            internal Mesh Mesh;
            internal MeshRenderer Renderer;
            internal float CellSize;
            internal float Lod01;
        }

        private readonly List<LodPatch> _patches = new List<LodPatch>();
        private PhysicalWaterSystem _system;
        private Transform _root;
        private Material _material;
        private bool _visible;
        private Vector3 _lastSnappedCenter = new Vector3(float.PositiveInfinity, 0f, float.PositiveInfinity);

        internal bool PresentationEnabled { get { return _visible && _material != null && _patches.Count > 0; } }

        internal int PatchCount { get { return _patches.Count; } }

        internal int EnabledPatchCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _patches.Count; i++)
                {
                    if (_patches[i].Renderer != null && _patches[i].Renderer.enabled) count++;
                }
                return count;
            }
        }

        internal bool HasCoverageAt(Vector3 worldPosition)
        {
            if (!PresentationEnabled || _root == null) return false;
            Vector3 local = worldPosition - _root.position;
            float outer = OuterHalfSizes[OuterHalfSizes.Length - 1];
            return Mathf.Abs(local.x) <= outer && Mathf.Abs(local.z) <= outer;
        }

        internal string GetBindingDiagnostics()
        {
            string result = "rootExists=" + (_root != null) +
                            ", rootY=" + (_root != null ? _root.position.y.ToString("F2") : "nan") +
                            ", intendedMaterial=" + (_material != null && _material.shader != null && _material.shader.name == "R4V9N1/Physical Ocean Surface");
            for (int i = 0; i < _patches.Count; i++)
            {
                LodPatch patch = _patches[i];
                MeshRenderer renderer = patch.Renderer;
                Mesh mesh = patch.Mesh;
                Material material = renderer != null ? renderer.sharedMaterial : null;
                result += "; LOD" + i +
                          " enabled=" + (renderer != null && renderer.enabled) +
                          " mesh=" + (mesh != null) +
                          " v=" + (mesh != null ? mesh.vertexCount : 0) +
                          " t=" + (mesh != null ? mesh.triangles.Length / 3 : 0) +
                          " layer=" + (patch.Object != null ? patch.Object.layer : -1) +
                          " material=" + (material != null ? material.name : "null") +
                          " shader=" + (material != null && material.shader != null ? material.shader.name : "null") +
                          " boundsY=" + (renderer != null ? renderer.bounds.center.y.ToString("F2") : "nan");
            }
            return result;
        }

        // Power-of-two boundaries guarantee that neighbouring rings share the same seams.
        // Near cells are dramatically denser than the old ~4.8 m follow-grid cells.
        private static readonly float[] OuterHalfSizes = { 96f, 192f, 384f, 768f, 1536f };
        private static readonly float[] CellSizes = { 1.5f, 3f, 6f, 12f, 24f };

        internal void Initialize(PhysicalWaterSystem system)
        {
            _system = system;
            GameObject root = new GameObject("PhysicalWater_LODOceanRoot");
            root.transform.SetParent(transform, false);
            _root = root.transform;
            BuildLodMeshes();
            SetMaterial(system != null ? system.RuntimeWaterMaterial : null);
            SetVisible(false);
        }

        internal void SetMaterial(Material material)
        {
            if (_material == material)
            {
                return;
            }

            _material = material;
            for (int i = 0; i < _patches.Count; i++)
            {
                if (_patches[i].Renderer != null)
                {
                    _patches[i].Renderer.sharedMaterial = _material;
                }
            }
        }

        internal void SetVisible(bool visible)
        {
            _visible = visible;
            for (int i = 0; i < _patches.Count; i++)
            {
                MeshRenderer renderer = _patches[i].Renderer;
                if (renderer != null && renderer.enabled != visible)
                {
                    renderer.enabled = visible;
                }
            }
        }

        internal void Tick()
        {
            if (_system == null || _root == null)
            {
                return;
            }

            SetMaterial(_system.RuntimeWaterMaterial);
            if (!_visible)
            {
                return;
            }

            Vector3 focus = Player.m_localPlayer != null
                ? Player.m_localPlayer.transform.position
                : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);

            float snap = CellSizes[0] * 4f;
            float x = Mathf.Round(focus.x / snap) * snap;
            float z = Mathf.Round(focus.z / snap) * snap;
            Vector3 snapped = new Vector3(x, PhysicalWaterPlugin.Settings.SeaLevel.Value, z);
            if ((_lastSnappedCenter - snapped).sqrMagnitude > 0.0001f)
            {
                _root.position = snapped;
                _lastSnappedCenter = snapped;
            }
        }

        private void BuildLodMeshes()
        {
            DestroyLodMeshes();
            if (_root == null)
            {
                return;
            }

            float inner = 0f;
            for (int lod = 0; lod < OuterHalfSizes.Length; lod++)
            {
                float outer = OuterHalfSizes[lod];
                float cell = CellSizes[lod];
                float lod01 = OuterHalfSizes.Length <= 1 ? 0f : (float)lod / (OuterHalfSizes.Length - 1);
                Mesh mesh = BuildRingMesh(inner, outer, cell, lod01, lod == 0);
                GameObject obj = new GameObject("PhysicalWater_LOD" + lod);
                obj.transform.SetParent(_root, false);
                MeshFilter filter = obj.AddComponent<MeshFilter>();
                MeshRenderer renderer = obj.AddComponent<MeshRenderer>();
                filter.sharedMesh = mesh;
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                renderer.allowOcclusionWhenDynamic = false;
                renderer.enabled = _visible;

                _patches.Add(new LodPatch
                {
                    Object = obj,
                    Mesh = mesh,
                    Renderer = renderer,
                    CellSize = cell,
                    Lod01 = lod01
                });

                inner = outer;
            }
        }

        private static Mesh BuildRingMesh(float inner, float outer, float cell, float lod01, bool center)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> colors = new List<Color>();
            List<int> triangles = new List<int>();

            if (center)
            {
                AppendRectGrid(-outer, outer, -outer, outer, cell, lod01, vertices, uvs, colors, triangles);
            }
            else
            {
                // North and south span the full width. East/west fill the middle only,
                // avoiding overlapping transparent quads and z-fighting at the corners.
                AppendRectGrid(-outer, outer, inner, outer, cell, lod01, vertices, uvs, colors, triangles);
                AppendRectGrid(-outer, outer, -outer, -inner, cell, lod01, vertices, uvs, colors, triangles);
                AppendRectGrid(inner, outer, -inner, inner, cell, lod01, vertices, uvs, colors, triangles);
                AppendRectGrid(-outer, -inner, -inner, inner, cell, lod01, vertices, uvs, colors, triangles);
            }

            Mesh mesh = new Mesh();
            mesh.name = "PhysicalWater_ConcentricLOD_" + lod01.ToString("F2");
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0, true);
            Vector3[] normals = new Vector3[vertices.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.normals = normals;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(outer * 2f + 64f, 16f, outer * 2f + 64f));
            mesh.UploadMeshData(false);
            return mesh;
        }

        private static void AppendRectGrid(
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            float targetCell,
            float lod01,
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> triangles)
        {
            float width = Mathf.Max(0.01f, maxX - minX);
            float depth = Mathf.Max(0.01f, maxZ - minZ);
            int xCells = Mathf.Max(1, Mathf.CeilToInt(width / targetCell));
            int zCells = Mathf.Max(1, Mathf.CeilToInt(depth / targetCell));
            float dx = width / xCells;
            float dz = depth / zCells;
            int start = vertices.Count;

            for (int z = 0; z <= zCells; z++)
            {
                float pz = minZ + dz * z;
                float vz = (float)z / zCells;
                for (int x = 0; x <= xCells; x++)
                {
                    float px = minX + dx * x;
                    float ux = (float)x / xCells;
                    vertices.Add(new Vector3(px, 0f, pz));
                    uvs.Add(new Vector2(ux, vz));
                    // r = surface opacity carrier, g = reserved foam/depth input,
                    // b = LOD level used by the shader to fade short geometric waves.
                    colors.Add(new Color(1f, 0f, ComputeContinuousLod01(px, pz), 1f));
                }
            }

            int row = xCells + 1;
            for (int z = 0; z < zCells; z++)
            {
                for (int x = 0; x < xCells; x++)
                {
                    int a = start + z * row + x;
                    int b = a + 1;
                    int c = a + row;
                    int d = c + 1;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }
        }


        private static float ComputeContinuousLod01(float x, float z)
        {
            float radius = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            if (radius <= OuterHalfSizes[0])
            {
                return 0f;
            }

            for (int i = 1; i < OuterHalfSizes.Length; i++)
            {
                float inner = OuterHalfSizes[i - 1];
                float outer = OuterHalfSizes[i];
                if (radius <= outer)
                {
                    float t = Mathf.InverseLerp(inner, outer, radius);
                    return ((i - 1) + t) / (OuterHalfSizes.Length - 1);
                }
            }

            return 1f;
        }

        private void DestroyLodMeshes()
        {
            for (int i = 0; i < _patches.Count; i++)
            {
                LodPatch patch = _patches[i];
                if (patch.Object != null) Destroy(patch.Object);
                if (patch.Mesh != null) Destroy(patch.Mesh);
            }
            _patches.Clear();
        }

        private void OnDestroy()
        {
            DestroyLodMeshes();
            if (_root != null)
            {
                Destroy(_root.gameObject);
                _root = null;
            }
        }
    }
}
