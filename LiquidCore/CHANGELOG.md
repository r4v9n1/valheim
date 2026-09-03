## 0.6.0-devE3.2-probe4

- Internal performance candidate only; no live milestone is claimed.
- Corrected cached cut-cell pressure-region expansion authority at the finite domain boundary. Liquid touching a real domain face no longer invalidates the cache as if that face were an expandable interior crop boundary; true interior crop contact still expands immediately.
- Preserved solver equations, pressure parameters, particle deposition, gravity, volume, PCE geometry, and surface reconstruction unchanged.
- The captured 216 m3 live state completed 600 steps with one readback stage per settled step instead of the three-stage fallback seen in Baseline A. Exact volume and all ownership/safety counters remained clean.
- Dedicated physical/interior boundary decisions and the full protected production regression pass. Live Valheim A/B acceptance remains pending.
- Assembly `0.6.0.20`.

## 0.6.0-devE3.2-probe3

- Moved causal snapshot revalidation to the completed-preparation application boundary. The preceding live F6 trace rebuilt and logged the same 111-root snapshot 186 times while one background preparation ran, despite zero intervening geometry events; the prepared result is still rejected before application if its generation or state revision changed.
- Reworked the PCE causal-root proof from repeated nested source scans and temporary per-call collections to one linear source pass over reusable scratch maps and sets. The Valheim adapter also reuses its root-id scratch set.
- The user-controlled F6 run reached `PW_E3_F6_READY` with healthy geometry, 145 roots, 163 sources, no particles, and a 2.824-second background preparation. This is F6 evidence only; F7 visual, construction/destruction, terrain, and idle acceptance remain pending.
- Integration build, PCE foundation/scale/stress, causal preparation, exact captured live replay, live-scale equilibrium, hydrostatic, corrected basin, lifecycle, persistence, all 14 streaming cases plus dedicated long settle, Phase 2 terrain, surface topology/spectrum, DevD3, and full production validation PASS.
- Assembly `0.6.0.19`; no manual F7 PASS is claimed.

## 0.6.0-devE3.2-probe2

- Added sampling-density-aware liquid reconstruction: dense domains retain marker-exact terrain behavior, while sparse domains accept horizontal parcel overlap only in each marker's own vertical layer.
- Added a 120-step marker-precision window after a solid edit so construction and terrain displacement remain causal before sparse steady-state connectivity resumes.
- Reduced full-particle ownership scans from every four steps to every 30 steps under the existing 12 m prefetch guard, removing delayed GPU/CPU serialization.
- Added exact replay of the preserved 216 m3 live failure snapshot. Visibility flips fell from 76,296 to 1,780 and liquid-mask components from 61 to 8 while terrain edits, equilibrium, streaming, persistence, PCE, conservation, and safety regressions pass.
- Assembly `0.6.0.18`; manual Valheim visual and construction acceptance remains pending.

## 0.6.0-devE3.2-probe1

- Added a reusable PCE required-discovery scheduler with explicit cached/sleeping/dirty/updating/active lifecycle. Causal, Valheim-event, and bounded-consistency tiles now remain readiness-blocking until downstream geometry work drains, while optional streaming discovery can be discarded at the clean sleep boundary.
- Fixed unchanged F6 worlds continuing speculative discovery after causal coverage, which advanced geometry generations and caused repeated full-domain synchronizations and multi-second stalls without F7 or player construction.
- Preserved sleeping after E3 releases its transient causal gate; the finite coverage window remains event-driven until the domain is replaced or the scene ends. Stage E1 also stays dormant before the first finite coverage request.
- Added deterministic coverage for clean sleep, persistent post-gate sleep, localized event wake, duplicate-key coalescing, downstream-work exclusion, bounded-consistency wake, and return to sleep. The current build remains pending manual Valheim acceptance.
- Finite-streaming installs no longer force verbose geometry scan/rejected-sample diagnostics; diagnostics-only installs retain the maintained geometry diagnostics path.


- Diagnostic-only candidate. No fluid solver, pressure, APIC/FLIP, SDF, E3 streaming, damping, E2 smoothing, topology, or shader behavior is changed from devE3.2.
- Added four bounded blocking live field snapshots at approximately 2, 5, 20, and 60 simulated seconds after explicit fill, plus `F11` / `pw_e3_probe` for a manual snapshot.
- Each snapshot writes a CSV comparing the authoritative fractional top interface, Valheim Heightmap terrain height, solid-SDF zero crossing below the liquid, final E2 column height, previous/current persistent render vertex height, and the actual presentation-interpolated render height.
- Logs terrain correlations and RMSEs needed to classify the live-only terrain blanket without another speculative physics change.

