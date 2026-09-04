using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using UnityEngine;

#pragma warning disable 0649 // DataContractJsonSerializer populates serialized fields from the embedded database.
namespace PhysicalWater
{
    public sealed class ValheimKnowledgeDatabase
    {
        internal const int SupportedSchemaVersion = 1;
        internal const string ResourceName = "PhysicalWater.knowledge.valheim-knowledge-v1.json";

        [Serializable]
        public sealed class Document
        {
            public int schemaVersion;
            public string databaseId;
            public string generatedUtc;
            public ValheimIdentity valheim;
            public ModSetIdentity modSet;
            public CacheContract cacheContract;
            public TypeRule[] typeRules;
            public SignalRule[] signalRules;
            public AssetRule[] observedAssets;
        }

        // Keep database DTOs public so the runtime serializer can populate the
        // complete nested document and its incrementally learned overlay.
        [Serializable] public sealed class ValheimIdentity { public string steamAppId; public string steamBuildId; public string assemblyName; public string assemblySha256; public int assemblyClassCount; }
        [Serializable] public sealed class ModSetIdentity { public string fingerprintAlgorithm; public string fingerprint; public int pluginCount; }
        [Serializable] public sealed class CacheContract { public string asset; public string instance; public string prepared; public string reuse; public string invalidate; public string remove; }
        [Serializable]
        public sealed class TypeRule
        {
            public string type;
            public string category;
            public string geometryKind;
            public string staticClass;
            public bool canFeedSdf;
            public string precompute;
            public string runtimeInspection;
            public string[] stateFields;
        }

        [Serializable]
        public sealed class SignalRule
        {
            public string eventLabel;
            public string ownerType;
            public string change;
            public string authority;
            public bool immediate;
            public bool discoveryFallback;
            public string bounds;
            public string revision;
            public string pceRule;
            public string liquidCoreRule;
        }

        [Serializable]
        public sealed class ColliderRecipe
        {
            public int[] transformChildIndices;
            public int colliderComponentIndex;
            public string colliderType;
            public bool enabled;
            public bool isTrigger;
            [OptionalField] public string meshName;
            [OptionalField] public int meshVertexCount;
            [OptionalField] public int meshTriangleCount;
            [OptionalField] public bool convex;
            [OptionalField] public float[] center;
            [OptionalField] public float[] size;
            [OptionalField] public float radius;
            [OptionalField] public float height;
            [OptionalField] public int direction;
        }

        [Serializable]
        public sealed class AssetRule
        {
            public string assetId;
            public string observedRootType;
            public string category;
            public string geometryKind;
            public string source;
            public bool precompute;
            [OptionalField] public int colliderCount;
            [OptionalField] public int meshColliderCount;
            [OptionalField] public int primitiveColliderCount;
            [OptionalField] public int triggerColliderCount;
            [OptionalField] public int meshVertices;
            [OptionalField] public int meshTriangles;
            [OptionalField] public string geometrySignature;
            [OptionalField] public string[] componentTypes;
            [OptionalField] public string[] colliderTypes;
            [OptionalField] public ColliderRecipe[] colliderRecipes;
            [OptionalField] public float[] localBoundsCenter;
            [OptionalField] public float[] localBoundsSize;
            [OptionalField] public string staticClass;
            [OptionalField] public bool destructible;
            [OptionalField] public bool buildPiece;
            [OptionalField] public bool door;
            [OptionalField] public string[] stateFields;
            [OptionalField] public string[] authoritativeCallbacks;
            [OptionalField] public string pceRule;
            [OptionalField] public string liquidCoreRule;
            [OptionalField] public bool runtimeInspectionRequired;
        }

        private readonly Dictionary<string, TypeRule> _types = new Dictionary<string, TypeRule>(StringComparer.Ordinal);
        private readonly Dictionary<string, SignalRule> _signals = new Dictionary<string, SignalRule>(StringComparer.Ordinal);
        private readonly Dictionary<string, AssetRule> _assets = new Dictionary<string, AssetRule>(StringComparer.Ordinal);
        private readonly List<AssetRule> _learnedAssets = new List<AssetRule>();
        private string _learnedPath;
        private bool _learnedDirty;

        internal Document Data { get; private set; }
        internal bool Loaded { get; private set; }
        internal int AssetCacheHits { get; private set; }
        internal int AssetCacheMisses { get; private set; }
        internal int SignalCacheHits { get; private set; }
        internal int SignalCacheMisses { get; private set; }
        internal int ReusableGeometryDescriptorCount { get; private set; }
        internal int ExactColliderRecipeCount { get; private set; }
        internal int GeometryDescriptorCacheHits { get; private set; }
        internal int GeometryDescriptorCacheMisses { get; private set; }

