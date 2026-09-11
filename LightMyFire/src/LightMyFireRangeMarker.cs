using System;
using System.Reflection;
using UnityEngine;

namespace LightMyFire
{
    /// <summary>
    /// Displays only a thin dotted circumference at the exact LightMyFire feeding radius.
    /// The ring appears while this barrel is the local placement ghost or while this barrel's
    /// inventory is open locally. There is deliberately no filled area/disc.
    /// </summary>
    public sealed class LightMyFireRangeMarker : MonoBehaviour
    {
        private const string MarkerName = "R4V9N1_LightMyFire_RangeMarker";
        private const float DotSpacingMetres = 1.25f;
        private const float DotLengthMetres = 0.34f;
        private const float DotWidthMetres = 0.14f;
        private const float HeightOffset = 0.055f;
        private const int MinDotCount = 32;
        private const int MaxDotCount = 512;

        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo PlacementGhostField = typeof(Player).GetField("m_placementGhost", Flags);
        private static readonly FieldInfo CurrentContainerField = typeof(InventoryGui).GetField("m_currentContainer", Flags);
        private static readonly MethodInfo ContainerIsInUseMethod = typeof(Container).GetMethod("IsInUse", Flags, null, Type.EmptyTypes, null);

        [SerializeField] private GameObject _marker;
        private MeshFilter _meshFilter;
        private Container _container;
        private ZNetView _nview;
        private Vector3 _lastMarkerWorldCenter = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        private float _lastAppliedRange = -1f;
        private bool _lastVisible;

        internal void Configure(GameObject marker)
        {
            _marker = marker;
            CacheMarkerParts();
            if (_marker != null)
            {
                _marker.SetActive(false);
            }
        }

        private void Awake()
        {
            _container = GetComponent<Container>();
            _nview = GetComponent<ZNetView>();
            RecoverMarkerReference();
            if (_marker != null)
            {
                _marker.SetActive(false);
            }
        }

        private void Start()
        {
            RecoverMarkerReference();
            ApplyCurrentRange(true, true);
        }

        private void Update()
        {
            RecoverMarkerReference();
            if (_marker == null)
            {
                return;
            }

            bool visible = IsPlacementPreviewInstance() || IsThisPlacementGhost() || IsThisInventoryOpenLocally();
            ApplyCurrentRange(false, visible);
            if (visible != _lastVisible || _marker.activeSelf != visible)
            {
                _marker.SetActive(visible);
                _lastVisible = visible;
            }
        }

        private void RecoverMarkerReference()
        {
            if (_marker == null)
            {
                Transform child = transform.Find(MarkerName);
                if (child == null)
                {
                    Transform[] all = GetComponentsInChildren<Transform>(true);
                    for (int i = 0; i < all.Length; ++i)
                    {
                        if (string.Equals(all[i].name, MarkerName, StringComparison.Ordinal))
                        {
                            child = all[i];
                            break;
                        }
                    }
                }

                if (child != null)
                {
                    _marker = child.gameObject;
                }
            }

            CacheMarkerParts();
        }

        private void CacheMarkerParts()
        {
            if (_marker != null && _meshFilter == null)
            {
                _meshFilter = _marker.GetComponent<MeshFilter>();
            }
        }

        private void ApplyCurrentRange(bool force, bool visible)
        {
            if (_marker == null || _meshFilter == null)
            {
                return;
            }

            float range = Mathf.Max(0.1f, LightMyFirePlugin.GetFeedRange());
            Vector3 worldCenter = transform.position;
            bool moved = (worldCenter - _lastMarkerWorldCenter).sqrMagnitude > 0.25f;
            if (!force && Mathf.Abs(range - _lastAppliedRange) < 0.01f && (!visible || !moved))
            {
                return;
            }

            Mesh old = _meshFilter.sharedMesh;
            _meshFilter.sharedMesh = BuildDottedRing(range);
            if (old != null && old.name.StartsWith("LightMyFire_DottedRange_", StringComparison.Ordinal))
            {
                Destroy(old);
            }

            _lastAppliedRange = range;
            _lastMarkerWorldCenter = worldCenter;
        }