- Coalesces bursts of causal Valheim geometry changes before applying the exact finite-domain solid synchronization. This prevents repeated synchronous occupancy/SDF/texture-upload rebuilds from turning world loading or construction activity into recurring multi-hundred-millisecond stalls; no geometry state or safety check is skipped.
- This build is evidence-gathering only and must not be marked live PASS from compilation or offline regressions.
## 0.6.0-devE3.2

- Fixed the terrain-conforming equilibrium at its mathematical source: liquid-air pressure neighbors treated every partial top cell as one full cell of hydrostatic head. The pressure matrix and projection now use the authoritative fractional vertical interface distance, while X/Z shoreline faces retain full grid distance because a scalar cell fraction contains no lateral interface location.
- Added deterministic D3D11 same-column forensics for terrain, particles, authoritative fractions, raw top, presentation height, pressure, conservation, divergence, and terrain correlation. The old equilibrium held `p = rho*g*0.75m` in every one-cell sheet regardless of fill; the corrected field reaches presentation-height standard deviation `0.045m` with terrain correlation `0.158`, exact `216.000m3`, fractional volume `1.000x`, and zero divergence.
- E1's closed downhill equilibrium case now observes 120 rather than 60 simulated seconds before applying the unchanged acceptance gates; direct checkpoints proved the corrected fluid was still draining at 60 seconds and passed the same `1.5m` spread gate at 120 seconds.
- Final D3D11 regression: Stage A/B/B1, devB7, devC1/C2, devD1-D6, devE1, and devE2 topology all PASS. E3 causal streaming, Heightmap/SDF handling, particle transport, conservation, density control, and E2 topology/presentation code are unchanged.
- Packaged assembly `0.6.0.16`: DLL SHA256 `4BCC1B132723CE639CAAA54B257CF1FD3EBF0B7A001505F19D8C08ADE41F004C`; rebuilt AssetBundle SHA256 `E346A2F239049564DF97ECC82A720CA54398BA4731E4CA53942454EA4CD55A32`.

## 0.6.0-devE3.1

- Fixed the live-only E3 causal geometry-coverage handoff where an explicit finite-domain coverage request could include 8 m discovery tiles just outside D6.4's normal snapped 64 m active discovery window. Such tiles could remain permanently unschedulable, leaving `CausalGeometryCoverageReady` false and the finite domain paused at geometry generation 0.
- Explicit E3 causal coverage now temporarily expands the adapter's effective discovery bounds until a coherent snapshot is actually synchronized. The temporary expansion is released only after successful E3 solid synchronization.
- The fluid spawn gate remains strict. No timeout, empty-geometry bypass, vanilla-water fallback, solver change, E1 math change, E2 presentation change, or Heightmap-physics change was introduced.
- Added pending causal-tile progress to the E3 preparation message for live diagnostics.
# PhysicalWater Changelog

## 0.6.0-devE3 - Conservative Local-Domain Streaming

