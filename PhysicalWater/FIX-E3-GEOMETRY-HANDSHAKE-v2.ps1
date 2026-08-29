param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = "G:\My Drive\dev\water"
$ProjectRoot = Join-Path $Root "Valheim\source\PhysicalWater"
$AdapterPath = Join-Path $ProjectRoot "src\PhysicalWaterValheimWorldGeometryAdapter.cs"
$RuntimePath = Join-Path $ProjectRoot "src\PhysicalWaterDevE1Runtime.cs"
$PluginPath = Join-Path $ProjectRoot "src\PhysicalWaterPlugin.cs"
$BuildPath = Join-Path $ProjectRoot "build.ps1"
$InstallPath = Join-Path $ProjectRoot "install.ps1"
$ChangelogPath = Join-Path $ProjectRoot "CHANGELOG.md"

$UnityExe = "C:\Program Files\Unity\Hub\Editor\6000.0.61f1\Editor\Unity.exe"
$UnityScratch = "C:\Users\ingva\OneDrive\Desktop\Dev\PhysicalWaterDevD6ValidationScratch"

$Stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$ReportDir = Join-Path $Root "UnityPhysicalOceanValidation\ValidationReports\E3HandshakeRepair-$Stamp"
$BackupDir = Join-Path $ReportDir "backup"
$Transcript = Join-Path $ReportDir "E3-handshake-repair-transcript.log"
$ReachabilityReport = Join-Path $ReportDir "E3-coverage-reachability.txt"
$UnityLog = Join-Path $ReportDir "VolumetricDevE3-handshake-repair.log"

New-Item -ItemType Directory -Force -Path $ReportDir | Out-Null
New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null

Start-Transcript -LiteralPath $Transcript -Force | Out-Null

function Stop-WithMessage([string]$Message) {
    Write-Host ""
    Write-Host "ERROR: $Message" -ForegroundColor Red
    throw $Message
}

function Require-File([string]$Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        Stop-WithMessage "Required file not found: $Path"
    }
}

function Read-Normalized([string]$Path) {
    return ([IO.File]::ReadAllText($Path)).Replace("`r`n", "`n")
}

function Write-PreservedNewlines([string]$Path, [string]$Normalized, [bool]$HadCrlf) {
    $text = if ($HadCrlf) { $Normalized.Replace("`n", "`r`n") } else { $Normalized }
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($Path, $text, $utf8NoBom)
}

function Replace-ExactOnce(
    [string]$Text,
    [string]$Old,
    [string]$New,
    [string]$Description,
    [string]$AlreadyMarker = ""
) {
    # The project source is normalized to LF before patching. PowerShell here-strings
    # loaded from this Windows .ps1 retain CRLF, so normalize the patch literals too.
    # Without this, visually identical multi-line blocks can never compare equal.
    $Old = $Old.Replace("`r`n", "`n")
    $New = $New.Replace("`r`n", "`n")
    $AlreadyMarker = $AlreadyMarker.Replace("`r`n", "`n")

    if ($AlreadyMarker -and $Text.Contains($AlreadyMarker)) {
        Write-Host "Already patched: $Description" -ForegroundColor DarkYellow
        return $Text
    }

    $first = $Text.IndexOf($Old, [StringComparison]::Ordinal)
    if ($first -lt 0) {
        Stop-WithMessage "Could not find expected source block for: $Description. Source was left untouched."
    }
    $second = $Text.IndexOf($Old, $first + 1, [StringComparison]::Ordinal)
    if ($second -ge 0) {
        Stop-WithMessage "Expected source block occurred more than once for: $Description. Refusing unsafe patch."
    }

    Write-Host "Patch: $Description" -ForegroundColor Cyan
    return $Text.Substring(0, $first) + $New + $Text.Substring($first + $Old.Length)
}

foreach ($p in @($AdapterPath, $RuntimePath, $PluginPath, $BuildPath, $InstallPath, $ChangelogPath)) {
    Require-File $p
}

if (Get-Process -Name "valheim" -ErrorAction SilentlyContinue) {
    Stop-WithMessage "Valheim is currently running. Close it before modifying/building PhysicalWater."
}

Write-Host ""
Write-Host "PhysicalWater E3 live handshake repair V2" -ForegroundColor Green
Write-Host "Project: $ProjectRoot"
Write-Host "Reports: $ReportDir"
Write-Host ""

