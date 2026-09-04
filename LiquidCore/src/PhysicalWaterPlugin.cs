using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

[assembly: AssemblyTitle(PhysicalWater.PhysicalWaterPlugin.PluginName)]
[assembly: AssemblyVersion(PhysicalWater.PhysicalWaterPlugin.PluginAssemblyVersion)]
[assembly: AssemblyFileVersion(PhysicalWater.PhysicalWaterPlugin.PluginAssemblyVersion)]
[assembly: AssemblyInformationalVersion(PhysicalWater.PhysicalWaterPlugin.PluginVersion)]
[assembly: AssemblyCompany("R4V9N1")]
[assembly: AssemblyProduct(PhysicalWater.PhysicalWaterPlugin.PluginName)]
[assembly: AssemblyDescription("Authoritative physics-based water replacement for Valheim.")]
[assembly: AssemblyMetadata("Creator", PhysicalWater.PhysicalWaterPlugin.CreatorCredit)]

namespace PhysicalWater
{
    [BepInPlugin(PluginGuid, PluginName, PluginBepInExVersion)]
    public sealed class PhysicalWaterPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "r4v9n1.physicalwater";
        public const string PluginName = "LiquidCore";
        public const string PluginVersion = "0.6.0-devE3.2-probe5";
        public const string PluginBepInExVersion = "0.6.0.21";
        public const string PluginAssemblyVersion = "0.6.0.21";
        public const string CreatorCredit = "Created by R4V9N1";

        internal static ManualLogSource Log;
        internal static PhysicalWaterSettings Settings;
        internal static ValheimKnowledgeDatabase ValheimKnowledge;

        private Harmony _harmony;
        private GameObject _systemObject;
        private GameObject _worldGeometryAdapterObject;
        private GameObject _pceRuntimeObject;
        private GameObject _devE1RuntimeObject;
        private GameObject _persistenceRuntimeObject;