- 2026-08-28 forensic follow-up: the last red legacy devD1 wall-opening case was not a real transport regression. Archived `8.301%` and failing `7.999%` runs came from the same D3D11 density-redistribution path; the first divergence appears in the closed-wall phase at step 28, before the opening matters, and repeated density-off runs remain bitwise identical. The single-shot density-on gate was therefore obsolete as a deterministic transport metric.
- `RunVolumetricDevD1Validation` now keeps the unchanged `>= 8.000%` threshold but evaluates that one sealed-wall transport case with `DensityControlIntervalSteps=0`, preserving the transport geometry/timestep/APIC path while moving density-redistribution coverage back to Stage B1 where it already belongs. Fresh deterministic devD1 result: `2601 / 31104 = 8.362%`.
- 2026-08-28 forensic follow-up: the Stage B1 vertical-drain failure was also a validator issue, not a solver regression. Historical D3D11 logs on the same machine already ranged from `belowShelf 6 -> 2648` through `11 -> 2745`. Fresh forensics show repeated density-on runs first diverge at closed-shelf step `32`, exactly on a `RedistributeCrowdedParticles` interval step after crowding begins at step `17`, while repeated density-off runs remain bitwise identical and the drain stays physically healthy in both modes.
- `RunVolumetricStageB1Validation` now keeps density control enabled but measures the vertical drain with a one-cell-inset deep lower-chamber probe instead of the brittle raw full-volume integer at the shelf boundary. Fresh isolated Stage B1 PASS: `belowShelf 8 -> 2660`, `deep 3 -> 2585`, `deepGain=2582`, `maxClosedDeep=5`.
- Replaced the fixed 24 m live diagnostic shell with a 72x18x48 m compact E3 GPU window partitioned into stable 24 m logical regions. Explicit finite water can cross former local-domain boundaries without independent pressure solves.
- Added active/dormant particle ownership, stable IDs, conservative region transfer, asynchronous ownership scans, off-frame prepared rebasing, causal geometry revisions, and streaming telemetry.
- Live geometry coverage now prefetches one logical-region margin around the current window so prepared rebases already contain future terrain and structures.
- Preserved the frozen E1 solver/math and E2.1.2 rendering. Global ocean replacement, vanilla-water suppression, water-query overrides, swimming, buoyancy, players, ships, fish, creatures, and far-ocean rendering remain off.
- E3 tests 1-13 and the final frozen Stage A/B/B1/devB7/devC1/devC2/devD1/devD2/devD3/devD4/devD5/devD6/devE1/devE2.1.2 regression pass offline. Focused inland live certification remains pending.
- Packaged assembly `0.6.0.14`: DLL SHA256 `6E460AF2A737FB2DC79C512DD1AB87540CD618DC67C4E4C8C39DA6C77897DC14`; unchanged frozen E2.1.2 AssetBundle SHA256 `BCBE180FAB6B4588F6503F28915200C77398483085A97CAB9F4DF54FD51DB11F`.


## 0.6.0-devE2.1.2 - Anchored Uniform Topology Fade

- The focused `devE2.1.1` live run FAILED visually. Direct consecutive window captures showed changing black triangle lattices; F10 pause froze the pattern, isolating the failure to the centroid-shrink presentation stage rather than depth ordering, camera motion, or solver instability.
- Live `.1.1` physics stayed conservative: `216.000 m3`, fractional ratio `0.988`, zero divergence, zero invalid/in-solid particles, fixed `7,938` draw slots, and no PhysicalWater errors. Components briefly varied `1-2`.
- Preserved the frozen E1 solver/math and E2 smoothing. Exact topology changes now fade the full last-valid anchored triangle uniformly during one render interval. No dry fallback geometry, one/two-corner wedge, threshold, hysteresis, morphology, component deletion, or solver change is used.
- The exact rebuilt AssetBundle matches the source-assets 300-frame D3D11 capture: `111.40` transitioning slots/frame mean, `239` max, zero draw-count/slot-identity changes, and no lattice in the captured worst transition.
- E2 topology, all 19 E1 cases, and all nine C2 cases pass offline.
- Focused live validation PASS. Direct stationary and moved normal/low views showed no localized black lattice, transient holes, missing triangle patches, or transparent-order flicker. The finite surface settled level instead of following terrain.
- Twelve final live telemetry samples retained `216.000 m3`, fractional ratio `0.988`, one physical/surface component, zero divergence, zero invalid/in-solid particles, and `7,938` stable persistent draw slots. Active CPU/reconstruction p95 were `0.493/0.350 ms`; no PhysicalWater errors were recorded.
- The straight rectangular outer edge remains the intentional closed `24 x 24 m` E1 diagnostic boundary, not a generated shoreline or final gameplay-water presentation.
- Packaged assembly `0.6.0.13`: DLL SHA256 `93F6314B59F7394E8478754CBE0963D66B535588A4D4C9F0BE1E19C8A392E471`; AssetBundle SHA256 `BCBE180FAB6B4588F6503F28915200C77398483085A97CAB9F4DF54FD51DB11F`.

## 0.6.0-devE2.1.1 - Anchored Exact-Topology Presentation