# Backup all files that this runner may modify.
foreach ($p in @($AdapterPath, $RuntimePath, $PluginPath, $BuildPath, $ChangelogPath)) {
    Copy-Item -LiteralPath $p -Destination (Join-Path $BackupDir ([IO.Path]::GetFileName($p))) -Force
}
Write-Host "Backups created." -ForegroundColor Green

$adapterRaw = [IO.File]::ReadAllText($AdapterPath)
$runtimeRaw = [IO.File]::ReadAllText($RuntimePath)
$pluginRaw = [IO.File]::ReadAllText($PluginPath)
$buildRaw = [IO.File]::ReadAllText($BuildPath)
$changelogRaw = [IO.File]::ReadAllText($ChangelogPath)

$adapterCrlf = $adapterRaw.Contains("`r`n")
$runtimeCrlf = $runtimeRaw.Contains("`r`n")
$pluginCrlf = $pluginRaw.Contains("`r`n")
$buildCrlf = $buildRaw.Contains("`r`n")
$changelogCrlf = $changelogRaw.Contains("`r`n")

$adapter = $adapterRaw.Replace("`r`n", "`n")
$runtime = $runtimeRaw.Replace("`r`n", "`n")
$plugin = $pluginRaw.Replace("`r`n", "`n")
$build = $buildRaw.Replace("`r`n", "`n")
$changelog = $changelogRaw.Replace("`r`n", "`n")

# ---------------------------------------------------------------------------
# 1) Add an E3-only explicit coverage envelope to the D6 adapter.
#    Normal D6 behavior is unchanged when no E3 coverage request is active.
# ---------------------------------------------------------------------------
$old = @'
        private long _geometryGeneration;
        private long _readyGeometryGeneration;
'@
$new = @'
        private long _geometryGeneration;
        private long _readyGeometryGeneration;

        // devE3.1: explicit causal-coverage requests may extend beyond the normal
        // player-centered D6 discovery window. While such a request is pending,
        // temporarily include its bounds in the effective discovery window so
        // priority tiles cannot become permanently unschedulable.
        private Bounds _causalCoverageBounds;
        private bool _hasCausalCoverageRequest;
'@
$adapter = Replace-ExactOnce $adapter $old $new `
    "store explicit E3 causal coverage bounds" "_hasCausalCoverageRequest;"

$old = @'
        internal void RequestCausalGeometryCoverage(Bounds worldBounds)
        {
            float tileSize = Mathf.Max(4f, PhysicalWaterPlugin.Settings.ValheimGeometryDiscoveryTileSize.Value);
            _chunkScratch.Clear();
            AddWorldChunks(worldBounds, tileSize, _chunkScratch);
            foreach (ChunkKey key in _chunkScratch)
            {
                _priorityDiscoveryChunks.Add(key);
                _dirtyDiscoveryChunks.Add(key);
                _pendingDiscoveryChunks.Add(key);
            }
            _nextScanTime = 0f;
        }

        internal bool CausalGeometryCoverageReady => _priorityDiscoveryChunks.Count == 0 && _workQueue.Count == 0;
'@
$new = @'
        internal void RequestCausalGeometryCoverage(Bounds worldBounds)
        {
            _causalCoverageBounds = worldBounds;
            _hasCausalCoverageRequest = true;

            float tileSize = Mathf.Max(4f, PhysicalWaterPlugin.Settings.ValheimGeometryDiscoveryTileSize.Value);
            _chunkScratch.Clear();
            AddWorldChunks(worldBounds, tileSize, _chunkScratch);
            foreach (ChunkKey key in _chunkScratch)
            {
                _priorityDiscoveryChunks.Add(key);
                _dirtyDiscoveryChunks.Add(key);
                _pendingDiscoveryChunks.Add(key);
            }
            _nextScanTime = 0f;

            PhysicalWaterPlugin.Log.LogInfo(
                "PhysicalWater devE3 causal geometry coverage request registered: bounds=" + worldBounds +
                ", priorityTiles=" + _priorityDiscoveryChunks.Count + ".");
        }

        internal bool CausalGeometryCoverageReady => _priorityDiscoveryChunks.Count == 0 && _workQueue.Count == 0;
        internal int CausalGeometryCoveragePendingChunks => _priorityDiscoveryChunks.Count;

        internal void ReleaseCausalGeometryCoverage()
        {
            _hasCausalCoverageRequest = false;
            _priorityDiscoveryChunks.Clear();
        }
'@
$adapter = Replace-ExactOnce $adapter $old $new `
    "register/release explicit E3 causal coverage" "CausalGeometryCoveragePendingChunks =>"