        private void Awake()
        {
            Log = Logger;
            Settings = new PhysicalWaterSettings(Config);
            ValheimKnowledge = ValheimKnowledgeDatabase.LoadEmbedded();
            Logger.LogInfo("LiquidCore Valheim knowledge database: " + ValheimKnowledge.Summary() + ".");
            bool legacyReplacementEnabled = Settings.Enabled.Value && !Settings.StageE1Enabled.Value;
            if (Settings.Enabled.Value && Settings.StageE1Enabled.Value)
            {
                Logger.LogWarning("LiquidCore devE3 forced the legacy/global replacement OFF despite General.Enabled=true. E3 permits only explicitly seeded finite streaming fluid.");
            }

            _harmony = new Harmony(PluginGuid);
            if (legacyReplacementEnabled)
            {
                PatchRequired(typeof(FloatingGetLiquidLevelPatch), "Floating.GetLiquidLevel water-height override");
                PatchRequired(typeof(FloatingGetWaterLevelPatch), "Floating.GetWaterLevel water-height override");
                PatchRequired(typeof(FloatingIsUnderWaterPatch), "Floating.IsUnderWater override");
                PatchRequired(typeof(FloatingCustomFixedUpdatePatch), "Floating authoritative water physics replacement");
                PatchRequired(typeof(ShipCustomFixedUpdatePatch), "Ship vanilla-water physics suppression");
                PatchRequired(typeof(CharacterCustomFixedUpdatePatch), "Character liquid-level feed");
                PatchSafely(typeof(GameCameraGetCameraPositionPatch), "underwater camera clamp bypass");
                PatchRequired(typeof(WaterVolumeGetWaterSurfacePatch), "WaterVolume.GetWaterSurface suppression");
                PatchRequired(typeof(WaterVolumeAwakePatch), "vanilla WaterVolume visual suppression hook");
                PatchRequired(typeof(WaterVolumeStartPatch), "vanilla WaterVolume startup visual suppression hook");
                PatchRequired(typeof(WaterVolumeOnEnablePatch), "vanilla WaterVolume enable visual suppression hook");
                PatchRequired(typeof(WaterVolumeOnTriggerEnterPatch), "vanilla WaterVolume trigger-enter suppression hook");
                PatchRequired(typeof(WaterVolumeOnTriggerExitPatch), "vanilla WaterVolume trigger-exit suppression hook");
                PatchRequired(typeof(WaterVolumeUpdateFloatersPatch), "vanilla WaterVolume floater suppression hook");
                PatchRequired(typeof(LiquidSurfaceAwakePatch), "vanilla LiquidSurface visual suppression hook");
                PatchRequired(typeof(LiquidSurfaceGetSurfacePatch), "LiquidSurface.GetSurface water suppression");
                PatchRequired(typeof(LiquidSurfaceOnTriggerEnterPatch), "vanilla LiquidSurface trigger-enter suppression hook");
                PatchRequired(typeof(LiquidSurfaceOnTriggerExitPatch), "vanilla LiquidSurface trigger-exit suppression hook");
                PatchRequired(typeof(LiquidSurfaceFixedUpdatePatch), "vanilla LiquidSurface floater suppression hook");

                _systemObject = new GameObject("R4V9N1_PhysicalWaterSystem");
                DontDestroyOnLoad(_systemObject);
                _systemObject.AddComponent<PhysicalWaterSystem>();
            }

            bool geometryAdapterRequired = Settings.ValheimGeometryDiagnosticsEnabled.Value || Settings.StageE1Enabled.Value;
            if (geometryAdapterRequired)
            {
                PatchSafely(typeof(ValheimGeometryTerrainOperationPatch), "devD6 terrain geometry dirty marker");
                PatchSafely(typeof(ValheimGeometryHeightmapRegeneratePatch), "devD6 heightmap regenerate dirty marker");
                PatchSafely(typeof(ValheimGeometryHeightmapPokePatch), "devD6 heightmap poke dirty marker");
                PatchSafely(typeof(ValheimGeometryPiecePlacedPatch), "devD6 placed-piece geometry dirty marker");
                PatchSafely(typeof(ValheimGeometryNetworkDestroyedPatch), "devD6 authoritative network-geometry destroy marker");
                PatchSafely(typeof(ValheimGeometryDoorStatePatch), "devD6 door/gate geometry dirty marker");
                PatchSafely(typeof(ValheimGeometryDestructibleDestroyedPatch), "devD6 destructible geometry dirty marker");
                PatchSafely(typeof(ValheimGeometryMineRockHiddenPatch), "devD6 mine-rock geometry dirty marker");
                PatchSafely(typeof(ValheimGeometryMineRock5MeshPatch), "devD6 MineRock5 geometry dirty marker");
            }

            if (geometryAdapterRequired)
            {
                _worldGeometryAdapterObject = new GameObject("R4V9N1_PhysicalWaterDevD6WorldGeometryAdapter");
                DontDestroyOnLoad(_worldGeometryAdapterObject);
                _worldGeometryAdapterObject.AddComponent<PhysicalWaterValheimWorldGeometryAdapter>();
                _pceRuntimeObject = new GameObject("R4V9N1_LiquidCorePceRuntime");
                DontDestroyOnLoad(_pceRuntimeObject);
                _pceRuntimeObject.AddComponent<LiquidCorePceRuntime>();
            }

            if (Settings.StageE1Enabled.Value)
            {
                PatchSafely(typeof(PhysicalWaterPersistenceWorldSetupPatch), "Valheim world-load persistence boundary");
                _devE1RuntimeObject = new GameObject("R4V9N1_PhysicalWaterDevE1Runtime");
                DontDestroyOnLoad(_devE1RuntimeObject);
                _devE1RuntimeObject.AddComponent<PhysicalWaterDevE1Runtime>();
                _persistenceRuntimeObject = new GameObject("R4V9N1_LiquidCorePersistenceRuntime");
                DontDestroyOnLoad(_persistenceRuntimeObject);
                _persistenceRuntimeObject.AddComponent<PhysicalWaterPersistenceRuntime>();
            }

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded in " +
                           (legacyReplacementEnabled ? "enabled" : "disabled") +
                           " mode. devD6 Valheim geometry diagnostics are " +
                           (geometryAdapterRequired ? "enabled" : "disabled") +
                           ". While the legacy replacement is enabled, vanilla water rendering/queries/floaters are unconditionally suppressed.");
            Logger.LogInfo("LiquidCore 0.6.0-devE3 adds conservative logical-region streaming around the frozen E1 solver/math and E2.1.2 presentation. FiniteStreaming=" + Settings.StageE1Enabled.Value + ", legacyReplacement=" + legacyReplacementEnabled + ".");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
            {
                try
                {
                    _harmony.UnpatchSelf();
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Could not unpatch PhysicalWater cleanly: " + ex.Message);
                }

                _harmony = null;
            }