- Focused live validation: FAIL. Shrinking the anchored triangle exposed a moving lattice between permanent slots, so this build is not certified.
- Preserved the frozen E1 solver/math, fractional field, pressure projection, particle transport, conservation, and E2 smoothing exactly.
- Corrected the presentation-only source of the failed live surface: unsupported one/two-corner wedges are no longer drawn, and an exact-zero triangle transition remains anchored to its valid-frame geometry instead of interpolating toward a fallback reference plane.
- Permanent per-cell draw-slot identity remains fixed. A slot grows or shrinks continuously over the render interval only when its matching authoritative three-corner topology appears or disappears; no threshold, hysteresis, morphology, component deletion, or solver change is used.
- The exact rebuilt AssetBundle matches the source-assets 300-frame D3D11 capture: `7,938` fixed slots, zero draw-count/slot-identity changes, zero partial translucent slots, and 60 Hz pixel-change mean/p95/max `0.007221/0.009491/0.010918`.
- E2 topology, all 19 E1 cases, and all nine C2 cases pass offline. This build is not marked live PASS until the focused Valheim confirmation.
- Packaged assembly `0.6.0.12`: DLL SHA256 `AB209DBA585A2037AD711CFEB8A0DDC67581FD71CCF72CF9510BAF893A808846`; AssetBundle SHA256 `54E66A1B83A5E58DAB421859306E3FF3868DC8A147A23CA73602F50EC26F32D4`.

## 0.6.0-devE2.1 - Persistent Surface Triangle Identity

- Focused live visual validation: FAIL. All samples retained `7,938` persistent draw slots with `drawStable=True`, but localized spots still disappeared/reappeared because the fixed slots were changed between active and zero-area geometry as corner validity changed.
- The live surface also followed and floated above terrain. Exact presentation cause: authoritative column reconstruction adds the complete summed column liquid depth to the highest solid below the lowest liquid sample instead of presenting the actual vertical free-surface position.
- Physics remained conservative (`216.000 m3`, fractional ratio `0.981-0.996`, zero invalid/in-solid particles), with p95 active CPU/reconstruction `0.302/0.218 ms` and no PhysicalWater errors. Components varied `1-2`; E2 is not certified or frozen.
- Preserved the frozen E1 solver/math and the validated E2 smoothing parameters exactly.
- Assigned two permanent presentation triangle slots to every gravity-aligned reconstruction cell. Active topology continues to follow the authoritative fractional field, while unused slots are finite degenerate geometry and no longer renumber the rendered mesh.
- Added live telemetry that separates active physical triangles from fixed presentation draw triangles and reports persistent slot count plus draw-count stability.
- The 180-frame live-like trace holds `7,938` draw slots with zero count changes or slot mismatches while the unchanged active set varies from `5,885-5,958`. E2, E1, and C2 regressions pass offline.
- The full 3D tetrahedral extraction path remains unchanged. No solver, pressure, fraction, particle, threshold, smoothing, morphology, or conservation behavior changed.
- Packaged assembly `0.6.0.11`: DLL SHA256 `58563ED9776F075CD7E71FC4DF2677872F5A0F27E38A56E931EF25B66F3FF2AB`; AssetBundle SHA256 `26E1828F5C7223A2A736A21CDE07AAED8552C7BA1D287BDE2C08265ED11F9879`.

## 0.6.0-devE2 - Conservative Surface Quality