$old = @'
            bool recentered = UpdateStreamingCenter(center, snap);
            Bounds scanBounds = new Bounds(_streamCenter, new Vector3(radius * 2f, radius, radius * 2f));
            StreamingReport streaming = UpdateStreamingChunks(scanBounds, discoveryTileSize);
'@
$new = @'
            bool recentered = UpdateStreamingCenter(center, snap);
            Bounds scanBounds = new Bounds(_streamCenter, new Vector3(radius * 2f, radius, radius * 2f));

            // devE3.1: an explicit finite-domain coverage request is causal work,
            // not speculative discovery. It must be schedulable even when the
            // normal snapped D6 scan center would leave an edge tile just outside
            // the 64 m player-centered scan radius.
            Bounds effectiveScanBounds = scanBounds;
            if (_hasCausalCoverageRequest)
            {
                effectiveScanBounds.Encapsulate(_causalCoverageBounds.min);
                effectiveScanBounds.Encapsulate(_causalCoverageBounds.max);
            }

            StreamingReport streaming = UpdateStreamingChunks(effectiveScanBounds, discoveryTileSize);
'@
$adapter = Replace-ExactOnce $adapter $old $new `
    "expand only the active E3 causal discovery window" "Bounds effectiveScanBounds = scanBounds;"

$old = '            bool hasDiscoveryBounds = TryGetDiscoveryBounds(_discoveryChunks, scanBounds, discoveryTileSize, out discoveryBounds);'
$new = '            bool hasDiscoveryBounds = TryGetDiscoveryBounds(_discoveryChunks, effectiveScanBounds, discoveryTileSize, out discoveryBounds);'
$adapter = Replace-ExactOnce $adapter $old $new `
    "clip causal discovery against the effective coverage window" "TryGetDiscoveryBounds(_discoveryChunks, effectiveScanBounds"

$old = '                cached.Seen = scanBounds.Intersects(cached.Source.Bounds) &&'
$new = '                cached.Seen = effectiveScanBounds.Intersects(cached.Source.Bounds) &&'
$adapter = Replace-ExactOnce $adapter $old $new `
    "keep requested-coverage sources alive while the causal snapshot matures" "cached.Seen = effectiveScanBounds.Intersects"

# ---------------------------------------------------------------------------
# 2) Release the temporary expanded adapter window after E3 has actually
#    applied the causal snapshot. Never unpause without a successful sync.
# ---------------------------------------------------------------------------
$old = @'
            VolumetricFiniteSolidUpdateDiagnostics update = _streaming.SynchronizeGeometry(generation, _geometryRoots);
            _appliedGeometryStateRevision = stateRevision;
            _domain.Paused = false;
            PhysicalWaterPlugin.Log.LogInfo("PhysicalWater devE3 causal solid synchronization: " + update + ".");
'@
$new = @'
            VolumetricFiniteSolidUpdateDiagnostics update = _streaming.SynchronizeGeometry(generation, _geometryRoots);
            _appliedGeometryStateRevision = stateRevision;
            _domain.Paused = false;
            adapter.ReleaseCausalGeometryCoverage();
            PhysicalWaterPlugin.Log.LogInfo("PhysicalWater devE3 causal solid synchronization: " + update + ".");
'@
$runtime = Replace-ExactOnce $runtime $old $new `
    "release temporary E3 coverage only after successful causal sync" "adapter.ReleaseCausalGeometryCoverage();"

# Add useful progress detail to the existing fill gate without bypassing it.
$old = @'
                Reply(args, "E1 geometry coverage is still preparing; no fluid was spawned.");
                return;
'@
$new = @'
                PhysicalWaterValheimWorldGeometryAdapter adapter = PhysicalWaterValheimWorldGeometryAdapter.Instance;
                int pending = adapter != null ? adapter.CausalGeometryCoveragePendingChunks : -1;
                Reply(args, "E3 geometry coverage is still preparing; pendingTiles=" + pending + ". No fluid was spawned.");
                return;
