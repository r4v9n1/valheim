# Changelog

## 0.9.5

- Retunes smoke pacing for smoother visible fire, hearth, smelter, and processing-station smoke.
- Lowers the default smoke update interval from 0.05s to 0.005s.
- Lowers the TerramizerServer companion smoke interval from 0.08s to 0.005s.
- Changes smoke pacing from one global throttle to adaptive per-renderer staggering, so smoke does not freeze and resume in one visible batch.
- Prioritizes smoother-than-vanilla smoke presentation over smoke CPU savings; the main performance gains remain structure pacing and server ownership.
- Keeps the stronger server-companion structure pacing unchanged.

## 0.9.4

- Moves the human/AI development disclosure to the top of the Thunderstore details text so it is visible immediately.
- Adds a concise human-directed, AI-assisted development note to the package manifest description.
- No gameplay changes intended relative to 0.9.3.

## 0.9.3

- Adds an explicit TerramizerServer companion RPC handshake for more reliable auto-detection.
- Keeps server-synced metadata detection as a fallback for older companion builds.
- Adds clearer waiting diagnostics that distinguish missing RPC response from missing synced metadata.

## 0.9.2

- Makes TerramizerServer companion detection more tolerant while server metadata is arriving.
- Enables companion mode when the TerramizerServer version key is present, even if the static-ownership key is delayed.
- Adds compact companion-detection diagnostics while waiting for server metadata.

## 0.9.1

- Adds TerramizerServer companion auto-detection through Valheim server-synced metadata.
- Adds optional server-companion smoothing config for servers using static-piece server ownership.
- Uses companion-specific WearNTear and smoke pacing intervals when TerramizerServer static ownership is detected.
- Extends diagnostic summaries with companion-mode state and detected server version.

## 0.9.0

- Improves frame-time consistency in dense bases by smoothing structure and smoke update work.
- Reduces grass-rebuild spikes by combining repeated reset requests and allowing terrain changes to settle briefly.
- Preserves Valheim's visual quality and normal object-loading behavior.
- Works standalone, with vanilla servers, and with the optional TerramizerServer companion.