- First focused live visual certification: FAIL. Most of the surface is visibly smoother, but localized spots flicker or become missing triangles at both normal and low camera angles. This build is not live-certified and the E2 surface changes are not frozen.
- The live E1 state itself remained valid: `216.000 m3` conserved, fractional volume `0.991-0.996x`, one liquid/surface component, zero post-projection divergence, and zero invalid/in-solid/out-of-bounds particles. The final settled window measured `0.242 ms` p95 active CPU and `0.260 ms` p95 reconstruction, with no PhysicalWater errors.
- Evidence isolates the remaining failure to localized surface-support/triangle-set instability: the final settled window varied from `6112` to `6286` extracted triangles with up to `2.3%` adjacent-sample triangle delta while volume and component count stayed fixed. The nearest-depth prepass and continuous E2 normals were active, so this is distinct from transparent triangle ordering and shared-edge lighting.
- Froze the validated E1 solver/math exactly. Pressure, fractional liquid volume, APIC/FLIP particle transport, conservation, wet support, component authority, and triangle connectivity are unchanged.
- Added eight alternating symmetric pairwise column-height relaxation passes at strength `0.18`. Each processed pair preserves its summed visual height, and no valid/dry column membership changes.
- Added shared finite-difference vertex normals with one-sided shoreline fallback, and render the debug surface from those extracted normals while preserving the E1.2 nearest-depth prepass.
- Dedicated offline A/B validation passed at identical `216.000 m3` physical volume, `5920` triangles, `3104` shoreline footprint keys, one component, zero post-projection divergence, and zero invalid/in-solid/out-of-bounds particles.
- Mean face slope improved from `19.688` to `13.611` degrees, shared-normal discontinuity from `24.159` to `0.000` degrees, and stable-interior mean height jitter from `2.15` to `1.32 mm`. E1 and C2 regressions pass.
- Stage E1 remains disabled by default. Global ocean replacement, vanilla-water suppression, gameplay water, player, ship, fish, swimming, buoyancy, and far-ocean paths remain off.
- Packaged assembly `0.6.0.10`: DLL SHA256 `FFA403EF68578C10FA9488CD93244464C4DC48FE5AB829A1AA428F7C9C99C286`; AssetBundle SHA256 `01872C092B17A9385237AB6498B140FD965891A8A9C431729A212D915E98BC6B`.

## 0.6.0-devE1.2 - Stable Nearest-Layer Debug Rendering

- Froze the E1 solver/math exactly at the validated devE1.1 state; pressure, fractional volume, reconstruction topology, particle transport, and conservation code are unchanged.
- The first finite live run is solver PASS: `216.000 m3` conserved, fractional volume `0.980-1.000x`, one liquid/surface component, zero reported divergence, and zero invalid or in-solid particles.
- Diagnosed the severe visible flicker as nondeterministic atomic triangle emission order feeding an order-dependent, two-sided transparent shader with depth writes disabled.
- Added an invisible nearest-depth prepass followed by the transparent color pass. High-angle and low-angle D3D11 rendering pass offline, while authoritative E1 math and devC2 regression remain PASS.
- The coarse/faceted reconstructed surface is a separate future visual-quality issue and is intentionally not refined in this build.
- Stage E1 remains disabled by default. Global ocean replacement, vanilla-water suppression, gameplay water, player, ship, fish, swimming, buoyancy, and far-ocean paths remain off.
- Packaged assembly `0.6.0.9`: DLL SHA256 `35F69ABB81CAA3C5EE676D83C7561CE37D1D57351C847B5E04D5CC8E53259796`; AssetBundle SHA256 `D19461B06136BDD2C8C64F3D8C0F8FFF65189068D242D14BACB2C9E340C3426D`.

## 0.6.0-devE1.1 - Authoritative Finite-Volume Surface Coupling

- Replaced independent sparse particle-splat topology with solid-aware, conservatively normalized particle-volume deposition into the authoritative fractional liquid field.
- Surface column depth now integrates that same fractional field used to classify E1 pressure cells; particle support no longer independently decides whether a cell is liquid.
- Removed E1 grounded full-cell duplication and corrected the pressure projection so liquid volume fraction is not misused as a solid cut-face aperture.
- Added validation-only GPU mask volume/component diagnostics and proper partial shoreline triangulation without smoothing, dilation, morphology, component deletion, or arbitrary shrink factors.
- Offline E1 validation passes with `27.000 m3` conserved, `1.000x` fractional/reconstructed volume, `1-1` settled components, zero reported divergence, `0.060 m` maximum temporal center movement, and `1.01%` surface-area swing. C2 regression passes.
- Stage E1 remains disabled by default. Global ocean replacement, vanilla-water suppression, gameplay water, player, ship, fish, swimming, buoyancy, and far-ocean paths remain off during E1 testing.

## 0.6.0-devD6.4 - Fine-Tiled Discovery Certification Fix

