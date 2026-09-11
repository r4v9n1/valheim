using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace PhysicalWater
{
    internal sealed class PhysicalWaterSystem : MonoBehaviour
    {
        internal static PhysicalWaterSystem Instance { get; private set; }

        private Mesh _mesh;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _farMesh;
        private MeshFilter _farMeshFilter;
        private MeshRenderer _farMeshRenderer;
        private Mesh _shorelineFoamMesh;
        private MeshFilter _shorelineFoamMeshFilter;
        private MeshRenderer _shorelineFoamMeshRenderer;
        private PhysicalWaterLodOcean _lodOcean;
        private Material _runtimeMaterial;
        private Material _shorelineFoamOverlayMaterial;
        private AssetBundle _waterAssetBundle;
        private GameObject _bundledVisualEffects;
        private ParticleSystem _splashParticles;
        private ParticleSystem _foamParticles;
        private ParticleSystem _shoreFoamParticles;
        // Audio lives in UnityEngine.AudioModule, which in current Valheim targets netstandard 2.1.
        // PhysicalWater remains net472-compatible by accessing AudioSource/AudioClip through reflection.
        private Component _ambientWaterAudio;
        private Component _splashAudio;
        private UnityEngine.Object _adoptedWaterLoopClip;
        private UnityEngine.Object _adoptedSplashClip;
        private Texture2D _normalA;
        private Texture2D _normalB;
        private Texture2D _foamTex;
        private Texture2D _noiseTex;
        private Texture2D _rippleFieldTexture;
        private Color32[] _rippleFieldPixels;
        private float _nextRippleTextureUploadTime;
        private Texture2D _bathymetryTexture;
        private Color32[] _bathymetryPixels;
        private Texture2D _oceanDomainTexture;
        private Color32[] _oceanDomainPixels;
        private bool[] _oceanDomainCandidate;
        private bool[] _oceanDomainWet;
        private int[] _oceanDomainQueue;
        private Vector3 _oceanDomainOrigin;
        private float _oceanDomainSize;
        private float _nextOceanDomainRefresh;
        private float[] _effectiveBedHeights;
        private float[] _waterDepthField;
        private float[] _shoreDistanceField;
        private float[] _obstacleDistanceField;
        private float[] _bedSlopeField;
        private bool[] _staticObstacleMask;
        private int[] _distanceQueue;
        private float[] _shiftScratchCurrent;
        private float[] _shiftScratchPrevious;
        private float[] _shiftScratchNext;
        private readonly RaycastHit[] _obstacleRayHits = new RaycastHit[12];
        private Texture2D _softSplashTex;
        private Texture2D _softFoamParticleTex;
        private Mesh _foamDecalMesh;
        private Material _foamDecalMaterial;
        private readonly List<FoamDecal> _foamDecals = new List<FoamDecal>();
        private readonly MaterialPropertyBlock _foamDecalPropertyBlock = new MaterialPropertyBlock();
        private Vector3[] _vertices;
        private Vector3[] _normals;
        private Vector2[] _uvs;
        private Color[] _colors;
        private Vector3[] _farVertices;
        private Vector3[] _farNormals;
        private Vector2[] _farUvs;
        private Color[] _farColors;
        private float[] _farTerrainHeights;
        private bool[] _farWaterMask;
        private bool[] _farVisualWaterMask;
        private int[] _triangles;
        private readonly List<int> _visibleTriangles = new List<int>();
        private readonly List<int> _farVisibleTriangles = new List<int>();
        private readonly List<Vector3> _shorelineFoamVertices = new List<Vector3>();
        private readonly List<Vector2> _shorelineFoamUvs = new List<Vector2>();
        private readonly List<Color> _shorelineFoamColors = new List<Color>();
        private readonly List<int> _shorelineFoamTriangles = new List<int>();
        private float[] _current;
        private float[] _previous;
        private float[] _next;

        // 0.5.1 Hydrodynamics Core audit. Unlike the old sea-level blanket, these arrays store
        // conserved water depth and horizontal face velocity. Water exists because mass
        // reached a cell, not merely because the terrain happens to be below SeaLevel.
        private float[] _hydroDepth;
        private float[] _hydroNextDepth;
        private float[] _hydroFaceEast;
        private float[] _hydroFaceNorth;
        private float[] _hydroFluxEast;
        private float[] _hydroFluxNorth;
        private float[] _hydroRequestedOutflow;
        private float[] _hydroOutflowScale;
        private float[] _shiftScratchHydroDepth;
        private float[] _shiftScratchHydroEast;
        private float[] _shiftScratchHydroNorth;
        private bool[] _hydroNewCellMask;
        private bool[] _oceanBoundaryMask;
        private bool[] _solidHydroMask;
        private bool[] _barrierEast;
        private bool[] _barrierNorth;
        private bool _hydroInitialized;
        private float _hydroAccumulator;
        private float _nextHydroGeometryRefresh;
        private float _nextHydroTextureRefresh;
        private float _lastHydroVolume;

        private float[] _terrainHeights;
        private bool[] _candidateWaterMask;
        private bool[] _waterMask;
        private bool[] _visualWaterMask;
        private int[] _floodQueue;
        private int _resolution;
        private float _extent;
        private float _cellSize;
        private Vector3 _origin;
        private Vector3 _lastOrigin;
        private int _farResolution;
        private float _farRadius;
        private float _farInnerRadius;
        private float _farCellSize;
        private Vector3 _farOrigin;
        private Vector3 _lastFarOrigin;
        private float _accumulator;
        private float _nextDiagnosticTime;
        private float _nextVanillaSuppressionTime;
        private float _nextDryBaselineSweepTime;
        private float _nextInteractionEffectTime;
        private float _nextLocalPlayerContactEffectTime;
        private float _nextSplashAudioTime;
        private float _nextShoreFoamTime;
        private int _queryCount;
        private int _waterVertexCount;
        private int _visibleTriangleCount;
        private int _farWaterVertexCount;
        private int _farVisibleTriangleCount;
        private int _suppressedWaterRendererCount;
        private int _suppressedWaterAudioCount;
        private int _shoreFoamEmissionCount;
        private int _interactionEmissionCount;
        private int _floatingProbeFallbackCount;
        private float _lastLocalPlayerWaterSurface = -10000f;
        private float _lastLocalPlayerWaterDepth;
        private bool _lastLocalPlayerWaterExists;
        private float _lastPlayerHydroDepth;
        private float _lastPlayerBedHeight;
        private bool _lastPlayerWetCell;
        private bool _lastPlayerCoarseDomainWet;
        private bool _lastPlayerInsideHydroTile;
        private bool _lastPlayerCandidateFluid;
        private bool _lastPlayerSolidCell;
        private Vector2 _lastPlayerGrid;
        private Vector3 _lastPlayerGridOrigin;
        private string _runtimeMaterialLabel = "none";
        private bool _hasUsableMaterial;
        private bool _masksDirty = true;
        private bool _trianglesDirty = true;
        private bool _farMeshDirty = true;
        private bool _meshDirty = true;

        private const float SimulationStep = 0.03333334f;
        private const float HydroStep = 0.03333334f;
        private const float HydroMinWetDepth = 0.018f;
        private const float HydroGravity = 9.81f;
        private const float HydroMaxHorizontalSpeed = 7.5f;

        internal Material RuntimeWaterMaterial { get { return _runtimeMaterial; } }

        private sealed class FoamDecal
        {
            internal GameObject Object;
            internal MeshRenderer Renderer;
            internal Transform Transform;
            internal float StartTime;
            internal float Lifetime;
            internal float StartScale;
            internal float EndScale;
            internal Color Color;
            internal Vector3 BasePosition;
            internal bool Active;
        }

        private struct WaveLayer
        {
            internal readonly Vector2 Direction;
            internal readonly float Amplitude;
            internal readonly float Wavelength;
            internal readonly float Speed;
            internal readonly float Steepness;

            internal WaveLayer(float directionDegrees, float amplitude, float wavelength, float speed, float steepness)
            {
                float radians = directionDegrees * Mathf.Deg2Rad;
                Direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                Amplitude = amplitude;
                Wavelength = wavelength;
                Speed = speed;
                Steepness = steepness;
            }
        }

        // 0.4.1 deep-water spectrum. Wavelengths span swell to short chop;
        // angular frequency is derived from deep-water dispersion (omega=sqrt(g*k))
        // so CPU buoyancy and GPU geometry advance from the same physical clock.
        private static readonly WaveLayer[] OceanWaveLayers =
        {
            new WaveLayer(14f, 0.78f, 110f, 0f, 0.42f),
            new WaveLayer(38f, 0.46f, 58f, 0f, 0.34f),
            new WaveLayer(63f, 0.27f, 29f, 0f, 0.25f),
            new WaveLayer(-28f, 0.15f, 14f, 0f, 0.18f),
            new WaveLayer(96f, 0.075f, 7f, 0f, 0.11f)
        };

        private void Awake()
        {
            Instance = this;
            CreatePreviewObjects();
            _lodOcean = gameObject.AddComponent<PhysicalWaterLodOcean>();
            _lodOcean.Initialize(this);
            CreateInteractionEffects();
            DisableLegacySurfaceEffectRenderers();
            RebuildGridIfNeeded(true);
            RebuildFarOceanIfNeeded(true);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_waterAssetBundle != null)
            {
                _waterAssetBundle.Unload(false);
                _waterAssetBundle = null;
            }

            if (_adoptedWaterLoopClip != null)
            {
                Destroy(_adoptedWaterLoopClip);
                _adoptedWaterLoopClip = null;
            }
            if (_adoptedSplashClip != null)
            {
                Destroy(_adoptedSplashClip);
                _adoptedSplashClip = null;
            }
            DestroyGeneratedWaterTextures();
            if (_bathymetryTexture != null)
            {
                Destroy(_bathymetryTexture);
                _bathymetryTexture = null;
            }
            if (_oceanDomainTexture != null)
            {
                Destroy(_oceanDomainTexture);
                _oceanDomainTexture = null;
            }
        }

        private void Update()
        {
            if (!IsEnabled())
            {
                SetPreviewVisible(false);
                return;
            }

            if (IsDryOceanFloorBaseline())
            {
                SuppressVanillaWaterIfNeeded();
                SetPreviewVisible(false);
                LogDiagnosticsIfNeeded();
                return;
            }

            RebuildGridIfNeeded(false);
            UpdateOrigin();

            // Built pieces, terrain edits and destroyed barriers can change flow topology without
            // moving the player. Re-sample local geometry periodically so walls begin/stop blocking
            // water without requiring an origin snap.
            if (Time.time >= _nextHydroGeometryRefresh)
            {
                _nextHydroGeometryRefresh = Time.time + 0.65f;
                _masksDirty = true;
            }

            // Bathymetry/barriers are geometry. Hydrodynamic wetness is separate state.
            UpdateWaterMasksIfNeeded(PhysicalWaterPlugin.Settings.SeaLevel.Value);
            EnsureHydrodynamicsInitialized(PhysicalWaterPlugin.Settings.SeaLevel.Value);
            StepHydrodynamics(Time.deltaTime);

            if (ShouldRunHeightfieldSimulation())
            {
                StepSimulation(Time.deltaTime);
            }
            else
            {
                _accumulator = 0f;
            }

            UpdateRippleFieldTexture();
            UpdateMesh();
            UpdateLocalPlayerContactEffects();
            // 0.5.1: all persistent surface foam/ripples live in continuous GPU fields.
            // Legacy shoreline billboards/decals exposed square/diamond sprites and caused stutter.
            // They remain compiled only for historical compatibility and are not ticked.
            UpdateFarOcean();
            SuppressVanillaWaterIfNeeded();
            SetPreviewVisible(PhysicalWaterPlugin.Settings.RenderPreviewSurface.Value);
            EnsureWaterCameraInputs();
            UpdateOceanDomainTexture(false);
            PushWaterMaterialGlobals(Time.time);
            if (_lodOcean != null)
            {
                _lodOcean.Tick();
            }
            UpdateLocalPlayerWaterDiagnostic();
            UpdateWaterAudio();
            LogDiagnosticsIfNeeded();
        }

        internal static bool IsEnabled()
        {
            return PhysicalWaterPlugin.Settings != null &&
                   PhysicalWaterPlugin.Settings.Enabled.Value &&
                   Instance != null;
        }

        internal static bool IsDryOceanFloorBaseline()
        {
            return PhysicalWaterPlugin.Settings != null &&
                   PhysicalWaterPlugin.Settings.DryOceanFloorBaseline.Value;
        }

        internal static bool IsPhysicalWaterInteractionEnabled()
        {
            return PhysicalWaterPlugin.Settings != null &&
                   PhysicalWaterPlugin.Settings.PhysicalWaterInteractionEnabled.Value;
        }

        internal static bool ShouldForceDryWaterQueries()
        {
            return PhysicalWaterPlugin.Settings != null &&
                   (PhysicalWaterPlugin.Settings.DryOceanFloorBaseline.Value ||
                    !PhysicalWaterPlugin.Settings.PhysicalWaterInteractionEnabled.Value);
        }

        internal static bool ShouldSuppressVanillaWaterSources()
        {
            // 0.3.17 hard cutover: when PhysicalWater is enabled, vanilla water is never
            // allowed to become an active visual/physics fallback. Rendering readiness is
            // diagnosed separately instead of silently reviving a second water system.
            return PhysicalWaterPlugin.Settings != null &&
                   PhysicalWaterPlugin.Settings.Enabled.Value &&
                   Instance != null;
        }

        private bool HasRenderableReplacementWater()
        {
            if (!_hasUsableMaterial ||
                PhysicalWaterPlugin.Settings == null ||
                !PhysicalWaterPlugin.Settings.RenderPreviewSurface.Value)
            {
                return false;
            }

            bool nearReady = _meshRenderer != null &&
                             _meshRenderer.sharedMaterial != null &&
                             _visibleTriangleCount > 0;
            bool farReady = PhysicalWaterPlugin.Settings.FarOceanEnabled.Value &&
                            _farMeshRenderer != null &&
                            _farMeshRenderer.sharedMaterial != null &&
                            _farVisibleTriangleCount > 0;
            return nearReady || farReady;
        }

        private static bool ShouldRunHeightfieldSimulation()
        {
            // 0.3.16 architecture correction: the heightfield is now a PHYSICS field only.
            // It drives ripple height/velocity and interaction energy, while the rendered
            // Valheim mesh stays geometrically stable. This preserves the anti-jelly rule
            // without throwing away physical ripples.
            return true;
        }

        private static bool ShouldAnimateVisualSurface()
        {
            // Hard guardrail after the 0.3.9 regression: do not animate the
            // near/far water mesh vertices. That reintroduced the visible
            // jelly-sheet failure mode. Water motion must come from material
            // normal/foam scrolling and explicit contact/shore effects until
            // a proper non-sheet ocean shader path is proven.
            return false;
        }

        internal float GetSurfaceHeight(Vector3 worldPosition, float waveFactor)
        {
            _queryCount++;

            if (IsDryOceanFloorBaseline())
            {
                return -10000f;
            }

            float baseSurface;
            float depth;
            if (TrySampleHydrodynamicSurface(worldPosition, out baseSurface, out depth))
            {
                if (depth <= HydroMinWetDepth) return -10000f;
                float procedural = GetProceduralWave(new Vector3(worldPosition.x, baseSurface, worldPosition.z), Time.time) * Mathf.Clamp01(waveFactor);
                procedural = Mathf.Clamp(procedural, -1.10f, 1.10f);
                float ripple = Mathf.Clamp(SampleHeightfield(worldPosition), -0.18f, 0.18f);
                return baseSurface + procedural + ripple;
            }

            // Outside the local hydrodynamic tile, only genuine open-ocean terrain receives
            // the spectral ocean. We never infer water merely from elevation below SeaLevel.
            if (!IsOpenOceanOutsideHydro(worldPosition)) return -10000f;
            float seaLevel = PhysicalWaterPlugin.Settings.SeaLevel.Value;
            float farWave = GetProceduralWave(new Vector3(worldPosition.x, seaLevel, worldPosition.z), Time.time) * Mathf.Clamp01(waveFactor);
            return seaLevel + Mathf.Clamp(farWave, -1.10f, 1.10f);
        }

        internal float GetCharacterSurfaceHeight(Vector3 worldPosition)
        {
            // Character locomotion follows the actual hydrodynamic surface, with only the
            // high-frequency ripple component reduced. This removes the old static SeaLevel rail.
            float baseSurface;
            float depth;
            if (TrySampleHydrodynamicSurface(worldPosition, out baseSurface, out depth))
            {
                if (depth <= HydroMinWetDepth) return -10000f;
                float wave = GetProceduralWave(new Vector3(worldPosition.x, baseSurface, worldPosition.z), Time.time) * 0.45f;
                return baseSurface + Mathf.Clamp(wave, -0.42f, 0.42f);
            }
            return GetSurfaceHeight(worldPosition, 0.45f);
        }

        internal bool TryGetPresentedCharacterSurface(Vector3 worldPosition, out float surface)
        {
            surface = GetCharacterSurfaceHeight(worldPosition);
            if (surface <= -9990f || _lodOcean == null || !_lodOcean.HasCoverageAt(worldPosition))
            {
                surface = -10000f;
                return false;
            }
            return true;
        }

        internal string GetPlayerGridDiagnostic()
        {
            return "grid=" + _lastPlayerGrid.ToString("F2") +
                   ",origin=" + _lastPlayerGridOrigin.ToString("F2") +
                   ",candidate=" + _lastPlayerCandidateFluid +
                   ",solid=" + _lastPlayerSolidCell +
                   ",hydroDepth=" + _lastPlayerHydroDepth.ToString("F3") +
                   ",wet=" + _lastPlayerWetCell;
        }

        internal float GetRenderSurfaceHeight(Vector3 worldPosition)
        {
            float surface = GetSurfaceHeight(worldPosition, 1f);
            if (surface <= -9990f) return surface;
            float seaLevel = PhysicalWaterPlugin.Settings.SeaLevel.Value;
            return Mathf.Clamp(surface, seaLevel - 2.4f, seaLevel + 2.4f);
        }

        internal float GetFloatingProbeSurfaceHeight(Vector3 worldPosition, float waveFactor, float fallbackRadius)
        {
            _queryCount++;
            if (IsDryOceanFloorBaseline()) return -10000f;

            // 0.5.1 has no proximity fallback. A probe is wet only when water mass exists at
            // the probe position (or it is genuinely outside the local tile in open ocean).
            // This prevents boats/objects on the dry side of a wall from borrowing a nearby
            // ocean surface merely because it is within a radius.
            return GetSurfaceHeight(worldPosition, Mathf.Clamp01(waveFactor));
        }

        internal float GetAnimatedRideSurfaceHeight(Vector3 worldPosition, float fallbackRadius, float visualWaveScale)
        {
            float surface = GetFloatingProbeSurfaceHeight(worldPosition, 1f, fallbackRadius);
            if (surface <= -9990f)
            {
                return surface;
            }

            // Physics queries already include procedural waves plus the ripple field.
            // Keep this compatibility method as a direct alias so callers cannot stack
            // a second visual wave on top of the physical surface.
            return surface;
        }

        internal void Disturb(Vector3 worldPosition, float strength, float radius)
        {
            // 0.3.17: interactions feed the physical ripple field, but their energy is
            // strictly bounded. The ripple solver is feedback, not a rigidbody launcher.
#pragma warning disable CS0162
            if (_current == null || radius <= 0f || Mathf.Abs(strength) < 0.0005f)
            {
                return;
            }

            float maxDisplacement = Mathf.Min(GetMaxSimulatedDisplacement(), 0.32f);
            strength = Mathf.Clamp(strength, -0.022f, 0.022f);

            Vector2 grid = WorldToGrid(worldPosition);
            float radiusCells = Mathf.Max(1f, radius / _cellSize);
            int minX = Mathf.Max(1, Mathf.FloorToInt(grid.x - radiusCells));
            int maxX = Mathf.Min(_resolution - 2, Mathf.CeilToInt(grid.x + radiusCells));
            int minY = Mathf.Max(1, Mathf.FloorToInt(grid.y - radiusCells));
            int maxY = Mathf.Min(_resolution - 2, Mathf.CeilToInt(grid.y + radiusCells));
            float radiusSq = radiusCells * radiusCells;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x - grid.x;
                    float dy = y - grid.y;
                    float distSq = dx * dx + dy * dy;
                    if (distSq > radiusSq)
                    {
                        continue;
                    }

                    float falloff = 1f - Mathf.Sqrt(distSq) / radiusCells;
                    int index = Index(x, y);
                    if (_waterMask != null && !_waterMask[index])
                    {
                        continue;
                    }

                    _current[index] = Mathf.Clamp(_current[index] + strength * falloff, -maxDisplacement, maxDisplacement);
                }
            }

