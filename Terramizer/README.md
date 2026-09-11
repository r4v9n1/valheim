# Terramizer

Terramizer `1.0.1` is a standalone Valheim client performance mod. It improves frame-time consistency in dense bases while preserving Valheim's visual quality and normal world behavior.

## Active optimizations

- Grass-reset burst coalescing.
- Short grass-rebuild settling after terrain regeneration.
- Vanilla WearNTear, vegetation, fire, and smoke update cadence is preserved.
- Unity development job-debugger disabling.
- Optional FPS and feature-counter diagnostics.
- Auto-detected TerramizerServer companion smoothing for servers using server-owned static pieces.

Terramizer works in single-player, local hosting, on vanilla servers, and with TerramizerServer. The server companion is optional. When TerramizerServer is detected through the validated companion RPC or compatible server metadata, Terramizer enables only the compatible terrain authority; without the server mod, remote worlds remain vanilla-authoritative.

The focused optimizations preserve normal terrain, object loading, lighting, detail, ownership, and save behavior.

## Build and package

```powershell
.\build.ps1
.\build-package.ps1
```

Outputs:

```text
dist\Terramizer.dll
G:\My Drive\build\Valheim\releases\Terramizer\R4V9N1-Terramizer-1.0.1.zip
```

## Install

Replace the older DLL in the Valheim client's `BepInEx\plugins` folder. Keep the existing config:

```text
BepInEx\config\r4v9n1.terramizer.cfg
```

The client companion settings only control lightweight detection and diagnostics:

```text
[ServerCompanion]
EnableServerCompanionOptimizations = true
AutoDetectTerramizerServer = true
ForceServerCompanionMode = false
LogServerCompanionDetection = true
```

Terramizer does not throttle vegetation, WearNTear, fire, or ambient smoke. The
only smoke removed is the one-shot placement burst attached to a newly placed
building piece. On a dedicated server, 16 m terrain limits are enabled only
after a validated TerramizerServer handshake; without the companion, terrain
authority remains vanilla.

Created by R4V9N1.