- Decoupled 8 m discovery tiles from the preserved 32 m causal SDF queue chunks, bounding dense overlap/classification work while retaining the validated generation and stale-job contract.
- Changed normal discovery to one newly active tile per 0.25-second scan and reduced full consistency work to a 900-second safety cadence.
- Ensured a source first encountered at a tile boundary contributes its complete enabled compound-collider hierarchy, preventing partial bounds and false revisions.
- Added discovery-tile/SDF-chunk telemetry and a dense-area scaling validation. The devD6.3 observed 32 m worst case (`103.277 ms`) produces a conservative 8 m estimate of `6.455 ms`.
- Fresh Stage A/B/B1/B7/C1/C2/D1/D2/D3/D4/D5/D6 regressions pass. Built and installed assembly `0.6.0.64`; DLL SHA256 `164D5B13051380527F61526FDFA3CDE75289C6D83509EA52C790496CFEC9C956`.
- Live D6.4 dense-base certification passed: 4,476 scans at p50/p95/p99/max `0.034/0.528/1.617/3.843 ms`; active discovery p95/max `1.804/3.843 ms`; cached p95/max `0.039/0.281 ms`; zero scan-frame warnings, false changes, overflows, errors, or ending backlog.
- The dense segment completed 320/320 queue jobs with active CPU p95/max `0.206/3.326 ms`, completion p95/max `15.3/18.4 ms`, zero overruns/stale jobs, and 99 coalesced chunks. WaterSurface and moving gameplay entities remained rejected.
- Certified with Terramizer `0.9.5` on the client and TerramizerServer `0.6.8` on the dedicated server; neither log contains a Terramizer error or patch conflict. Gameplay water remains disabled. Stage E tiny-domain testing is safe to begin only as a separate, deliberate milestone.

## 0.6.0-devD6.3 - Targeted Event Streaming Fix

- Routed known geometry events directly through source classification/revision and dirty-region queueing instead of repeatedly rediscovering every collider in the affected dense chunk.
- Removed the event follow-up rescan window; chunk discovery remains for stream load/eviction, unresolved sources, and periodic consistency checks.
- Suppressed no-op `Door.SetState` notifications and coalesced a delayed settled-door collider recheck by stable source ID.
- Kept the generation-checked 4 ms causal queue unchanged. Fresh devD6.3 and GPU devD5 validators pass.
- Built and installed assembly `0.6.0.63`; DLL SHA256 `D5A552BADADA1D922DEEEE72F30445E5FB399F71645ECD083B7F9205CC816540`.
- The devD6.2 final live client log exposed the scan problem and remains failed evidence. devD6.3 needs one focused live recertification; Stage E remains blocked.

## 0.6.0-devD6.2 - Live Lifecycle and Terrain Certification Fix

- Excluded unplaced build previews without stable network identity, Hugin/raven name fallbacks, and destructible vegetation prefabs that lack Valheim's normal vegetation marker components.
- Replaced broad Piece/WearNTear Unity teardown callbacks with Valheim's authoritative `ZNetScene.Destroy(GameObject)` event path.
- Coalesced Heightmap pokes into the completed regeneration event and added dirty-only hashing of actual Heightmap height samples.
- Restricted event discovery dirtying to active chunks and stopped uncached destruction callbacks outside the active cache from forcing immediate scans.
- Preserved the devD5 generation-checked causal queue without modification. Fresh devD6.2 and GPU devD5 validators pass.
- Built assembly version `0.6.0.62`; DLL SHA256 `AB2EB7A500B2C8B9895EEC841469582075A5A82C14C664571B8D3DD403E0043C`.
- The devD6.1 live run exposed these defects and is archived as failed evidence. A clean devD6.2 live recertification is still required.

## 0.6.0-devD6.1 - Live Certification Fix

- Fixed trusted Valheim build pieces with child particle effects, observed live on `wood_floor(Clone)`, being rejected as `ParticleOrEffect`.
- Added application-quit suppression for geometry event callbacks so scene teardown does not generate a false destruction-event storm.
- Preserved all diagnostics-only safety settings and the generation-checked causal queue.
- Built assembly version `0.6.0.61`; DLL SHA256 `99B307F6F601F82746997BD49EA22704AA7AFDBB2E21821AC6733D4198AF5DE6`.
- Updated devD6.1 classification validation and reran the devD5 queue regression: PASS. Live recertification remains pending.

## 0.6.0-devD6 - Geometry Adapter Correctness + Incremental Discovery