        internal static ValheimKnowledgeDatabase LoadEmbedded()
        {
            var database = new ValheimKnowledgeDatabase();
            try
            {
                Assembly assembly = typeof(ValheimKnowledgeDatabase).Assembly;
                using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
                {
                    if (stream == null) throw new InvalidOperationException("missing embedded resource " + ResourceName);
                    using (var reader = new StreamReader(stream))
                    {
                        database.Data = DeserializeDocument(reader.ReadToEnd());
                    }
                }

                if (database.Data == null || database.Data.schemaVersion != SupportedSchemaVersion)
                    throw new InvalidOperationException("unsupported schema version");

                // Key persistent knowledge to the installed game artifact, not the
                // assembly image already loaded through BepInEx. A preloader may
                // rewrite/load an image whose bytes are no longer the immutable
                // Steam installation fingerprint used to build this database.
                string installedGameAssembly = Path.Combine(
                    Paths.GameRootPath,
                    "valheim_Data",
                    "Managed",
                    "assembly_valheim.dll");
                string gameAssemblyHash = HashFile(installedGameAssembly);
                if (database.Data.valheim == null ||
                    !string.Equals(gameAssemblyHash, database.Data.valheim.assemblySha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "installed Valheim assembly fingerprint does not match the database" +
                        "; path=" + installedGameAssembly +
                        "; actual=" + gameAssemblyHash +
                        "; expected=" + (database.Data.valheim != null ? database.Data.valheim.assemblySha256 : "<missing>"));

                string modSetFingerprint = ComputeModSetFingerprint(Paths.PluginPath);
                if (database.Data.modSet == null ||
                    !string.Equals(modSetFingerprint, database.Data.modSet.fingerprint, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("installed mod-set fingerprint does not match the database");

                database.Index();
                database.LoadLearnedAssets();
                database.Loaded = true;
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log?.LogWarning("LiquidCore Valheim knowledge database unavailable; safe runtime inspection fallback remains active: " + ex.Message);
            }
            return database;
        }

        private void Index()
        {
            if (Data.typeRules != null)
                foreach (TypeRule rule in Data.typeRules)
                    if (rule != null && !string.IsNullOrEmpty(rule.type)) _types[rule.type] = rule;
            if (Data.signalRules != null)
                foreach (SignalRule rule in Data.signalRules)
                    if (rule != null && !string.IsNullOrEmpty(rule.eventLabel)) _signals[rule.eventLabel] = rule;
            if (Data.observedAssets != null)
                foreach (AssetRule rule in Data.observedAssets)
                    if (rule != null && !string.IsNullOrEmpty(rule.assetId))
                    {
                        _assets[rule.assetId] = rule;
                        if (HasReusableGeometryDescriptor(rule))
                        {
                            ReusableGeometryDescriptorCount++;
                            ExactColliderRecipeCount += rule.colliderRecipes.Length;
                        }
                    }
        }

        internal bool TryGetSignal(string eventLabel, out SignalRule rule)
        {
            rule = null;
            bool found = Loaded && !string.IsNullOrEmpty(eventLabel) && _signals.TryGetValue(eventLabel, out rule);
            if (found) SignalCacheHits++; else SignalCacheMisses++;
            return found;
        }

        internal bool TryGetType(string typeName, out TypeRule rule)
        {
            rule = null;
            return Loaded && !string.IsNullOrEmpty(typeName) && _types.TryGetValue(typeName, out rule);
        }

        internal bool TryGetAsset(string assetId, out AssetRule rule)
        {
            rule = null;
            bool found = Loaded && !string.IsNullOrEmpty(assetId) && _assets.TryGetValue(assetId, out rule);
            if (found) AssetCacheHits++; else AssetCacheMisses++;
            return found;
        }

        internal bool ContainsAsset(string assetId)
        {
            return Loaded && !string.IsNullOrEmpty(assetId) && _assets.ContainsKey(assetId);
        }

        internal bool TryGetReusableGeometryAsset(string assetId, out AssetRule rule)
        {
            rule = null;
            bool found = Loaded && !string.IsNullOrEmpty(assetId) && _assets.TryGetValue(assetId, out rule) &&
                         HasReusableGeometryDescriptor(rule);
            if (found) GeometryDescriptorCacheHits++;
            else
            {
                GeometryDescriptorCacheMisses++;
                rule = null;
            }
            return found;
        }

        private static bool HasReusableGeometryDescriptor(AssetRule rule)
        {
            return rule != null && rule.precompute && !rule.runtimeInspectionRequired &&
                   rule.colliderCount > rule.triggerColliderCount &&
                   rule.localBoundsCenter != null && rule.localBoundsCenter.Length == 3 &&
                   rule.localBoundsSize != null && rule.localBoundsSize.Length == 3 &&
                   !string.IsNullOrEmpty(rule.geometrySignature) &&
                   rule.colliderTypes != null && rule.colliderTypes.Length > 0 &&
                   rule.colliderRecipes != null && rule.colliderRecipes.Length == rule.colliderCount;
        }

        internal bool LearnAsset(
            string assetId,
            string rootType,
            string category,
            string geometryKind,
            int colliderCount,
            int meshColliderCount,
            int primitiveColliderCount,
            int triggerColliderCount,
            int meshVertices,
            int meshTriangles,
            string geometrySignature,
            string[] componentTypes,
            string[] colliderTypes,
            Vector3 localBoundsCenter,
            Vector3 localBoundsSize,
            bool destructible,
            bool buildPiece,
            bool door,
            ColliderRecipe[] colliderRecipes = null)
        {
            if (!Loaded || string.IsNullOrEmpty(assetId) || _assets.ContainsKey(assetId)) return false;
            var rule = new AssetRule
            {
                assetId = assetId,
                observedRootType = rootType,
                category = category,
                geometryKind = geometryKind,
                source = "runtime-learned",
                precompute = true,
                colliderCount = colliderCount,
                meshColliderCount = meshColliderCount,
                primitiveColliderCount = primitiveColliderCount,
                triggerColliderCount = triggerColliderCount,
                meshVertices = meshVertices,
                meshTriangles = meshTriangles,
                geometrySignature = geometrySignature,
                componentTypes = componentTypes,
                colliderTypes = colliderTypes,
                colliderRecipes = colliderRecipes,
                localBoundsCenter = new[] { localBoundsCenter.x, localBoundsCenter.y, localBoundsCenter.z },
                localBoundsSize = new[] { localBoundsSize.x, localBoundsSize.y, localBoundsSize.z },
                destructible = destructible,
                buildPiece = buildPiece,
                door = door
            };
            _assets.Add(assetId, rule);
            _learnedAssets.Add(rule);
            if (HasReusableGeometryDescriptor(rule))
            {
                ReusableGeometryDescriptorCount++;
                ExactColliderRecipeCount += rule.colliderRecipes.Length;
            }
            _learnedDirty = true;
            return true;
        }

        internal void FlushLearnedAssets()
        {
            if (!Loaded || !_learnedDirty || string.IsNullOrEmpty(_learnedPath)) return;
            try
            {
                string directory = Path.GetDirectoryName(_learnedPath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                var overlay = new Document
                {
                    schemaVersion = Data.schemaVersion,
                    databaseId = Data.databaseId + "-learned",
                    generatedUtc = DateTime.UtcNow.ToString("O"),
                    valheim = Data.valheim,
                    modSet = Data.modSet,
                    observedAssets = _learnedAssets.ToArray()
                };
                using (var stream = File.Create(_learnedPath))
                    new DataContractJsonSerializer(typeof(Document)).WriteObject(stream, overlay);
                _learnedDirty = false;
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log?.LogWarning("LiquidCore could not persist learned Valheim knowledge: " + ex.Message);
            }
        }

        internal string Summary()
        {
            return !Loaded || Data == null
                ? "loaded=False"
                : "loaded=True, schema=" + Data.schemaVersion +
                  ", steamBuild=" + (Data.valheim != null ? Data.valheim.steamBuildId : "unknown") +
                  ", types=" + _types.Count + ", signals=" + _signals.Count + ", assets=" + _assets.Count +
                  ", reusableGeometryDescriptors=" + ReusableGeometryDescriptorCount +
                  ", exactColliderRecipes=" + ExactColliderRecipeCount;
        }

        internal string StartupCertification()
        {
            if (!Loaded || Data == null || Data.valheim == null || Data.modSet == null)
                return "fingerprint match unavailable; database-first serving inactive";

            SignalRule terrain;
            bool terrainReady = _signals.TryGetValue("heightmap terrain operation/regenerate", out terrain) &&
                                terrain != null && terrain.immediate &&
                                string.Equals(terrain.authority, "authoritative-local", StringComparison.Ordinal);
            return "MATCHED current game/mod/schema fingerprint: schema=" + Data.schemaVersion +
                   ", steamBuild=" + Data.valheim.steamBuildId +
                   ", assemblySha256=" + Data.valheim.assemblySha256 +
                   ", modSetSha256=" + Data.modSet.fingerprint +
                   "; known assets/terrain rules are being served from the database rather than runtime reinspection: knownAssetClassifications=" + _assets.Count +
                   " bypass hierarchy/category reinspection on hit, reusableAssetGeometry=" + ReusableGeometryDescriptorCount +
                   " descriptors with exactColliderAddressRecipes=" + ExactColliderRecipeCount +
                   " bypass collider/component hierarchy reinspection on immutable cache hits, terrainRules=" + (terrainReady ? "authoritative-local/immediate" : "INVALID") +
                   " bypass discovery; stateful and unknown assets retain targeted safe inspection fallback";
        }

        private static string ComputeModSetFingerprint(string pluginRoot)
        {
            if (string.IsNullOrEmpty(pluginRoot) || !Directory.Exists(pluginRoot)) return string.Empty;
            string[] files = Directory.GetFiles(pluginRoot, "*.dll", SearchOption.AllDirectories);
            Array.Sort(files, (left, right) => string.Compare(
                left.Substring(pluginRoot.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/'),
                right.Substring(pluginRoot.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/'),
                StringComparison.Ordinal));
            var rows = new List<string>(files.Length);
            for (int i = 0; i < files.Length; i++)
            {
                if (string.Equals(Path.GetFileName(files[i]), "LiquidCore.dll", StringComparison.OrdinalIgnoreCase)) continue;
                var info = new FileInfo(files[i]);
                string relative = files[i].Substring(pluginRoot.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/');
                rows.Add(relative + "|" + info.Length + "|" + HashFile(files[i]));
            }
            return HashBytes(Encoding.UTF8.GetBytes(string.Join("\n", rows.ToArray())));
        }

        private void LoadLearnedAssets()
        {
            _learnedPath = Path.Combine(Paths.ConfigPath, "LiquidCore", "valheim-knowledge-learned-v1.json");
            if (!File.Exists(_learnedPath)) return;
            try
            {
                Document overlay = DeserializeDocument(File.ReadAllText(_learnedPath));
                if (overlay == null || overlay.schemaVersion != Data.schemaVersion || overlay.valheim == null || overlay.modSet == null ||
                    !string.Equals(overlay.valheim.assemblySha256, Data.valheim.assemblySha256, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(overlay.modSet.fingerprint, Data.modSet.fingerprint, StringComparison.OrdinalIgnoreCase)) return;
                if (overlay.observedAssets == null) return;
                for (int i = 0; i < overlay.observedAssets.Length; i++)
                {
                    AssetRule rule = overlay.observedAssets[i];
                    if (rule == null || string.IsNullOrEmpty(rule.assetId) || _assets.ContainsKey(rule.assetId)) continue;
                    NormalizeLearnedAssetRule(rule);
                    // Legacy learned overlays predate exact collider-address recipes. Do not let an
                    // incomplete entry occupy the asset ID forever: skipping it makes the next real
                    // encounter perform the one authorized inspection and persist a current recipe.
                    if (!HasReusableGeometryDescriptor(rule)) continue;
                    _assets.Add(rule.assetId, rule);
                    _learnedAssets.Add(rule);
                    ReusableGeometryDescriptorCount++;
                    ExactColliderRecipeCount += rule.colliderRecipes.Length;
                }
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log?.LogWarning("LiquidCore ignored invalid learned Valheim knowledge: " + ex.Message);
            }
        }

        private static void NormalizeLearnedAssetRule(AssetRule rule)
        {
            if (rule == null || (rule.colliderTypes != null && rule.colliderTypes.Length > 0)) return;
            if (rule.meshColliderCount > 0 && rule.primitiveColliderCount > 0)
                rule.colliderTypes = new[] { "MeshCollider", "PrimitiveCollider" };
            else if (rule.meshColliderCount > 0)
                rule.colliderTypes = new[] { "MeshCollider" };
            else if (string.Equals(rule.geometryKind, "BoxCollider", StringComparison.Ordinal) ||
                     string.Equals(rule.geometryKind, "SphereCollider", StringComparison.Ordinal) ||
                     string.Equals(rule.geometryKind, "CapsuleCollider", StringComparison.Ordinal))
                rule.colliderTypes = new[] { rule.geometryKind };
            else if (rule.primitiveColliderCount > 0)
                rule.colliderTypes = new[] { "PrimitiveCollider" };
        }

        private static string HashFile(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
                return ToHex(sha.ComputeHash(stream));
        }

        private static Document DeserializeDocument(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (Document)new DataContractJsonSerializer(typeof(Document)).ReadObject(stream);
        }

        private static string HashBytes(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create()) return ToHex(sha.ComputeHash(bytes));
        }

        private static string ToHex(byte[] bytes)
        {
            var result = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) result.Append(bytes[i].ToString("x2"));
            return result.ToString();
        }
    }
}
#pragma warning restore 0649