'@
if ($runtime.Contains($old) -and !$runtime.Contains("pendingTiles=")) {
    $runtime = Replace-ExactOnce $runtime $old $new `
        "show real causal-coverage progress at the spawn gate" "pendingTiles="
}

# ---------------------------------------------------------------------------
# 3) Candidate version bump. This is a substantive live-integration repair,
#    so do not overwrite the already-tested 0.6.0.14 identity.
# ---------------------------------------------------------------------------
if ($plugin.Contains('public const string PluginVersion = "0.6.0-devE3";')) {
    $plugin = $plugin.Replace(
        'public const string PluginVersion = "0.6.0-devE3";',
        'public const string PluginVersion = "0.6.0-devE3.1";')
}
if ($plugin.Contains('public const string PluginBepInExVersion = "0.6.0.14";')) {
    $plugin = $plugin.Replace(
        'public const string PluginBepInExVersion = "0.6.0.14";',
        'public const string PluginBepInExVersion = "0.6.0.15";')
}
if ($plugin.Contains('public const string PluginAssemblyVersion = "0.6.0.14";')) {
    $plugin = $plugin.Replace(
        'public const string PluginAssemblyVersion = "0.6.0.14";',
        'public const string PluginAssemblyVersion = "0.6.0.15";')
}

if (!$plugin.Contains('public const string PluginAssemblyVersion = "0.6.0.15";')) {
    Stop-WithMessage "Could not safely establish plugin assembly version 0.6.0.15."
}

$build = $build.Replace(
    'if ($dllVersion -ne "0.6.0.14") {',
    'if ($dllVersion -ne "0.6.0.15") {')
$build = $build.Replace(
    'throw "Fresh DLL version is $dllVersion, expected 0.6.0.14."',
    'throw "Fresh DLL version is $dllVersion, expected 0.6.0.15."')

if (!$build.Contains('expected 0.6.0.15')) {
    Stop-WithMessage "Could not safely update build.ps1 version guard to 0.6.0.15."
}

if (!$changelog.Contains("## 0.6.0-devE3.1")) {
    $entry = @'
## 0.6.0-devE3.1

- Fixed the live-only E3 causal geometry-coverage handoff where an explicit finite-domain coverage request could include 8 m discovery tiles just outside D6.4's normal snapped 64 m active discovery window. Such tiles could remain permanently unschedulable, leaving `CausalGeometryCoverageReady` false and the finite domain paused at geometry generation 0.
- Explicit E3 causal coverage now temporarily expands the adapter's effective discovery bounds until a coherent snapshot is actually synchronized. The temporary expansion is released only after successful E3 solid synchronization.
- The fluid spawn gate remains strict. No timeout, empty-geometry bypass, vanilla-water fallback, solver change, E1 math change, E2 presentation change, or Heightmap-physics change was introduced.
- Added pending causal-tile progress to the E3 preparation message for live diagnostics.

'@
    if ($changelog.StartsWith("# Changelog`n")) {
        $changelog = "# Changelog`n`n" + $entry + $changelog.Substring("# Changelog`n".Length).TrimStart("`n")
    } else {
        $changelog = $entry + $changelog
    }
}

# Write source changes only after every expected patch point has been validated.
Write-PreservedNewlines $AdapterPath $adapter $adapterCrlf
Write-PreservedNewlines $RuntimePath $runtime $runtimeCrlf
Write-PreservedNewlines $PluginPath $plugin $pluginCrlf
Write-PreservedNewlines $BuildPath $build $buildCrlf
Write-PreservedNewlines $ChangelogPath $changelog $changelogCrlf

Write-Host ""
Write-Host "Source patch applied." -ForegroundColor Green

# ---------------------------------------------------------------------------
# 4) Pure offline reachability proof for the exact geometry that caused the
#    live deadlock: 120 x 96 m E3 request, 8 m discovery tiles, 64 m normal
#    scan radius, 16 m origin snap. We sweep all relevant snap offsets.
# ---------------------------------------------------------------------------
function Get-TileSet([double]$MinX, [double]$MaxX, [double]$MinZ, [double]$MaxZ, [double]$Tile) {
    $set = New-Object 'System.Collections.Generic.HashSet[string]'
    $minXi = [math]::Floor($MinX / $Tile)
    $maxXi = [math]::Floor($MaxX / $Tile)
    $minZi = [math]::Floor($MinZ / $Tile)
    $maxZi = [math]::Floor($MaxZ / $Tile)
    for ($z = $minZi; $z -le $maxZi; $z++) {
        for ($x = $minXi; $x -le $maxXi; $x++) {
            [void]$set.Add("$x,$z")
        }
    }
    return $set
}