- Added hard rejection for vanilla water/ocean/liquid hierarchies and moving gameplay entities including birds, creatures, characters, fish, ships, and players.
- Restricted revision signatures to geometry-affecting transforms, collider state/shape, mesh identity/topology, and whitelisted destructible/terrain revisions.
- Added cached active-chunk discovery reuse, amortized consistency scans, detailed discovery-stage profiling, and guarded handling for uncached destruction callbacks.
- Preserved the causal generation-checked SDF queue and diagnostics-only safety mode.
- Built assembly version `0.6.0.6`; DLL SHA256 `EBCAEE19849D7BFEAC9A134589FA15A01709DF18A3E89F03F113EB8DB8DDEE45`.
- Standalone devD6 and prior regression gates pass. Manual live geometry edits and a 10-15 minute soak remain required before Stage E.

## 0.6.0-devD5 - Live Geometry Events + Frame-Budgeted SDF Streaming Diagnostics

- Added diagnostics-only live geometry event logging for Valheim pieces, terrain/heightmap updates, doors/gates, destructibles, and mine rocks, including dirty reason, source ID/type/category, old/new AABBs, dirty voxel bounds/cell counts, cache hit/miss state, and estimated occupancy/SDF/upload/total timing.
- Added a causal chunk work queue for dirty geometry -> occupancy -> SDF -> upload. Chunk jobs carry generation IDs and are discarded as stale when superseded by newer work for the same chunk before apply.
- Added queue telemetry for active CPU time per frame, queue latency, total completion latency, pending chunks, stale jobs discarded, chunks coalesced, worst queue depth, frame-budget overruns, and batch completion summaries.
- Added source-to-chunk membership caching and dirty chunk coalescing so static unchanged geometry is not continually reprocessed.
- Kept diagnostics-only safety mode: `General.Enabled=false`, no PhysicalWater gameplay simulation, no preview rendering, no vanilla-water suppression, no water-query overrides, and no swimming/buoyancy/ship/player/fish hooks.
- Built assembly version `0.6.0.5`; DLL SHA256 `830F4DED7F2D7AF2A1076D80F84F76BD2CD3B4FFC49872E0CCEB2ED569F07516`.
- Unity devD5 and all preserved regression gates passed. Manual live Valheim edit events and a 10-15 minute live diagnostics soak remain pending, so Stage E gameplay water is not yet cleared.

## 0.6.0-devD4 - Controlled Valheim Geometry Streaming Diagnostics

- Added diagnostics-only Valheim geometry streaming on top of the devD3 adapter: snapped local scan origins, chunk load/evict diagnostics, source cache hit/miss reporting, dirty-region batching, and frame/incremental budget warnings.
- Added dedicated `Heightmap` sampling diagnostics with installed-game reflection fallbacks for hidden terrain fields/methods, collider-vs-height error measurements, sample caps, deferred-sample counts, and estimated terrain occupied columns.
- Added read-only dirty markers for `Heightmap.Regenerate` and `Heightmap.Poke`.
- Changed plugin startup so `General.Enabled=false` plus `ValheimGeometryAdapter.DiagnosticsEnabled=true` creates only the geometry adapter; gameplay/replacement-water hooks and `PhysicalWaterSystem` remain disabled.
- Updated local install config to diagnostics-only: no water-query override, no vanilla water suppression, no preview surface, no swimming/buoyancy/ship/player/fish changes.
- Built assembly version `0.6.0.4`; DLL SHA256 `982FC3AC7B326173FF7219C1F7D72C143E15DE42D1D4799D0CBE2FA3EF536E8E`.
- Preserved and reran devD4 through Stage A validation gates. Captured post-restart live Valheim diagnostics with gameplay water disabled: 58 adapter scans, dense-base mean `17.517 ms`, dense-base max `25.333 ms`, and no fatal/error/exception lines. Stage E gameplay water remains blocked until manual live edit-event tests and additional dense-base optimization are complete.

## 0.6.0-devD3 - Valheim World Geometry Adapter Prototype

- Added the diagnostics-only Valheim World Geometry Adapter prototype.
- Added local-region classification for Valheim terrain, terrain modifications, build pieces, WearNTear pieces, doors/gates, destructible rocks, mine rocks, dungeon rooms/interiors, and static collider hierarchies.
- Added explicit non-SDF exclusions for ships, players, creatures, fish, dropped items, standalone floaters, vanilla water/liquid volumes, trigger-only colliders, vegetation, and particle/effect objects.
- Added transform/revision cache diagnostics for source IDs, old/new bounds, non-uniform scale, added/changed/removed objects, and estimated padded dirty voxel regions.
- Added read-only dirty markers for terrain operations, placed/destroyed pieces, doors/gates, destructibles, and mine-rock mesh updates.
- Kept the adapter disabled by default and avoided Valheim gameplay water integration.

