using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using UnityEngine;

#pragma warning disable 0649 // JsonUtility populates serialized fields from the embedded database.
namespace PhysicalWater
{
    internal sealed class ValheimKnowledgeDatabase
    {
        internal const int SupportedSchemaVersion = 1;
        internal const string ResourceName = "PhysicalWater.knowledge.valheim-knowledge-v1.json";

        [Serializable]
        internal sealed class Document
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

        [Serializable] internal sealed class ValheimIdentity { public string steamAppId; public string steamBuildId; public string assemblyName; public string assemblySha256; public int assemblyClassCount; }
        [Serializable] internal sealed class ModSetIdentity { public string fingerprintAlgorithm; public string fingerprint; public int pluginCount; }
        [Serializable] internal sealed class CacheContract { public string asset; public string instance; public string prepared; public string reuse; public string invalidate; public string remove; }
        [Serializable]
        internal sealed class TypeRule
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
        internal sealed class SignalRule
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
        internal sealed class AssetRule
        {
            public string assetId;
            public string observedRootType;
            public string category;
            public string geometryKind;
            public string source;
            public bool precompute;
            public int colliderCount;
            public int meshColliderCount;
            public int primitiveColliderCount;
            public int triggerColliderCount;
            public int meshVertices;
            public int meshTriangles;
            public string geometrySignature;
            public string[] componentTypes;
            public float[] localBoundsCenter;
            public float[] localBoundsSize;
            public bool destructible;
            public bool buildPiece;
            public bool door;
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
                        database.Data = JsonUtility.FromJson<Document>(reader.ReadToEnd());
                    }
                }

                if (database.Data == null || database.Data.schemaVersion != SupportedSchemaVersion)
                    throw new InvalidOperationException("unsupported schema version");

                string gameAssemblyHash = HashFile(typeof(TerrainComp).Assembly.Location);
                if (database.Data.valheim == null ||
                    !string.Equals(gameAssemblyHash, database.Data.valheim.assemblySha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("installed Valheim assembly fingerprint does not match the database");

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
                    if (rule != null && !string.IsNullOrEmpty(rule.assetId)) _assets[rule.assetId] = rule;
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
            Vector3 localBoundsCenter,
            Vector3 localBoundsSize,
            bool destructible,
            bool buildPiece,
            bool door)
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
                localBoundsCenter = new[] { localBoundsCenter.x, localBoundsCenter.y, localBoundsCenter.z },
                localBoundsSize = new[] { localBoundsSize.x, localBoundsSize.y, localBoundsSize.z },
                destructible = destructible,
                buildPiece = buildPiece,
                door = door
            };
            _assets.Add(assetId, rule);
            _learnedAssets.Add(rule);
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
                File.WriteAllText(_learnedPath, JsonUtility.ToJson(overlay, true));
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
                  ", types=" + _types.Count + ", signals=" + _signals.Count + ", assets=" + _assets.Count;
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
                Document overlay = JsonUtility.FromJson<Document>(File.ReadAllText(_learnedPath));
                if (overlay == null || overlay.schemaVersion != Data.schemaVersion || overlay.valheim == null || overlay.modSet == null ||
                    !string.Equals(overlay.valheim.assemblySha256, Data.valheim.assemblySha256, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(overlay.modSet.fingerprint, Data.modSet.fingerprint, StringComparison.OrdinalIgnoreCase)) return;
                if (overlay.observedAssets == null) return;
                for (int i = 0; i < overlay.observedAssets.Length; i++)
                {
                    AssetRule rule = overlay.observedAssets[i];
                    if (rule == null || string.IsNullOrEmpty(rule.assetId) || _assets.ContainsKey(rule.assetId)) continue;
                    _assets.Add(rule.assetId, rule);
                    _learnedAssets.Add(rule);
                }
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log?.LogWarning("LiquidCore ignored invalid learned Valheim knowledge: " + ex.Message);
            }
        }

        private static string HashFile(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
                return ToHex(sha.ComputeHash(stream));
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
