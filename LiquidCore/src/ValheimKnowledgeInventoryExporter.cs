using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using Stopwatch = System.Diagnostics.Stopwatch;
using HarmonyLib;
using UnityEngine;

namespace PhysicalWater
{
    internal static class ValheimKnowledgeInventoryExporter
    {
        internal const string OutputEnvironmentVariable = "LIQUIDCORE_KNOWLEDGE_INVENTORY_PATH";

        [Serializable]
        private sealed class InventoryDocument
        {
            public int schemaVersion;
            public string generatedUtc;
            public string source;
            public ValheimKnowledgeDatabase.ValheimIdentity valheim;
            public ValheimKnowledgeDatabase.ModSetIdentity modSet;
            public int registeredPrefabCount;
            public int colliderPrefabCount;
            public int databaseReusableMatches;
            public double meanDatabaseLookupNanoseconds;
            public double meanHierarchyInspectionMicroseconds;
            public double hierarchyInspectionToLookupRatio;
            public int hierarchyElementsInspected;
            public ValheimKnowledgeDatabase.AssetRule[] assets;
        }

        internal static bool Enabled
        {
            get { return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OutputEnvironmentVariable)); }
        }

        internal static void Export(ZNetScene scene)
        {
            string outputPath = Environment.GetEnvironmentVariable(OutputEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(outputPath) || scene == null || PhysicalWaterPlugin.ValheimKnowledge == null ||
                !PhysicalWaterPlugin.ValheimKnowledge.Loaded)
                return;

            try
            {
                var prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
                AddPrefabs(prefabs, scene.m_prefabs);
                AddPrefabs(prefabs, scene.m_nonNetViewPrefabs);
                var namedField = AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs");
                var named = namedField != null
                    ? namedField.GetValue(scene) as Dictionary<int, GameObject>
                    : null;
                if (named != null)
                    foreach (GameObject prefab in named.Values) AddPrefab(prefabs, prefab);

                WriteInventory(outputPath, prefabs, "fingerprinted ZNetScene prefab registry");
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log?.LogError("LiquidCore Valheim knowledge inventory export failed: " + ex);
            }
        }

        internal static IEnumerator ExportLoadedResourcesAfterStartup()
        {
            yield return new WaitForSecondsRealtime(3f);
            string outputPath = Environment.GetEnvironmentVariable(OutputEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(outputPath) || PhysicalWaterPlugin.ValheimKnowledge == null ||
                !PhysicalWaterPlugin.ValheimKnowledge.Loaded)
                yield break;
            try
            {
                GameObject[] loaded = Resources.FindObjectsOfTypeAll<GameObject>();
                var prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
                for (int i = 0; i < loaded.Length; i++)
                {
                    GameObject candidate = loaded[i];
                    if (candidate == null || string.IsNullOrEmpty(candidate.name)) continue;
                    GameObject current;
                    if (!prefabs.TryGetValue(candidate.name, out current) ||
                        candidate.GetComponentsInChildren<Collider>(true).Length > current.GetComponentsInChildren<Collider>(true).Length)
                        prefabs[candidate.name] = candidate;
                }
                WriteInventory(outputPath, prefabs, "fingerprinted loaded Valheim resource inventory");
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log?.LogError("LiquidCore loaded-resource knowledge inventory export failed: " + ex);
            }
        }

        private static void WriteInventory(string outputPath, Dictionary<string, GameObject> prefabs, string source)
        {
            var assets = new List<ValheimKnowledgeDatabase.AssetRule>(prefabs.Count);
            int colliderPrefabs = 0;
            foreach (KeyValuePair<string, GameObject> pair in prefabs)
            {
                ValheimKnowledgeDatabase.AssetRule rule = DescribePrefab(pair.Value, pair.Key);
                if (rule == null) continue;
                if (rule.colliderCount > 0) colliderPrefabs++;
                assets.Add(rule);
            }
            assets.Sort((left, right) => string.Compare(left.assetId, right.assetId, StringComparison.Ordinal));

            var reusablePrefabs = new List<GameObject>();
            foreach (KeyValuePair<string, GameObject> pair in prefabs)
            {
                ValheimKnowledgeDatabase.AssetRule ignored;
                if (PhysicalWaterPlugin.ValheimKnowledge.TryGetReusableGeometryAsset(pair.Key, out ignored)) reusablePrefabs.Add(pair.Value);
            }
            const int lookupRounds = 100;
            Stopwatch lookupWatch = Stopwatch.StartNew();
            for (int round = 0; round < lookupRounds; round++)
            for (int i = 0; i < reusablePrefabs.Count; i++)
            {
                ValheimKnowledgeDatabase.AssetRule ignored;
                PhysicalWaterPlugin.ValheimKnowledge.TryGetReusableGeometryAsset(reusablePrefabs[i].name, out ignored);
            }
            lookupWatch.Stop();
            Stopwatch hierarchyWatch = Stopwatch.StartNew();
            int hierarchyElements = 0;
            for (int i = 0; i < reusablePrefabs.Count; i++)
            {
                hierarchyElements += reusablePrefabs[i].GetComponentsInChildren<Collider>(true).Length;
                hierarchyElements += reusablePrefabs[i].GetComponentsInChildren<Component>(true).Length;
            }
            hierarchyWatch.Stop();
            double meanLookupNanoseconds = reusablePrefabs.Count > 0
                ? lookupWatch.Elapsed.TotalMilliseconds * 1000000.0 / (reusablePrefabs.Count * lookupRounds)
                : 0.0;
            double meanHierarchyMicroseconds = reusablePrefabs.Count > 0
                ? hierarchyWatch.Elapsed.TotalMilliseconds * 1000.0 / reusablePrefabs.Count
                : 0.0;
            double speedup = meanLookupNanoseconds > 0.0 ? meanHierarchyMicroseconds * 1000.0 / meanLookupNanoseconds : 0.0;

            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
            var document = new InventoryDocument
            {
                schemaVersion = PhysicalWaterPlugin.ValheimKnowledge.Data.schemaVersion,
                generatedUtc = DateTime.UtcNow.ToString("O"),
                source = source + "; opt-in only and never executed during normal startup",
                valheim = PhysicalWaterPlugin.ValheimKnowledge.Data.valheim,
                modSet = PhysicalWaterPlugin.ValheimKnowledge.Data.modSet,
                registeredPrefabCount = assets.Count,
                colliderPrefabCount = colliderPrefabs,
                databaseReusableMatches = reusablePrefabs.Count,
                meanDatabaseLookupNanoseconds = meanLookupNanoseconds,
                meanHierarchyInspectionMicroseconds = meanHierarchyMicroseconds,
                hierarchyInspectionToLookupRatio = speedup,
                hierarchyElementsInspected = hierarchyElements,
                assets = assets.ToArray()
            };
            using (var stream = File.Create(outputPath))
                new DataContractJsonSerializer(typeof(InventoryDocument)).WriteObject(stream, document);
            PhysicalWaterPlugin.Log?.LogInfo(
                "LiquidCore Valheim knowledge inventory exported once: source=" + source +
                ", registeredPrefabs=" + assets.Count + ", colliderPrefabs=" + colliderPrefabs +
                ", reusableDatabaseMatches=" + reusablePrefabs.Count +
                ", meanLookupNs=" + meanLookupNanoseconds.ToString("F1") +
                ", meanHierarchyInspectionUs=" + meanHierarchyMicroseconds.ToString("F1") +
                ", speedup=" + speedup.ToString("F1") + "x" +
                ", path=" + outputPath + ".");
        }

        private static void AddPrefabs(Dictionary<string, GameObject> target, List<GameObject> prefabs)
        {
            if (prefabs == null) return;
            for (int i = 0; i < prefabs.Count; i++) AddPrefab(target, prefabs[i]);
        }

        private static void AddPrefab(Dictionary<string, GameObject> target, GameObject prefab)
        {
            if (prefab == null || string.IsNullOrEmpty(prefab.name)) return;
            if (!target.ContainsKey(prefab.name)) target.Add(prefab.name, prefab);
        }

        private static ValheimKnowledgeDatabase.AssetRule DescribePrefab(GameObject prefab, string assetId)
        {
            Component[] components = prefab.GetComponentsInChildren<Component>(true);
            Collider[] colliders = prefab.GetComponentsInChildren<Collider>(true);
            var componentNames = new HashSet<string>(StringComparer.Ordinal);
            var colliderNames = new HashSet<string>(StringComparer.Ordinal);
            bool destructible = false;
            bool buildPiece = false;
            bool door = false;
            int meshColliders = 0;
            int primitiveColliders = 0;
            int triggerColliders = 0;
            int meshVertices = 0;
            int meshTriangles = 0;
            var signatureRows = new List<string>();
            Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;
                string typeName = component.GetType().Name;
                componentNames.Add(typeName);
                destructible |= Matches(typeName, "Destructible", "MineRock", "MineRock5", "WearNTear");
                buildPiece |= Matches(typeName, "Piece", "WearNTear");
                door |= Matches(typeName, "Door");
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null) continue;
                string colliderType = collider.GetType().Name;
                colliderNames.Add(colliderType);
                if (collider.isTrigger) triggerColliders++;
                if (collider is MeshCollider meshCollider)
                {
                    meshColliders++;
                    Mesh mesh = meshCollider.sharedMesh;
                    if (mesh != null)
                    {
                        int triangleCount = MeshTriangleCount(mesh);
                        meshVertices += mesh.vertexCount;
                        meshTriangles += triangleCount;
                        AccumulateBounds(prefab.transform, collider.transform, mesh.bounds, ref minimum, ref maximum);
                        signatureRows.Add(PathFrom(prefab.transform, collider.transform) + "|MeshCollider|" +
                                          mesh.name + "|" + mesh.vertexCount + "|" + triangleCount + "|" +
                                          VectorKey(mesh.bounds.center) + "|" + VectorKey(mesh.bounds.size) + "|trigger=" + collider.isTrigger);
                    }
                }
                else if (collider is BoxCollider box)
                {
                    primitiveColliders++;
                    var bounds = new Bounds(box.center, box.size);
                    AccumulateBounds(prefab.transform, collider.transform, bounds, ref minimum, ref maximum);
                    signatureRows.Add(PathFrom(prefab.transform, collider.transform) + "|BoxCollider|" + VectorKey(box.center) + "|" + VectorKey(box.size) + "|trigger=" + collider.isTrigger);
                }
                else if (collider is SphereCollider sphere)
                {
                    primitiveColliders++;
                    var bounds = new Bounds(sphere.center, Vector3.one * sphere.radius * 2f);
                    AccumulateBounds(prefab.transform, collider.transform, bounds, ref minimum, ref maximum);
                    signatureRows.Add(PathFrom(prefab.transform, collider.transform) + "|SphereCollider|" + VectorKey(sphere.center) + "|" + Quantize(sphere.radius) + "|trigger=" + collider.isTrigger);
                }
                else if (collider is CapsuleCollider capsule)
                {
                    primitiveColliders++;
                    Vector3 size = Vector3.one * capsule.radius * 2f;
                    if (capsule.direction == 0) size.x = capsule.height;
                    else if (capsule.direction == 1) size.y = capsule.height;
                    else size.z = capsule.height;
                    var bounds = new Bounds(capsule.center, size);
                    AccumulateBounds(prefab.transform, collider.transform, bounds, ref minimum, ref maximum);
                    signatureRows.Add(PathFrom(prefab.transform, collider.transform) + "|CapsuleCollider|" + VectorKey(capsule.center) + "|" + VectorKey(size) + "|trigger=" + collider.isTrigger);
                }
                else
                {
                    signatureRows.Add(PathFrom(prefab.transform, collider.transform) + "|" + colliderType + "|trigger=" + collider.isTrigger);
                }
            }

            string[] componentTypes = ToSortedArray(componentNames);
            string[] colliderTypes = ToSortedArray(colliderNames);
            signatureRows.Sort(StringComparer.Ordinal);
            bool hasBounds = !float.IsInfinity(minimum.x);
            Bounds localBounds = hasBounds
                ? new Bounds((minimum + maximum) * 0.5f, maximum - minimum)
                : new Bounds(Vector3.zero, Vector3.zero);
            string category = Classify(assetId, componentNames, colliders.Length, triggerColliders);
            string rootType = RootType(componentNames);
            bool stateful = door || componentNames.Contains("MineRock") || componentNames.Contains("MineRock5");
            return new ValheimKnowledgeDatabase.AssetRule
            {
                assetId = assetId,
                observedRootType = rootType,
                category = category,
                geometryKind = GeometryKind(meshColliders, primitiveColliders),
                source = "fingerprinted ZNetScene prefab registry",
                precompute = colliders.Length > 0,
                colliderCount = colliders.Length,
                meshColliderCount = meshColliders,
                primitiveColliderCount = primitiveColliders,
                triggerColliderCount = triggerColliders,
                meshVertices = meshVertices,
                meshTriangles = meshTriangles,
                geometrySignature = Hash(string.Join("\n", signatureRows.ToArray())),
                componentTypes = componentTypes,
                colliderTypes = colliderTypes,
                colliderRecipes = ValheimColliderRecipeCapture.Capture(prefab),
                localBoundsCenter = new[] { localBounds.center.x, localBounds.center.y, localBounds.center.z },
                localBoundsSize = new[] { localBounds.size.x, localBounds.size.y, localBounds.size.z },
                staticClass = door ? "dynamic-solid" : destructible ? "destructible-static" : buildPiece ? "stateful-static" : "prefab-static",
                destructible = destructible,
                buildPiece = buildPiece,
                door = door,
                stateFields = door ? new[] { "door state", "settled collider transforms" } : destructible ? new[] { "destroyed/hidden geometry state" } : buildPiece ? new[] { "placement", "transform" } : new[] { "transform" },
                authoritativeCallbacks = door ? new[] { "Door.SetState" } : destructible ? new[] { "Destructible.DestroyNow", "MineRock.RPC_Hide", "MineRock5.UpdateMesh", "ZNetView.ResetZDO" } : buildPiece ? new[] { "Piece.OnPlaced", "ZNetView.ResetZDO" } : new[] { "ZNetScene.CreateObject", "ZNetView.ResetZDO" },
                pceRule = "asset lookup -> instance cache keyed by SourceID/transform/state/revision",
                liquidCoreRule = "reuse immutable prefab descriptor; invalidate only changed source and intersecting prepared region",
                runtimeInspectionRequired = stateful
            };
        }

        private static string Classify(string assetId, HashSet<string> components, int colliderCount, int triggers)
        {
            if (MatchesAny(components, "WaterVolume", "LiquidSurface", "LiquidVolume")) return "WaterVolume";
            if (MatchesAny(components, "Ship", "ShipControlls", "Vagon", "Sadle")) return "VehicleShip";
            if (MatchesAny(components, "Player", "Character", "Humanoid", "Fish", "MonsterAI", "AnimalAI", "BaseAI", "RandomFlyingBird", "Tameable")) return "CharacterCreature";
            if (MatchesAny(components, "ItemDrop", "Floating", "Projectile") && !components.Contains("Piece")) return "ItemDrop";
            if (MatchesAny(components, "TreeBase", "TreeLog", "Pickable") || ContainsAny(assetId, "beech", "firtree", "pinetree", "oaktree", "bush", "shrub", "sapling")) return "Vegetation";
            if (components.Contains("Door")) return "ThinBlockingBarrier";
            if (MatchesAny(components, "MineRock", "MineRock5", "Destructible")) return "DynamicSolid";
            if (MatchesAny(components, "Piece", "WearNTear", "DungeonGenerator", "Room", "Location")) return "SolidBarrier";
            if (components.Contains("ParticleSystem") || ContainsAny(assetId, "vfx", "sfx", "fx_", "smoke", "mist", "splash")) return "ParticleOrEffect";
            if (colliderCount > 0 && triggers == colliderCount) return "Trigger";
            return colliderCount > 0 ? "SolidBarrier" : "NonSolidDecorative";
        }

        private static string RootType(HashSet<string> components)
        {
            string[] priority = { "Door", "MineRock5", "MineRock", "Destructible", "Piece", "WearNTear", "Location", "ZNetView" };
            for (int i = 0; i < priority.Length; i++) if (components.Contains(priority[i])) return priority[i];
            return "GameObject";
        }

        private static string GeometryKind(int meshColliders, int primitiveColliders)
        {
            if (meshColliders > 0 && primitiveColliders > 0) return "ColliderHierarchy";
            if (meshColliders > 0) return "MeshCollider";
            if (primitiveColliders > 0) return "PrimitiveCollider";
            return "None";
        }

        private static void AccumulateBounds(Transform root, Transform child, Bounds childBounds, ref Vector3 minimum, ref Vector3 maximum)
        {
            Matrix4x4 childToRoot = root.worldToLocalMatrix * child.localToWorldMatrix;
            Vector3 center = childBounds.center;
            Vector3 extents = childBounds.extents;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 point = childToRoot.MultiplyPoint3x4(center + Vector3.Scale(extents, new Vector3(x, y, z)));
                minimum = Vector3.Min(minimum, point);
                maximum = Vector3.Max(maximum, point);
            }
        }

        private static string PathFrom(Transform root, Transform child)
        {
            if (root == child) return ".";
            var names = new List<string>();
            Transform current = child;
            while (current != null && current != root)
            {
                names.Add(current.name);
                current = current.parent;
            }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static bool Matches(string value, params string[] expected)
        {
            for (int i = 0; i < expected.Length; i++) if (string.Equals(value, expected[i], StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool MatchesAny(HashSet<string> values, params string[] expected)
        {
            for (int i = 0; i < expected.Length; i++) if (values.Contains(expected[i])) return true;
            return false;
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            if (string.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < needles.Length; i++) if (value.IndexOf(needles[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static string[] ToSortedArray(HashSet<string> values)
        {
            string[] result = new string[values.Count];
            values.CopyTo(result);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        private static string VectorKey(Vector3 value)
        {
            return Quantize(value.x) + "," + Quantize(value.y) + "," + Quantize(value.z);
        }

        private static string Quantize(float value)
        {
            return Math.Round(value, 5, MidpointRounding.AwayFromZero).ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static int MeshTriangleCount(Mesh mesh)
        {
            ulong indexCount = 0;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++) indexCount += mesh.GetIndexCount(subMesh);
            ulong triangles = indexCount / 3UL;
            return triangles > int.MaxValue ? int.MaxValue : (int)triangles;
        }

        private static string Hash(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var result = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++) result.Append(bytes[i].ToString("x2"));
                return result.ToString();
            }
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ValheimKnowledgeInventoryZNetScenePatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ZNetScene __instance)
        {
            ValheimKnowledgeInventoryExporter.Export(__instance);
        }
    }

    [HarmonyPatch(typeof(FejdStartup), "Start")]
    internal static class ValheimKnowledgeInventoryStartupPatch
    {
        [HarmonyPostfix]
        private static void Postfix(FejdStartup __instance)
        {
            if (__instance != null) __instance.StartCoroutine(ValheimKnowledgeInventoryExporter.ExportLoadedResourcesAfterStartup());
        }
    }
}