            if (_systemObject != null)
            {
                Destroy(_systemObject);
                _systemObject = null;
            }

            if (_worldGeometryAdapterObject != null)
            {
                Destroy(_worldGeometryAdapterObject);
                _worldGeometryAdapterObject = null;
            }

            if (_devE1RuntimeObject != null)
            {
                Destroy(_devE1RuntimeObject);
                _devE1RuntimeObject = null;
            }

            if (_persistenceRuntimeObject != null)
            {
                Destroy(_persistenceRuntimeObject);
                _persistenceRuntimeObject = null;
            }
        }

        private void PatchRequired(Type patchType, string label)
        {
            try
            {
                _harmony.CreateClassProcessor(patchType).Patch();
            }
            catch (Exception ex)
            {
                Logger.LogError("Required PhysicalWater hook failed: " + label + ": " + ex.Message);
                throw new InvalidOperationException("PhysicalWater refuses to run with a missing authoritative/no-vanilla hook: " + label, ex);
            }
        }

        private void PatchSafely(Type patchType, string label)
        {
            try
            {
                _harmony.CreateClassProcessor(patchType).Patch();
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not install " + label + "; continuing without that hook: " + ex.Message);
            }
        }
    }

    internal sealed class PhysicalWaterSettings
    {
        internal readonly ConfigEntry<bool> Enabled;
        internal readonly ConfigEntry<bool> DryOceanFloorBaseline;
        internal readonly ConfigEntry<bool> RenderPreviewSurface;
        internal readonly ConfigEntry<bool> OverrideWaterQueries;
        internal readonly ConfigEntry<bool> PhysicalWaterInteractionEnabled;
        internal readonly ConfigEntry<bool> FeedFloatingLiquidLevel;
        internal readonly ConfigEntry<bool> FeedCharactersLiquidLevel;
        internal readonly ConfigEntry<bool> HideVanillaWaterRenderers;
        internal readonly ConfigEntry<bool> SuppressVanillaWaterVolumeFloaters;
        internal readonly ConfigEntry<bool> ReactToFloatingObjects;
        internal readonly ConfigEntry<bool> DisableUnderwaterCameraClamp;
        internal readonly ConfigEntry<float> UnderwaterCameraFreeDepth;
        internal readonly ConfigEntry<float> SeaLevel;
        internal readonly ConfigEntry<float> FollowRadius;
        internal readonly ConfigEntry<float> OriginSnapMeters;
        internal readonly ConfigEntry<bool> TerrainMaskEnabled;
        internal readonly ConfigEntry<bool> RequireOceanConnection;
        internal readonly ConfigEntry<float> ShorelineDryMargin;
        internal readonly ConfigEntry<float> ShorelineVisualBand;
        internal readonly ConfigEntry<float> VisualBoundarySinkMeters;
        internal readonly ConfigEntry<float> VisualBoundarySinkDepth;
        internal readonly ConfigEntry<bool> FarOceanEnabled;
        internal readonly ConfigEntry<float> FarOceanRadius;
        internal readonly ConfigEntry<float> FarOceanInnerRadius;
        internal readonly ConfigEntry<int> FarOceanResolution;
        internal readonly ConfigEntry<bool> UseVanillaWaterMaterial;
        internal readonly ConfigEntry<bool> DebugOpaqueWater;
        internal readonly ConfigEntry<bool> ShorelineEffectsEnabled;
        internal readonly ConfigEntry<float> ShorelineEffectsInterval;
        internal readonly ConfigEntry<int> ShorelineEffectsBudget;
        internal readonly ConfigEntry<int> GridResolution;
        internal readonly ConfigEntry<float> WaveSpeed;
        internal readonly ConfigEntry<float> Damping;
        internal readonly ConfigEntry<float> WindWaveAmplitude;
        internal readonly ConfigEntry<float> WindWaveLength;
        internal readonly ConfigEntry<float> MaxSimulatedDisplacement;
        internal readonly ConfigEntry<float> ObjectDisturbanceScale;
        internal readonly ConfigEntry<float> CharacterDisturbanceScale;
        internal readonly ConfigEntry<bool> ShipBuoyancyAssist;
        internal readonly ConfigEntry<float> ShipBuoyancyAcceleration;
        internal readonly ConfigEntry<float> ShipSurfaceLift;
        internal readonly ConfigEntry<float> ShipMaxLiftDepth;
        internal readonly ConfigEntry<float> ShipVerticalDamping;
        internal readonly ConfigEntry<float> ShipWaterDrag;
        internal readonly ConfigEntry<float> ShipWakeStrength;
        internal readonly ConfigEntry<bool> ShipUprightAssist;
        internal readonly ConfigEntry<float> ShipUprightTorque;
        internal readonly ConfigEntry<float> ShipAngularDamping;
        internal readonly ConfigEntry<float> ShipMaxAngularVelocity;
        internal readonly ConfigEntry<bool> SmallCreatureSwimAssist;
        internal readonly ConfigEntry<float> SmallCreatureMaxHeight;
        internal readonly ConfigEntry<float> SmallCreatureSwimDepthFactor;
        internal readonly ConfigEntry<float> BuoyancyProbeRadius;
        internal readonly ConfigEntry<float> FloatingProbeFallbackRadius;
        internal readonly ConfigEntry<float> CharacterWaterFeedInterval;
        internal readonly ConfigEntry<bool> Diagnostics;
        internal readonly ConfigEntry<bool> ValheimGeometryDiagnosticsEnabled;
        internal readonly ConfigEntry<float> ValheimGeometryScanRadius;
        internal readonly ConfigEntry<float> ValheimGeometryScanInterval;
        internal readonly ConfigEntry<float> ValheimGeometryFullConsistencyInterval;
        internal readonly ConfigEntry<int> ValheimGeometryDiscoveryChunksPerScan;
        internal readonly ConfigEntry<float> ValheimGeometryCellSize;
        internal readonly ConfigEntry<int> ValheimGeometryDirtyPaddingCells;
        internal readonly ConfigEntry<bool> ValheimGeometryLogRejected;
        internal readonly ConfigEntry<bool> ValheimGeometryDebugVisualization;
        internal readonly ConfigEntry<float> ValheimGeometryChunkSize;
        internal readonly ConfigEntry<float> ValheimGeometryDiscoveryTileSize;
        internal readonly ConfigEntry<float> ValheimGeometryOriginSnapMeters;
        internal readonly ConfigEntry<float> ValheimGeometryTerrainSampleSpacing;
        internal readonly ConfigEntry<int> ValheimGeometryTerrainMaxSamplesPerScan;
        internal readonly ConfigEntry<float> ValheimGeometryFrameBudgetMilliseconds;
        internal readonly ConfigEntry<float> ValheimGeometryIncrementalBudgetMilliseconds;
        internal readonly ConfigEntry<bool> StageE1Enabled;
        internal readonly ConfigEntry<bool> StageE1RenderSurface;
        internal readonly ConfigEntry<float> StageE1TelemetryInterval;
        internal readonly ConfigEntry<int> StageE1MaxSubstepsPerFrame;

        internal PhysicalWaterSettings(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", false,
                "Master switch for the PhysicalWater prototype.");
            DryOceanFloorBaseline = config.Bind("General", "DryOceanFloorBaseline", false,
                "Hard baseline mode: remove vanilla water influence and report all water as dry so the ocean floor is walkable. Use this to prove vanilla water is gone before rebuilding PhysicalWater.");
            RenderPreviewSurface = config.Bind("General", "RenderPreviewSurface", true,
                "Render the prototype physical water mesh around the local player.");
            OverrideWaterQueries = config.Bind("Integration", "OverrideWaterQueries", true,
                "Route all water-height queries to PhysicalWater while the replacement is enabled. Vanilla water never supplies the active water height.");
            PhysicalWaterInteractionEnabled = config.Bind("Integration", "PhysicalWaterInteractionEnabled", true,
                "Let PhysicalWater affect player, creature, ship, and floating-object water physics. Keep false while validating the visual fill surface.");
            FeedFloatingLiquidLevel = config.Bind("Integration", "FeedFloatingLiquidLevel", true,
                "Feed floating objects from the PhysicalWater surface. Vanilla WaterVolume flotation is suppressed while PhysicalWater is enabled.");
            FeedCharactersLiquidLevel = config.Bind("Integration", "FeedCharactersLiquidLevel", true,
                "Replacement test mode: actively feed player/creature liquid level from the PhysicalWater surface.");
            HideVanillaWaterRenderers = config.Bind("Integration", "HideVanillaWaterRenderers", true,
                "Legacy compatibility flag. Ignored while PhysicalWater is enabled: vanilla water renderers are always suppressed.");
            SuppressVanillaWaterVolumeFloaters = config.Bind("Integration", "SuppressVanillaWaterVolumeFloaters", true,
                "Replacement test mode: stop vanilla WaterVolume.UpdateFloaters from feeding liquid levels so PhysicalWater is the active water source.");
            ReactToFloatingObjects = config.Bind("Integration", "ReactToFloatingObjects", true,
                "Let vanilla Floating objects disturb the prototype water heightfield when they are near the physical surface.");
            DisableUnderwaterCameraClamp = config.Bind("Camera", "DisableUnderwaterCameraClamp", true,
                "Let the camera follow the player below the PhysicalWater surface instead of being pinned above the waterline by Valheim's vanilla camera clamp.");
            UnderwaterCameraFreeDepth = config.Bind("Camera", "UnderwaterCameraFreeDepth", 0.65f,
                "Meters the local player eye must be below the PhysicalWater surface before the underwater camera clamp is bypassed.");
            SeaLevel = config.Bind("WaterShape", "SeaLevel", 30f,
                "Base water surface height. Valheim's ocean is roughly around Y=30.");
            FollowRadius = config.Bind("WaterShape", "FollowRadius", 120f,
                "Half-size of the LOCAL physics/interaction field. 0.5.1 renders the long-range ocean through separate concentric LOD geometry.");
            OriginSnapMeters = config.Bind("WaterShape", "OriginSnapMeters", 48f,
                "Meters the local physics/interaction field snaps by when following the player.");
            TerrainMaskEnabled = config.Bind("WaterShape", "TerrainMaskEnabled", true,
                "0.5.1 geometry input: terrain and static solids define the hydrodynamic bed/barriers; elevation alone never creates water.");
            RequireOceanConnection = config.Bind("WaterShape", "RequireOceanConnection", true,
                "Legacy compatibility flag. 0.5.1 hydrodynamics always conserves water and seeds only genuine open-ocean boundary cells.");
            ShorelineDryMargin = config.Bind("WaterShape", "ShorelineDryMargin", 0.04f,
                "Meters below calm sea level that terrain must be before PhysicalWater considers that point ocean. Raise if water creeps onto shore.");
            ShorelineVisualBand = config.Bind("WaterShape", "ShorelineVisualBand", 2.25f,
                "Extra meters above sea level used only to draw shoreline transition triangles. This is visual overlap, not physics water.");
            VisualBoundarySinkMeters = config.Bind("WaterShape", "VisualBoundarySinkMeters", 96f,
                "Outer meters of the preview grid that are pushed below terrain to hide the prototype's finite square edge.");
            VisualBoundarySinkDepth = config.Bind("WaterShape", "VisualBoundarySinkDepth", 18f,
                "Meters below sampled terrain used at the outer edge of the preview grid.");
            FarOceanEnabled = config.Bind("WaterShape", "FarOceanEnabled", true,
                "Render an ultra-cheap far-ocean visual impostor beyond the near PhysicalWater tile. This is visual only and has no physics.");
            FarOceanRadius = config.Bind("WaterShape", "FarOceanRadius", 2200f,
                "Half-size in meters of the cheap far-ocean visual impostor around the local client.");
            FarOceanInnerRadius = config.Bind("WaterShape", "FarOceanInnerRadius", 300f,
                "Inner half-size in meters cut out of the far-ocean impostor so the near PhysicalWater tile owns the close water.");
            FarOceanResolution = config.Bind("WaterShape", "FarOceanResolution", 65,
                new ConfigDescription("Vertex count per side for the cheap far-ocean impostor.",
                    new AcceptableValueRange<int>(9, 65)));
            UseVanillaWaterMaterial = config.Bind("Visuals", "UseVanillaWaterMaterial", false,
                "Legacy option retained for config compatibility only. 0.5.1 never adopts or renders a Valheim water material.");
            DebugOpaqueWater = config.Bind("Visuals", "DebugOpaqueWater", false,
                "Render PhysicalWater with a deliberately obvious opaque blue test material so visibility problems are easy to diagnose.");
            ShorelineEffectsEnabled = config.Bind("Visuals", "ShorelineEffectsEnabled", false,
                "Legacy CPU shoreline particle toggle retained for compatibility. 0.5.1 continuous bathymetry foam is GPU-owned and ignores this option.");
            ShorelineEffectsInterval = config.Bind("Visuals", "ShorelineEffectsInterval", 0.32f,
                "Seconds between shoreline foam emission passes. Higher values cost less CPU.");
            ShorelineEffectsBudget = config.Bind("Visuals", "ShorelineEffectsBudget", 0,
                new ConfigDescription("Maximum shoreline foam particles emitted per shoreline pass.",
                    new AcceptableValueRange<int>(0, 320)));
            GridResolution = config.Bind("WaterShape", "GridResolution", 161,
                new ConfigDescription("Vertex count per side for the local water grid. Odd numbers keep the player centered on a vertex.",
                    new AcceptableValueRange<int>(17, 257)));
            WaveSpeed = config.Bind("Simulation", "WaveSpeed", 8.5f,
                "Speed used by the heightfield wave equation.");
            Damping = config.Bind("Simulation", "Damping", 0.965f,
                "Wave damping per simulation step. Lower values settle faster.");
            WindWaveAmplitude = config.Bind("Simulation", "WindWaveAmplitude", 0.72f,
                "Procedural wind-wave height added to the simulated water.");
            WindWaveLength = config.Bind("Simulation", "WindWaveLength", 38f,
                "Approximate wavelength for procedural wind waves.");
            MaxSimulatedDisplacement = config.Bind("Simulation", "MaxSimulatedDisplacement", 0.16f,
                "Maximum positive/negative heightfield displacement before procedural waves are added.");
            ObjectDisturbanceScale = config.Bind("Simulation", "ObjectDisturbanceScale", 0.16f,
                "Multiplier for floating-object visual disturbance energy. In the non-jelly ocean pass this drives foam/splash feedback, not gameplay surface height.");
            CharacterDisturbanceScale = config.Bind("Simulation", "CharacterDisturbanceScale", 0.18f,
                "Multiplier for character/player visual disturbance energy. In the non-jelly ocean pass this drives foam/splash feedback, not gameplay surface height.");
            ShipBuoyancyAssist = config.Bind("ShipPhysics", "ShipBuoyancyAssist", true,
                "Enable authoritative PhysicalWater hydrostatic hull-probe buoyancy. 0.5.1 suppresses vanilla ship/floater water forces and applies one bounded PhysicalWater solver.");
            ShipBuoyancyAcceleration = config.Bind("ShipPhysics", "ShipBuoyancyAcceleration", 48.0f,
                "Vertical correction strength for stable ship flotation when the boat center is below its target PhysicalWater ride height.");
            ShipSurfaceLift = config.Bind("ShipPhysics", "ShipSurfaceLift", 0.15f,
                "Meters added to the sampled PhysicalWater surface for ship buoyancy probes so hulls ride on the surface instead of half-sinking.");
            ShipMaxLiftDepth = config.Bind("ShipPhysics", "ShipMaxLiftDepth", 4.0f,
                "Maximum ride-height error considered by the stable ship flotation controller.");
            ShipVerticalDamping = config.Bind("ShipPhysics", "ShipVerticalDamping", 18.0f,
                "Damping against vertical ship velocity while the stable PhysicalWater flotation assist is active.");
            ShipWaterDrag = config.Bind("ShipPhysics", "ShipWaterDrag", 0.16f,
                "Small horizontal drag applied to ships by the PhysicalWater flotation assist.");
            ShipWakeStrength = config.Bind("ShipPhysics", "ShipWakeStrength", 0.028f,
                "Wake/ripple strength emitted by moving ships into the shared PhysicalWater ripple field and visible foam/splash layer.");
            ShipUprightAssist = config.Bind("ShipPhysics", "ShipUprightAssist", false,
                "Apply a gentle upright torque and angular damping after native ship physics so boats do not roll onto their side while PhysicalWater replaces vanilla water.");
            ShipUprightTorque = config.Bind("ShipPhysics", "ShipUprightTorque", 8.0f,
                "Strength of the upright torque applied to ships by PhysicalWater.");
            ShipAngularDamping = config.Bind("ShipPhysics", "ShipAngularDamping", 1.5f,
                "Angular damping applied to ships by PhysicalWater while the upright assist is active.");
            ShipMaxAngularVelocity = config.Bind("ShipPhysics", "ShipMaxAngularVelocity", 1.8f,
                "Maximum angular velocity magnitude allowed by the PhysicalWater ship stabilizer.");
            SmallCreatureSwimAssist = config.Bind("CreatureWater", "SmallCreatureSwimAssist", true,
                "Help small non-player creatures enter swim mode in PhysicalWater instead of walking along the seabed near shore.");
            SmallCreatureMaxHeight = config.Bind("CreatureWater", "SmallCreatureMaxHeight", 1.8f,
                "Only non-player characters at or below this height receive swim-depth assistance.");
            SmallCreatureSwimDepthFactor = config.Bind("CreatureWater", "SmallCreatureSwimDepthFactor", 0.45f,
                "Assisted swim depth as a fraction of creature height. Lower values make small creatures float/swim in shallower water.");
            BuoyancyProbeRadius = config.Bind("Physics", "BuoyancyProbeRadius", 0.75f,
                "Radius used when disturbance/buoyancy callers query local water height.");
            FloatingProbeFallbackRadius = config.Bind("Physics", "FloatingProbeFallbackRadius", 8.0f,
                "Legacy compatibility value only. 0.5.1 never borrows nearby water for a dry buoyancy probe.");
            CharacterWaterFeedInterval = config.Bind("Integration", "CharacterWaterFeedInterval", 0.2f,
                "Seconds between experimental Character.SetLiquidLevel feeds.");
            Diagnostics = config.Bind("Diagnostics", "Diagnostics", true,
                "Log compact startup and periodic diagnostics.");
            ValheimGeometryDiagnosticsEnabled = config.Bind("ValheimGeometryAdapter", "DiagnosticsEnabled", false,
                "devD6 read-only mode: cache nearby solid-geometry discovery chunks and log SDF-adapter diagnostics without enabling gameplay water.");
            ValheimGeometryScanRadius = config.Bind("ValheimGeometryAdapter", "ScanRadius", 64f,
                new ConfigDescription("Half-size in meters of the local Valheim geometry adapter scan around the local player/camera.",
                    new AcceptableValueRange<float>(16f, 384f)));
            ValheimGeometryScanInterval = config.Bind("ValheimGeometryAdapter", "ScanInterval", 0.25f,
                new ConfigDescription("Seconds between read-only Valheim geometry scans. Lower values are only for profiling.",
                    new AcceptableValueRange<float>(0.25f, 60f)));
            ValheimGeometryFullConsistencyInterval = config.Bind("ValheimGeometryAdapter", "FullConsistencyInterval", 900f,
                new ConfigDescription("Seconds between low-frequency full solid-geometry consistency scans. Normal scans reuse unchanged discovery chunks.",
                    new AcceptableValueRange<float>(15f, 3600f)));
            ValheimGeometryDiscoveryChunksPerScan = config.Bind("ValheimGeometryAdapter", "DiscoveryChunksPerScan", 1,
                new ConfigDescription("Maximum nearby world chunks rediscovered in one diagnostics scan. Event chunks are prioritized and consistency work is amortized.",
                    new AcceptableValueRange<int>(1, 16)));
            ValheimGeometryCellSize = config.Bind("ValheimGeometryAdapter", "EstimatedVoxelCellSize", 0.75f,
                new ConfigDescription("Cell size used only for devD3 dirty-region size estimates. It mirrors the validated 0.6 local volumetric domain scale.",
                    new AcceptableValueRange<float>(0.25f, 3f)));
            ValheimGeometryDirtyPaddingCells = config.Bind("ValheimGeometryAdapter", "DirtyPaddingCells", 4,
                new ConfigDescription("Extra cells included around changed Valheim geometry for eventual SDF propagation.",
                    new AcceptableValueRange<int>(0, 24)));
            ValheimGeometryLogRejected = config.Bind("ValheimGeometryAdapter", "LogRejectedSamples", false,
                "Log up to twelve rejected/non-solid source samples per devD3 scan.");
            ValheimGeometryDebugVisualization = config.Bind("ValheimGeometryAdapter", "DebugVisualization", false,
                "Reserved for an optional local adapter bounds visualization. Disabled by default and not used by gameplay.");
            ValheimGeometryChunkSize = config.Bind("ValheimGeometryAdapter", "ChunkSize", 32f,
                new ConfigDescription("Meters per SDF occupancy/update/upload queue chunk.",
                    new AcceptableValueRange<float>(8f, 128f)));
            ValheimGeometryDiscoveryTileSize = config.Bind("ValheimGeometryAdapter", "DiscoveryTileSize", 8f,
                new ConfigDescription("Meters per collider-discovery tile. This stays independent from the larger SDF queue chunk so dense-base classification is frame bounded.",
                    new AcceptableValueRange<float>(8f, 32f)));
            ValheimGeometryOriginSnapMeters = config.Bind("ValheimGeometryAdapter", "OriginSnapMeters", 16f,
                new ConfigDescription("Meters the diagnostics scan center must move before the local geometry streaming domain recenters.",
                    new AcceptableValueRange<float>(4f, 96f)));
            ValheimGeometryTerrainSampleSpacing = config.Bind("ValheimGeometryAdapter", "TerrainSampleSpacing", 3.0f,
                new ConfigDescription("Meters between dedicated Heightmap samples used by devD6 terrain occupancy diagnostics.",
                    new AcceptableValueRange<float>(0.5f, 6f)));
            ValheimGeometryTerrainMaxSamplesPerScan = config.Bind("ValheimGeometryAdapter", "TerrainMaxSamplesPerScan", 2048,
                new ConfigDescription("Maximum Heightmap samples per diagnostics scan. Extra terrain chunks are deferred to later scans.",
                    new AcceptableValueRange<int>(512, 100000)));
            ValheimGeometryFrameBudgetMilliseconds = config.Bind("ValheimGeometryAdapter", "FrameBudgetMilliseconds", 16f,
                new ConfigDescription("Warn when a devD6 diagnostics scan exceeds this frame-time budget.",
                    new AcceptableValueRange<float>(1f, 33f)));
            ValheimGeometryIncrementalBudgetMilliseconds = config.Bind("ValheimGeometryAdapter", "IncrementalBudgetMilliseconds", 4f,
                new ConfigDescription("Target budget for a simple event-triggered geometry update.",
                    new AcceptableValueRange<float>(1f, 20f)));
            StageE1Enabled = config.Bind("StageE1", "Enabled", false,
                "Compatibility switch for the experimental E3 finite streaming APIC/FLIP mode. Does not suppress or query vanilla water and is disabled by default.");
            StageE1RenderSurface = config.Bind("StageE1", "RenderSurface", true,
                "Render the Stage C reconstructed E1 debug surface for the explicitly created finite domain.");
            StageE1TelemetryInterval = config.Bind("StageE1", "TelemetryInterval", 2f,
                new ConfigDescription("Seconds between blocking E1 validation telemetry samples.",
                    new AcceptableValueRange<float>(0.5f, 30f)));
            StageE1MaxSubstepsPerFrame = config.Bind("StageE1", "MaxSubstepsPerFrame", 2,
                new ConfigDescription("Maximum fixed 30 Hz E1 solver steps dispatched in one rendered frame.",
                    new AcceptableValueRange<int>(1, 4)));
        }
    }
}
