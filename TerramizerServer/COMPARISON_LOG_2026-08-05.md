# TerramizerServer Progress Comparison

Date: 2026-08-05  
Baseline: pre-0.4.3 telemetry  
Current telemetry: complete TerramizerServer 0.4.5 session from 21:04 through 21:34  
Next build: TerramizerServer 0.4.6

## Executive summary

TerramizerServer now performs substantially less repeated scanning and bulk boost work. Normal post-load synchronization settles to single-digit object lists and a few hundred queued bytes. Large login, base, forest, and dungeon-entry bursts still occur because clients must receive the objects in those areas, but the recorded bursts drain instead of remaining permanently congested.

The mod has not deleted world ZDOs. The last total in the complete session was 1,027,875: 476 records (approximately 0.046%) above the original 1,027,399 baseline, and 55 fewer than the earlier sample in the same session. That does not resemble runaway persistent creation.

## Representative status comparison

| Metric | Original representative status | Current 0.4.5 settled/moving status | Observed change |
|---|---:|---:|---:|
| Create/destroy passes | 821 | 456-480 | About 42-44% fewer passes |
| Near candidates | 20,865 | Approximately 7,856-9,144 | Location-dependent; approximately 56-62% lower in this sample |
| Distant candidates | 2,982 | Approximately 1,303-1,338 | Location-dependent; approximately 55-56% lower in this sample |
| Zone attempts | 708 | Approximately 111-121 | About 83-84% fewer attempts |
| Bulk boost objects per interval | 14,848 | 768-1,920 while travelling; 0 when settled | About 87-95% lower while travelling |
| Backpressure skips | 4 at the original sampled burst | Usually 0; 1-2 during current bursts | Backpressure activates only when needed |
| Settled sync-list maximum | Commonly 7-12 after the original bursts | Commonly 4-10 after current loading drains | Similar healthy settled state, lower currently |
| Settled socket queue | Often 552-899 bytes | Commonly 224-484 bytes | Lower steady queue pressure |
| Dense/login sync peaks | 1,997 in the original sampled base burst | 1,300-3,253 during current travel; 8,286 at initial login | Workload-dependent; raw peaks are not directly comparable |
| Worst observed socket burst | 24,723 bytes | 23,474 bytes during initial login | Approximately 5% lower; both are transient login/travel bursts |

Candidate totals depend heavily on the players' locations. Forests, large bases, and dungeon zones contain different numbers of ZDOs, so candidate counts should not be treated as a controlled benchmark.

## Observed burst recovery examples

| Event | Burst | Following settled value | Recovery |
|---|---:|---:|---:|
| Initial terrain/dungeon load during optimized testing | 9,164 pending ZDOs | 13 | 99.86% reduction by the following report |
| Large base load | 1,985 pending ZDOs | 10 | 99.50% reduction |
| Later base load | 1,998 pending ZDOs | 9 | 99.55% reduction |
| Socket burst | 19,762 bytes | 312 bytes | 98.42% reduction |
| Current 0.4.5 travel/dungeon load | 743 pending ZDOs | 6 | 99.19% reduction by the following report |
| Current 0.4.5 travel socket load | 11,600 bytes | 314 bytes | 97.29% reduction by the following report |

Current dense warnings while both users are travelling are expected. Examples include:

- Dense forest entry: 6,368 pending objects, dominated by beech trees, rocks, and pickable stones.
- Built-base entry: approximately 1,300-3,253 pending objects, dominated by floors, walls, roofs, stakes, and nearby natural objects.
- Between transitions, current 0.4.5 reports repeatedly return to 4-10 pending objects and roughly 224-500 queued bytes.

## World ZDO count

| Observation | Total ZDOs |
|---|---:|
| Original | 1,027,399 |
| Later | 1,027,501 |
| Current complete-session sample | 1,027,875 |
| Net change | +476 (+0.046%) |

This is not a cleanup metric: TerramizerServer intentionally does not bulk-delete ZDOs. The small increase while players explored and loaded dungeons does not resemble rapid runaway object multiplication.

## New safeguards visible in 0.4.5

- `dense-pressure pauses=12-48` during observed dense intervals: extra Terramizer generation/boost work was paused while existing synchronization drained.
- `zone TTL refreshes=1,561-2,912`: already-loaded zones in the two players' active areas were kept alive. This counts repeated in-memory TTL resets, not unique zones or created objects.
- `sync duplicates removed=128` during an initial connection burst: duplicate outbound references were removed from the send list, not from the world.
- Item ownership diagnostics are split into transfers and authoritative refreshes.
- Sleep fast-forward remained enabled and logged its four-second target.
- Build removal now uses Valheim's exact removal handler and preserves the routed argument/package position.

## Current interpretation

Both connected users were playing and moving during the latest samples. Repeated dungeon-loading messages, zone creation, boosts, and changing candidate totals are therefore consistent with travelling through different active zones. The current logs do not constitute a stationary churn test.

Healthy behavior is demonstrated when each travel/login burst is followed by:

```text
max sync list=4-10
peak socket queue=approximately 224-500 bytes
```

No current log demonstrates broad ZDO deletion, rapidly multiplying ZDOs, or a synchronization backlog that remains permanently high.

## Complete 0.4.5 session audit and 0.4.6 response

The complete 21:04-21:34 log contains 131 `Loading dungeon` events and 30 TerramizerServer status intervals. Across those intervals, TerramizerServer recorded 400 successful full local-zone creations out of 3,350 attempts and 84,383 TTL refresh operations. The repeated dungeon loads therefore occurred while experimental full local peer-zone instantiation was active; they were not accompanied by persistent ZDO growth.

The only exception in the complete log is an `ArgumentNullException` from `ShieldDomeImageEffect.Awake` attempting to create a material with a missing shader on the headless server. There are no TerramizerServer ownership-repair failures, `Double ZNetview` warnings, or successful item ownership interventions in this session.

Version 0.4.6 changes `EnablePeerZoneCreation` to `false` and migrates existing configurations to that value. This stops TerramizerServer's extra full local-zone scene instantiation, while Valheim's normal center-zone loading and per-peer ghost-zone generation continue. Multi-peer object coverage, predictive scanning, bounded ZDO streaming boosts, sleep fast-forward, and narrow build/item ownership recovery remain enabled. The change does not delete, move, regenerate, or edit saved world objects.

Expected 0.4.6 diagnostic signature after migration:

```text
zone creates=0/0
zone TTL refreshes=0
```

`boost queued` may remain nonzero during movement, and vanilla can still log an occasional dungeon load when a player legitimately activates a new zone. The specific TerramizerServer-origin repeated full peer-zone load path should no longer contribute.
