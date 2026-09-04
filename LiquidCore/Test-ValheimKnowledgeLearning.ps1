[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$databasePath = Join-Path $PSScriptRoot 'src\ValheimKnowledgeDatabase.cs'
$databaseSource = Get-Content -LiteralPath $databasePath -Raw
$databaseBody = $databaseSource -replace '(?m)^using .*(\r?\n)', ''
$validationRoot = Join-Path $env:LOCALAPPDATA 'R4V9N1\LiquidCore\validation'
New-Item -ItemType Directory -Force -Path $validationRoot | Out-Null
$runRoot = Join-Path $validationRoot ("knowledge-learning-{0}" -f [Guid]::NewGuid().ToString('N'))
$overlayPath = Join-Path $runRoot 'LiquidCore\valheim-knowledge-learned-v1.json'

$stubs = @'
namespace BepInEx
{
    public static class Paths
    {
        public static string GameRootPath = "";
        public static string PluginPath = "";
        public static string ConfigPath = "";
    }
}

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
}

namespace PhysicalWater
{
    public sealed class ValidationLogger
    {
        public void LogWarning(object value) { }
    }

    public static class PhysicalWaterPlugin
    {
        public static ValidationLogger Log = new ValidationLogger();
    }
}
'@

$harness = @'
namespace PhysicalWater
{
    public sealed class KnowledgeLearningBenchmarkResult
    {
        public bool FirstMiss;
        public bool Learned;
        public bool SecondHit;
        public bool RestartHit;
        public bool DuplicateCollapsed;
        public bool LegacyIncompleteSkipped;
        public double FirstEncounterMilliseconds;
        public double SecondLookupNanoseconds;
        public double RestartLoadMilliseconds;
        public double RestartLookupNanoseconds;
        public long OverlayBytes;
    }

    public static class KnowledgeLearningHarness
    {
        public static KnowledgeLearningBenchmarkResult Run(string overlayPath)
        {
            const string assetId = "LiquidCore_UnknownLearningFixture";
            var fixtureRecipes = new[]
            {
                new ValheimKnowledgeDatabase.ColliderRecipe
                {
                    transformChildIndices = new int[0],
                    colliderComponentIndex = 0,
                    colliderType = "BoxCollider",
                    enabled = true,
                    isTrigger = false,
                    center = new[] { 0f, 0.5f, 0f },
                    size = new[] { 1f, 1f, 1f }
                }
            };
            BepInEx.Paths.ConfigPath = System.IO.Directory.GetParent(
                System.IO.Path.GetDirectoryName(overlayPath)).FullName;

            WriteLegacyIncompleteOverlay(overlayPath, assetId);
            ValheimKnowledgeDatabase first = Create(overlayPath);
            typeof(ValheimKnowledgeDatabase).GetMethod(
                "LoadLearnedAssets",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(first, null);
            ValheimKnowledgeDatabase.AssetRule legacyRule;
            bool legacyIncompleteSkipped = !first.TryGetReusableGeometryAsset(assetId, out legacyRule);
            var firstWatch = System.Diagnostics.Stopwatch.StartNew();
            ValheimKnowledgeDatabase.AssetRule rule;
            bool firstMiss = !first.TryGetReusableGeometryAsset(assetId, out rule);
            bool learned = first.LearnAsset(
                assetId,
                "Piece",
                "SolidBarrier",
                "BoxCollider",
                1, 0, 1, 0, 8, 12,
                "BoxCollider:1:0:1:0:8:12:fixture",
                new[] { "Piece", "BoxCollider" },
                new[] { "BoxCollider" },
                new UnityEngine.Vector3(0f, 0.5f, 0f),
                new UnityEngine.Vector3(1f, 1f, 1f),
                false, true, false, fixtureRecipes);
            firstWatch.Stop();

            var secondWatch = System.Diagnostics.Stopwatch.StartNew();
            bool secondHit = false;
            for (int i = 0; i < 100000; i++)
                secondHit = first.TryGetReusableGeometryAsset(assetId, out rule);
            secondWatch.Stop();
            first.FlushLearnedAssets();

            var restartWatch = System.Diagnostics.Stopwatch.StartNew();
            ValheimKnowledgeDatabase restarted = Create(overlayPath);
            typeof(ValheimKnowledgeDatabase).GetMethod(
                "LoadLearnedAssets",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(restarted, null);
            restartWatch.Stop();
            var restartLookupWatch = System.Diagnostics.Stopwatch.StartNew();
            bool restartHit = false;
            for (int i = 0; i < 100000; i++)
                restartHit = restarted.TryGetReusableGeometryAsset(assetId, out rule);
            restartLookupWatch.Stop();
            bool duplicateCollapsed = !restarted.LearnAsset(
                assetId, "Piece", "SolidBarrier", "BoxCollider",
                1, 0, 1, 0, 8, 12, "duplicate", new[] { "Piece", "BoxCollider" },
                new[] { "BoxCollider" },
                new UnityEngine.Vector3(), new UnityEngine.Vector3(1f, 1f, 1f), false, true, false, fixtureRecipes);

            return new KnowledgeLearningBenchmarkResult
            {
                FirstMiss = firstMiss,
                Learned = learned,
                SecondHit = secondHit,
                RestartHit = restartHit,
                DuplicateCollapsed = duplicateCollapsed,
                LegacyIncompleteSkipped = legacyIncompleteSkipped,
                FirstEncounterMilliseconds = firstWatch.Elapsed.TotalMilliseconds,
                SecondLookupNanoseconds = secondWatch.Elapsed.TotalMilliseconds * 1000000.0 / 100000.0,
                RestartLoadMilliseconds = restartWatch.Elapsed.TotalMilliseconds,
                RestartLookupNanoseconds = restartLookupWatch.Elapsed.TotalMilliseconds * 1000000.0 / 100000.0,
                OverlayBytes = new System.IO.FileInfo(overlayPath).Length
            };
        }

        private static void WriteLegacyIncompleteOverlay(string overlayPath, string assetId)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(overlayPath));
            var document = new ValheimKnowledgeDatabase.Document
            {
                schemaVersion = 1,
                databaseId = "legacy-incomplete-overlay",
                valheim = new ValheimKnowledgeDatabase.ValheimIdentity { assemblySha256 = "game-fixture" },
                modSet = new ValheimKnowledgeDatabase.ModSetIdentity { fingerprint = "mods-fixture" },
                observedAssets = new[]
                {
                    new ValheimKnowledgeDatabase.AssetRule
                    {
                        assetId = assetId,
                        precompute = true,
                        runtimeInspectionRequired = false,
                        colliderCount = 1,
                        triggerColliderCount = 0,
                        colliderTypes = new[] { "BoxCollider" },
                        localBoundsCenter = new[] { 0f, 0.5f, 0f },
                        localBoundsSize = new[] { 1f, 1f, 1f },
                        geometrySignature = "legacy-without-exact-recipe"
                    }
                }
            };
            using (var stream = System.IO.File.Create(overlayPath))
            {
                new System.Runtime.Serialization.Json.DataContractJsonSerializer(
                    typeof(ValheimKnowledgeDatabase.Document)).WriteObject(stream, document);
            }
        }