        private Mesh BuildDottedRing(float radius)
        {
            float circumference = Mathf.PI * 2f * radius;
            int dotCount = Mathf.Clamp(Mathf.RoundToInt(circumference / DotSpacingMetres), MinDotCount, MaxDotCount);

            Vector3[] vertices = new Vector3[dotCount * 4];
            Vector3[] normals = new Vector3[vertices.Length];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[dotCount * 6];

            float halfLength = DotLengthMetres * 0.5f;
            float halfWidth = DotWidthMetres * 0.5f;

            for (int i = 0; i < dotCount; ++i)
            {
                float angle = (Mathf.PI * 2f * i) / dotCount;
                float c = Mathf.Cos(angle);
                float s = Mathf.Sin(angle);

                Vector3 localFlat = new Vector3(c * radius, 0f, s * radius);
                Vector3 world = transform.TransformPoint(localFlat);
                float localY = HeightOffset;
                RaycastHit hit;
                Vector3 rayStart = world + Vector3.up * 120f;
                if (Physics.Raycast(rayStart, Vector3.down, out hit, 240f, ~0, QueryTriggerInteraction.Ignore))
                {
                    Vector3 localHit = transform.InverseTransformPoint(hit.point + hit.normal * HeightOffset);
                    localY = localHit.y;
                }
                Vector3 center = new Vector3(c * radius, localY, s * radius);
                Vector3 tangent = new Vector3(-s, 0f, c);
                Vector3 radial = new Vector3(c, 0f, s);

                int v = i * 4;
                vertices[v + 0] = center - tangent * halfLength - radial * halfWidth;
                vertices[v + 1] = center + tangent * halfLength - radial * halfWidth;
                vertices[v + 2] = center + tangent * halfLength + radial * halfWidth;
                vertices[v + 3] = center - tangent * halfLength + radial * halfWidth;

                normals[v + 0] = Vector3.up;
                normals[v + 1] = Vector3.up;
                normals[v + 2] = Vector3.up;
                normals[v + 3] = Vector3.up;

                uv[v + 0] = new Vector2(0f, 0f);
                uv[v + 1] = new Vector2(1f, 0f);
                uv[v + 2] = new Vector2(1f, 1f);
                uv[v + 3] = new Vector2(0f, 1f);

                int t = i * 6;
                // Clockwise winding when viewed from above makes the dot face upward.
                // 0.2.12 had this reversed, so back-face culling made the entire ring invisible.
                triangles[t + 0] = v + 0;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 0;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;
            }

            Mesh mesh = new Mesh();
            mesh.name = "LightMyFire_DottedRange_" + radius.ToString("0.##");
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private bool IsPlacementPreviewInstance()
        {
            // Placement ghosts are active scene clones without a live network ZDO. This avoids
            // depending solely on Valheim private Player field names, which have changed across
            // game versions. Registered prefab templates are inactive, while the actual placement
            // ghost is active and updating.
            if (!gameObject.activeInHierarchy)
            {
                return false;
            }

            if (_nview == null)
            {
                _nview = GetComponent<ZNetView>();
            }

            return _nview == null || !_nview.IsValid() || _nview.GetZDO() == null;
        }

        private bool IsThisPlacementGhost()
        {
            Player player = Player.m_localPlayer;
            if (player == null || PlacementGhostField == null)
            {
                return false;
            }

            try
            {
                GameObject ghost = PlacementGhostField.GetValue(player) as GameObject;
                if (ghost == null)
                {
                    return false;
                }

                return ghost == gameObject || ghost.transform.IsChildOf(transform) || transform.IsChildOf(ghost.transform);
            }
            catch
            {
                return false;
            }
        }

        private bool IsThisInventoryOpenLocally()
        {
            if (_container == null)
            {
                _container = GetComponent<Container>();
            }
            if (_container == null)
            {
                return false;
            }

            try
            {
                InventoryGui gui = InventoryGui.instance;
                if (gui != null && CurrentContainerField != null)
                {
                    Container current = CurrentContainerField.GetValue(gui) as Container;
                    if (current == _container)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Fall through to compatibility path.
            }

            try
            {
                return ContainerIsInUseMethod != null && (bool)ContainerIsInUseMethod.Invoke(_container, null);
            }
            catch
            {
                return false;
            }
        }

        private void OnDisable()
        {
            if (_marker != null)
            {
                _marker.SetActive(false);
            }
            _lastVisible = false;
        }
    }
}