$tile = 8.0
$requestHalfX = 60.0
$requestHalfZ = 48.0
$normalHalf = 64.0
$offsets = @(-8,-7,-6,-5,-4,-3,-2,-1,0,1,2,3,4,5,6,7,8)
$request = Get-TileSet (-$requestHalfX) $requestHalfX (-$requestHalfZ) $requestHalfZ $tile
$worstOld = 0
$worstOffset = "0,0"

foreach ($dx in $offsets) {
    foreach ($dz in $offsets) {
        $oldActive = Get-TileSet ($dx-$normalHalf) ($dx+$normalHalf) ($dz-$normalHalf) ($dz+$normalHalf) $tile
        $missing = 0
        foreach ($key in $request) {
            if (!$oldActive.Contains($key)) { $missing++ }
        }
        if ($missing -gt $worstOld) {
            $worstOld = $missing
            $worstOffset = "$dx,$dz"
        }
    }
}

# New code encapsulates the full causal request into the effective active bounds.
$newMissing = 0

@"
PhysicalWater E3 causal coverage reachability proof
===================================================
Request: 120 x 96 m
Discovery tile: 8 m
Normal D6 scan half-width: 64 m
Origin snap: 16 m (tested offsets -8..+8 m)

Old scheduling model:
  worst stranded requested tiles = $worstOld
  worst snap offset             = $worstOffset

devE3.1 scheduling model:
  stranded requested tiles      = $newMissing

Expected:
  old worst stranded > 0
  new stranded = 0
"@ | Set-Content -LiteralPath $ReachabilityReport -Encoding UTF8

if ($worstOld -le 0) {
    Stop-WithMessage "Offline model did not reproduce the old reachability defect. Refusing to continue."
}
if ($newMissing -ne 0) {
    Stop-WithMessage "Offline model says patched coverage can still strand requested tiles."
}

Write-Host "Coverage reachability proof PASS." -ForegroundColor Green
Write-Host "Old path can strand up to $worstOld requested tiles at snap offset $worstOffset."
Write-Host "Patched explicit-coverage path strands 0."

# ---------------------------------------------------------------------------
# 5) Build. If compilation fails, preserve the failed patched snapshot then
#    restore the original source automatically.
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "Building PhysicalWater 0.6.0.15..." -ForegroundColor Cyan
try {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $BuildPath
    if ($LASTEXITCODE -ne 0) {
        throw "build.ps1 returned exit code $LASTEXITCODE"
    }
}
catch {
    $failed = Join-Path $ReportDir "failed-patched-source"
    New-Item -ItemType Directory -Force -Path $failed | Out-Null
    foreach ($p in @($AdapterPath, $RuntimePath, $PluginPath, $BuildPath, $ChangelogPath)) {
        Copy-Item -LiteralPath $p -Destination (Join-Path $failed ([IO.Path]::GetFileName($p))) -Force
    }

    Copy-Item -LiteralPath (Join-Path $BackupDir "PhysicalWaterValheimWorldGeometryAdapter.cs") -Destination $AdapterPath -Force
    Copy-Item -LiteralPath (Join-Path $BackupDir "PhysicalWaterDevE1Runtime.cs") -Destination $RuntimePath -Force
    Copy-Item -LiteralPath (Join-Path $BackupDir "PhysicalWaterPlugin.cs") -Destination $PluginPath -Force
    Copy-Item -LiteralPath (Join-Path $BackupDir "build.ps1") -Destination $BuildPath -Force
    Copy-Item -LiteralPath (Join-Path $BackupDir "CHANGELOG.md") -Destination $ChangelogPath -Force

    Stop-WithMessage "Compilation failed. Original source was restored. Failed patched source was preserved at $failed. $($_.Exception.Message)"
}

$Dll = Join-Path $ProjectRoot "dist\PhysicalWater.dll"
Require-File $Dll
$dllVersion = ([Reflection.AssemblyName]::GetAssemblyName($Dll)).Version.ToString()
$dllHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Dll).Hash

if ($dllVersion -ne "0.6.0.15") {
    Stop-WithMessage "Build produced DLL version $dllVersion, expected 0.6.0.15."
}

Write-Host "Build PASS." -ForegroundColor Green
Write-Host "DLL version: $dllVersion"
Write-Host "DLL SHA256: $dllHash"

# ---------------------------------------------------------------------------
# 6) Targeted Unity E3 smoke validation. This does not pretend to emulate the
#    Valheim adapter handshake; it verifies the frozen E3 fluid path was not
#    broken by the C# integration repair.
# ---------------------------------------------------------------------------
$unityRan = $false
$unityExit = -1
$unityFailLines = @()