#pragma warning restore CS0162
        }

        private void CreateInteractionEffects()
        {
            // 0.5.1 audit: persistent/billboard foam objects are deliberately not created.
            // Surface interaction lives in the continuous ripple + shader fields. CPU geometry
            // was the source of the frozen white discs, diamonds and stutter seen in 0.4.x.
            // A tiny ballistic splash system is retained only for genuinely high-energy impacts.
            _splashParticles = CreateParticleSystem("PhysicalWater_SplashParticles", 0.58f, 1.55f, 0.045f, ParticleSystemRenderMode.Billboard, new Color(0.78f, 0.94f, 1f, 0.58f), true);
            _foamParticles = null;
            _shoreFoamParticles = null;
            CreateWaterAudioSources();
            TryLoadBundledVisualEffects();
        }

        private void CreateFoamDecalResources()
        {
            _foamDecalMesh = new Mesh();
            _foamDecalMesh.name = "PhysicalWater_FoamDecalMesh";
            _foamDecalMesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f)
            };
            _foamDecalMesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };
            _foamDecalMesh.normals = new[]
            {
                Vector3.up,
                Vector3.up,
                Vector3.up,
                Vector3.up
            };
            _foamDecalMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            _foamDecalMesh.RecalculateBounds();

            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Particles/Standard Unlit");
            if (shader == null)
            {
                return;
            }

            _foamDecalMaterial = new Material(shader);
            _foamDecalMaterial.name = "PhysicalWater_FoamDecalMaterial";
            ConfigureTransparentMaterial(_foamDecalMaterial);
            _foamDecalMaterial.renderQueue = 3300;
            Texture2D texture = GetSoftFoamParticleTexture();
            if (texture != null && _foamDecalMaterial.HasProperty("_MainTex"))
            {
                _foamDecalMaterial.SetTexture("_MainTex", texture);
            }
            if (texture != null && _foamDecalMaterial.HasProperty("_BaseMap"))
            {
                _foamDecalMaterial.SetTexture("_BaseMap", texture);
            }
            if (_foamDecalMaterial.HasProperty("_Color"))
            {
                _foamDecalMaterial.SetColor("_Color", Color.white);
            }
            if (_foamDecalMaterial.HasProperty("_TintColor"))
            {
                _foamDecalMaterial.SetColor("_TintColor", Color.white);
            }

            _shorelineFoamOverlayMaterial = new Material(_foamDecalMaterial);
            _shorelineFoamOverlayMaterial.name = "PhysicalWater_ShorelineFoamOverlayMaterial";
            _shorelineFoamOverlayMaterial.renderQueue = 3310;
            if (_shorelineFoamMeshRenderer != null)
            {
                _shorelineFoamMeshRenderer.sharedMaterial = _shorelineFoamOverlayMaterial;
            }
        }

        internal void EmitWaterContact(Vector3 worldPosition, float strength, float radius)
        {
            EmitInteractionEffect(worldPosition, strength, radius);
        }

        private void UpdateLocalPlayerContactEffects()
        {
            Player player = Player.m_localPlayer;
            if (player == null ||
                Time.time < _nextLocalPlayerContactEffectTime ||
                !PhysicalWaterPlugin.Settings.FeedCharactersLiquidLevel.Value)
            {
                return;
            }

            Vector3 position = player.transform.position;
            float surface = GetSurfaceHeight(position, 1f);
            if (surface <= -9990f)
            {
                return;
            }

            float waterDepth = surface - position.y;
            if (waterDepth < -0.35f || waterDepth > 1.15f)
            {
                return;
            }

            Rigidbody body = player.GetComponent<Rigidbody>();
            float speed = body != null ? body.linearVelocity.magnitude : 0f;
            float interval = waterDepth > 0.75f ? 0.32f : 0.18f;
            _nextLocalPlayerContactEffectTime = Time.time + interval;

            float shallowFactor = Mathf.Clamp01(1.15f - Mathf.Abs(waterDepth - 0.35f) * 0.28f);
            float movementFactor = Mathf.Clamp01(speed / 5.5f);
            float strength = Mathf.Clamp(0.006f + movementFactor * 0.018f + shallowFactor * 0.010f, 0.006f, 0.035f);
            float radius = Mathf.Lerp(0.85f, 1.65f, Mathf.Max(movementFactor, shallowFactor * 0.65f));
            Vector3 contact = new Vector3(position.x, surface, position.z);
            EmitInteractionEffect(contact, strength, radius);
        }

        private void CreateWaterAudioSources()
        {
            Type audioSourceType = GetAudioSourceType();
            if (audioSourceType == null)
            {
                PhysicalWaterPlugin.Log.LogWarning("PhysicalWater could not resolve UnityEngine.AudioSource at runtime; water audio is disabled, water physics/rendering remain active.");
                return;
            }

            GameObject ambient = new GameObject("PhysicalWater_AmbientAudio");
            ambient.transform.SetParent(transform, false);
            _ambientWaterAudio = ambient.AddComponent(audioSourceType) as Component;
            SetAudioProperty(_ambientWaterAudio, "loop", true);
            SetAudioProperty(_ambientWaterAudio, "playOnAwake", false);
            SetAudioProperty(_ambientWaterAudio, "spatialBlend", 0.0f);
            SetAudioProperty(_ambientWaterAudio, "volume", 0f);
            SetAudioProperty(_ambientWaterAudio, "priority", 180);

            GameObject splash = new GameObject("PhysicalWater_SplashAudio");
            splash.transform.SetParent(transform, false);
            _splashAudio = splash.AddComponent(audioSourceType) as Component;
            SetAudioProperty(_splashAudio, "loop", false);
            SetAudioProperty(_splashAudio, "playOnAwake", false);
            SetAudioProperty(_splashAudio, "spatialBlend", 0.85f);
            SetAudioProperty(_splashAudio, "volume", 0.55f);
            SetAudioProperty(_splashAudio, "priority", 120);
            SetAudioProperty(_splashAudio, "maxDistance", 36f);

            CreateProceduralWaterAudioClips();
        }

        private void TryLoadBundledVisualEffects()
        {
            AssetBundle bundle = LoadWaterAssetBundle();
            if (bundle == null)
            {
                return;
            }

            try
            {
                GameObject prefab = bundle.LoadAsset<GameObject>("R4V9N1_PhysicalOceanVisual") ??
                                    bundle.LoadAsset<GameObject>("Assets/PhysicalWaterBundle/R4V9N1_PhysicalOceanVisual.prefab");
                if (prefab == null)
                {
                    GameObject[] prefabs = bundle.LoadAllAssets<GameObject>();
                    if (prefabs != null && prefabs.Length > 0)
                    {
                        prefab = prefabs[0];
                    }
                }

                if (prefab == null)
                {
                    return;
                }

                _bundledVisualEffects = Instantiate(prefab);
                _bundledVisualEffects.name = "PhysicalWater_BundledVisualEffects";
                _bundledVisualEffects.transform.SetParent(transform, false);

                MeshRenderer[] meshRenderers = _bundledVisualEffects.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < meshRenderers.Length; i++)
                {
                    if (meshRenderers[i] != null)
                    {
                        meshRenderers[i].enabled = false;
                    }
                }

                ParticleSystem[] particleSystems = _bundledVisualEffects.GetComponentsInChildren<ParticleSystem>(true);
                if (particleSystems != null && particleSystems.Length > 0)
                {
                    for (int i = 0; i < particleSystems.Length; i++)
                    {
                        if (particleSystems[i] != null)
                        {
                            ParticleSystemRenderer particleRenderer = particleSystems[i].GetComponent<ParticleSystemRenderer>();
                            if (particleRenderer != null)
                            {
                                particleRenderer.enabled = false;
                            }
                        }
                    }
                }

                if (PhysicalWaterPlugin.Settings.Diagnostics.Value)
                {
                    PhysicalWaterPlugin.Log.LogInfo("PhysicalWater loaded bundled Unity visual effects prefab.");
                }
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log.LogWarning("PhysicalWater failed to load bundled visual effects prefab: " + ex.Message);
            }
        }

        private ParticleSystem CreateParticleSystem(string systemName, float lifetime, float speed, float size, ParticleSystemRenderMode renderMode, Color color, bool splashTexture)
        {
            GameObject child = new GameObject(systemName);
            child.transform.SetParent(transform, false);
            ParticleSystem system = child.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = renderMode == ParticleSystemRenderMode.HorizontalBillboard ? 0f : 0.32f;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = renderMode == ParticleSystemRenderMode.HorizontalBillboard ? 88f : 24f;
            shape.radius = renderMode == ParticleSystemRenderMode.HorizontalBillboard ? 1.25f : 0.16f;

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(color.r, color.g, color.b), 0f),
                    new GradientColorKey(new Color(color.r, color.g, color.b), 0.55f),
                    new GradientColorKey(new Color(color.r, color.g, color.b), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(color.a, 0.18f),
                    new GradientAlphaKey(color.a * 0.35f, 0.62f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = gradient;

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.45f),
                new Keyframe(0.22f, 1f),
                new Keyframe(1f, renderMode == ParticleSystemRenderMode.HorizontalBillboard ? 1.8f : 0.65f));
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            ParticleSystemRenderer renderer = child.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = renderMode;
            renderer.sortingFudge = 2f;
            renderer.maxParticleSize = renderMode == ParticleSystemRenderMode.HorizontalBillboard ? 0.34f : 0.16f;

            Shader shader = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                Material material = new Material(shader);
                material.name = systemName + "_Material";
                material.color = color;
                Texture2D texture = splashTexture ? GetSoftSplashParticleTexture() : GetSoftFoamParticleTexture();
                if (texture != null)
                {
                    if (material.HasProperty("_MainTex"))
                    {
                        material.SetTexture("_MainTex", texture);
                    }
                    if (material.HasProperty("_BaseMap"))
                    {
                        material.SetTexture("_BaseMap", texture);
                    }
                }
                if (material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", color);
                }
                if (material.HasProperty("_TintColor"))
                {
                    material.SetColor("_TintColor", color);
                }
                renderer.sharedMaterial = material;
            }

            return system;
        }

        private void EmitInteractionEffect(Vector3 worldPosition, float strength, float radius)
        {
            if (Time.time < _nextInteractionEffectTime || Mathf.Abs(strength) < 0.004f)
            {
                return;
            }

            float surface = GetSurfaceHeight(worldPosition, 1f);
            if (surface <= -9990f)
            {
                return;
            }

            // Project all contacts to the PhysicalWater surface. Do not discard
            // boat/player wakes merely because their source transform sits deep
            // inside the hull/body.
            // 0.5.1 moves most contact feedback into the live ripple/foam shader.
            // CPU particles are accents only; thousands of emissions per diagnostic window caused visible hitching.
            _nextInteractionEffectTime = Time.time + 0.18f;
            Vector3 position = new Vector3(worldPosition.x, surface + 0.026f, worldPosition.z);
            float intensity = Mathf.Clamp(Mathf.Abs(strength) * 18f + radius * 0.09f, 0.05f, 1.0f);
            // Continuous ripple/foam shader owns ordinary contact response. Only a very
            // energetic hit may throw one or two short-lived ballistic droplets.
            int splashCount = intensity > 0.90f ? Mathf.Clamp(Mathf.RoundToInt((intensity - 0.86f) * 8f), 1, 2) : 0;
            int foamCount = 0;
            if (_splashParticles != null)
            {
                _splashParticles.transform.position = position;
                ParticleSystem.MainModule main = _splashParticles.main;
                main.startSpeedMultiplier = Mathf.Clamp(intensity * 0.42f, 0.16f, 0.85f);
                main.startSizeMultiplier = Mathf.Clamp(0.035f + radius * 0.010f, 0.035f, 0.080f);
                if (splashCount > 0)
                {
                    _splashParticles.Emit(splashCount);
                }
            }

            if (_foamParticles != null)
            {
                _foamParticles.transform.position = position;
                ParticleSystem.MainModule main = _foamParticles.main;
                main.startSizeMultiplier = Mathf.Clamp(0.10f + radius * 0.040f, 0.10f, 0.28f);
                if (foamCount > 0)
                {
                    _foamParticles.Emit(foamCount);
                }
            }

            if (intensity > 0.95f)
            {
                PlaySplashAudio(position, intensity);
            }
            _interactionEmissionCount += splashCount + foamCount;
        }

        private void EmitFoamDecal(Vector3 position, float radius, Color color, float lifetime)
        {
            // 0.5.1 fail-safe: CPU surface decals are banned. Persistent interaction
            // visuals must be represented by the continuous GPU ripple/foam field.
            return;
        }

        private void UpdateFoamDecals()
        {
            if (_foamDecals.Count == 0)
            {
                return;
            }

            float now = Time.time;
            for (int i = 0; i < _foamDecals.Count; i++)
            {
                FoamDecal decal = _foamDecals[i];
                if (decal == null || !decal.Active)
                {
                    continue;
                }

                float age = now - decal.StartTime;
                float t = Mathf.Clamp01(age / Mathf.Max(0.01f, decal.Lifetime));
                if (t >= 1f)
                {
                    decal.Active = false;
                    if (decal.Object != null)
                    {
                        decal.Object.SetActive(false);
                    }
                    continue;
                }

                float smooth = t * t * (3f - 2f * t);
                float scale = Mathf.Lerp(decal.StartScale, decal.EndScale, smooth);
                decal.Transform.position = GetVisualSurfaceDecalPosition(decal.BasePosition);
                decal.Transform.localScale = new Vector3(scale, 1f, scale);
                Color color = decal.Color;
                color.a *= 1f - smooth;
                _foamDecalPropertyBlock.Clear();
                _foamDecalPropertyBlock.SetColor("_Color", color);
                decal.Renderer.SetPropertyBlock(_foamDecalPropertyBlock);
            }
        }

        private Vector3 GetVisualSurfaceDecalPosition(Vector3 basePosition)
        {
            float seaLevel = PhysicalWaterPlugin.Settings != null ? PhysicalWaterPlugin.Settings.SeaLevel.Value : basePosition.y;
            float wave = ShouldAnimateVisualSurface()
                ? GetProceduralWave(new Vector3(basePosition.x, seaLevel, basePosition.z), Time.time)
                : 0f;
            return new Vector3(basePosition.x, seaLevel + wave + 0.090f, basePosition.z);
        }

        private void UpdateShorelineEffects()
        {
            // 0.5.1: persistent shoreline is fully continuous GPU bathymetry foam.
            // Discrete particles/decals caused rows of dots, diamonds and CPU hitches.
            return;
#pragma warning disable CS0162
            if (!PhysicalWaterPlugin.Settings.ShorelineEffectsEnabled.Value ||
                _shoreFoamParticles == null ||
                _waterMask == null ||
                _visualWaterMask == null ||
                _terrainHeights == null ||
                Time.time < _nextShoreFoamTime)
            {
                return;
            }

            float interval = Mathf.Clamp(PhysicalWaterPlugin.Settings.ShorelineEffectsInterval.Value, 0.22f, 4f);
            int budget = Mathf.Clamp(PhysicalWaterPlugin.Settings.ShorelineEffectsBudget.Value, 0, 64);
            _nextShoreFoamTime = Time.time + interval;
            if (budget <= 0)
            {
                return;
            }

            float seaLevel = PhysicalWaterPlugin.Settings.SeaLevel.Value;
            int emitted = 0;
            int step = Mathf.Max(1, Mathf.RoundToInt(4f / Mathf.Max(0.5f, _cellSize)));
            ParticleSystem.MainModule main = _shoreFoamParticles.main;
            main.startSizeMultiplier = Mathf.Clamp(_cellSize * 0.085f, 0.14f, 0.38f);
            main.startSpeedMultiplier = 0.070f;

            for (int y = step; y < _resolution - step && emitted < budget; y += step)
            {
                for (int x = step; x < _resolution - step && emitted < budget; x += step)
                {
                    int index = Index(x, y);
                    if (!_waterMask[index] || !HasDryNeighbor(x, y))
                    {
                        continue;
                    }

                    float depth = seaLevel - _terrainHeights[index];
                    if (depth < 0.03f || depth > 2.6f)
                    {
                        continue;
                    }

                    float phase = Mathf.Sin((_origin.x + x * _cellSize) * 0.17f + (_origin.z + y * _cellSize) * 0.11f + Time.time * 1.7f);
                    if (phase < -0.72f)
                    {
                        continue;
                    }

                    Vector3 position = new Vector3(_origin.x + x * _cellSize, seaLevel + 0.018f, _origin.z + y * _cellSize);
                    _shoreFoamParticles.transform.position = position;
                    _shoreFoamParticles.Emit(1);
                    // Shader depth foam now owns the continuous shoreline. Sparse particles/decal accents
                    // keep the coast alive without rebuilding thousands of CPU objects every few seconds.
                    if ((emitted % 8) == 0)
                    {
                        EmitFoamDecal(position, Mathf.Clamp(_cellSize * 0.34f, 0.85f, 2.0f), new Color(0.88f, 0.98f, 1f, 0.28f), 1.25f);
                    }
                    emitted++;
                }
            }

            _shoreFoamEmissionCount += emitted;
#pragma warning restore CS0162
        }

        private void CreatePreviewObjects()
        {
            _mesh = new Mesh();
            _mesh.name = "PhysicalWater_HeightfieldMesh";
            _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            _meshFilter = gameObject.AddComponent<MeshFilter>();
            _meshRenderer = gameObject.AddComponent<MeshRenderer>();
            _meshFilter.sharedMesh = _mesh;

            GameObject farObject = new GameObject("PhysicalWater_FarOceanImpostor");
            farObject.transform.SetParent(transform, false);
            _farMesh = new Mesh();
            _farMesh.name = "PhysicalWater_FarOceanMesh";
            _farMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _farMeshFilter = farObject.AddComponent<MeshFilter>();
            _farMeshRenderer = farObject.AddComponent<MeshRenderer>();
            _farMeshFilter.sharedMesh = _farMesh;

            GameObject shorelineFoamObject = new GameObject("PhysicalWater_UnityShorelineFoamOverlay");
            shorelineFoamObject.transform.SetParent(transform, false);
            _shorelineFoamMesh = new Mesh();
            _shorelineFoamMesh.name = "PhysicalWater_UnityShorelineFoamMesh";
            _shorelineFoamMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _shorelineFoamMeshFilter = shorelineFoamObject.AddComponent<MeshFilter>();
            _shorelineFoamMeshRenderer = shorelineFoamObject.AddComponent<MeshRenderer>();
            _shorelineFoamMeshFilter.sharedMesh = _shorelineFoamMesh;

            Material bundledMaterial = TryLoadBundledWaterMaterial();
            bool bundledMaterialSupported = bundledMaterial != null &&
                                            bundledMaterial.shader != null &&
                                            bundledMaterial.shader.name == "R4V9N1/Physical Ocean Surface" &&
                                            bundledMaterial.shader.isSupported &&
                                            bundledMaterial.passCount > 0;
            if (bundledMaterialSupported)
            {
                _runtimeMaterial = bundledMaterial;
                _runtimeMaterialLabel = "bundle:" + _runtimeMaterial.shader.name;
                _meshRenderer.sharedMaterial = _runtimeMaterial;
                _farMeshRenderer.sharedMaterial = _runtimeMaterial;
                _shorelineFoamMeshRenderer.sharedMaterial = _shorelineFoamOverlayMaterial ?? _runtimeMaterial;
                _hasUsableMaterial = true;

                if (PhysicalWaterPlugin.Settings.Diagnostics.Value)
                {
                    PhysicalWaterPlugin.Log.LogInfo("PhysicalWater loaded supported bundled Unity PhysicalOcean material.");
                }
            }
            else
            {
                string shaderName = bundledMaterial != null && bundledMaterial.shader != null ? bundledMaterial.shader.name : "missing";
                _hasUsableMaterial = false;
                _runtimeMaterial = null;
                _runtimeMaterialLabel = "ERROR:PhysicalOceanMaterial:" + shaderName;
                PhysicalWaterPlugin.Log.LogError("LiquidCore failed closed: required cel-shaded material/shader is missing, unsupported, or not R4V9N1/Physical Ocean Surface (shader=" + shaderName + ", no generic fallback accepted).");
            }

            ConfigureReplacementRenderers();
            SetPreviewVisible(false);
        }

        private void ConfigureReplacementRenderers()
        {
            MeshRenderer[] renderers = { _meshRenderer, _farMeshRenderer, _shorelineFoamMeshRenderer };
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }
                renderer.forceRenderingOff = false;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.motionVectorGenerationMode = UnityEngine.MotionVectorGenerationMode.ForceNoMotion;
                renderer.allowOcclusionWhenDynamic = false;
            }
        }

        private void DisableLegacySurfaceEffectRenderers()
        {
            if (_shorelineFoamMeshRenderer != null) _shorelineFoamMeshRenderer.forceRenderingOff = true;
            if (_shoreFoamParticles != null) _shoreFoamParticles.gameObject.SetActive(false);
            if (_foamParticles != null) _foamParticles.gameObject.SetActive(false);
        }

        private AssetBundle LoadWaterAssetBundle()
        {
            if (_waterAssetBundle != null)
            {
                return _waterAssetBundle;
            }

            string assemblyPath = typeof(PhysicalWaterPlugin).Assembly.Location;
            string pluginDirectory = Path.GetDirectoryName(assemblyPath);
            if (string.IsNullOrEmpty(pluginDirectory))
            {
                return null;
            }

            string bundlePath = Path.Combine(pluginDirectory, "physicalwater_assets");
            if (!File.Exists(bundlePath))
            {
                bundlePath = Path.Combine(pluginDirectory, "physicalwater_assets.bundle");
            }

            if (!File.Exists(bundlePath))
            {
                return null;
            }

            try
            {
                _waterAssetBundle = AssetBundle.LoadFromFile(bundlePath);
                if (_waterAssetBundle == null && PhysicalWaterPlugin.Settings.Diagnostics.Value)
                {
                    PhysicalWaterPlugin.Log.LogWarning("PhysicalWater found a Unity asset bundle but Unity could not load it: " + bundlePath);
                }
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log.LogWarning("PhysicalWater failed to load Unity asset bundle: " + ex.Message);
            }

            return _waterAssetBundle;
        }

        private Material TryLoadBundledWaterMaterial()
        {
            AssetBundle bundle = LoadWaterAssetBundle();
            if (bundle == null)
            {
                return null;
            }

            try
            {
                Material material = bundle.LoadAsset<Material>("R4V9N1_PhysicalOceanMaterial");
                if (material == null)
                {
                    Material[] materials = bundle.LoadAllAssets<Material>();
                    if (materials != null && materials.Length > 0)
                    {
                        material = materials[0];
                    }
                }

                if (material == null)
                {
                    Shader shader = bundle.LoadAsset<Shader>("R4V9N1/Physical Ocean Surface");
                    if (shader == null)
                    {
                        Shader[] shaders = bundle.LoadAllAssets<Shader>();
                        if (shaders != null && shaders.Length > 0)
                        {
                            shader = shaders[0];
                        }
                    }
                    if (shader == null || !shader.isSupported)
                    {
                        return null;
                    }
                    material = new Material(shader);
                    material.name = "R4V9N1_PhysicalOceanMaterial_RuntimeRecovered";
                }

                Material instance = new Material(material);
                instance.name = "PhysicalWater_BundledPhysicalOceanMaterial";
                ConfigurePhysicalOceanMaterial(instance);
                return instance;
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log.LogWarning("PhysicalWater failed to load bundled Unity water material: " + ex.Message);
                return null;
            }
        }

        private static Shader FindRuntimeWaterShader()
        {
            string[] names =
            {
                "Sprites/Default",
                "Particles/Standard Unlit",
                "UI/Default",
                "Hidden/Internal-Colored",
                "Unlit/Color",
                "Standard"
            };

            for (int i = 0; i < names.Length; i++)
            {
                Shader shader = Shader.Find(names[i]);
                if (shader != null)
                {
                    return shader;
                }
            }

            return null;
        }

        private static Material CreateSimpleFallbackWaterMaterial()
        {
            Shader shader = FindRuntimeWaterShader();
            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader);
            material.name = "PhysicalWater_SimpleStableWaterMaterial";
            Color color = new Color(0.02f, 0.18f, 0.24f, 0.30f);
            material.color = color;
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
            if (material.HasProperty("_TintColor"))
            {
                material.SetColor("_TintColor", color);
            }
            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", 0.55f);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.55f);
            }
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0.0f);
            }
            if (material.HasProperty("_Cull"))
            {
                material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            }

            ConfigureTransparentMaterial(material);
            return material;
        }

        internal void TryAdoptWaterMaterial(Renderer sourceRenderer)
        {
            // 0.3.17 hard rule: never adopt a Valheim water material. The UnityPhysicalOcean
            // shader/material is the only allowed surface material.
            return;
        }

        internal void TryAdoptWaterAudio(Component source)
        {
            // Hard rule: never adopt Valheim water audio. PhysicalWater generates
            // its own clips; this method only exists as a compatibility no-op.
            return;
        }

        private static Type GetAudioSourceType()
        {
            return Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule", false);
        }

        private static Type GetAudioClipType()
        {
            return Type.GetType("UnityEngine.AudioClip, UnityEngine.AudioModule", false);
        }

        private static object GetAudioProperty(Component component, string propertyName)
        {
            if (component == null) return null;
            PropertyInfo property = component.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            return property != null && property.CanRead ? property.GetValue(component, null) : null;
        }

        private static void SetAudioProperty(Component component, string propertyName, object value)
        {
            if (component == null) return;
            PropertyInfo property = component.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanWrite) return;
            try
            {
                object converted = value;
                if (value != null && !property.PropertyType.IsInstanceOfType(value))
                {
                    converted = Convert.ChangeType(value, property.PropertyType);
                }
                property.SetValue(component, converted, null);
            }
            catch { }
        }

        private static void InvokeAudio(Component component, string methodName)
        {
            if (component == null) return;
            MethodInfo method = component.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
            if (method != null)
            {
                try { method.Invoke(component, null); } catch { }
            }
        }

        private static UnityEngine.Object CreateAudioClipReflective(string name, float[] samples, int sampleRate)
        {
            Type clipType = GetAudioClipType();
            if (clipType == null || samples == null || samples.Length == 0) return null;
            MethodInfo create = clipType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public, null,
                new[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(bool) }, null);
            if (create == null) return null;
            object clip = null;
            try
            {
                clip = create.Invoke(null, new object[] { name, samples.Length, 1, sampleRate, false });
                MethodInfo setData = clipType.GetMethod("SetData", BindingFlags.Instance | BindingFlags.Public, null,
                    new[] { typeof(float[]), typeof(int) }, null);
                if (clip != null && setData != null) setData.Invoke(clip, new object[] { samples, 0 });
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log.LogWarning("PhysicalWater reflective AudioClip creation failed: " + ex.Message);
                return null;
            }
            return clip as UnityEngine.Object;
        }

        private static void SetAudioClip(Component source, UnityEngine.Object clip)
        {
            if (source == null) return;
            PropertyInfo property = source.GetType().GetProperty("clip", BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.CanWrite)
            {
                try { property.SetValue(source, clip, null); } catch { }
            }
        }

        private void CreateProceduralWaterAudioClips()
        {
            const int sampleRate = 22050;
            const int ambientSeconds = 4;
            int ambientCount = sampleRate * ambientSeconds;
            float[] ambient = new float[ambientCount];
            System.Random random = new System.Random(4170318);
            float filtered = 0f;
            for (int i = 0; i < ambientCount; i++)
            {
                float t = (float)i / sampleRate;
                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                filtered = filtered * 0.985f + white * 0.015f;
                float swell = Mathf.Sin(t * Mathf.PI * 0.75f) * 0.035f + Mathf.Sin(t * Mathf.PI * 1.63f) * 0.020f;
                ambient[i] = Mathf.Clamp(filtered * 0.14f + swell, -0.20f, 0.20f);
            }
            _adoptedWaterLoopClip = CreateAudioClipReflective("PhysicalWater_ProceduralOceanLoop", ambient, sampleRate);
            SetAudioClip(_ambientWaterAudio, _adoptedWaterLoopClip);

            int splashCount = Mathf.RoundToInt(sampleRate * 0.42f);
            float[] splash = new float[splashCount];
            filtered = 0f;
            for (int i = 0; i < splashCount; i++)
            {
                float t = (float)i / splashCount;
                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                filtered = filtered * 0.72f + white * 0.28f;
                float envelope = Mathf.Pow(1f - t, 2.4f) * Mathf.Min(1f, t * 18f);
                splash[i] = Mathf.Clamp(filtered * envelope * 0.48f, -0.65f, 0.65f);
            }
            _adoptedSplashClip = CreateAudioClipReflective("PhysicalWater_ProceduralSplash", splash, sampleRate);
            SetAudioClip(_splashAudio, _adoptedSplashClip);
        }

        private void UpdateWaterAudio()
        {
            if (_ambientWaterAudio == null || _adoptedWaterLoopClip == null) return;
            SetAudioClip(_ambientWaterAudio, _adoptedWaterLoopClip);

            Player player = Player.m_localPlayer;
            bool nearWater = _lastLocalPlayerWaterExists;
            if (!nearWater && player != null)
            {
                Vector3 position = player.transform.position;
                nearWater = GetSurfaceHeight(position + player.transform.forward * 8f, 1f) > -9990f ||
                            GetSurfaceHeight(position + player.transform.right * 8f, 1f) > -9990f ||
                            GetSurfaceHeight(position - player.transform.right * 8f, 1f) > -9990f;
            }

            float currentVolume = 0f;
            object volumeValue = GetAudioProperty(_ambientWaterAudio, "volume");
            if (volumeValue is float) currentVolume = (float)volumeValue;
            float targetVolume = nearWater ? 0.08f : 0f;
            float newVolume = Mathf.MoveTowards(currentVolume, targetVolume, Time.deltaTime * 0.12f);
            SetAudioProperty(_ambientWaterAudio, "volume", newVolume);
            bool isPlaying = GetAudioProperty(_ambientWaterAudio, "isPlaying") is bool && (bool)GetAudioProperty(_ambientWaterAudio, "isPlaying");
            if (targetVolume > 0.001f && !isPlaying) InvokeAudio(_ambientWaterAudio, "Play");
            else if (targetVolume <= 0.001f && newVolume <= 0.001f && isPlaying) InvokeAudio(_ambientWaterAudio, "Stop");
        }

        private void PlaySplashAudio(Vector3 position, float intensity)
        {
            if (_splashAudio == null || _adoptedSplashClip == null || Time.time < _nextSplashAudioTime) return;
            _nextSplashAudioTime = Time.time + 0.75f;
            _splashAudio.transform.position = position;
            SetAudioClip(_splashAudio, _adoptedSplashClip);
            SetAudioProperty(_splashAudio, "volume", Mathf.Clamp(0.06f + intensity * 0.035f, 0.06f, 0.22f));
            SetAudioProperty(_splashAudio, "pitch", UnityEngine.Random.Range(0.96f, 1.04f));
            InvokeAudio(_splashAudio, "Play");
        }

        private void RebuildGridIfNeeded(bool force)
        {
            int wantedResolution = Mathf.Clamp(PhysicalWaterPlugin.Settings.GridResolution.Value, 17, 257);
            if (wantedResolution % 2 == 0)
            {
                wantedResolution++;
            }

            float wantedExtent = Mathf.Max(24f, PhysicalWaterPlugin.Settings.FollowRadius.Value);

            if (!force && wantedResolution == _resolution && Mathf.Abs(wantedExtent - _extent) < 0.01f)
            {
                return;
            }

            _resolution = wantedResolution;
            _extent = wantedExtent;
            _cellSize = (_extent * 2f) / (_resolution - 1);

            int count = _resolution * _resolution;
            _vertices = new Vector3[count];
            _normals = new Vector3[count];
            _uvs = new Vector2[count];
            _colors = new Color[count];
            _current = new float[count];
            _previous = new float[count];
            _next = new float[count];
            _hydroDepth = new float[count];
            _hydroNextDepth = new float[count];
            _hydroFaceEast = new float[count];
            _hydroFaceNorth = new float[count];
            _hydroFluxEast = new float[count];
            _hydroFluxNorth = new float[count];
            _hydroRequestedOutflow = new float[count];
            _hydroOutflowScale = new float[count];
            _shiftScratchHydroDepth = new float[count];
            _shiftScratchHydroEast = new float[count];
            _shiftScratchHydroNorth = new float[count];
            _hydroNewCellMask = new bool[count];
            _oceanBoundaryMask = new bool[count];
            _solidHydroMask = new bool[count];
            _barrierEast = new bool[count];
            _barrierNorth = new bool[count];
            _hydroInitialized = false;
            _terrainHeights = new float[count];
            _effectiveBedHeights = new float[count];
            _waterDepthField = new float[count];
            _shoreDistanceField = new float[count];
            _obstacleDistanceField = new float[count];
            _bedSlopeField = new float[count];
            _staticObstacleMask = new bool[count];
            _distanceQueue = new int[count];
            _shiftScratchCurrent = new float[count];
            _shiftScratchPrevious = new float[count];
            _shiftScratchNext = new float[count];
            _candidateWaterMask = new bool[count];
            _waterMask = new bool[count];
            _visualWaterMask = new bool[count];
            _floodQueue = new int[count];
            _triangles = new int[(_resolution - 1) * (_resolution - 1) * 6];
            RebuildRippleFieldTexture();
            RebuildBathymetryTexture();
            RebuildOceanDomainTexture();

            int tri = 0;
            for (int y = 0; y < _resolution; y++)
            {
                for (int x = 0; x < _resolution; x++)
                {
                    int index = Index(x, y);
                    _uvs[index] = new Vector2((float)x / (_resolution - 1), (float)y / (_resolution - 1));
                    _normals[index] = Vector3.up;
                    _colors[index] = Color.white;

                    if (x < _resolution - 1 && y < _resolution - 1)
                    {
                        int a = Index(x, y);
                        int b = Index(x + 1, y);
                        int c = Index(x, y + 1);
                        int d = Index(x + 1, y + 1);

                        _triangles[tri++] = a;
                        _triangles[tri++] = c;
                        _triangles[tri++] = b;
                        _triangles[tri++] = b;
                        _triangles[tri++] = c;
                        _triangles[tri++] = d;
                    }
                }
            }

            _mesh.Clear();
            _mesh.MarkDynamic();
            _mesh.vertices = _vertices;
            _mesh.normals = _normals;
            _mesh.uv = _uvs;
            _mesh.colors = _colors;
            _mesh.triangles = new int[0];
            UpdateMeshBounds(PhysicalWaterPlugin.Settings.SeaLevel.Value);
            _masksDirty = true;
            _trianglesDirty = true;
            _meshDirty = true;

            if (PhysicalWaterPlugin.Settings.Diagnostics.Value)
            {
                PhysicalWaterPlugin.Log.LogInfo("Physical water grid rebuilt: " + _resolution + "x" + _resolution +
                                                ", extent=" + _extent.ToString("F1") +
                                                "m, cell=" + _cellSize.ToString("F2") + "m.");
            }
        }

        private void RebuildFarOceanIfNeeded(bool force)
        {
            if (_farMesh == null || PhysicalWaterPlugin.Settings == null)
            {
                return;
            }

            int wantedResolution = Mathf.Clamp(PhysicalWaterPlugin.Settings.FarOceanResolution.Value, 9, 65);
            if (wantedResolution % 2 == 0)
            {
                wantedResolution++;
            }

            float wantedRadius = Mathf.Max(_extent + 64f, PhysicalWaterPlugin.Settings.FarOceanRadius.Value);
            float wantedInner = Mathf.Clamp(PhysicalWaterPlugin.Settings.FarOceanInnerRadius.Value, 0f, wantedRadius - 32f);

            if (!force &&
                wantedResolution == _farResolution &&
                Mathf.Abs(wantedRadius - _farRadius) < 0.01f &&
                Mathf.Abs(wantedInner - _farInnerRadius) < 0.01f)
            {
                return;
            }

            _farResolution = wantedResolution;
            _farRadius = wantedRadius;
            _farInnerRadius = wantedInner;
            _farCellSize = (_farRadius * 2f) / (_farResolution - 1);

            int count = _farResolution * _farResolution;
            _farVertices = new Vector3[count];
            _farNormals = new Vector3[count];
            _farUvs = new Vector2[count];
            _farColors = new Color[count];
            _farTerrainHeights = new float[count];
            _farWaterMask = new bool[count];
            _farVisualWaterMask = new bool[count];

            for (int y = 0; y < _farResolution; y++)
            {
                for (int x = 0; x < _farResolution; x++)
                {
                    int index = FarIndex(x, y);
                    _farUvs[index] = new Vector2((float)x / (_farResolution - 1), (float)y / (_farResolution - 1));
                    _farNormals[index] = Vector3.up;
                    _farColors[index] = new Color(1f, 1f, 1f, 0.45f);
                }
            }

            _farMesh.Clear();
            _farMesh.MarkDynamic();
            _farMesh.vertices = _farVertices;
            _farMesh.normals = _farNormals;
            _farMesh.uv = _farUvs;
            _farMesh.colors = _farColors;
            _farMesh.triangles = new int[0];
            UpdateFarMeshBounds(PhysicalWaterPlugin.Settings.SeaLevel.Value);
            _farMeshDirty = true;

            if (PhysicalWaterPlugin.Settings.Diagnostics.Value)
            {
                PhysicalWaterPlugin.Log.LogInfo("Physical far ocean rebuilt: " + _farResolution + "x" + _farResolution +
                                                ", radius=" + _farRadius.ToString("F1") +
                                                "m, inner=" + _farInnerRadius.ToString("F1") +
                                                "m, cell=" + _farCellSize.ToString("F2") + "m.");
            }
        }

        private static void ConfigureRuntimeWaterMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            ConfigurePhysicalOceanMaterial(material);

            if (PhysicalWaterPlugin.Settings != null &&
                PhysicalWaterPlugin.Settings.DebugOpaqueWater.Value)
            {
                ConfigureOpaqueMaterial(material);
                return;
            }

            ConfigureTransparentMaterial(material);
        }

        private static void ConfigurePhysicalOceanMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_DeepColor"))
            {
                material.SetColor("_DeepColor", new Color(0.005f, 0.055f, 0.095f, 0.82f));
            }
            if (material.HasProperty("_ShallowColor"))
            {
                material.SetColor("_ShallowColor", new Color(0.015f, 0.28f, 0.34f, 0.72f));
            }
            if (material.HasProperty("_FoamColor"))
            {
                material.SetColor("_FoamColor", new Color(0.90f, 0.98f, 1.0f, 0.96f));
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.94f);
            }
            if (material.HasProperty("_FresnelPower"))
            {
                material.SetFloat("_FresnelPower", 4.4f);
            }
            if (material.HasProperty("_FoamStrength"))
            {
                material.SetFloat("_FoamStrength", 0.85f);
            }
            if (material.HasProperty("_GlintStrength"))
            {
                material.SetFloat("_GlintStrength", 0.22f);
            }
            if (material.HasProperty("_NormalStrength"))
            {
                material.SetFloat("_NormalStrength", 0.72f);
            }
            if (material.HasProperty("_RefractionStrength"))
            {
                material.SetFloat("_RefractionStrength", 0.018f);
            }
            if (material.HasProperty("_StormWhitecap"))
            {
                material.SetFloat("_StormWhitecap", 0.85f);
            }
            if (material.HasProperty("_VisualWaveStrength"))
            {
                material.SetFloat("_VisualWaveStrength", 0.68f);
            }
            if (material.HasProperty("_CausticStrength"))
            {
                material.SetFloat("_CausticStrength", 0.075f);
            }
            if (material.HasProperty("_DepthAbsorption"))
            {
                material.SetFloat("_DepthAbsorption", 0.20f);
            }
            if (material.HasProperty("_ShoreFoamDepth"))
            {
                material.SetFloat("_ShoreFoamDepth", 2.20f);
            }
            if (material.HasProperty("_ReflectionStrength"))
            {
                material.SetFloat("_ReflectionStrength", 0.78f);
            }
        }

        private void EnsureWaterCameraInputs()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            // 0.5.1 depth-aware optics require the opaque scene depth below the transparent ocean.
            // Unity keeps this flag additive, so we do not interfere with any other effect asking for depth.
            camera.depthTextureMode |= DepthTextureMode.Depth;
        }

        private void PushWaterMaterialGlobals(float time)
        {
            if (_runtimeMaterial == null)
            {
                return;
            }

            PushGeneratedWaterTextures(_runtimeMaterial);
            float size = Mathf.Max(_extent * 2f, 1f);
            _runtimeMaterial.SetFloat("_OceanTime", time);
            _runtimeMaterial.SetVector("_OceanOriginSize", new Vector4(_origin.x, _origin.z, size, PhysicalWaterPlugin.Settings.SeaLevel.Value));
            float stormBlend = GetStormBlend();
            float precipitationBlend = GetPrecipitationBlend();
            _runtimeMaterial.SetFloat("_StormBlend", stormBlend);
            _runtimeMaterial.SetFloat("_WindIntensity", Mathf.Clamp(GetOceanWindIntensity(), 0.08f, 1.35f));
            if (_runtimeMaterial.HasProperty("_PrecipitationBlend"))
            {
                _runtimeMaterial.SetFloat("_PrecipitationBlend", precipitationBlend);
            }
            if (_runtimeMaterial.HasProperty("_CameraUnderwater"))
            {
                Camera camera = Camera.main;
                float underwater = 0f;
                if (camera != null)
                {
                    float surface = GetPhysicalSurfaceHeightUnchecked(camera.transform.position, 0.75f);
                    underwater = surface > -9990f && camera.transform.position.y < surface - 0.08f ? 1f : 0f;
                }
                _runtimeMaterial.SetFloat("_CameraUnderwater", underwater);
            }
            if (_runtimeMaterial.HasProperty("_WaveAmplitude"))
            {
                _runtimeMaterial.SetFloat("_WaveAmplitude", Mathf.Clamp(PhysicalWaterPlugin.Settings.WindWaveAmplitude.Value, 0f, 2.5f));
            }
            if (_runtimeMaterial.HasProperty("_RippleScale"))
            {
                _runtimeMaterial.SetFloat("_RippleScale", Mathf.Min(GetMaxSimulatedDisplacement(), 0.32f));
            }
            if (_runtimeMaterial.HasProperty("_RippleTex") && _rippleFieldTexture != null)
            {
                _runtimeMaterial.SetTexture("_RippleTex", _rippleFieldTexture);
            }
            if (_runtimeMaterial.HasProperty("_BathymetryTex") && _bathymetryTexture != null)
            {
                _runtimeMaterial.SetTexture("_BathymetryTex", _bathymetryTexture);
            }
            if (_runtimeMaterial.HasProperty("_BathymetryOriginSize"))
            {
                _runtimeMaterial.SetVector("_BathymetryOriginSize", new Vector4(_origin.x, _origin.z, size, 64f));
            }
            if (_runtimeMaterial.HasProperty("_BathymetryMaxDepth"))
            {
                _runtimeMaterial.SetFloat("_BathymetryMaxDepth", 64f);
            }
            if (_runtimeMaterial.HasProperty("_OceanDomainTex") && _oceanDomainTexture != null)
            {
                _runtimeMaterial.SetTexture("_OceanDomainTex", _oceanDomainTexture);
            }
            if (_runtimeMaterial.HasProperty("_OceanDomainOriginSize"))
            {
                _runtimeMaterial.SetVector("_OceanDomainOriginSize", new Vector4(_oceanDomainOrigin.x, _oceanDomainOrigin.z, Mathf.Max(1f, _oceanDomainSize), 0f));
            }
            Vector2 wind = GetOceanWindDirection2D();
            _runtimeMaterial.SetVector("_WindDirection", new Vector4(wind.x, wind.y, 0f, 0f));
            PushCommonWaterTextureAnimation(_runtimeMaterial, time, wind);
        }

        private static void PushCommonWaterTextureAnimation(Material material, float time, Vector2 wind)
        {
            if (material == null)
            {
                return;
            }

            Vector2 primary = wind.sqrMagnitude > 0.001f ? wind.normalized : new Vector2(0.94f, 0.34f).normalized;
            Vector2 cross = new Vector2(-primary.y, primary.x);
            Vector2 slowOffset = primary * (time * 0.014f) + cross * (Mathf.Sin(time * 0.13f) * 0.006f);
            Vector2 fineOffset = primary * (time * 0.046f) - cross * (time * 0.016f);

            if (material.HasProperty("_MainTex"))
            {
                material.SetTextureOffset("_MainTex", slowOffset);
            }
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTextureOffset("_BaseMap", slowOffset);
            }
            if (material.HasProperty("_BumpMap"))
            {
                material.SetTextureOffset("_BumpMap", fineOffset);
            }
            if (material.HasProperty("_NormalMap"))
            {
                material.SetTextureOffset("_NormalMap", fineOffset);
            }
            if (material.HasProperty("_NormalA"))
            {
                material.SetTextureOffset("_NormalA", fineOffset);
            }
            if (material.HasProperty("_NormalB"))
            {
                material.SetTextureOffset("_NormalB", slowOffset);
            }
            if (material.HasProperty("_FoamTex"))
            {
                material.SetTextureOffset("_FoamTex", primary * (time * 0.022f) + cross * (Mathf.Sin(time * 0.19f) * 0.012f));
            }
        }

        private void PushGeneratedWaterTextures(Material material)
        {
            if (material == null)
            {
                return;
            }

            EnsureGeneratedWaterTextures();
            if (_normalA != null && material.HasProperty("_NormalA"))
            {
                material.SetTexture("_NormalA", _normalA);
            }
            if (_normalB != null && material.HasProperty("_NormalB"))
            {
                material.SetTexture("_NormalB", _normalB);
            }
            if (_foamTex != null && material.HasProperty("_FoamTex"))
            {
                material.SetTexture("_FoamTex", _foamTex);
            }
            if (_noiseTex != null && material.HasProperty("_NoiseTex"))
            {
                material.SetTexture("_NoiseTex", _noiseTex);
            }
        }

        private void RebuildRippleFieldTexture()
        {
            if (_rippleFieldTexture != null)
            {
                Destroy(_rippleFieldTexture);
                _rippleFieldTexture = null;
            }

            if (_resolution < 2)
            {
                _rippleFieldPixels = null;
                return;
            }

            _rippleFieldTexture = new Texture2D(_resolution, _resolution, TextureFormat.RGBA32, false, true);
            _rippleFieldTexture.name = "PhysicalWater_LiveRippleField";
            _rippleFieldTexture.wrapMode = TextureWrapMode.Clamp;
            _rippleFieldTexture.filterMode = FilterMode.Bilinear;
            _rippleFieldTexture.hideFlags = HideFlags.DontSave;
            _rippleFieldPixels = new Color32[_resolution * _resolution];
            for (int i = 0; i < _rippleFieldPixels.Length; i++)
            {
                _rippleFieldPixels[i] = new Color32(128, 128, 128, 255);
            }
            _rippleFieldTexture.SetPixels32(_rippleFieldPixels);
            _rippleFieldTexture.Apply(false, false);
            _nextRippleTextureUploadTime = 0f;
        }

        private void UpdateRippleFieldTexture()
        {
            if (_current == null || _rippleFieldTexture == null || _rippleFieldPixels == null ||
                _rippleFieldPixels.Length != _current.Length || Time.time < _nextRippleTextureUploadTime)
            {
                return;
            }

            _nextRippleTextureUploadTime = Time.time + 0.03333334f;
            float scale = Mathf.Max(0.01f, Mathf.Min(GetMaxSimulatedDisplacement(), 0.32f));
            for (int i = 0; i < _current.Length; i++)
            {
                float normalized = Mathf.Clamp01(_current[i] / (scale * 2f) + 0.5f);
                byte encoded = (byte)Mathf.RoundToInt(normalized * 255f);
                _rippleFieldPixels[i] = new Color32(encoded, encoded, encoded, 255);
            }
            _rippleFieldTexture.SetPixels32(_rippleFieldPixels);
            _rippleFieldTexture.Apply(false, false);
        }

        private void RebuildOceanDomainTexture()
        {
            if (_oceanDomainTexture != null)
            {
                Destroy(_oceanDomainTexture);
                _oceanDomainTexture = null;
            }
            const int domainResolution = 65;
            _oceanDomainTexture = new Texture2D(domainResolution, domainResolution, TextureFormat.RGBA32, false, true);
            _oceanDomainTexture.name = "PhysicalWater_FarOceanDomainMask";
            _oceanDomainTexture.wrapMode = TextureWrapMode.Clamp;
            _oceanDomainTexture.filterMode = FilterMode.Bilinear;
            _oceanDomainTexture.hideFlags = HideFlags.DontSave;
            int count = domainResolution * domainResolution;
            _oceanDomainPixels = new Color32[count];
            _oceanDomainCandidate = new bool[count];
            _oceanDomainWet = new bool[count];
            _oceanDomainQueue = new int[count];
            // Cover the 1536 m outer LOD with enough margin for independent camera/domain snapping.
            _oceanDomainSize = 3584f;
            _nextOceanDomainRefresh = 0f;
            UpdateOceanDomainTexture(true);
        }

        private void UpdateOceanDomainTexture(bool force)
        {
            if (_oceanDomainTexture == null || _oceanDomainPixels == null || _oceanDomainCandidate == null ||
                _oceanDomainWet == null || _oceanDomainQueue == null || WorldGenerator.instance == null) return;
            Vector3 focus = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            const float snap = 192f;
            Vector3 wanted = new Vector3(Mathf.Round(focus.x / snap) * snap - _oceanDomainSize * 0.5f, 0f, Mathf.Round(focus.z / snap) * snap - _oceanDomainSize * 0.5f);
            if (!force && Time.time < _nextOceanDomainRefresh && (wanted - _oceanDomainOrigin).sqrMagnitude < 1f) return;
            _nextOceanDomainRefresh = Time.time + 1.25f;
            _oceanDomainOrigin = wanted;
            int resolution = _oceanDomainTexture.width;
            float step = _oceanDomainSize / Mathf.Max(1, resolution - 1);
            float seaLevel = PhysicalWaterPlugin.Settings.SeaLevel.Value;
            Array.Clear(_oceanDomainCandidate, 0, _oceanDomainCandidate.Length);
            Array.Clear(_oceanDomainWet, 0, _oceanDomainWet.Length);

            // Build a COARSE connected-ocean visibility domain. Elevation alone never marks a far
            // cell wet: cells must be low enough for water AND connected to an Ocean-biome seed.
            int head = 0;
            int tail = 0;
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int index = x + y * resolution;
                    Vector3 world = new Vector3(_oceanDomainOrigin.x + x * step, seaLevel, _oceanDomainOrigin.z + y * step);
                    float terrainHeight;
                    bool hasHeight = TryGetTerrainHeight(world, out terrainHeight);
                    bool candidate = !hasHeight || terrainHeight < seaLevel - 0.04f;
                    _oceanDomainCandidate[index] = candidate;
                    if (!candidate) continue;
                    bool ocean = false;
                    try { ocean = WorldGenerator.instance.GetBiome(world) == Heightmap.Biome.Ocean; }
                    catch { ocean = false; }
                    if (ocean)
                    {
                        _oceanDomainWet[index] = true;
                        if (tail < _oceanDomainQueue.Length) _oceanDomainQueue[tail++] = index;
                    }
                }
            }

            while (head < tail)
            {
                int index = _oceanDomainQueue[head++];
                int x = index % resolution;
                int y = index / resolution;
                if (x > 0) EnqueueOceanDomainCell(index - 1, ref tail);
                if (x < resolution - 1) EnqueueOceanDomainCell(index + 1, ref tail);
                if (y > 0) EnqueueOceanDomainCell(index - resolution, ref tail);
                if (y < resolution - 1) EnqueueOceanDomainCell(index + resolution, ref tail);
            }

            for (int i = 0; i < _oceanDomainPixels.Length; i++)
            {
                byte v = _oceanDomainWet[i] ? (byte)255 : (byte)0;
                _oceanDomainPixels[i] = new Color32(v, v, v, 255);
            }
            _oceanDomainTexture.SetPixels32(_oceanDomainPixels);
            _oceanDomainTexture.Apply(false, false);
        }

        private void EnqueueOceanDomainCell(int index, ref int tail)
        {
            if (!_oceanDomainCandidate[index] || _oceanDomainWet[index]) return;
            _oceanDomainWet[index] = true;
            if (tail < _oceanDomainQueue.Length) _oceanDomainQueue[tail++] = index;
        }

        private void RebuildBathymetryTexture()
        {
            if (_bathymetryTexture != null)
            {
                Destroy(_bathymetryTexture);
                _bathymetryTexture = null;
            }
            if (_resolution < 2)
            {
                _bathymetryPixels = null;
                return;
            }
            _bathymetryTexture = new Texture2D(_resolution, _resolution, TextureFormat.RGBA32, false, true);
            _bathymetryTexture.name = "PhysicalWater_BathymetryObstacleField";
            _bathymetryTexture.wrapMode = TextureWrapMode.Clamp;
            _bathymetryTexture.filterMode = FilterMode.Bilinear;
            _bathymetryTexture.hideFlags = HideFlags.DontSave;
            _bathymetryPixels = new Color32[_resolution * _resolution];
            for (int i = 0; i < _bathymetryPixels.Length; i++) _bathymetryPixels[i] = new Color32(255, 0, 0, 0);
            _bathymetryTexture.SetPixels32(_bathymetryPixels);
            _bathymetryTexture.Apply(false, false);
        }

        private void UpdateBathymetryDerivedFields(float seaLevel)
        {
            if (_waterDepthField == null || _shoreDistanceField == null || _obstacleDistanceField == null ||
                _bedSlopeField == null || _effectiveBedHeights == null || _bathymetryPixels == null)
            {
                return;
            }

            ComputeDistanceField(_waterMask, null, _shoreDistanceField);
            ComputeDistanceField(null, _staticObstacleMask, _obstacleDistanceField);

            for (int y = 0; y < _resolution; y++)
            {
                for (int x = 0; x < _resolution; x++)
                {
                    int index = Index(x, y);
                    float bed = _effectiveBedHeights[index];
                    _waterDepthField[index] = _hydroInitialized && _hydroDepth != null
                        ? Mathf.Max(0f, _hydroDepth[index])
                        : 0f;
                    if (_waterMask != null && !_waterMask[index] && _current != null)
                    {
                        _current[index] = 0f;
                        _previous[index] = 0f;
                        _next[index] = 0f;
                    }
                    int xl = Mathf.Max(0, x - 1);
                    int xr = Mathf.Min(_resolution - 1, x + 1);
                    int yd = Mathf.Max(0, y - 1);
                    int yu = Mathf.Min(_resolution - 1, y + 1);
                    float dx = (_effectiveBedHeights[Index(xr, y)] - _effectiveBedHeights[Index(xl, y)]) /
                               Mathf.Max(_cellSize * (xr - xl), 0.001f);
                    float dz = (_effectiveBedHeights[Index(x, yu)] - _effectiveBedHeights[Index(x, yd)]) /
                               Mathf.Max(_cellSize * (yu - yd), 0.001f);
                    _bedSlopeField[index] = Mathf.Clamp01(new Vector2(dx, dz).magnitude / 1.35f);
                }
            }

            const float maxDepth = 64f;
            const float shoreRange = 14f;
            const float obstacleRange = 9f;
            for (int i = 0; i < _bathymetryPixels.Length; i++)
            {
                float depth01 = Mathf.Clamp01(_waterDepthField[i] / maxDepth);
                float shore = _waterMask[i] ? 1f - Mathf.Clamp01(_shoreDistanceField[i] / shoreRange) : 1f;
                float obstacle = 1f - Mathf.Clamp01(_obstacleDistanceField[i] / obstacleRange);
                // Alpha is authoritative hydrodynamic wetness in 0.5.1. The shader clips local
                // dry cells entirely, so an excavated dry pit or the dry side of a wall cannot
                // receive the offshore blanket renderer. Bed-slope foam is derived from the
                // depth/shore field in shader instead of consuming this channel.
                bool wet = _waterMask != null && _waterMask[i] && _hydroDepth != null && _hydroDepth[i] > HydroMinWetDepth;
                _bathymetryPixels[i] = new Color32(
                    (byte)Mathf.RoundToInt(depth01 * 255f),
                    (byte)Mathf.RoundToInt(shore * 255f),
                    (byte)Mathf.RoundToInt(obstacle * 255f),
                    wet ? (byte)255 : (byte)0);
            }
            _bathymetryTexture.SetPixels32(_bathymetryPixels);
            _bathymetryTexture.Apply(false, false);
        }

        private void ComputeDistanceField(bool[] dryFromWaterMask, bool[] sourceMask, float[] distances)
        {
            if (distances == null || _distanceQueue == null) return;
            int head = 0;
            int tail = 0;
            for (int i = 0; i < distances.Length; i++)
            {
                bool source = sourceMask != null ? sourceMask[i] : (dryFromWaterMask != null && !dryFromWaterMask[i]);
                if (source)
                {
                    distances[i] = 0f;
                    _distanceQueue[tail++] = i;
                }
                else distances[i] = 99999f;
            }
            while (head < tail)
            {
                int index = _distanceQueue[head++];
                int x = index % _resolution;
                int y = index / _resolution;
                float nextDistance = distances[index] + _cellSize;
                TryRelaxDistance(x - 1, y, nextDistance, distances, ref tail);
                TryRelaxDistance(x + 1, y, nextDistance, distances, ref tail);
                TryRelaxDistance(x, y - 1, nextDistance, distances, ref tail);
                TryRelaxDistance(x, y + 1, nextDistance, distances, ref tail);
            }
        }

        private void TryRelaxDistance(int x, int y, float candidate, float[] distances, ref int tail)
        {
            if (x < 0 || y < 0 || x >= _resolution || y >= _resolution) return;
            int index = Index(x, y);
            if (candidate + 0.001f >= distances[index]) return;
            distances[index] = candidate;
            if (tail < _distanceQueue.Length) _distanceQueue[tail++] = index;
        }

        private bool TryGetStaticObstacleSurface(float worldX, float worldZ, float terrainHeight, float seaLevel, out float obstacleTop)
        {
            obstacleTop = terrainHeight;
            if (terrainHeight < seaLevel - 10f || terrainHeight > seaLevel + 0.75f) return false;
            Vector3 start = new Vector3(worldX, seaLevel + 7f, worldZ);
            float castDistance = Mathf.Clamp((seaLevel + 7f) - (terrainHeight - 1.5f), 8.5f, 22f);
            int hitCount = Physics.RaycastNonAlloc(start, Vector3.down, _obstacleRayHits, castDistance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool found = false;
            float highest = terrainHeight;
            for (int i = 0; i < hitCount; i++)
            {
                Collider collider = _obstacleRayHits[i].collider;
                if (collider == null || collider.isTrigger || collider.attachedRigidbody != null) continue;
                if (collider.GetComponentInParent<Heightmap>() != null) continue;
                Bounds bounds = collider.bounds;
                if (bounds.min.y > seaLevel + 0.35f || bounds.max.y < terrainHeight + 0.20f) continue;
                float top = _obstacleRayHits[i].point.y;
                if (top > highest + 0.12f)
                {
                    highest = top;
                    found = true;
                }
            }
            obstacleTop = highest;
            return found;
        }

        private float SampleBathymetryDepth(Vector3 worldPosition)
        {
            if (_waterDepthField == null || _resolution < 2) return 64f;
            Vector2 grid = WorldToGrid(worldPosition);
            if (grid.x < 0f || grid.y < 0f || grid.x > _resolution - 1 || grid.y > _resolution - 1) return 64f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(grid.x), 0, _resolution - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(grid.y), 0, _resolution - 1);
            int x1 = Mathf.Min(x0 + 1, _resolution - 1);
            int y1 = Mathf.Min(y0 + 1, _resolution - 1);
            float tx = Mathf.Clamp01(grid.x - x0);
            float ty = Mathf.Clamp01(grid.y - y0);
            return Mathf.Lerp(
                Mathf.Lerp(_waterDepthField[Index(x0, y0)], _waterDepthField[Index(x1, y0)], tx),
                Mathf.Lerp(_waterDepthField[Index(x0, y1)], _waterDepthField[Index(x1, y1)], tx), ty);
        }

        private float GetWaveDepthScale(Vector3 worldPosition, float wavelength)
        {
            float depth = SampleBathymetryDepth(worldPosition);
            float halfWave = Mathf.Max(0.5f, wavelength * 0.5f);
            float ratio = Mathf.Clamp01(depth / halfWave);
            float attenuation = Mathf.SmoothStep(0.06f, 1f, Mathf.Clamp01((ratio - 0.015f) / 0.72f));
            float veryShallow = Mathf.SmoothStep(0.08f, 1.4f, depth);
            float shoalBand = Mathf.SmoothStep(0.04f, 0.26f, ratio) * (1f - Mathf.SmoothStep(0.30f, 0.78f, ratio));
            return Mathf.Clamp((0.05f + 0.95f * attenuation) * veryShallow * (1f + shoalBand * 0.16f), 0f, 1.16f);
        }

        private void EnsureGeneratedWaterTextures()
        {
            if (_normalA != null && _normalB != null && _foamTex != null && _noiseTex != null)
            {
                return;
            }

            _normalA = GenerateNormalTexture(512, 5.5f, 0.028f, 11);
            _normalB = GenerateNormalTexture(512, 12.0f, 0.018f, 37);
            _foamTex = GenerateFoamTexture(512);
            _noiseTex = GenerateNoiseTexture(512, 91);
        }

        private Texture2D GetSoftSplashParticleTexture()
        {
            if (_softSplashTex == null)
            {
                _softSplashTex = GenerateSoftParticleTexture(128, 0.38f, 0.76f, 0.18f, 0.60f, 123);
            }

            return _softSplashTex;
        }

        private Texture2D GetSoftFoamParticleTexture()
        {
            if (_softFoamParticleTex == null)
            {
                _softFoamParticleTex = GenerateSoftParticleTexture(128, 0.30f, 0.88f, 0.10f, 0.42f, 211);
            }

            return _softFoamParticleTex;
        }

        private static Texture2D GenerateNormalTexture(int size, float frequency, float strength, int seed)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true);
            texture.name = "PhysicalWater_ProceduralNormal";
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 8;
            texture.hideFlags = HideFlags.DontSave;

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float v = (float)y / size;
                    float hL = FractalNoise(u - 1f / size, v, frequency, seed);
                    float hR = FractalNoise(u + 1f / size, v, frequency, seed);
                    float hD = FractalNoise(u, v - 1f / size, frequency, seed);
                    float hU = FractalNoise(u, v + 1f / size, frequency, seed);
                    Vector3 normal = new Vector3((hL - hR) * strength, (hD - hU) * strength, 1f).normalized;
                    pixels[x + y * size] = new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z, 1f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true, false);
            return texture;
        }

        private static Texture2D GenerateFoamTexture(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true);
            texture.name = "PhysicalWater_ProceduralFoam";
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 8;
            texture.hideFlags = HideFlags.DontSave;

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float v = (float)y / size;
                    float noise = FractalNoise(u, v, 18f, 53);
                    float streak = Mathf.Abs(Mathf.Sin((u * 12.0f + v * 2.5f + noise * 2f) * Mathf.PI));
                    float foam = SmoothStep(0.58f, 0.93f, noise * 0.75f + streak * 0.25f);
                    pixels[x + y * size] = new Color(foam, foam, foam, foam);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true, false);
            return texture;
        }

        private static Texture2D GenerateNoiseTexture(int size, int seed)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true);
            texture.name = "PhysicalWater_ProceduralNoise";
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Trilinear;
            texture.hideFlags = HideFlags.DontSave;

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float v = (float)y / size;
                    float noise = FractalNoise(u, v, 8f, seed);
                    pixels[x + y * size] = new Color(noise, noise * noise, 1f - noise, 1f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true, false);
            return texture;
        }

        private static Texture2D GenerateSoftParticleTexture(int size, float coreRadius, float outerRadius, float noiseStrength, float alphaScale, int seed)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true);
            texture.name = "PhysicalWater_RuntimeSoftParticle";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.DontSave;

            Color[] pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float inverseCenter = center > 0f ? 1f / center : 1f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) * inverseCenter;
                    float dy = (y - center) * inverseCenter;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float softDisc = 1f - SmoothStep(coreRadius, outerRadius, distance);
                    float ring = SmoothStep(0.16f, 0.74f, distance) * (1f - SmoothStep(0.72f, 0.96f, distance));
                    float noise = FractalNoise((float)x / size, (float)y / size, 8.5f, seed);
                    float brokenFoam = Mathf.Clamp01(softDisc * (0.78f + noise * noiseStrength) + ring * 0.22f * noise);
                    float alpha = Mathf.Clamp01(brokenFoam * alphaScale);
                    pixels[x + y * size] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true, true);
            return texture;
        }

        private static float FractalNoise(float u, float v, float frequency, int seed)
        {
            float value = 0f;
            float amplitude = 0.5f;
            float total = 0f;
            for (int i = 0; i < 4; i++)
            {
                float x = u * frequency + seed * 0.013f;
                float y = v * frequency + seed * 0.031f;
                value += Mathf.PerlinNoise(x, y) * amplitude;
                total += amplitude;
                frequency *= 2.07f;
                amplitude *= 0.5f;
                seed += 17;
            }

            return total > 0f ? value / total : 0f;
        }

        private static float SmoothStep(float from, float to, float value)
        {
            float t = Mathf.Clamp01((value - from) / Mathf.Max(0.0001f, to - from));
            return t * t * (3f - 2f * t);
        }

        private void DestroyGeneratedWaterTextures()
        {
            DestroyTexture(ref _normalA);
            DestroyTexture(ref _normalB);
            DestroyTexture(ref _foamTex);
            DestroyTexture(ref _noiseTex);
            DestroyTexture(ref _rippleFieldTexture);
            _rippleFieldPixels = null;
            DestroyTexture(ref _softSplashTex);
            DestroyTexture(ref _softFoamParticleTex);
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            Destroy(texture);
            texture = null;
        }

        private static void ConfigureOpaqueMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 0f);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetInt("_ZWrite", 1);
            }

            if (material.HasProperty("_Cull"))
            {
                material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            }

            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        }

        private static void ConfigureTransparentMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3f);
            }

            if (material.HasProperty("_SrcBlend"))
            {
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetInt("_ZWrite", 0);
            }

            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private void UpdateOrigin()
        {
            Vector3 center = Vector3.zero;
            if (Player.m_localPlayer != null)
            {
                center = Player.m_localPlayer.transform.position;
            }
            else if (ZNetScene.instance != null)
            {
                center = ZNetScene.instance.transform.position;
            }

            float snap = Mathf.Max(_cellSize, PhysicalWaterPlugin.Settings.OriginSnapMeters.Value);
            float snappedCenterX = Mathf.Round(center.x / snap) * snap;
            float snappedCenterZ = Mathf.Round(center.z / snap) * snap;
            Vector3 newOrigin = new Vector3(snappedCenterX - _extent, PhysicalWaterPlugin.Settings.SeaLevel.Value, snappedCenterZ - _extent);
            if ((newOrigin - _origin).sqrMagnitude > 0.01f && _resolution > 1 && _cellSize > 0.001f)
            {
                ShiftSimulationToNewOrigin(_origin, newOrigin);
                _lastOrigin = _origin;
                _origin = newOrigin;
                _masksDirty = true;
                _trianglesDirty = true;
            }
            else
            {
                _origin = newOrigin;
            }

            transform.position = Vector3.zero;
        }

        private void ShiftSimulationToNewOrigin(Vector3 oldOrigin, Vector3 newOrigin)
        {
            if (_current == null || _previous == null || _next == null ||
                _shiftScratchCurrent == null || _shiftScratchCurrent.Length != _current.Length)
            {
                ClearSimulation();
                return;
            }

            int shiftX = Mathf.RoundToInt((newOrigin.x - oldOrigin.x) / _cellSize);
            int shiftY = Mathf.RoundToInt((newOrigin.z - oldOrigin.z) / _cellSize);
            if (Mathf.Abs(shiftX) >= _resolution || Mathf.Abs(shiftY) >= _resolution)
            {
                ClearSimulation();
                return;
            }

            Array.Clear(_shiftScratchCurrent, 0, _shiftScratchCurrent.Length);
            Array.Clear(_shiftScratchPrevious, 0, _shiftScratchPrevious.Length);
            Array.Clear(_shiftScratchNext, 0, _shiftScratchNext.Length);
            for (int y = 0; y < _resolution; y++)
            {
                int srcY = y + shiftY;
                if (srcY < 0 || srcY >= _resolution) continue;
                for (int x = 0; x < _resolution; x++)
                {
                    int srcX = x + shiftX;
                    if (srcX < 0 || srcX >= _resolution) continue;
                    int dst = Index(x, y);
                    int src = Index(srcX, srcY);
                    _shiftScratchCurrent[dst] = _current[src];
                    _shiftScratchPrevious[dst] = _previous[src];
                    _shiftScratchNext[dst] = _next[src];
                }
            }
            Array.Copy(_shiftScratchCurrent, _current, _current.Length);
            Array.Copy(_shiftScratchPrevious, _previous, _previous.Length);
            Array.Copy(_shiftScratchNext, _next, _next.Length);

            ShiftHydrodynamicState(shiftX, shiftY);
        }

        private void ClearSimulation()
        {
            if (_current != null)
            {
                Array.Clear(_current, 0, _current.Length);
            }

            if (_previous != null)
            {
                Array.Clear(_previous, 0, _previous.Length);
            }

            if (_next != null)
            {
                Array.Clear(_next, 0, _next.Length);
            }

            if (_hydroDepth != null) Array.Clear(_hydroDepth, 0, _hydroDepth.Length);
            if (_hydroNextDepth != null) Array.Clear(_hydroNextDepth, 0, _hydroNextDepth.Length);
            if (_hydroFaceEast != null) Array.Clear(_hydroFaceEast, 0, _hydroFaceEast.Length);
            if (_hydroFaceNorth != null) Array.Clear(_hydroFaceNorth, 0, _hydroFaceNorth.Length);
            if (_hydroFluxEast != null) Array.Clear(_hydroFluxEast, 0, _hydroFluxEast.Length);
            if (_hydroFluxNorth != null) Array.Clear(_hydroFluxNorth, 0, _hydroFluxNorth.Length);
            if (_hydroRequestedOutflow != null) Array.Clear(_hydroRequestedOutflow, 0, _hydroRequestedOutflow.Length);
            if (_hydroOutflowScale != null) Array.Clear(_hydroOutflowScale, 0, _hydroOutflowScale.Length);
            if (_hydroNewCellMask != null) Array.Clear(_hydroNewCellMask, 0, _hydroNewCellMask.Length);
            _hydroInitialized = false;
        }

        private void ShiftHydrodynamicState(int shiftX, int shiftY)
        {
            if (_hydroDepth == null || _shiftScratchHydroDepth == null) return;
            Array.Clear(_shiftScratchHydroDepth, 0, _shiftScratchHydroDepth.Length);
            Array.Clear(_shiftScratchHydroEast, 0, _shiftScratchHydroEast.Length);
            Array.Clear(_shiftScratchHydroNorth, 0, _shiftScratchHydroNorth.Length);
            if (_hydroNewCellMask != null) Array.Clear(_hydroNewCellMask, 0, _hydroNewCellMask.Length);

            for (int y = 0; y < _resolution; y++)
            {
                int srcY = y + shiftY;
                for (int x = 0; x < _resolution; x++)
                {
                    int srcX = x + shiftX;
                    int dst = Index(x, y);
                    if (srcX < 0 || srcX >= _resolution || srcY < 0 || srcY >= _resolution)
                    {
                        if (_hydroNewCellMask != null) _hydroNewCellMask[dst] = true;
                        continue;
                    }
                    int src = Index(srcX, srcY);
                    _shiftScratchHydroDepth[dst] = _hydroDepth[src];
                    _shiftScratchHydroEast[dst] = _hydroFaceEast[src];
                    _shiftScratchHydroNorth[dst] = _hydroFaceNorth[src];
                }
            }
            Array.Copy(_shiftScratchHydroDepth, _hydroDepth, _hydroDepth.Length);
            Array.Copy(_shiftScratchHydroEast, _hydroFaceEast, _hydroFaceEast.Length);
            Array.Copy(_shiftScratchHydroNorth, _hydroFaceNorth, _hydroFaceNorth.Length);
            _hydroInitialized = true;
        }

        private void EnsureHydrodynamicsInitialized(float seaLevel)
        {
            if (_hydroInitialized || _hydroDepth == null || _candidateWaterMask == null) return;
            Array.Clear(_hydroDepth, 0, _hydroDepth.Length);
            Array.Clear(_hydroFaceEast, 0, _hydroFaceEast.Length);
            Array.Clear(_hydroFaceNorth, 0, _hydroFaceNorth.Length);

            // Initialization is the only instant flood-fill. The coarse domain is an already
            // classified connected-ocean capture; seed every open local cell it identifies, not
            // only the local tile perimeter. A player-centred tile can be wholly inside the ocean,
            // so perimeter-only seeding leaves its valid ocean floor dry forever. Solid/obstacle
            // cells remain excluded and isolated sub-sea basins remain dry because the coarse
            // connected-domain classification does not mark them.
            Array.Clear(_waterMask, 0, _waterMask.Length);
            int head = 0;
            int tail = 0;
            for (int y = 0; y < _resolution; y++)
            {
                for (int x = 0; x < _resolution; x++)
                {
                    int index = Index(x, y);
                    if (!_candidateWaterMask[index] || _solidHydroMask[index]) continue;
                    Vector3 world = new Vector3(_origin.x + x * _cellSize, seaLevel, _origin.z + y * _cellSize);
                    if (IsCoarseOceanDomainWet(world))
                    {
                        _waterMask[index] = true;
                        if (tail < _floodQueue.Length) _floodQueue[tail++] = index;
                    }
                }
            }
            for (int x = 0; x < _resolution; x++)
            {
                SeedHydroBoundary(Index(x, 0), ref tail);
                SeedHydroBoundary(Index(x, _resolution - 1), ref tail);
            }
            for (int y = 1; y < _resolution - 1; y++)
            {
                SeedHydroBoundary(Index(0, y), ref tail);
                SeedHydroBoundary(Index(_resolution - 1, y), ref tail);
            }

            while (head < tail)
            {
                int index = _floodQueue[head++];
                int x = index % _resolution;
                int y = index / _resolution;
                SeedHydroNeighbor(x - 1, y, index, ref tail);
                SeedHydroNeighbor(x + 1, y, index, ref tail);
                SeedHydroNeighbor(x, y - 1, index, ref tail);
                SeedHydroNeighbor(x, y + 1, index, ref tail);
            }

            for (int i = 0; i < _hydroDepth.Length; i++)
            {
                if (_waterMask[i] && !_solidHydroMask[i])
                    _hydroDepth[i] = Mathf.Max(0f, seaLevel - _effectiveBedHeights[i]);
                else
                    _hydroDepth[i] = 0f;
            }
            _hydroInitialized = true;
            RefreshHydrodynamicWetMasks();
            _lastHydroVolume = CalculateHydroVolume();
        }

        private void SeedHydroBoundary(int index, ref int tail)
        {
            if (!_oceanBoundaryMask[index] || _solidHydroMask[index] || _waterMask[index]) return;
            _waterMask[index] = true;
            if (tail < _floodQueue.Length) _floodQueue[tail++] = index;
        }

        private void SeedHydroNeighbor(int x, int y, int fromIndex, ref int tail)
        {
            if (x < 0 || y < 0 || x >= _resolution || y >= _resolution) return;
            int index = Index(x, y);
            if (!_candidateWaterMask[index] || _solidHydroMask[index] || _waterMask[index]) return;
            int fx = fromIndex % _resolution;
            int fy = fromIndex / _resolution;
            if (IsHydroBarrierBetween(fx, fy, x, y)) return;
            _waterMask[index] = true;
            if (tail < _floodQueue.Length) _floodQueue[tail++] = index;
        }

        private void StepHydrodynamics(float deltaTime)
        {
            if (!_hydroInitialized || _hydroDepth == null) return;
            _hydroAccumulator += Mathf.Clamp(deltaTime, 0f, 0.10f);
            int steps = 0;
            while (_hydroAccumulator >= HydroStep && steps < 4)
            {
                SimulateHydrodynamics(HydroStep);
                _hydroAccumulator -= HydroStep;
                steps++;
            }
            if (steps > 0 && Time.time >= _nextHydroTextureRefresh)
            {
                _nextHydroTextureRefresh = Time.time + 0.0667f; // ~15 Hz visual wet/depth front
                UpdateHydrodynamicBathymetryTextureOnly();
            }
        }

        private void SimulateHydrodynamics(float dt)
        {
            Array.Copy(_hydroDepth, _hydroNextDepth, _hydroDepth.Length);
            Array.Clear(_hydroFluxEast, 0, _hydroFluxEast.Length);
            Array.Clear(_hydroFluxNorth, 0, _hydroFluxNorth.Length);
            Array.Clear(_hydroRequestedOutflow, 0, _hydroRequestedOutflow.Length);
            float invDx = 1f / Mathf.Max(_cellSize, 0.25f);
            const float velocityDamping = 0.986f;

            // Pass 1: update face velocities and calculate requested depth transfers, but do not
            // mutate cell depths yet. This lets us bound the TOTAL outflow from each donor cell.
            for (int y = 0; y < _resolution; y++)
            {
                for (int x = 0; x < _resolution; x++)
                {
                    int a = Index(x, y);
                    if (_solidHydroMask[a]) continue;
                    if (x < _resolution - 1)
                    {
                        int b = Index(x + 1, y);
                        float transfer = CalculateHydroFaceTransfer(a, b, ref _hydroFaceEast[a], _barrierEast[a], dt, invDx, velocityDamping);
                        _hydroFluxEast[a] = transfer;
                        if (transfer > 0f) _hydroRequestedOutflow[a] += transfer;
                        else if (transfer < 0f) _hydroRequestedOutflow[b] += -transfer;
                    }
                    if (y < _resolution - 1)
                    {
                        int b = Index(x, y + 1);
                        float transfer = CalculateHydroFaceTransfer(a, b, ref _hydroFaceNorth[a], _barrierNorth[a], dt, invDx, velocityDamping);
                        _hydroFluxNorth[a] = transfer;
                        if (transfer > 0f) _hydroRequestedOutflow[a] += transfer;
                        else if (transfer < 0f) _hydroRequestedOutflow[b] += -transfer;
                    }
                }
            }

            // A donor may feed up to four faces. Scale ALL of its requested transfers together so
            // a cell can never export more water than it actually owns. This is the mass-conservation
            // guard that the 0.5.0 audit found missing.
            for (int i = 0; i < _hydroDepth.Length; i++)
            {
                float available = Mathf.Max(0f, _hydroDepth[i]);
                float requested = _hydroRequestedOutflow[i];
                _hydroOutflowScale[i] = requested > available && requested > 0.000001f
                    ? available / requested
                    : 1f;
            }

            // Pass 2: apply the donor-scaled conservative transfers.
            for (int y = 0; y < _resolution; y++)
            {
                for (int x = 0; x < _resolution; x++)
                {
                    int a = Index(x, y);
                    if (x < _resolution - 1) ApplyConservativeHydroTransfer(a, Index(x + 1, y), _hydroFluxEast[a]);
                    if (y < _resolution - 1) ApplyConservativeHydroTransfer(a, Index(x, y + 1), _hydroFluxNorth[a]);
                }
            }

            float seaLevel = PhysicalWaterPlugin.Settings.SeaLevel.Value;
            for (int i = 0; i < _hydroNextDepth.Length; i++)
            {
                if (_solidHydroMask[i])
                {
                    _hydroNextDepth[i] = 0f;
                    continue;
                }

                // This clamp should now only remove floating-point dust, never repair overdraw.
                _hydroNextDepth[i] = Mathf.Max(0f, _hydroNextDepth[i]);
                if (_oceanBoundaryMask[i])
                {
                    // Open-ocean perimeter is an external reservoir, so mass may legitimately
                    // enter/leave the LOCAL control volume here. Interior faces remain conservative.
                    float target = Mathf.Max(0f, seaLevel - _effectiveBedHeights[i]);
                    float blend = 1f - Mathf.Exp(-dt * 4.5f);
                    _hydroNextDepth[i] = Mathf.Lerp(_hydroNextDepth[i], target, blend);
                }
            }

            float[] swap = _hydroDepth;
            _hydroDepth = _hydroNextDepth;
            _hydroNextDepth = swap;
            RefreshHydrodynamicWetMasks();
        }

        private float CalculateHydroFaceTransfer(int a, int b, ref float faceVelocity, bool barrier, float dt, float invDx, float damping)
        {
            if (barrier || _solidHydroMask[a] || _solidHydroMask[b])
            {
                faceVelocity = 0f;
                return 0f;
            }

            float depthA = Mathf.Max(0f, _hydroDepth[a]);
            float depthB = Mathf.Max(0f, _hydroDepth[b]);
            if (depthA <= HydroMinWetDepth && depthB <= HydroMinWetDepth)
            {
                faceVelocity = 0f;
                return 0f;
            }

            float etaA = _effectiveBedHeights[a] + depthA;
            float etaB = _effectiveBedHeights[b] + depthB;
            faceVelocity += -HydroGravity * (etaB - etaA) * invDx * dt;
            faceVelocity *= damping;
            faceVelocity = Mathf.Clamp(faceVelocity, -HydroMaxHorizontalSpeed, HydroMaxHorizontalSpeed);

            float donorDepth = faceVelocity >= 0f ? depthA : depthB;
            if (donorDepth <= HydroMinWetDepth) return 0f;
            // Signed transfer measured as WATER DEPTH moved across this face during this step.
            return faceVelocity * donorDepth * dt * invDx;
        }

        private void ApplyConservativeHydroTransfer(int a, int b, float requestedTransfer)
        {
            if (requestedTransfer > 0f)
            {
                float amount = requestedTransfer * _hydroOutflowScale[a];
                _hydroNextDepth[a] -= amount;
                _hydroNextDepth[b] += amount;
            }
            else if (requestedTransfer < 0f)
            {
                float amount = -requestedTransfer * _hydroOutflowScale[b];
                _hydroNextDepth[b] -= amount;
                _hydroNextDepth[a] += amount;
            }
        }

        private void RefreshHydrodynamicWetMasks()
        {
            if (_hydroDepth == null || _waterMask == null) return;
            _waterVertexCount = 0;
            for (int i = 0; i < _hydroDepth.Length; i++)
            {
                bool wet = !_solidHydroMask[i] && _hydroDepth[i] > HydroMinWetDepth;
                _waterMask[i] = wet;
                _visualWaterMask[i] = wet;
                if (wet) _waterVertexCount++;
            }
            // One-cell visual transition only around ACTUAL wet cells, never around all sub-sea terrain.
            for (int y = 0; y < _resolution; y++)
            {
                for (int x = 0; x < _resolution; x++)
                {
                    int index = Index(x, y);
                    if (!_visualWaterMask[index] && !_solidHydroMask[index] && HasNeighborWater(x, y))
                        _visualWaterMask[index] = true;
                }
            }
            _trianglesDirty = true;
            _meshDirty = true;
        }

        private bool TrySampleHydrodynamicSurface(Vector3 worldPosition, out float surface, out float depth)
        {
            surface = -10000f;
            depth = 0f;
            if (!_hydroInitialized || _hydroDepth == null || _resolution < 2) return false;
            Vector2 grid = WorldToGrid(worldPosition);
            if (grid.x < 0f || grid.y < 0f || grid.x > _resolution - 1 || grid.y > _resolution - 1) return false;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(grid.x), 0, _resolution - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(grid.y), 0, _resolution - 1);
            int x1 = Mathf.Min(x0 + 1, _resolution - 1);
            int y1 = Mathf.Min(y0 + 1, _resolution - 1);
            float tx = Mathf.Clamp01(grid.x - x0);
            float ty = Mathf.Clamp01(grid.y - y0);
            int nearestX = Mathf.Clamp(Mathf.RoundToInt(grid.x), 0, _resolution - 1);
            int nearestY = Mathf.Clamp(Mathf.RoundToInt(grid.y), 0, _resolution - 1);
            int nearest = Index(nearestX, nearestY);
            // Gameplay wetness is cell-authoritative. Do not bilinearly smear water through a dry
            // cell, wall, or isolated basin just because an adjacent cell happens to be wet.
            if (_solidHydroMask[nearest] || _hydroDepth[nearest] <= HydroMinWetDepth) return false;
            float d00 = _hydroDepth[Index(x0, y0)];
            float d10 = _hydroDepth[Index(x1, y0)];
            float d01 = _hydroDepth[Index(x0, y1)];
            float d11 = _hydroDepth[Index(x1, y1)];
            depth = Mathf.Lerp(Mathf.Lerp(d00, d10, tx), Mathf.Lerp(d01, d11, tx), ty);
            float b00 = _effectiveBedHeights[Index(x0, y0)];
            float b10 = _effectiveBedHeights[Index(x1, y0)];
            float b01 = _effectiveBedHeights[Index(x0, y1)];
            float b11 = _effectiveBedHeights[Index(x1, y1)];
            float bed = Mathf.Lerp(Mathf.Lerp(b00, b10, tx), Mathf.Lerp(b01, b11, tx), ty);
            surface = bed + depth;
            return true;
        }

        private bool IsOpenOceanOutsideHydro(Vector3 worldPosition)
        {
            if (_resolution > 1)
            {
                Vector2 grid = WorldToGrid(worldPosition);
                if (grid.x >= 0f && grid.y >= 0f && grid.x <= _resolution - 1 && grid.y <= _resolution - 1)
                    return false;
            }

            // Outside the active hydro tile, use the coarse CONNECTED ocean-domain mask rather
            // than a raw biome label. Coastal bays frequently transition to Meadows/BlackForest
            // while remaining physically connected to the ocean.
            return IsCoarseOceanDomainWet(worldPosition);
        }

        private bool IsCoarseOceanDomainWet(Vector3 worldPosition)
        {
            if (_oceanDomainWet == null || _oceanDomainTexture == null || _oceanDomainTexture.width < 2 ||
                _oceanDomainSize <= 1f)
            {
                return false;
            }

            float u = (worldPosition.x - _oceanDomainOrigin.x) / _oceanDomainSize;
            float v = (worldPosition.z - _oceanDomainOrigin.z) / _oceanDomainSize;
            if (u < 0f || v < 0f || u > 1f || v > 1f)
            {
                return false;
            }

            int resolution = _oceanDomainTexture.width;
            int x = Mathf.Clamp(Mathf.RoundToInt(u * (resolution - 1)), 0, resolution - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(v * (resolution - 1)), 0, resolution - 1);
            return _oceanDomainWet[x + y * resolution];
        }

        private void MarkHydrodynamicOceanBoundaries(float seaLevel)
        {
            for (int y = 0; y < _resolution; y++)
            {
                for (int x = 0; x < _resolution; x++)
                {
                    int index = Index(x, y);
                    bool edge = x == 0 || y == 0 || x == _resolution - 1 || y == _resolution - 1;
                    if (!edge || _solidHydroMask[index] || !_candidateWaterMask[index]) continue;
                    Vector3 world = new Vector3(_origin.x + x * _cellSize, seaLevel, _origin.z + y * _cellSize);
                    _oceanBoundaryMask[index] = IsCoarseOceanDomainWet(world);
                }
            }
        }

        private void SeedNewlyExposedOpenOceanCells(float seaLevel)
        {
            if (!_hydroInitialized || _hydroNewCellMask == null || _hydroDepth == null) return;
            for (int y = 0; y < _resolution; y++)
            {
                for (int x = 0; x < _resolution; x++)
                {
                    int index = Index(x, y);
                    if (!_hydroNewCellMask[index]) continue;
                    _hydroNewCellMask[index] = false;
                    if (_solidHydroMask[index] || !_candidateWaterMask[index]) continue;
                    Vector3 world = new Vector3(_origin.x + x * _cellSize, seaLevel, _origin.z + y * _cellSize);
                    if (!IsCoarseOceanDomainWet(world)) continue;
                    _hydroDepth[index] = Mathf.Max(_hydroDepth[index], Mathf.Max(0f, seaLevel - _effectiveBedHeights[index]));
                }
            }
        }

        private void RebuildHydrodynamicFaceBarriers(float seaLevel)
        {
            Array.Clear(_barrierEast, 0, _barrierEast.Length);
            Array.Clear(_barrierNorth, 0, _barrierNorth.Length);
            for (int y = 0; y < _resolution; y++)
            {
                for (int x = 0; x < _resolution; x++)
                {
                    int index = Index(x, y);
                    if (x < _resolution - 1)
                    {
                        int right = Index(x + 1, y);
                        _barrierEast[index] = _solidHydroMask[index] || _solidHydroMask[right] ||
                            HasStaticBarrierBetweenCells(x, y, x + 1, y, seaLevel);
                    }
                    if (y < _resolution - 1)
                    {
                        int up = Index(x, y + 1);
                        _barrierNorth[index] = _solidHydroMask[index] || _solidHydroMask[up] ||
                            HasStaticBarrierBetweenCells(x, y, x, y + 1, seaLevel);
                    }
                }
            }
        }

        private bool HasStaticBarrierBetweenCells(int ax, int ay, int bx, int by, float seaLevel)
        {
            int a = Index(ax, ay);
            int b = Index(bx, by);
            float localDepth = Mathf.Max(seaLevel - _effectiveBedHeights[a], seaLevel - _effectiveBedHeights[b]);
            if (localDepth > 6f) return false;
            Vector3 p0 = new Vector3(_origin.x + ax * _cellSize, seaLevel + 0.10f, _origin.z + ay * _cellSize);
            Vector3 p1 = new Vector3(_origin.x + bx * _cellSize, seaLevel + 0.10f, _origin.z + by * _cellSize);
            Vector3 delta = p1 - p0;
            float distance = delta.magnitude;
            if (distance < 0.05f) return false;
            int hits = Physics.RaycastNonAlloc(p0, delta / distance, _obstacleRayHits, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++)
            {
                Collider collider = _obstacleRayHits[i].collider;
                if (collider == null || collider.isTrigger || collider.attachedRigidbody != null) continue;
                if (collider.GetComponentInParent<Heightmap>() != null) continue;
                Bounds bounds = collider.bounds;
                if (bounds.max.y < seaLevel - 0.35f || bounds.min.y > seaLevel + 3.0f) continue;
                return true;
            }
            return false;
        }

        private bool IsHydroBarrierBetween(int ax, int ay, int bx, int by)
        {
            if (bx == ax + 1 && by == ay) return _barrierEast[Index(ax, ay)];
            if (bx == ax - 1 && by == ay) return _barrierEast[Index(bx, by)];
            if (by == ay + 1 && bx == ax) return _barrierNorth[Index(ax, ay)];
            if (by == ay - 1 && bx == ax) return _barrierNorth[Index(bx, by)];
            return true;
        }

        private void DisplaceWaterFromNewSolids()
        {
            if (_hydroDepth == null) return;
            for (int y = 0; y < _resolution; y++)
            {
                for (int x = 0; x < _resolution; x++)
                {
                    int index = Index(x, y);
                    if (!_solidHydroMask[index] || _hydroDepth[index] <= HydroMinWetDepth) continue;
                    float amount = _hydroDepth[index];
                    _hydroDepth[index] = 0f;

                    // Construction displaces the water that occupied the new solid's footprint.
                    // Do not consult the new wall faces here: they are barriers to future FLOW, but
                    // using them for displacement made every newly-solid cell delete its water.
                    int[] neighbors = new int[12];
                    int count = 0;
                    for (int radius = 1; radius <= 2 && count == 0; radius++)
                    {
                        for (int oy = -radius; oy <= radius; oy++)
                        {
                            for (int ox = -radius; ox <= radius; ox++)
                            {
                                if (Mathf.Abs(ox) != radius && Mathf.Abs(oy) != radius) continue;
                                int nx = x + ox;
                                int ny = y + oy;
                                if (nx < 0 || ny < 0 || nx >= _resolution || ny >= _resolution) continue;
                                int ni = Index(nx, ny);
                                if (_solidHydroMask[ni]) continue;
                                if (count < neighbors.Length) neighbors[count++] = ni;
                            }
                        }
                    }

                    if (count > 0)
                    {
                        float share = amount / count;
                        for (int n = 0; n < count; n++) _hydroDepth[neighbors[n]] += share;
                    }
                    else
                    {
                        // Extremely pathological fully-solid neighborhood: keep the volume in the
                        // cell for one refresh rather than destroying mass. The next geometry update
                        // will retry displacement if/when a fluid cell exists.
                        _hydroDepth[index] = amount;
                    }
                }
            }
        }

        private void UpdateHydrodynamicBathymetryTextureOnly()
        {
            if (_bathymetryTexture == null || _bathymetryPixels == null || _hydroDepth == null) return;
            const float maxDepth = 64f;
            for (int i = 0; i < _bathymetryPixels.Length; i++)
            {
                Color32 pixel = _bathymetryPixels[i];
                float depth01 = Mathf.Clamp01(_hydroDepth[i] / maxDepth);
                pixel.r = (byte)Mathf.RoundToInt(depth01 * 255f);
                pixel.a = (!_solidHydroMask[i] && _hydroDepth[i] > HydroMinWetDepth) ? (byte)255 : (byte)0;
                _bathymetryPixels[i] = pixel;
            }
            _bathymetryTexture.SetPixels32(_bathymetryPixels);
            _bathymetryTexture.Apply(false, false);
        }

        private float CalculateHydroVolume()
        {
            if (_hydroDepth == null) return 0f;
            float area = _cellSize * _cellSize;
            double total = 0.0;
            for (int i = 0; i < _hydroDepth.Length; i++) total += Math.Max(0.0, _hydroDepth[i]) * area;
            return (float)total;
        }

        private void StepSimulation(float deltaTime)
        {
            _accumulator += Mathf.Clamp(deltaTime, 0f, 0.1f);
            while (_accumulator >= SimulationStep)
            {
                SimulateStep(SimulationStep);
                _accumulator -= SimulationStep;
            }
        }

        private void SimulateStep(float dt)
        {
            if (_current == null || _previous == null || _next == null)
            {
                return;
            }

            float speed = Mathf.Max(0.1f, PhysicalWaterPlugin.Settings.WaveSpeed.Value);
            float damping = Mathf.Clamp(PhysicalWaterPlugin.Settings.Damping.Value, 0.8f, 0.9999f);
            float coefficient = Mathf.Clamp((speed * speed * dt * dt) / (_cellSize * _cellSize), 0.002f, 0.22f);
            float maxDisplacement = GetMaxSimulatedDisplacement();

            for (int y = 1; y < _resolution - 1; y++)
            {
                for (int x = 1; x < _resolution - 1; x++)
                {
                    int index = Index(x, y);
                    if (_waterMask != null && !_waterMask[index])
                    {
                        _next[index] = 0f;
                        continue;
                    }

                    float left = GetWaterNeighborHeight(x - 1, y, index);
                    float right = GetWaterNeighborHeight(x + 1, y, index);
                    float down = GetWaterNeighborHeight(x, y - 1, index);
                    float up = GetWaterNeighborHeight(x, y + 1, index);
                    float laplacian =
                        left +
                        right +
                        down +
                        up -
                        _current[index] * 4f;

                    float depth = _waterDepthField != null ? _waterDepthField[index] : 12f;
                    // Local dynamic waves obey shallow-water behaviour: propagation slows as depth drops,
                    // energy damps strongly in the surf zone, and dry/static obstacles reflect through GetWaterNeighborHeight.
                    float shallowSpeed = Mathf.Clamp01(Mathf.Sqrt(Mathf.Max(depth, 0.03f) / 6f));
                    float localCoefficient = coefficient * Mathf.Lerp(0.08f, 1f, shallowSpeed * shallowSpeed);
                    float shallowDamping = Mathf.Lerp(0.82f, 1f, Mathf.Clamp01(depth / 2.4f));
                    float next = (_current[index] * 2f - _previous[index] + laplacian * localCoefficient) * damping * shallowDamping;
                    _next[index] = Mathf.Clamp(next, -maxDisplacement, maxDisplacement);
                }
            }

            DampBoundary();

            float[] oldPrevious = _previous;
            _previous = _current;
            _current = _next;
            _next = oldPrevious;

            // Wind/swell belongs to the spectral ocean. The local ripple field is transient
            // interaction energy only and must decay toward zero when nothing disturbs it.
        }

        private float GetWaterNeighborHeight(int x, int y, int fallbackIndex)
        {
            int index = Index(x, y);
            if (_waterMask != null && !_waterMask[index])
            {
                return _current[fallbackIndex];
            }

            return _current[index];
        }

        private void DampBoundary()
        {
            if (_next == null || _resolution <= 2)
            {
                return;
            }

            for (int i = 0; i < _resolution; i++)
            {
                _next[Index(i, 0)] = 0f;
                _next[Index(i, _resolution - 1)] = 0f;
                _next[Index(0, i)] = 0f;
                _next[Index(_resolution - 1, i)] = 0f;
            }
        }

        private void AddWindEnergy(float dt)
        {
            if (_resolution < 5)
            {
                return;
            }

            float wind = EnvMan.instance != null ? EnvMan.instance.GetWindIntensity() : 0.35f;
            float amount = PhysicalWaterPlugin.Settings.WindWaveAmplitude.Value * wind * dt * 0.015f;
            if (amount <= 0.0001f)
            {
                return;
            }

            float maxDisplacement = GetMaxSimulatedDisplacement();
            float t = Time.time * 0.37f;
            for (int y = 2; y < _resolution - 2; y += 11)
            {
                for (int x = 2; x < _resolution - 2; x += 11)
                {
                    float phase = Mathf.Sin((x * 0.73f + y * 0.31f) + t);
                    int index = Index(x, y);
                    _current[index] = Mathf.Clamp(_current[index] + phase * amount, -maxDisplacement, maxDisplacement);
                }
            }
        }

        private static float GetMaxSimulatedDisplacement()
        {
            return Mathf.Clamp(PhysicalWaterPlugin.Settings.MaxSimulatedDisplacement.Value, 0.05f, 2.5f);
        }

        private void UpdateMesh()
        {
            if (_vertices == null)
            {
                return;
            }

            float seaLevel = PhysicalWaterPlugin.Settings.SeaLevel.Value;
            bool masksChanged = UpdateWaterMasksIfNeeded(seaLevel);
            bool animateVisualSurface = ShouldAnimateVisualSurface();
            if (!masksChanged && !_trianglesDirty && !_meshDirty && !animateVisualSurface)
            {
                return;
            }

            float time = Time.time;

            for (int y = 0; y < _resolution; y++)
            {
                for (int x = 0; x < _resolution; x++)
                {
                    int index = Index(x, y);
                    float worldX = _origin.x + x * _cellSize;
                    float worldZ = _origin.z + y * _cellSize;
                    Vector3 world = new Vector3(worldX, seaLevel, worldZ);
                    float height = seaLevel;
                    float vertexX = worldX;
                    float vertexZ = worldZ;
                    if (_visualWaterMask[index] && (_waterMask[index] || _candidateWaterMask[index]) && animateVisualSurface)
                    {
                        float shoreLimiter = _waterMask[index] ? 1f : 0.35f;
                        height += GetProceduralWave(world, time) * shoreLimiter;
                    }

                    if (!_waterMask[index])
                    {
                        _current[index] = 0f;
                        _previous[index] = 0f;
                        _next[index] = 0f;
                        if (_visualWaterMask[index] && !_candidateWaterMask[index])
                        {
                            height = seaLevel;
                        }
                        else if (!_visualWaterMask[index])
                        {
                            height = HideWaterUnderTerrain(world, height);
                            vertexX = worldX;
                            vertexZ = worldZ;
                        }
                    }

                    if (_visualWaterMask[index])
                    {
                        height = ApplyOuterBoundarySink(x, y, height, _terrainHeights[index], seaLevel);
                    }

                    _vertices[index] = new Vector3(vertexX, height, vertexZ);
                    if (_colors != null)
                    {
                        _colors[index] = ComputeWaterVertexColor(x, y, index, seaLevel);
                    }
                }
            }

            _mesh.vertices = _vertices;
            if (_colors != null)
            {
                _mesh.colors = _colors;
            }
            _mesh.normals = _normals;
            if (masksChanged || _trianglesDirty)
            {
                UpdateVisibleTriangles();
                // 0.5.1: continuous bathymetry/foam shader owns shoreline presentation; no quad overlay mesh.
                _trianglesDirty = false;
            }

            UpdateMeshBounds(seaLevel);
            _meshDirty = false;
        }

        private void UpdateMeshBounds(float seaLevel)
        {
            if (_mesh == null)
            {
                return;
            }

            Vector3 center = new Vector3(_origin.x + _extent, seaLevel, _origin.z + _extent);
            Vector3 size = new Vector3(_extent * 2f + 64f, 128f, _extent * 2f + 64f);
            _mesh.bounds = new Bounds(center, size);
            if (_shorelineFoamMesh != null)
            {
                _shorelineFoamMesh.bounds = new Bounds(center, new Vector3(_extent * 2f + 64f, 32f, _extent * 2f + 64f));
            }
        }

        private void UpdateShorelineFoamOverlay(float seaLevel)
        {
            if (_shorelineFoamMesh == null ||
                _waterMask == null ||
                _visualWaterMask == null ||
                _terrainHeights == null)
            {
                return;
            }

            _shorelineFoamVertices.Clear();
            _shorelineFoamUvs.Clear();
            _shorelineFoamColors.Clear();
            _shorelineFoamTriangles.Clear();

            int maxQuads = Mathf.Clamp(PhysicalWaterPlugin.Settings.ShorelineEffectsBudget.Value * 10, 240, 1800);
            int stride = _resolution > 145 ? 2 : 1;
            float size = Mathf.Clamp(_cellSize * 1.15f, 1.8f, 5.2f);
            float half = size * 0.5f;
            int quads = 0;

            for (int y = 1; y < _resolution - 1 && quads < maxQuads; y += stride)
            {
                for (int x = 1; x < _resolution - 1 && quads < maxQuads; x += stride)
                {
                    int index = Index(x, y);
                    if (!_waterMask[index] || !_visualWaterMask[index] || !HasDryNeighbor(x, y))
                    {
                        continue;
                    }

                    float depth = seaLevel - _terrainHeights[index];
                    if (depth < 0.02f || depth > 3.4f)
                    {
                        continue;
                    }

                    float worldX = _origin.x + x * _cellSize;
                    float worldZ = _origin.z + y * _cellSize;
                    float yPos = seaLevel + 0.052f;
                    int baseVertex = _shorelineFoamVertices.Count;
                    _shorelineFoamVertices.Add(new Vector3(worldX - half, yPos, worldZ - half));
                    _shorelineFoamVertices.Add(new Vector3(worldX + half, yPos, worldZ - half));
                    _shorelineFoamVertices.Add(new Vector3(worldX - half, yPos, worldZ + half));
                    _shorelineFoamVertices.Add(new Vector3(worldX + half, yPos, worldZ + half));
                    _shorelineFoamUvs.Add(new Vector2(0f, 0f));
                    _shorelineFoamUvs.Add(new Vector2(1f, 0f));
                    _shorelineFoamUvs.Add(new Vector2(0f, 1f));
                    _shorelineFoamUvs.Add(new Vector2(1f, 1f));

                    float depthFade = 1f - Mathf.Clamp01(depth / 2.2f);
                    float broken = 0.55f + 0.45f * Mathf.PerlinNoise(worldX * 0.08f, worldZ * 0.08f);
                    Color foamColor = new Color(0.86f, 0.98f, 1.0f, Mathf.Clamp01(0.18f + depthFade * 0.22f) * broken);
                    _shorelineFoamColors.Add(foamColor);
                    _shorelineFoamColors.Add(foamColor);
                    _shorelineFoamColors.Add(foamColor);
                    _shorelineFoamColors.Add(foamColor);

                    _shorelineFoamTriangles.Add(baseVertex);
                    _shorelineFoamTriangles.Add(baseVertex + 2);
                    _shorelineFoamTriangles.Add(baseVertex + 1);
                    _shorelineFoamTriangles.Add(baseVertex + 1);
                    _shorelineFoamTriangles.Add(baseVertex + 2);
                    _shorelineFoamTriangles.Add(baseVertex + 3);
                    quads++;
                }
            }

            _shorelineFoamMesh.Clear();
            if (_shorelineFoamVertices.Count == 0)
            {
                return;
            }

            _shorelineFoamMesh.SetVertices(_shorelineFoamVertices);
            _shorelineFoamMesh.SetUVs(0, _shorelineFoamUvs);
            _shorelineFoamMesh.SetColors(_shorelineFoamColors);
            _shorelineFoamMesh.SetTriangles(_shorelineFoamTriangles, 0, true);
            _shorelineFoamMesh.RecalculateNormals();
            _shorelineFoamMesh.RecalculateBounds();
        }

        private static float ComputeStableVisualWave(float worldX, float worldZ, float time)
        {
            // Visual only. Gameplay water queries and buoyancy stay at stable sea level.
            Vector2 wind = GetOceanWindDirection2D();
            Vector2 cross = new Vector2(-wind.y, wind.x);
            Vector2 p = new Vector2(worldX, worldZ);
            float windIntensity = Mathf.Clamp(GetOceanWindIntensity(), 0.15f, 1.15f);
            float swell = Mathf.Sin(Vector2.Dot(p, wind) * 0.022f + time * (0.22f + windIntensity * 0.08f)) * 0.14f;
            float crossSwell = Mathf.Sin(Vector2.Dot(p, cross) * 0.013f + time * 0.07f) * 0.055f;
            return swell + crossSwell;
        }

        private void UpdateFarOcean()
        {
            if (PhysicalWaterPlugin.Settings == null ||
                !PhysicalWaterPlugin.Settings.FarOceanEnabled.Value ||
                _farMesh == null)
            {
                SetFarPreviewVisible(false);
                return;
            }

            RebuildFarOceanIfNeeded(false);
            UpdateFarOceanOrigin();
            UpdateFarOceanMesh();
        }

        private void UpdateFarOceanOrigin()
        {
            Vector3 center = _origin + new Vector3(_extent, 0f, _extent);
            if (Player.m_localPlayer != null)
            {
                center = Player.m_localPlayer.transform.position;
            }

            float snap = Mathf.Max(128f, PhysicalWaterPlugin.Settings.OriginSnapMeters.Value * 2f);
            float snappedCenterX = Mathf.Round(center.x / snap) * snap;
            float snappedCenterZ = Mathf.Round(center.z / snap) * snap;
            _farOrigin = new Vector3(snappedCenterX - _farRadius, PhysicalWaterPlugin.Settings.SeaLevel.Value, snappedCenterZ - _farRadius);
            if ((_farOrigin - _lastFarOrigin).sqrMagnitude > 0.01f)
            {
                _lastFarOrigin = _farOrigin;
                _farMeshDirty = true;
            }
        }

        private void UpdateFarOceanMesh()
        {
            if (_farVertices == null || _farResolution < 2)
            {
                return;
            }

            float seaLevel = PhysicalWaterPlugin.Settings.SeaLevel.Value;
            for (int y = 0; y < _farResolution; y++)
            {
                for (int x = 0; x < _farResolution; x++)
                {
                    int index = FarIndex(x, y);
                    float worldX = _farOrigin.x + x * _farCellSize;
                    float worldZ = _farOrigin.z + y * _farCellSize;
                    float wave = ShouldAnimateVisualSurface()
                        ? ComputeStableVisualWave(worldX, worldZ, Time.time) * 0.55f
                        : 0f;
                    _farVertices[index] = new Vector3(worldX, seaLevel - 0.075f + wave, worldZ);
                    if (_farColors != null)
                    {
                        Vector3 center = _farOrigin + new Vector3(_farRadius, 0f, _farRadius);
                        float distance = Mathf.Max(Mathf.Abs(worldX - center.x), Mathf.Abs(worldZ - center.z));
                        float feather = Mathf.Clamp01((distance - _farInnerRadius) / 220f);
                        _farColors[index] = new Color(1f, 1f, 1f, Mathf.Lerp(0.08f, 0.62f, feather));
                    }
                }
            }

            _farMesh.vertices = _farVertices;
            if (_farColors != null)
            {
                _farMesh.colors = _farColors;
            }
            _farMesh.RecalculateNormals();
            if (_farMeshDirty)
            {
                UpdateFarOceanMasks(seaLevel);
                UpdateFarOceanTriangles();
                _farMeshDirty = false;
            }

            UpdateFarMeshBounds(seaLevel);
        }

        private void UpdateFarOceanMasks(float seaLevel)
        {
            if (_farWaterMask == null || _farVisualWaterMask == null || _farTerrainHeights == null)
            {
                return;
            }

            _farWaterVertexCount = 0;
            for (int y = 0; y < _farResolution; y++)
            {
                for (int x = 0; x < _farResolution; x++)
                {
                    int index = FarIndex(x, y);
                    // The far ocean is intentionally an always-water visual impostor.
                    // The near tile owns shore masking and gameplay. Sampling terrain
                    // here made the distance ocean patchy, so players only saw water
                    // immediately around themselves.
                    _farTerrainHeights[index] = seaLevel - 40f;
                    _farWaterMask[index] = true;
                    _farVisualWaterMask[index] = true;
                    _farWaterVertexCount++;
                }
            }
        }

        private void UpdateFarOceanTriangles()
        {
            if (_farWaterMask == null || _farVisualWaterMask == null)
            {
                return;
            }

            _farVisibleTriangles.Clear();
            _farVisibleTriangleCount = 0;
            Vector3 center = _farOrigin + new Vector3(_farRadius, 0f, _farRadius);
            for (int y = 0; y < _farResolution - 1; y++)
            {
                for (int x = 0; x < _farResolution - 1; x++)
                {
                    int a = FarIndex(x, y);
                    int b = FarIndex(x + 1, y);
                    int c = FarIndex(x, y + 1);
                    int d = FarIndex(x + 1, y + 1);

                    AddFarTriangleIfWater(a, c, b, center);
                    AddFarTriangleIfWater(b, c, d, center);
                }
            }

            _farMesh.SetTriangles(_farVisibleTriangles, 0, true);
        }

        private void AddFarTriangleIfWater(int a, int b, int c, Vector3 center)
        {
            if (IsInsideFarInnerHole(a, center) && IsInsideFarInnerHole(b, center) && IsInsideFarInnerHole(c, center))
            {
                return;
            }

            bool hasRealWater = _farWaterMask[a] || _farWaterMask[b] || _farWaterMask[c];
            bool canDraw = _farVisualWaterMask[a] && _farVisualWaterMask[b] && _farVisualWaterMask[c];
            if (hasRealWater && canDraw)
            {
                _farVisibleTriangles.Add(a);
                _farVisibleTriangles.Add(b);
                _farVisibleTriangles.Add(c);
                _farVisibleTriangleCount++;
            }
        }

        private bool IsInsideFarInnerHole(int index, Vector3 center)
        {
            Vector3 vertex = _farVertices[index];
            float distance = Mathf.Max(Mathf.Abs(vertex.x - center.x), Mathf.Abs(vertex.z - center.z));
            return distance < _farInnerRadius;
        }

        private void UpdateFarMeshBounds(float seaLevel)
        {
            if (_farMesh == null)
            {
                return;
            }

            Vector3 center = new Vector3(_farOrigin.x + _farRadius, seaLevel, _farOrigin.z + _farRadius);
            Vector3 size = new Vector3(_farRadius * 2f + 128f, 96f, _farRadius * 2f + 128f);
            _farMesh.bounds = new Bounds(center, size);
        }

        private bool UpdateWaterMasksIfNeeded(float seaLevel)
        {
            if (!_masksDirty)
            {
                return false;
            }

            UpdateWaterMasks(seaLevel);
            _masksDirty = false;
            _trianglesDirty = true;
            return true;
        }

        private void UpdateWaterMasks(float seaLevel)
        {
            if (_candidateWaterMask == null || _waterMask == null || _visualWaterMask == null) return;

            _waterVertexCount = 0;
            for (int y = 0; y < _resolution; y++)
            {
                for (int x = 0; x < _resolution; x++)
                {
                    int index = Index(x, y);
                    float worldX = _origin.x + x * _cellSize;
                    float worldZ = _origin.z + y * _cellSize;
                    Vector3 world = new Vector3(worldX, seaLevel, worldZ);
                    float terrainHeight;
                    bool hasHeight = TryGetTerrainHeight(world, out terrainHeight);
                    terrainHeight = hasHeight ? terrainHeight : seaLevel - 100f;
                    _terrainHeights[index] = terrainHeight;

                    float obstacleTop = terrainHeight;
                    float terrainDepth = seaLevel - terrainHeight;
                    bool detailedObstacleSample = terrainDepth <= 3.5f || ((x & 1) == 0 && (y & 1) == 0);
                    bool hasObstacle = hasHeight && terrainDepth > -1.0f && terrainDepth < 12f && detailedObstacleSample &&
                                       TryGetStaticObstacleSurface(worldX, worldZ, terrainHeight, seaLevel, out obstacleTop);
                    float effectiveBed = hasObstacle ? Mathf.Max(terrainHeight, obstacleTop) : terrainHeight;
                    _effectiveBedHeights[index] = effectiveBed;
                    _staticObstacleMask[index] = hasObstacle;

                    bool potentialFluid = !hasHeight || effectiveBed < seaLevel - 0.02f;
                    _candidateWaterMask[index] = potentialFluid;
                    _solidHydroMask[index] = !potentialFluid || (hasObstacle && obstacleTop >= seaLevel - 0.02f);
                    _oceanBoundaryMask[index] = false;
                    _waterMask[index] = _hydroInitialized && _hydroDepth != null && _hydroDepth[index] > HydroMinWetDepth && !_solidHydroMask[index];
                    _visualWaterMask[index] = _waterMask[index];
                }
            }

            RebuildHydrodynamicFaceBarriers(seaLevel);
            MarkHydrodynamicOceanBoundaries(seaLevel);
            SeedNewlyExposedOpenOceanCells(seaLevel);

            // If a new solid occupies a previously wet cell, displace its conserved water into
            // neighboring fluid cells instead of deleting it or allowing it through the wall.
            if (_hydroInitialized) DisplaceWaterFromNewSolids();

            RefreshHydrodynamicWetMasks();
            UpdateBathymetryDerivedFields(seaLevel);
        }

        // The pre-0.5 candidate-mask flood-fill was removed. Hydrodynamic wetness is owned by
        // _hydroDepth; initialization uses SeedHydroBoundary/SeedHydroNeighbor only.

        private bool HasNeighborWater(int x, int y)
        {
            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    if (ox == 0 && oy == 0)
                    {
                        continue;
                    }

                    int nx = x + ox;
                    int ny = y + oy;
                    if (nx < 0 || ny < 0 || nx >= _resolution || ny >= _resolution)
                    {
                        continue;
                    }

                    if (_waterMask[Index(nx, ny)])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool HasDryNeighbor(int x, int y)
        {
            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    if (ox == 0 && oy == 0)
                    {
                        continue;
                    }

                    int nx = x + ox;
                    int ny = y + oy;
                    if (nx < 0 || ny < 0 || nx >= _resolution || ny >= _resolution)
                    {
                        return true;
                    }

                    int neighborIndex = Index(nx, ny);
                    if (!_waterMask[neighborIndex] || !_visualWaterMask[neighborIndex])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void UpdateVisibleTriangles()
        {
            if (_waterMask == null || _visualWaterMask == null)
            {
                return;
            }

            _visibleTriangles.Clear();
            _visibleTriangleCount = 0;
            for (int y = 0; y < _resolution - 1; y++)
            {
                for (int x = 0; x < _resolution - 1; x++)
                {
                    int a = Index(x, y);
                    int b = Index(x + 1, y);
                    int c = Index(x, y + 1);
                    int d = Index(x + 1, y + 1);

                    AddTriangleIfWater(a, c, b);
                    AddTriangleIfWater(b, c, d);
                }
            }

            _mesh.SetTriangles(_visibleTriangles, 0, true);
        }

        private void AddTriangleIfWater(int a, int b, int c)
        {
            // The near ocean surface must never bridge onto dry terrain. 0.3.14
            // allowed one real-water vertex plus three visual-band vertices, which
            // produced the huge rectangular/slab overlay visible on shore. Shore
            // transition is handled by the dedicated foam overlay instead.
            bool realWaterTriangle = _waterMask[a] && _waterMask[b] && _waterMask[c];
            if (realWaterTriangle)
            {
                _visibleTriangles.Add(a);
                _visibleTriangles.Add(b);
                _visibleTriangles.Add(c);
                _visibleTriangleCount++;
            }
        }

        private float SampleHeightfield(Vector3 worldPosition)
        {
            if (_current == null || _resolution < 2)
            {
                return 0f;
            }

            Vector2 grid = WorldToGrid(worldPosition);
            if (grid.x < 0f || grid.y < 0f || grid.x > _resolution - 1 || grid.y > _resolution - 1)
            {
                return 0f;
            }

            int x0 = Mathf.Clamp(Mathf.FloorToInt(grid.x), 0, _resolution - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(grid.y), 0, _resolution - 1);
            int x1 = Mathf.Clamp(x0 + 1, 0, _resolution - 1);
            int y1 = Mathf.Clamp(y0 + 1, 0, _resolution - 1);
            float tx = Mathf.Clamp01(grid.x - x0);
            float ty = Mathf.Clamp01(grid.y - y0);

            float a = _current[Index(x0, y0)];
            float b = _current[Index(x1, y0)];
            float c = _current[Index(x0, y1)];
            float d = _current[Index(x1, y1)];

            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        private float GetPhysicalSurfaceHeightUnchecked(Vector3 worldPosition, float waveFactor)
        {
            // Dry means dry for every subsystem, including camera optics and helper probes.
            return GetSurfaceHeight(worldPosition, waveFactor);
        }

        internal Vector3 GetSurfaceNormal(Vector3 worldPosition)
        {
            float center = GetSurfaceHeight(worldPosition, 1f);
            if (center <= -9990f) return Vector3.up;
            float d = Mathf.Max(0.45f, _cellSize > 0f ? _cellSize * 0.55f : 0.75f);
            float hL = GetSurfaceHeight(worldPosition + Vector3.left * d, 1f);
            float hR = GetSurfaceHeight(worldPosition + Vector3.right * d, 1f);
            float hD = GetSurfaceHeight(worldPosition + Vector3.back * d, 1f);
            float hU = GetSurfaceHeight(worldPosition + Vector3.forward * d, 1f);
            if (hL <= -9990f) hL = center;
            if (hR <= -9990f) hR = center;
            if (hD <= -9990f) hD = center;
            if (hU <= -9990f) hU = center;
            return new Vector3(hL - hR, d * 2f, hD - hU).normalized;
        }

        internal Vector3 GetWaterVelocity(Vector3 worldPosition)
        {
            float surface = GetSurfaceHeight(worldPosition, 0f);
            if (surface <= -9990f) return Vector3.zero;

            float rippleVertical = 0f;
            if (_current != null && _previous != null && _resolution > 1)
            {
                Vector2 grid = WorldToGrid(worldPosition);
                if (grid.x >= 0f && grid.y >= 0f && grid.x <= _resolution - 1 && grid.y <= _resolution - 1)
                {
                    int x = Mathf.Clamp(Mathf.RoundToInt(grid.x), 0, _resolution - 1);
                    int y = Mathf.Clamp(Mathf.RoundToInt(grid.y), 0, _resolution - 1);
                    int index = Index(x, y);
                    rippleVertical = (_current[index] - _previous[index]) / Mathf.Max(SimulationStep, 0.001f);
                }
            }

            Vector3 velocity = TrySampleHydrodynamicHorizontalVelocity(worldPosition);
            velocity += GetProceduralWaterVelocity(worldPosition, Time.time);
            velocity.y += Mathf.Clamp(rippleVertical, -0.55f, 0.55f);
            return Vector3.ClampMagnitude(velocity, 7.5f);
        }

        private Vector3 TrySampleHydrodynamicHorizontalVelocity(Vector3 worldPosition)
        {
            if (!_hydroInitialized || _hydroFaceEast == null || _hydroFaceNorth == null || _resolution < 2) return Vector3.zero;
            Vector2 grid = WorldToGrid(worldPosition);
            if (grid.x < 0f || grid.y < 0f || grid.x > _resolution - 1 || grid.y > _resolution - 1) return Vector3.zero;
            int x = Mathf.Clamp(Mathf.RoundToInt(grid.x), 0, _resolution - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(grid.y), 0, _resolution - 1);
            int index = Index(x, y);
            if (_solidHydroMask[index] || _hydroDepth[index] <= HydroMinWetDepth) return Vector3.zero;
            float east = x < _resolution - 1 ? _hydroFaceEast[index] : 0f;
            float west = x > 0 ? _hydroFaceEast[Index(x - 1, y)] : east;
            float north = y < _resolution - 1 ? _hydroFaceNorth[index] : 0f;
            float south = y > 0 ? _hydroFaceNorth[Index(x, y - 1)] : north;
            return new Vector3((east + west) * 0.5f, 0f, (north + south) * 0.5f);
        }

        private Vector3 GetProceduralWaterVelocity(Vector3 worldPosition, float time)
        {
            Vector3 velocity = Vector3.zero;
            Vector2 parameterXZ = FindOceanSurfaceParameter(worldPosition, time);
            float globalScale = Mathf.Max(0f, PhysicalWaterPlugin.Settings.WindWaveAmplitude.Value);
            float wind = Mathf.Max(0.08f, GetOceanWindIntensity());
            float stormBlend = GetStormBlend();
            for (int i = 0; i < OceanWaveLayers.Length; i++)
            {
                WaveLayer wave = OceanWaveLayers[i];
                Vector2 dir = BlendWaveDirection(wave.Direction, stormBlend);
                float k = (Mathf.PI * 2f) / Mathf.Max(0.1f, wave.Wavelength);
                float omega = Mathf.Sqrt(9.81f * k);
                float phase = k * Vector2.Dot(dir, parameterXZ) -
                              omega * time * Mathf.Lerp(0.72f, 1.24f, wind);
                float depthScale = GetWaveDepthScale(worldPosition, wave.Wavelength);
                float amplitude = wave.Amplitude * globalScale * Mathf.Lerp(0.20f, 1f + stormBlend * 0.30f, wind) * depthScale;
                float horizontalSpeed = wave.Steepness * amplitude * omega * Mathf.Sin(phase);
                velocity.x += dir.x * horizontalSpeed;
                velocity.z += dir.y * horizontalSpeed;
                velocity.y += -amplitude * omega * Mathf.Cos(phase);
            }
            return velocity;
        }

        private float GetProceduralWave(Vector3 worldPosition, float time)
        {
            // Gerstner waves move vertices horizontally. Solve the parametric XZ that
            // lands at the requested world XZ so CPU collision/buoyancy samples the same
            // visible surface instead of the undisplaced parameter point.
            Vector2 parameterXZ = FindOceanSurfaceParameter(worldPosition, time);
            Vector3 sample = new Vector3(parameterXZ.x, PhysicalWaterPlugin.Settings.SeaLevel.Value, parameterXZ.y);
            return EvaluateOceanWaveSurface(sample, time).y - PhysicalWaterPlugin.Settings.SeaLevel.Value;
        }

        private Vector2 FindOceanSurfaceParameter(Vector3 worldPosition, float time)
        {
            Vector2 target = new Vector2(worldPosition.x, worldPosition.z);
            Vector2 parameter = target;
            float seaLevel = PhysicalWaterPlugin.Settings.SeaLevel.Value;

            // Two fixed-point iterations are enough for the bounded steepness used here
            // and keep probe cost predictable for multiplayer boats.
            for (int iteration = 0; iteration < 2; iteration++)
            {
                Vector3 displaced = EvaluateOceanWaveSurface(new Vector3(parameter.x, seaLevel, parameter.y), time);
                Vector2 error = new Vector2(displaced.x - target.x, displaced.z - target.y);
                parameter -= error;
            }

            return parameter;
        }

        private Vector3 EvaluateOceanWaveSurface(Vector3 worldPosition, float time)
        {
            float seaLevel = PhysicalWaterPlugin.Settings.SeaLevel.Value;
            Vector3 result = new Vector3(worldPosition.x, seaLevel, worldPosition.z);
            float globalScale = Mathf.Max(0f, PhysicalWaterPlugin.Settings.WindWaveAmplitude.Value);
            float wind = Mathf.Max(0.08f, GetOceanWindIntensity());
            float stormBlend = GetStormBlend();

            for (int i = 0; i < OceanWaveLayers.Length; i++)
            {
                WaveLayer wave = OceanWaveLayers[i];
                Vector2 dir = BlendWaveDirection(wave.Direction, stormBlend);
                float k = (Mathf.PI * 2f) / Mathf.Max(0.1f, wave.Wavelength);
                float omega = Mathf.Sqrt(9.81f * k);
                float phase = k * Vector2.Dot(dir, new Vector2(worldPosition.x, worldPosition.z)) -
                              omega * time * Mathf.Lerp(0.72f, 1.24f, wind);
                float s = Mathf.Sin(phase);
                float c = Mathf.Cos(phase);
                float depthScale = GetWaveDepthScale(worldPosition, wave.Wavelength);
                float amplitude = wave.Amplitude * globalScale * Mathf.Lerp(0.20f, 1f + stormBlend * 0.30f, wind) * depthScale;
                float horizontal = wave.Steepness * amplitude;
                result.x += dir.x * horizontal * c;
                result.z += dir.y * horizontal * c;
                result.y += amplitude * s;
            }

            return result;
        }

        private static Vector2 BlendWaveDirection(Vector2 layerDirection, float stormBlend)
        {
            Vector2 baseDir = layerDirection.sqrMagnitude > 0.001f ? layerDirection.normalized : Vector2.right;
            Vector2 windDir = GetOceanWindDirection2D();
            return Vector2.Lerp(baseDir, windDir, Mathf.Clamp01(stormBlend * 0.22f)).normalized;
        }

        private static Vector2 GetOceanWindDirection2D()
        {
            Vector3 windDir = EnvMan.instance != null ? EnvMan.instance.GetWindDir() : new Vector3(0.94f, 0f, 0.34f);
            Vector2 wind = new Vector2(windDir.x, windDir.z);
            return wind.sqrMagnitude > 0.001f ? wind.normalized : new Vector2(0.94f, 0.34f).normalized;
        }

        private static float GetOceanWindIntensity()
        {
            float wind = EnvMan.instance != null ? EnvMan.instance.GetWindIntensity() : 0.35f;
            return Mathf.Clamp(wind, 0.05f, 2.5f);
        }

        private static float GetStormBlend()
        {
            return Mathf.Clamp01((GetOceanWindIntensity() - 0.65f) / 0.55f);
        }

        private static float GetPrecipitationBlend()
        {
            float blend = 0f;
            string environmentName = TryGetCurrentEnvironmentName();

            if (!string.IsNullOrEmpty(environmentName))
            {
                string lower = environmentName.ToLowerInvariant();
                if (lower.Contains("thunder"))
                {
                    blend = 1f;
                }
                else if (lower.Contains("rain") || lower.Contains("ashrain") || lower.Contains("snowstorm"))
                {
                    blend = 0.82f;
                }
                else if (lower.Contains("snow") || lower.Contains("mist"))
                {
                    blend = 0.48f;
                }
            }

            // RenderSettings fog is already applied directly in the shader. This small contribution
            // only controls surface roughness/contrast so foggy weather does not leave a polished ocean.
            if (RenderSettings.fog)
            {
                blend = Mathf.Max(blend, Mathf.Clamp01(RenderSettings.fogDensity * 10f));
            }

            return Mathf.Clamp01(Mathf.Max(blend, GetStormBlend() * 0.42f));
        }

        private static string TryGetCurrentEnvironmentName()
        {
            object env = EnvMan.instance;
            if (env == null)
            {
                return string.Empty;
            }

            try
            {
                Type envType = env.GetType();

                // Valheim 0.221.x keeps these implementation details non-public. Resolve them
                // dynamically so PhysicalWater does not bind its net472 assembly to private API.
                string forced = ReadStringMember(env, envType, "m_forceEnv") ??
                                ReadStringMember(env, envType, "forceEnv") ??
                                ReadStringMember(env, envType, "ForceEnv");
                if (!string.IsNullOrEmpty(forced))
                {
                    return forced;
                }

                object current = ReadObjectMember(env, envType, "m_currentEnv") ??
                                 ReadObjectMember(env, envType, "currentEnv") ??
                                 ReadObjectMember(env, envType, "CurrentEnv");
                if (current != null)
                {
                    Type currentType = current.GetType();
                    string name = ReadStringMember(current, currentType, "m_name") ??
                                  ReadStringMember(current, currentType, "name") ??
                                  ReadStringMember(current, currentType, "Name");
                    if (!string.IsNullOrEmpty(name))
                    {
                        return name;
                    }
                }

                // Future-proof fallback for versions that expose an environment getter instead
                // of fields. We intentionally invoke by reflection to avoid another hard API bind.
                foreach (string methodName in new[] { "GetCurrentEnvironment", "GetCurrentEnv", "GetEnvironment" })
                {
                    MethodInfo method = envType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    if (method == null)
                    {
                        continue;
                    }

                    object value = method.Invoke(env, null);
                    if (value is string text && !string.IsNullOrEmpty(text))
                    {
                        return text;
                    }
                    if (value != null)
                    {
                        Type valueType = value.GetType();
                        string name = ReadStringMember(value, valueType, "m_name") ??
                                      ReadStringMember(value, valueType, "name") ??
                                      ReadStringMember(value, valueType, "Name");
                        if (!string.IsNullOrEmpty(name))
                        {
                            return name;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (PhysicalWaterPlugin.Settings != null && PhysicalWaterPlugin.Settings.Diagnostics.Value)
                {
                    PhysicalWaterPlugin.Log.LogDebug("PhysicalWater weather reflection bridge could not resolve EnvMan environment: " + ex.Message);
                }
            }

            return string.Empty;
        }

        private static object ReadObjectMember(object instance, Type type, string memberName)
        {
            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                return field.GetValue(instance);
            }

            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property != null && property.GetIndexParameters().Length == 0 ? property.GetValue(instance, null) : null;
        }

        private static string ReadStringMember(object instance, Type type, string memberName)
        {
            object value = ReadObjectMember(instance, type, memberName);
            return value as string;
        }

        private Color ComputeWaterVertexColor(int x, int y, int index, float seaLevel)
        {
            float edgeAlpha = 1f;
            float sinkMeters = Mathf.Max(0f, PhysicalWaterPlugin.Settings.VisualBoundarySinkMeters.Value);
            if (sinkMeters > 0.01f && _cellSize > 0.01f)
            {
                int cellsFromEdge = Math.Min(Math.Min(x, y), Math.Min(_resolution - 1 - x, _resolution - 1 - y));
                edgeAlpha = Mathf.Clamp01((cellsFromEdge * _cellSize) / sinkMeters);
                edgeAlpha = edgeAlpha * edgeAlpha * (3f - 2f * edgeAlpha);
            }

            float shoreFoam = 0f;
            float shoreAlpha = 1f;
            if (_terrainHeights != null && index >= 0 && index < _terrainHeights.Length)
            {
                float depth = seaLevel - _terrainHeights[index];
                if (depth < 0f)
                {
                    float visualBand = Mathf.Max(0.05f, PhysicalWaterPlugin.Settings.ShorelineVisualBand.Value);
                    shoreAlpha = Mathf.Clamp01(1f + depth / visualBand) * 0.32f;
                    shoreFoam = shoreAlpha * 0.45f;
                }
                else
                {
                    shoreFoam = 1f - Mathf.Clamp01(depth / 1.15f);
                    shoreFoam = shoreFoam * shoreFoam * (3f - 2f * shoreFoam);
                    shoreAlpha = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(depth / 0.85f));
                }
            }

            float foam = Mathf.Clamp01(shoreFoam);
            float ripple = (_current != null && index >= 0 && index < _current.Length)
                ? Mathf.Clamp01(Mathf.Abs(_current[index]) / Mathf.Max(0.001f, GetMaxSimulatedDisplacement()))
                : 0f;
            return new Color(edgeAlpha * shoreAlpha, foam, ripple, 1f);
        }

        private bool IsGameplayWaterAt(Vector3 worldPosition, float seaLevel)
        {
            float hydroSurface;
            float hydroDepth;
            if (TrySampleHydrodynamicSurface(worldPosition, out hydroSurface, out hydroDepth))
            {
                return hydroDepth > HydroMinWetDepth;
            }

            return IsOpenOceanOutsideHydro(worldPosition);
        }

        private float ApplyOuterBoundarySink(int x, int y, float waterHeight, float terrainHeight, float seaLevel)
        {
            float sinkMeters = Mathf.Max(0f, PhysicalWaterPlugin.Settings.VisualBoundarySinkMeters.Value);
            if (sinkMeters <= 0.01f || _cellSize <= 0.01f)
            {
                return waterHeight;
            }

            int cellsFromEdge = Math.Min(Math.Min(x, y), Math.Min(_resolution - 1 - x, _resolution - 1 - y));
            float metersFromEdge = cellsFromEdge * _cellSize;
            if (metersFromEdge >= sinkMeters)
            {
                return waterHeight;
            }

            float sinkDepth = Mathf.Max(1f, PhysicalWaterPlugin.Settings.VisualBoundarySinkDepth.Value);
            float hiddenHeight = terrainHeight - sinkDepth;
            if (terrainHeight > seaLevel + 64f)
            {
                hiddenHeight = seaLevel - sinkDepth;
            }

            float t = Mathf.Clamp01(metersFromEdge / sinkMeters);
            t = t * t * (3f - 2f * t);
            return Mathf.Lerp(hiddenHeight, waterHeight, t);
        }

        private static float HideWaterUnderTerrain(Vector3 worldPosition, float waterHeight)
        {
            float terrainHeight;
            Vector3 probe = new Vector3(worldPosition.x, waterHeight, worldPosition.z);
            if (Heightmap.GetHeight(probe, out terrainHeight) && terrainHeight > waterHeight - 0.18f)
            {
                return terrainHeight - 8f;
            }

            return waterHeight;
        }

        private static bool TryGetTerrainHeight(Vector3 worldPosition, out float terrainHeight)
        {
            float margin = Mathf.Clamp(PhysicalWaterPlugin.Settings.ShorelineDryMargin.Value, -1f, 2f);
            Vector3 probe = new Vector3(worldPosition.x, PhysicalWaterPlugin.Settings.SeaLevel.Value - margin, worldPosition.z);
            if (Heightmap.GetHeight(probe, out terrainHeight))
            {
                return true;
            }

            if (WorldGenerator.instance != null)
            {
                terrainHeight = WorldGenerator.instance.GetHeight(worldPosition.x, worldPosition.z);
                return true;
            }

            terrainHeight = 0f;
            return false;
        }

        private bool TryGetNearestGridIndex(Vector3 worldPosition, out int index)
        {
            Vector2 grid = WorldToGrid(worldPosition);
            int x = Mathf.RoundToInt(grid.x);
            int y = Mathf.RoundToInt(grid.y);
            if (x < 0 || y < 0 || x >= _resolution || y >= _resolution)
            {
                index = -1;
                return false;
            }

            index = Index(x, y);
            return true;
        }

        private Vector2 WorldToGrid(Vector3 worldPosition)
        {
            return new Vector2((worldPosition.x - _origin.x) / _cellSize, (worldPosition.z - _origin.z) / _cellSize);
        }

        private int Index(int x, int y)
        {
            return x + y * _resolution;
        }

        private void SetPreviewVisible(bool visible)
        {
            bool shouldShow = visible && _hasUsableMaterial;

            // 0.4.1: the old finite near/far grids are physics and shoreline-mask data only.
            // They must never render again; doing so recreates the giant jelly sheet.
            if (_meshRenderer != null) _meshRenderer.enabled = false;
            if (_farMeshRenderer != null) _farMeshRenderer.enabled = false;

            if (_shorelineFoamMeshRenderer != null && _shorelineFoamMeshRenderer.enabled != shouldShow)
            {
                _shorelineFoamMeshRenderer.enabled = shouldShow;
            }

            if (_lodOcean != null)
            {
                _lodOcean.SetMaterial(_runtimeMaterial);
                _lodOcean.SetVisible(shouldShow);
            }
        }

        private void SetFarPreviewVisible(bool visible)
        {
            // Retained for compatibility with old call sites. 0.4.1 renders all ocean
            // distance bands through PhysicalWaterLodOcean instead.
            if (_farMeshRenderer != null) _farMeshRenderer.enabled = false;
        }

        private void SuppressVanillaWaterIfNeeded()
        {
            // 0.3.17 hard cutover: vanilla water stays suppressed for the entire session.
            // Replacement-render failures are surfaced as PhysicalWater errors, never as
            // a hidden fallback to the vanilla ocean.
            if (!ShouldSuppressVanillaWaterSources() ||
                Time.realtimeSinceStartup < _nextVanillaSuppressionTime)
            {
                return;
            }

            _nextVanillaSuppressionTime = Time.realtimeSinceStartup + 1f;

            if (WaterVolume.Instances == null)
            {
                return;
            }

            for (int i = 0; i < WaterVolume.Instances.Count; i++)
            {
                WaterVolume waterVolume = WaterVolume.Instances[i];
                if (waterVolume != null)
                {
                    VanillaWaterSuppression.HideRenderers(waterVolume);
                }
            }

            if (ShouldSuppressVanillaWaterSources())
            {
                SuppressDryBaselineSceneWater();
            }
        }

        private void SuppressDryBaselineSceneWater()
        {
            if (Time.realtimeSinceStartup < _nextDryBaselineSweepTime)
            {
                return;
            }

            _nextDryBaselineSweepTime = Time.realtimeSinceStartup + 8f;
            _suppressedWaterRendererCount = 0;
            _suppressedWaterAudioCount = 0;

            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || IsPhysicalWaterObject(renderer.gameObject))
                {
                    continue;
                }

                if (LooksLikeVanillaWaterRenderer(renderer))
                {
                    renderer.enabled = false;
                    _suppressedWaterRendererCount++;
                }
            }

            // Suppress Valheim water audio without a compile-time UnityEngine.AudioModule reference.
            // We enumerate Components from CoreModule and identify AudioSource by runtime type name.
            Component[] components = UnityEngine.Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || component.GetType().FullName != "UnityEngine.AudioSource" || IsPhysicalWaterObject(component.gameObject))
                {
                    continue;
                }

                if (LooksLikeVanillaWaterAudio(component))
                {
                    InvokeAudio(component, "Stop");
                    SetAudioProperty(component, "mute", true);
                    SetAudioProperty(component, "volume", 0f);
                    _suppressedWaterAudioCount++;
                }
            }
        }

        private static bool LooksLikeVanillaWaterRenderer(Renderer renderer)
        {
            if (ContainsWaterWord(renderer.name) || ContainsWaterWord(GetHierarchyPath(renderer.transform)))
            {
                return true;
            }

            Material[] materials = renderer.sharedMaterials;
            if (materials == null)
            {
                return false;
            }

            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null)
                {
                    continue;
                }

                string shaderName = material.shader != null ? material.shader.name : string.Empty;
                if (ContainsWaterWord(material.name) || ContainsWaterWord(shaderName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool LooksLikeVanillaWaterAudio(Component audioSource)
        {
            if (audioSource == null) return false;
            string clipName = string.Empty;
            object clip = GetAudioProperty(audioSource, "clip");
            if (clip is UnityEngine.Object)
            {
                clipName = ((UnityEngine.Object)clip).name;
            }
            return ContainsWaterWord(audioSource.name) ||
                   ContainsWaterWord(GetHierarchyPath(audioSource.transform)) ||
                   ContainsWaterWord(clipName);
        }

        private static bool ContainsWaterWord(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            string lower = value.ToLowerInvariant();
            return lower.Contains("water") ||
                   lower.Contains("ocean") ||
                   lower.Contains("wave") ||
                   lower.Contains("sea") ||
                   lower.Contains("shore");
        }

        private static bool IsPhysicalWaterObject(GameObject gameObject)
        {
            return gameObject != null && GetHierarchyPath(gameObject.transform).Contains("PhysicalWater");
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            string path = transform.name;
            Transform parent = transform.parent;
            int guard = 0;
            while (parent != null && guard++ < 16)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        private void LogDiagnosticsIfNeeded()
        {
            if (!PhysicalWaterPlugin.Settings.Diagnostics.Value || Time.realtimeSinceStartup < _nextDiagnosticTime)
            {
                return;
            }

            _nextDiagnosticTime = Time.realtimeSinceStartup + 30f;
            PhysicalWaterPlugin.Log.LogInfo("Physical water diagnostics: queries=" + _queryCount +
                                            ", dryBaseline=" + PhysicalWaterPlugin.Settings.DryOceanFloorBaseline.Value +
                                            ", physicalInteraction=" + PhysicalWaterPlugin.Settings.PhysicalWaterInteractionEnabled.Value +
                                            ", preview=" + PhysicalWaterPlugin.Settings.RenderPreviewSurface.Value +
                                            ", overrideQueries=" + PhysicalWaterPlugin.Settings.OverrideWaterQueries.Value +
                                            ", feedFloating=" + PhysicalWaterPlugin.Settings.FeedFloatingLiquidLevel.Value +
                                            ", reactToFloatingObjects=" + PhysicalWaterPlugin.Settings.ReactToFloatingObjects.Value +
                                            ", feedCharacters=" + PhysicalWaterPlugin.Settings.FeedCharactersLiquidLevel.Value +
                                            ", hideVanilla=" + PhysicalWaterPlugin.Settings.HideVanillaWaterRenderers.Value +
                                            ", suppressVanillaFloaters=" + PhysicalWaterPlugin.Settings.SuppressVanillaWaterVolumeFloaters.Value +
                                            ", materialReady=" + _hasUsableMaterial +
                                            ", material=" + _runtimeMaterialLabel +
                                            ", waterVertices=" + _waterVertexCount +
                                            ", visibleTriangles=" + _visibleTriangleCount +
                                            ", farWaterVertices=" + _farWaterVertexCount +
                                            ", farVisibleTriangles=" + _farVisibleTriangleCount +
                                            ", suppressedWaterRenderers=" + _suppressedWaterRendererCount +
                                            ", suppressedWaterAudio=" + _suppressedWaterAudioCount +
                                            ", shoreFoamEmitted=" + _shoreFoamEmissionCount +
                                            ", interactionParticles=" + _interactionEmissionCount +
                                            ", floatingProbeFallbacks=" + _floatingProbeFallbackCount +
                                            ", lodPresentation=" + (_lodOcean != null && _lodOcean.PresentationEnabled) +
                                            ", lodPatches=" + (_lodOcean != null ? _lodOcean.PatchCount : 0) +
                                            ", lodEnabledPatches=" + (_lodOcean != null ? _lodOcean.EnabledPatchCount : 0) +
                                            ", lodBindings=" + (_lodOcean != null ? _lodOcean.GetBindingDiagnostics() : "null") +
                                            ", playerHydroDepth=" + _lastPlayerHydroDepth.ToString("F3") +
                                            ", playerBed=" + _lastPlayerBedHeight.ToString("F2") +
                                            ", playerWetCell=" + _lastPlayerWetCell +
                                            ", playerCoarseDomainWet=" + _lastPlayerCoarseDomainWet +
                                            ", playerInsideHydroTile=" + _lastPlayerInsideHydroTile +
                                            ", playerCandidateFluid=" + _lastPlayerCandidateFluid +
                                            ", playerSolidCell=" + _lastPlayerSolidCell +
                                            ", playerGrid=" + _lastPlayerGrid.ToString("F2") +
                                            ", gridOrigin=" + _lastPlayerGridOrigin.ToString("F2") +
                                            ", localPlayerWaterExists=" + _lastLocalPlayerWaterExists +
                                            ", localPlayerSurfaceY=" + _lastLocalPlayerWaterSurface.ToString("F2") +
                                            ", localPlayerDepth=" + _lastLocalPlayerWaterDepth.ToString("F2") +
                                            ", hydroVolume=" + CalculateHydroVolume().ToString("F1") + "m3.");
            _queryCount = 0;
            _shoreFoamEmissionCount = 0;
            _interactionEmissionCount = 0;
            _floatingProbeFallbackCount = 0;
        }

        private void UpdateLocalPlayerWaterDiagnostic()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                _lastPlayerHydroDepth = 0f;
                _lastPlayerBedHeight = 0f;
                _lastPlayerWetCell = false;
                _lastPlayerCoarseDomainWet = false;
                _lastPlayerInsideHydroTile = false;
                _lastPlayerCandidateFluid = false;
                _lastPlayerSolidCell = false;
                _lastPlayerGrid = Vector2.zero;
                _lastPlayerGridOrigin = Vector3.zero;
                _lastLocalPlayerWaterExists = false;
                _lastLocalPlayerWaterSurface = -10000f;
                _lastLocalPlayerWaterDepth = 0f;
                return;
            }

            Vector3 position = player.transform.position;
            Vector2 grid = WorldToGrid(position);
            _lastPlayerGrid = grid;
            _lastPlayerGridOrigin = _origin;
            _lastPlayerInsideHydroTile = grid.x >= 0f && grid.y >= 0f && grid.x <= _resolution - 1 && grid.y <= _resolution - 1;
            _lastPlayerCoarseDomainWet = IsCoarseOceanDomainWet(position);
            _lastPlayerHydroDepth = 0f;
            _lastPlayerBedHeight = 0f;
            _lastPlayerWetCell = false;
            _lastPlayerCandidateFluid = false;
            _lastPlayerSolidCell = false;
            if (_lastPlayerInsideHydroTile && _hydroDepth != null && _waterMask != null)
            {
                int px = Mathf.Clamp(Mathf.RoundToInt(grid.x), 0, _resolution - 1);
                int py = Mathf.Clamp(Mathf.RoundToInt(grid.y), 0, _resolution - 1);
                int pi = Index(px, py);
                _lastPlayerHydroDepth = _hydroDepth[pi];
                _lastPlayerBedHeight = _effectiveBedHeights[pi];
                _lastPlayerCandidateFluid = _candidateWaterMask[pi];
                _lastPlayerSolidCell = _solidHydroMask[pi];
                _lastPlayerWetCell = !_solidHydroMask[pi] && _waterMask[pi] && _hydroDepth[pi] > HydroMinWetDepth;
            }
            float surface = GetSurfaceHeight(position, 1f);
            _lastLocalPlayerWaterSurface = surface;
            _lastLocalPlayerWaterExists = surface > -9990f;
            _lastLocalPlayerWaterDepth = _lastLocalPlayerWaterExists ? surface - position.y : 0f;
        }

        private int FarIndex(int x, int y)
        {
            return x + y * _farResolution;
        }
    }
}