## 0.5.1 - Hydrodynamics Core Audit

0.5.1 is the mandatory pre-build correction to the first 0.5.0 hydrodynamics implementation. Do not test 0.5.0.

## Mass conservation fixes

- Replaced independent per-face transfer with a two-pass flux solver.
- Every cell first sums all requested outgoing transfers, then scales those transfers together so total outflow can never exceed owned water depth.
- Interior transfers are equal-and-opposite between donor and receiver. Only explicit open-ocean reservoir boundary cells may add or remove mass from the active local control volume.
- Building a new solid no longer deletes the water that occupied its footprint; displaced water is redistributed into nearby non-solid cells.

## Moving-domain correctness

- The 120 m hydrodynamic tile still preserves overlapping state when it snaps.
- Newly exposed cells are explicitly tracked. Genuine Ocean-biome cells in the new strip are reseeded from the ocean reservoir after geometry is rebuilt, preventing dry bands from appearing merely because the local window moved.
- This is active-tile state, not world persistence. Hydrodynamic state is not yet serialized to the world save and long-lived remote lakes/basins are a later persistence milestone.

## No blanket water paths

- Gameplay queries inside the active tile require actual local water depth at the nearest hydrodynamic cell. Bilinear sampling can no longer smear water through a dry cell or wall.
- Floating probes retain no nearby-water fallback.
- The far LOD shader now receives a separate coarse Ocean-biome domain texture. Outside the local hydrodynamic tile, the renderer fails closed unless the world domain is genuine Ocean biome.
- The old shader behavior that assumed all space outside the local bathymetry texture was open ocean is removed.

## Integration correctness

- Legacy `OverrideWaterQueries` can no longer re-enable Valheim water while PhysicalWater is enabled; missing replacement-system state fails dry/closed.
- Restored the terrain-height sampling helper accidentally dropped during the 0.5.0 rewrite; the pre-build audit caught this compile blocker before release.
- Authoritative Floating water-query hooks now fail closed on exceptions instead of returning control to Valheim's original water query. No silent vanilla-water fallback remains in those prefix paths.
- Water/ship/floater suppression Harmony patches are now required startup hooks. If a required hook cannot be installed, PhysicalWater refuses to initialize instead of quietly running beside vanilla water.
- Vanilla WaterVolume/LiquidSurface renderer suppression is unconditional whenever PhysicalWater is enabled; the old config toggle is retained only for compatibility.
- Camera underwater detection uses the same wet/dry authority as gameplay and cannot fabricate a SeaLevel surface in a dry cell.
- Surface normals treat dry neighbors as the current wet surface instead of sampling a fictitious global ocean.
- `GetWaterVelocity` now includes the shallow-water solver's horizontal face velocity, so later floating/player/boat interactions can feel actual flood/channel flow rather than only spectral wave motion.

## Walls and terrain

- Static collider face barriers remain authoritative for horizontal flow.
- New solid construction displaces existing volume instead of erasing it.
- Geometry is resampled every 0.65 s so placed/destroyed walls and terrain edits alter the flow topology without requiring the player to move.

## Interaction visual audit

- Removed continuous wind forcing from the local ripple solver. Wind/swell remains in the spectral ocean; local ripple energy is transient and now decays when interactions stop.
- Removed creation/emission of CPU foam and shoreline billboard systems from the active path.
- `EmitFoamDecal` is now a hard no-op, so frozen white discs/diamonds cannot persist even if an old/dead call site is accidentally reached.
- Ordinary player/object contact is represented only by the continuous ripple texture and shader foam field.
- CPU splash particles are limited to one or two short-lived droplets for only very high-energy impacts.
- The local ripple solver remains damped every fixed step and heavily damps very shallow cells; dry cells are zeroed, so interaction displacement has a defined decay path instead of becoming a static decal.

## Audit scope / known milestone limits

- 0.5.1 is a local real-time shallow-water milestone, not yet a persistent whole-world water save system.
- Existing ship solver code is carried forward but is not part of the 0.5.1 acceptance test; boat behavior remains a later subsystem milestone.
- First acceptance test: isolated pit remains dry, ocean-connected trench fills over time, solid wall blocks face flux, opening the wall permits flow.
