# Terramizer

Terramizer `0.9.5` is a standalone Valheim client performance mod. It improves frame-time consistency in dense bases while preserving Valheim's visual quality and normal world behavior.

## Active optimizations

- Grass-reset burst coalescing.
- Short grass-rebuild settling after terrain regeneration.
- Client WearNTear updater pacing.
- Adaptive client smoke-renderer pacing tuned for smoother visible smoke.
- Unity development job-debugger disabling.
- Optional FPS and feature-counter diagnostics.
- Auto-detected TerramizerServer companion smoothing for servers using server-owned static pieces.

Terramizer works in single-player, local hosting, on vanilla servers, and with TerramizerServer. The server companion is optional. When TerramizerServer `0.6.5` or newer is detected through the lightweight companion RPC, Terramizer keeps normal gameplay behavior but uses slightly stronger client pacing for structure visual maintenance while prioritizing smoother-than-vanilla smoke presentation. Smoke pacing now runs every frame at normal frame rates, and only staggers smoke renderers when there is extreme frame rate headroom. Older TerramizerServer metadata detection remains as a fallback.

The focused optimizations preserve normal terrain, object loading, lighting, detail, ownership, and save behavior.

## Build and package

```powershell
.\build.ps1
.\build-package.ps1
```

Outputs:

```text
dist\Terramizer.dll
artifacts\R4V9N1-Terramizer-0.9.5.zip
```

## Install

Replace the older DLL in the Valheim client's `BepInEx\plugins` folder. Keep the existing config:

```text
BepInEx\config\r4v9n1.terramizer.cfg
```

Server companion config:

```text
[ServerCompanion]
EnableServerCompanionOptimizations = true
AutoDetectTerramizerServer = true
ForceServerCompanionMode = false
LogServerCompanionDetection = true
ServerCompanionWearNTearMinIntervalSeconds = 0.16
ServerCompanionSmokeMinIntervalSeconds = 0.005
```

Created by R4V9N1.