        private static ValheimKnowledgeDatabase Create(string overlayPath)
        {
            var database = new ValheimKnowledgeDatabase();
            var document = new ValheimKnowledgeDatabase.Document
            {
                schemaVersion = 1,
                databaseId = "liquidcore-learning-validation",
                valheim = new ValheimKnowledgeDatabase.ValheimIdentity { assemblySha256 = "game-fixture" },
                modSet = new ValheimKnowledgeDatabase.ModSetIdentity { fingerprint = "mods-fixture" },
                observedAssets = new ValheimKnowledgeDatabase.AssetRule[0]
            };
            System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                                                   System.Reflection.BindingFlags.NonPublic |
                                                   System.Reflection.BindingFlags.Public;
            typeof(ValheimKnowledgeDatabase).GetProperty("Data", flags).SetValue(database, document, null);
            typeof(ValheimKnowledgeDatabase).GetProperty("Loaded", flags).SetValue(database, true, null);
            typeof(ValheimKnowledgeDatabase).GetField("_learnedPath", flags).SetValue(database, overlayPath);
            return database;
        }
    }
}
'@

try {
    $usings = @'
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
'@
    Add-Type -TypeDefinition ($usings + "`n" + $stubs + "`n" + $databaseBody + "`n" + $harness) -Language CSharp
    $result = [PhysicalWater.KnowledgeLearningHarness]::Run($overlayPath)
    $pass = $result.FirstMiss -and $result.Learned -and $result.SecondHit -and
            $result.RestartHit -and $result.DuplicateCollapsed -and
            $result.LegacyIncompleteSkipped -and $result.OverlayBytes -gt 0
    [pscustomobject]@{
        Result = if ($pass) { 'PASS' } else { 'FAIL' }
        K_UnknownFirst = "miss=True inspect+learn=$([math]::Round($result.FirstEncounterMilliseconds, 4))ms"
        L_SamePrefabSecond = "databaseHit=$($result.SecondHit) meanLookup=$([math]::Round($result.SecondLookupNanoseconds, 1))ns"
        M_RestartHit = "fingerprintOverlayHit=$($result.RestartHit) load=$([math]::Round($result.RestartLoadMilliseconds, 4))ms meanLookup=$([math]::Round($result.RestartLookupNanoseconds, 1))ns"
        DuplicateNoOp = $result.DuplicateCollapsed
        LegacyIncompleteOverlaySkipped = $result.LegacyIncompleteSkipped
        OverlayBytes = $result.OverlayBytes
    } | Format-List
    if (!$pass) { exit 2 }
}
finally {
    $resolvedValidationRoot = [IO.Path]::GetFullPath($validationRoot)
    $resolvedRunRoot = [IO.Path]::GetFullPath($runRoot)
    if (!$resolvedRunRoot.StartsWith($resolvedValidationRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove a learning fixture outside the validation root.'
    }
    if (Test-Path -LiteralPath $resolvedRunRoot) { Remove-Item -LiteralPath $resolvedRunRoot -Recurse -Force }
}