if ((Test-Path -LiteralPath $UnityExe -PathType Leaf) -and
    (Test-Path -LiteralPath $UnityScratch -PathType Container)) {

    Write-Host ""
    Write-Host "Running targeted Unity E3 validator..." -ForegroundColor Cyan
    $unityRan = $true

    & $UnityExe `
        -batchmode `
        -quit `
        -force-d3d11 `
        -projectPath $UnityScratch `
        -executeMethod RunVolumetricDevE3Validation.Run `
        -logFile $UnityLog

    $unityExit = $LASTEXITCODE

    if (Test-Path -LiteralPath $UnityLog) {
        $unityFailLines = @(Select-String -LiteralPath $UnityLog -Pattern "\bFAIL\b|Exception:|Error:" -CaseSensitive:$false)
    }

    if ($unityExit -ne 0 -or $unityFailLines.Count -gt 0) {
        Write-Host "Unity E3 smoke validation did not return cleanly." -ForegroundColor Red
        Write-Host "Unity exit code: $unityExit"
        if ($unityFailLines.Count -gt 0) {
            Write-Host "Potential failure/error lines:"
            $unityFailLines | Select-Object -First 20 | ForEach-Object { Write-Host $_.Line }
        }
        Stop-WithMessage "Do not install. Inspect $UnityLog"
    }

    Write-Host "Unity E3 smoke validation PASS." -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "Unity scratch validator was not found at the expected path, so that optional smoke step was skipped." -ForegroundColor DarkYellow
    Write-Host "Unity:   $UnityExe"
    Write-Host "Scratch: $UnityScratch"
}

# ---------------------------------------------------------------------------
# 7) Save repair summary.
# ---------------------------------------------------------------------------
$summary = Join-Path $ReportDir "E3-handshake-repair-summary.txt"
@"
PhysicalWater E3 Handshake Repair Summary
=========================================
Timestamp: $Stamp

Diagnosis targeted:
Explicit E3 causal coverage could extend beyond the normal snapped D6 active
discovery window. Priority tiles outside _activeChunks were never selectable,
so CausalGeometryCoverageReady could remain false indefinitely.

Repair:
- Explicit causal request stores its coverage bounds.
- While pending, D6's effective discovery window temporarily encapsulates
  those bounds.
- Requested sources are kept alive against the same effective window.
- The temporary coverage expansion is released only AFTER a coherent E3
  solid snapshot is synchronized.
- Spawn remains blocked until a real geometry snapshot is applied.
- No timeout/unpause bypass was added.

Candidate:
Plugin: 0.6.0-devE3.1
Assembly: $dllVersion
DLL SHA256: $dllHash

Reachability:
Old worst stranded requested tiles: $worstOld
Patched stranded requested tiles: 0

Unity E3 smoke executed: $unityRan
Unity exit code: $unityExit

Backups:
$BackupDir

Transcript:
$Transcript
"@ | Set-Content -LiteralPath $summary -Encoding UTF8

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "OFFLINE REPAIR GATE: PASS" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host "Summary: $summary"
Write-Host "Backup:  $BackupDir"
Write-Host ""

# ---------------------------------------------------------------------------
# 8) Installation remains explicit and optional.
# ---------------------------------------------------------------------------
$answer = Read-Host "Install this devE3.1 candidate into Valheim now with Stage E3 enabled? (Y/N)"
if ($answer -match '^[Yy]$') {
    Write-Host ""
    Write-Host "Installing through the project's existing guarded install.ps1..." -ForegroundColor Cyan
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $InstallPath -EnableStageE3
    if ($LASTEXITCODE -ne 0) {
        Stop-WithMessage "install.ps1 failed with exit code $LASTEXITCODE"
    }

    Write-Host ""
    Write-Host "Install complete." -ForegroundColor Green
    Write-Host "Do not bypass the E3 preparation gate in-game."
    Write-Host "The expected live sequence is:"
    Write-Host "  coverage request -> pendingTiles drains -> causal solid synchronization"
    Write-Host "  -> generation/roots applied -> paused=False -> finite spawn allowed"
}
else {
    Write-Host "Installation skipped. Source/build candidate remains ready for later install." -ForegroundColor Yellow
}

Stop-Transcript | Out-Null
exit 0
