param(
    [ValidateSet("prepare","collect")]
    [string]$Mode = "prepare"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = "G:\My Drive\dev\water"
$ProjectRoot = Join-Path $Root "Valheim\source\PhysicalWater"
$RuntimePath = Join-Path $ProjectRoot "src\PhysicalWaterDevE1Runtime.cs"
$PluginPath = Join-Path $ProjectRoot "src\PhysicalWaterPlugin.cs"
$BuildPath = Join-Path $ProjectRoot "build.ps1"
$InstallPath = Join-Path $ProjectRoot "install.ps1"
$ChangelogPath = Join-Path $ProjectRoot "CHANGELOG.md"

$ValheimDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
$PluginDir = Join-Path $ValheimDir "BepInEx\plugins\PhysicalWater"
$InstalledDll = Join-Path $PluginDir "PhysicalWater.dll"
$InstalledBundle = Join-Path $PluginDir "physicalwater_assets"
$BepInExLog = Join-Path $ValheimDir "BepInEx\LogOutput.log"
$DiagnosticsDir = Join-Path $PluginDir "Diagnostics"

$ExpectedBundleHash = "E346A2F239049564DF97ECC82A720CA54398BA4731E4CA53942454EA4CD55A32"
$Stamp = Get-Date -Format "yyyyMMdd-HHmmss"

function Stop-Run([string]$Message) {
    Write-Host ""
    Write-Host "ERROR: $Message" -ForegroundColor Red
    throw $Message
}

function Require-File([string]$Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        Stop-Run "Required file not found: $Path"
    }
}

function Normalize-Lf([string]$Text) {
    return $Text.Replace("`r`n", "`n")
}

function Write-Preserved([string]$Path, [string]$Normalized, [bool]$HadCrlf) {
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
    $Old = Normalize-Lf $Old
    $New = Normalize-Lf $New
    $AlreadyMarker = Normalize-Lf $AlreadyMarker

    if ($AlreadyMarker -and $Text.Contains($AlreadyMarker)) {
        Write-Host "Already patched: $Description" -ForegroundColor DarkYellow
        return $Text
    }

    $first = $Text.IndexOf($Old, [StringComparison]::Ordinal)
    if ($first -lt 0) {
        Stop-Run "Could not find expected source block for: $Description. Refusing to guess."
    }
    $second = $Text.IndexOf($Old, $first + 1, [StringComparison]::Ordinal)
    if ($second -ge 0) {
        Stop-Run "Expected source block occurred more than once for: $Description. Refusing unsafe patch."
    }

    Write-Host "Patch: $Description" -ForegroundColor Cyan
    return $Text.Substring(0, $first) + $New + $Text.Substring($first + $Old.Length)
}

function Collect-Probes {
    if (Get-Process -Name "valheim" -ErrorAction SilentlyContinue) {
        Stop-Run "Valheim is still running. Close it first so LogOutput.log and the CSV snapshots are complete."
    }

    $ReportDir = Join-Path $Root "UnityPhysicalOceanValidation\ValidationReports\E3LiveFieldProbe-$Stamp"
    New-Item -ItemType Directory -Force -Path $ReportDir | Out-Null

    if (Test-Path -LiteralPath $BepInExLog -PathType Leaf) {
        Copy-Item -LiteralPath $BepInExLog -Destination (Join-Path $ReportDir "LogOutput-E3-field-probe.log") -Force
    }

    $probeFiles = @()
    if (Test-Path -LiteralPath $DiagnosticsDir -PathType Container) {
        $probeFiles = @(Get-ChildItem -LiteralPath $DiagnosticsDir -Filter "E3Probe-*.csv" -File | Sort-Object LastWriteTime)
        foreach ($file in $probeFiles) {
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $ReportDir $file.Name) -Force
        }
    }

    $summaryPath = Join-Path $ReportDir "E3-field-probe-summary.txt"
    $summaryLines = New-Object System.Collections.Generic.List[string]
    $summaryLines.Add("PhysicalWater E3 live field probe collection")
    $summaryLines.Add("============================================")
    $summaryLines.Add("Collected: $(Get-Date -Format o)")
    $summaryLines.Add("CSV snapshots: $($probeFiles.Count)")
    $summaryLines.Add("")

    if (Test-Path -LiteralPath $BepInExLog -PathType Leaf) {
        $matches = @(Select-String -LiteralPath $BepInExLog -Pattern "PW_E3_PROBE summary")
        if ($matches.Count -gt 0) {
            $summaryLines.Add("Probe summaries from live log:")
            foreach ($m in $matches) { $summaryLines.Add($m.Line) }
        } else {
            $summaryLines.Add("No PW_E3_PROBE summary lines were found in the live log.")
        }
    }

    $summaryLines.Add("")
    $summaryLines.Add("Interpretation:")
    $summaryLines.Add("- corrRawTerrain high: authoritative fractional free surface is already terrain-coupled.")
    $summaryLines.Add("- corrRawTerrain low but corrColumnTerrain high: presentation column reconstruction is introducing terrain shape.")
    $summaryLines.Add("- rmseColumnRender high: final persistent render buffer diverges from the column field.")
    $summaryLines.Add("- solid/terrain and raw/terrain deltas in the CSV show the exact affected columns.")

    $summaryLines | Set-Content -LiteralPath $summaryPath -Encoding UTF8

    Write-Host ""
    Write-Host "Collected probe evidence:" -ForegroundColor Green
    Write-Host $ReportDir
    Write-Host ""
    if ($probeFiles.Count -eq 0) {
        Write-Host "WARNING: no E3Probe CSV files were found. The diagnostic candidate may not have been run/fill may not have occurred." -ForegroundColor Yellow
    } else {
        Get-Content -LiteralPath $summaryPath | Select-String "PW_E3_PROBE summary" | ForEach-Object { Write-Host $_.Line }
    }
    return
}

if ($Mode -eq "collect") {
    Collect-Probes
    exit 0
}

# ---------------------------------------------------------------------------
# PREPARE
# ---------------------------------------------------------------------------
foreach ($p in @($RuntimePath, $PluginPath, $BuildPath, $InstallPath, $ChangelogPath)) {
    Require-File $p
}

if (Get-Process -Name "valheim" -ErrorAction SilentlyContinue) {
    Stop-Run "Valheim is currently running. Close it before preparing the diagnostic build."
}

$ReportDir = Join-Path $Root "UnityPhysicalOceanValidation\ValidationReports\E3LiveFieldProbePrepare-$Stamp"
$BackupDir = Join-Path $ReportDir "backup"
$Transcript = Join-Path $ReportDir "prepare-transcript.log"
New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null

Start-Transcript -LiteralPath $Transcript -Force | Out-Null

foreach ($p in @($RuntimePath, $PluginPath, $BuildPath, $InstallPath, $ChangelogPath)) {
    Copy-Item -LiteralPath $p -Destination (Join-Path $BackupDir ([IO.Path]::GetFileName($p))) -Force
}

$runtimeRaw = [IO.File]::ReadAllText($RuntimePath)
$pluginRaw = [IO.File]::ReadAllText($PluginPath)
$buildRaw = [IO.File]::ReadAllText($BuildPath)
$installRaw = [IO.File]::ReadAllText($InstallPath)
$changelogRaw = [IO.File]::ReadAllText($ChangelogPath)

$runtimeCrlf = $runtimeRaw.Contains("`r`n")
$pluginCrlf = $pluginRaw.Contains("`r`n")
$buildCrlf = $buildRaw.Contains("`r`n")
$installCrlf = $installRaw.Contains("`r`n")
$changelogCrlf = $changelogRaw.Contains("`r`n")

$runtime = Normalize-Lf $runtimeRaw
$plugin = Normalize-Lf $pluginRaw
$build = Normalize-Lf $buildRaw
$install = Normalize-Lf $installRaw
$changelog = Normalize-Lf $changelogRaw

# Require either the current devE3.2 base or an already-patched diagnostic source.
if (!$plugin.Contains('PluginAssemblyVersion = "0.6.0.16"') -and
    !$plugin.Contains('PluginAssemblyVersion = "0.6.0.17"')) {
    Stop-Run "Expected devE3.2 assembly 0.6.0.16 (or already-patched 0.6.0.17), but the source is different."
}

# 1) Probe fields.
$old = @'
        private Vector3 _coverageWindowOrigin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        private Bounds _geometryCoverageBounds;
'@
$new = @'
        private Vector3 _coverageWindowOrigin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        private Bounds _geometryCoverageBounds;

        // Diagnostic-only devE3.2 probe. These checkpoints perform blocking GPU
        // readback only four times per explicit fill, never every frame.
        private static readonly float[] LiveFieldProbeCheckpoints = { 2f, 5f, 20f, 60f };
        private int _nextLiveFieldProbeCheckpoint;
'@
$runtime = Replace-ExactOnce $runtime $old $new `
    "add four bounded live-field probe checkpoints" "LiveFieldProbeCheckpoints"

# 2) Controls.
$runtime = Replace-ExactOnce $runtime `
    '            PhysicalWaterPlugin.Log.LogInfo("PhysicalWater devE3 explicit test controls: console pw_e3_* (pw_e1_* aliases retained); F6=create domain, F7=fill 6m cube, F8=clear, F9=status, F10=pause.");' `
    '            PhysicalWaterPlugin.Log.LogInfo("PhysicalWater devE3 explicit test controls: console pw_e3_* (pw_e1_* aliases retained); F6=create domain, F7=fill 6m cube, F8=clear, F9=status, F10=pause, F11=raw/live field probe.");' `
    "advertise F11 probe" "F11=raw/live field probe"

$old = '            if (Input.GetKeyDown(KeyCode.F10)) PauseCommand(null);'
$new = @'
            if (Input.GetKeyDown(KeyCode.F10)) PauseCommand(null);
            if (Input.GetKeyDown(KeyCode.F11)) ProbeCommand(null);
'@
$runtime = Replace-ExactOnce $runtime $old $new `
    "bind F11 to one blocking probe" "KeyCode.F11"

# 3) Auto-probe after simulation/telemetry work.
$old = @'
            if (Time.unscaledTime >= _nextTelemetryTime)
            {
                _nextTelemetryTime = Time.unscaledTime + Mathf.Max(0.5f, PhysicalWaterPlugin.Settings.StageE1TelemetryInterval.Value);
                LogTelemetry();
            }
'@
$new = @'
            if (Time.unscaledTime >= _nextTelemetryTime)
            {
                _nextTelemetryTime = Time.unscaledTime + Mathf.Max(0.5f, PhysicalWaterPlugin.Settings.StageE1TelemetryInterval.Value);
                LogTelemetry();
            }

            MaybeCaptureLiveFieldProbe();
'@
$runtime = Replace-ExactOnce $runtime $old $new `
    "run only the scheduled 2/5/20/60 second probes" "MaybeCaptureLiveFieldProbe();"

# 4) Console commands.
$old = @'
            new Terminal.ConsoleCommand("pw_e1_step", "advance paused E1 simulation by one 1/30 second step", StepCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e3_create_domain", "create finite PhysicalWater E3 streaming window here", CreateDomainCommand, false, false, false, false, true);
'@
$new = @'
            new Terminal.ConsoleCommand("pw_e1_step", "advance paused E1 simulation by one 1/30 second step", StepCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e1_probe", "capture one blocking raw/fraction/presentation field probe", ProbeCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e3_create_domain", "create finite PhysicalWater E3 streaming window here", CreateDomainCommand, false, false, false, false, true);
'@
$runtime = Replace-ExactOnce $runtime $old $new `
    "add legacy-alias probe console command" '"pw_e1_probe"'

$old = @'
            new Terminal.ConsoleCommand("pw_e3_pause", "toggle E3 simulation pause", PauseCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e3_step", "advance paused E3 simulation by one 1/30 second step", StepCommand, false, false, false, false, true);
'@
$new = @'
            new Terminal.ConsoleCommand("pw_e3_pause", "toggle E3 simulation pause", PauseCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e3_step", "advance paused E3 simulation by one 1/30 second step", StepCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e3_probe", "capture one blocking raw/fraction/presentation field probe", ProbeCommand, false, false, false, false, true);
'@
$runtime = Replace-ExactOnce $runtime $old $new `
    "add E3 probe console command" '"pw_e3_probe"'

# 5) Reset checkpoint schedule when domain/fill state changes.
$old = '            _nextTelemetryTime = 0f;'
$new = @'
            _nextTelemetryTime = 0f;
            _nextLiveFieldProbeCheckpoint = 0;
'@
$runtime = Replace-ExactOnce $runtime $old $new `
    "reset probe checkpoints on domain creation" "_nextTelemetryTime = 0f;`n            _nextLiveFieldProbeCheckpoint = 0;"

$old = '            int count = _streaming.SeedWorldBox(requested);'
$new = @'
            int count = _streaming.SeedWorldBox(requested);
            _nextLiveFieldProbeCheckpoint = 0;
'@
$runtime = Replace-ExactOnce $runtime $old $new `
    "reset automatic probe timeline on explicit fill" "SeedWorldBox(requested);`n            _nextLiveFieldProbeCheckpoint = 0;"

# 6) Insert the probe implementation before geometry synchronization.
$anchor = @'
        private void SynchronizeGeometryIfReady()
'@
$probeCode = @'
        private void ProbeCommand(Terminal.ConsoleEventArgs args)
        {
            if (!ReadyForCommand(args, false)) return;
            if (_domain == null || _domain.ParticleCount <= 0)
            {
                Reply(args, "E3 live-field probe requires explicitly spawned fluid.");
                return;
            }

            CaptureLiveFieldProbe(args == null ? "manual-F11" : "manual-console");
            Reply(args, "E3 live-field probe captured. See BepInEx log and PhysicalWater\\Diagnostics CSV.");
        }

        private void MaybeCaptureLiveFieldProbe()
        {
            if (_domain == null || _domain.Paused || _domain.ParticleCount <= 0) return;
            if (_nextLiveFieldProbeCheckpoint >= LiveFieldProbeCheckpoints.Length) return;
            VolumetricWaterDomain mac = _domain.MacDomain;
            if (mac == null) return;

            float simulatedSeconds = mac.Diagnostics.SimulatedSeconds;
            float checkpoint = LiveFieldProbeCheckpoints[_nextLiveFieldProbeCheckpoint];
            if (simulatedSeconds + 0.0001f < checkpoint) return;

            _nextLiveFieldProbeCheckpoint++;
            CaptureLiveFieldProbe("auto-" + checkpoint.ToString("0", CultureInfo.InvariantCulture) + "s");
        }

        private void CaptureLiveFieldProbe(string reason)
        {
            try
            {
                VolumetricWaterDomain mac = _domain.MacDomain;
                VolumetricSurfaceReconstructor surface = _domain.Surface;
                if (mac == null || surface == null || !surface.Ready)
                {
                    PhysicalWaterPlugin.Log.LogWarning("PW_E3_PROBE skipped: MAC/surface not ready.");
                    return;
                }

                float[] fractions = mac.CaptureLiquidFractionsSync();
                float[] solidSdf = mac.CaptureSolidSdfSync();
                float[] columnHeights = surface.CaptureColumnHeightsSync();
                if (fractions.Length != mac.CellCount || solidSdf.Length != mac.CellCount ||
                    columnHeights.Length != surface.SurfaceResolutionX * surface.SurfaceResolutionZ)
                {
                    PhysicalWaterPlugin.Log.LogError(
                        "PW_E3_PROBE failed readback sizes: fractions=" + fractions.Length +
                        ", sdf=" + solidSdf.Length + ", columns=" + columnHeights.Length + ".");
                    return;
                }

                ComputeBuffer currentBuffer = surface.RenderSurfaceVertexBuffer;
                ComputeBuffer previousBuffer = surface.PreviousRenderSurfaceVertexBuffer;
                VolumetricSurfaceReconstructor.SurfaceVertex[] currentVertices = Array.Empty<VolumetricSurfaceReconstructor.SurfaceVertex>();
                VolumetricSurfaceReconstructor.SurfaceVertex[] previousVertices = Array.Empty<VolumetricSurfaceReconstructor.SurfaceVertex>();
                if (currentBuffer != null)
                {
                    currentVertices = new VolumetricSurfaceReconstructor.SurfaceVertex[currentBuffer.count];
                    currentBuffer.GetData(currentVertices);
                }
                if (previousBuffer != null)
                {
                    previousVertices = new VolumetricSurfaceReconstructor.SurfaceVertex[previousBuffer.count];
                    previousBuffer.GetData(previousVertices);
                }

                VolumetricWaterSettings settings = mac.Settings;
                int nx = settings.ResolutionX;
                int ny = settings.ResolutionY;
                int nz = settings.ResolutionZ;
                float dx = settings.CellSize;
                int sx = surface.SurfaceResolutionX;
                int sz = surface.SurfaceResolutionZ;
                float sdx = surface.SurfaceCellSize;
                float alpha = Mathf.Clamp01(_accumulator / (1f / 30f));
                float sim = mac.Diagnostics.SimulatedSeconds;
                Vector3 origin = _domain.WorldOrigin;

                var allRaw = new List<float>();
                var allColumn = new List<float>();
                var allTerrain = new List<float>();
                var allRender = new List<float>();
                var rawTerrainA = new List<float>();
                var rawTerrainB = new List<float>();
                var columnTerrainA = new List<float>();
                var columnTerrainB = new List<float>();
                var rawColumnA = new List<float>();
                var rawColumnB = new List<float>();
                var columnRenderA = new List<float>();
                var columnRenderB = new List<float>();
                var representative = new List<string>();
                var csv = new List<string>(sx * sz + 2)
                {
                    "reason,simSeconds,surfaceX,surfaceZ,worldX,worldZ,terrainWorldY,solidSurfaceWorldY,rawFractionTopWorldY,columnHeightWorldY,renderPreviousWorldY,renderCurrentWorldY,renderInterpolatedWorldY,topLiquidCell,topFraction,rawMinusTerrain,columnMinusRaw,renderMinusColumn"
                };

                int wetColumns = 0;
                for (int z = 0; z < sz; z++)
                for (int x = 0; x < sx; x++)
                {
                    float localX = (x + 0.5f) * sdx;
                    float localZ = (z + 0.5f) * sdx;
                    int liquidX = Mathf.Clamp(Mathf.FloorToInt(localX / dx), 0, nx - 1);
                    int liquidZ = Mathf.Clamp(Mathf.FloorToInt(localZ / dx), 0, nz - 1);

                    int topY = -1;
                    float topFraction = 0f;
                    for (int y = 0; y < ny; y++)
                    {
                        float fraction = Mathf.Clamp01(fractions[ProbeCellIndex(liquidX, y, liquidZ, nx, ny)]);
                        if (fraction <= 0f) continue;
                        topY = y;
                        topFraction = fraction;
                    }
                    if (topY < 0) continue;

                    wetColumns++;
                    float rawLocalY = (topY + topFraction) * dx;
                    float rawWorldY = origin.y + rawLocalY;
                    float columnLocalY = columnHeights[x + sx * z];
                    float columnWorldY = columnLocalY >= 0f ? origin.y + columnLocalY : float.NaN;

                    float worldX = origin.x + localX;
                    float worldZ = origin.z + localZ;
                    float terrainWorldY;
                    bool hasTerrain = Heightmap.GetHeight(
                        new Vector3(worldX, origin.y + _domain.WorldSize.y * 0.5f, worldZ),
                        out terrainWorldY);
                    if (!hasTerrain) terrainWorldY = float.NaN;

                    float solidLocalY = ProbeSolidSurfaceBelow(
                        solidSdf, liquidX, liquidZ, topY, nx, ny, dx);
                    float solidWorldY = ProbeFinite(solidLocalY) ? origin.y + solidLocalY : float.NaN;

                    float renderPreviousLocalY = ProbeRenderColumnY(previousVertices, sx, sz, x, z);
                    float renderCurrentLocalY = ProbeRenderColumnY(currentVertices, sx, sz, x, z);
                    float renderInterpolatedLocalY =
                        ProbeFinite(renderPreviousLocalY) && ProbeFinite(renderCurrentLocalY)
                            ? Mathf.Lerp(renderPreviousLocalY, renderCurrentLocalY, alpha)
                            : renderCurrentLocalY;
                    float renderPreviousWorldY = ProbeFinite(renderPreviousLocalY) ? origin.y + renderPreviousLocalY : float.NaN;
                    float renderCurrentWorldY = ProbeFinite(renderCurrentLocalY) ? origin.y + renderCurrentLocalY : float.NaN;
                    float renderWorldY = ProbeFinite(renderInterpolatedLocalY) ? origin.y + renderInterpolatedLocalY : float.NaN;

                    allRaw.Add(rawWorldY);
                    if (ProbeFinite(columnWorldY)) allColumn.Add(columnWorldY);
                    if (ProbeFinite(terrainWorldY)) allTerrain.Add(terrainWorldY);
                    if (ProbeFinite(renderWorldY)) allRender.Add(renderWorldY);

                    if (ProbeFinite(terrainWorldY))
                    {
                        rawTerrainA.Add(rawWorldY);
                        rawTerrainB.Add(terrainWorldY);
                        if (ProbeFinite(columnWorldY))
                        {
                            columnTerrainA.Add(columnWorldY);
                            columnTerrainB.Add(terrainWorldY);
                        }
                    }
                    if (ProbeFinite(columnWorldY))
                    {
                        rawColumnA.Add(rawWorldY);
                        rawColumnB.Add(columnWorldY);
                        if (ProbeFinite(renderWorldY))
                        {
                            columnRenderA.Add(columnWorldY);
                            columnRenderB.Add(renderWorldY);
                        }
                    }

                    string row =
                        reason + "," + ProbeFmt(sim) + "," + x + "," + z + "," +
                        ProbeFmt(worldX) + "," + ProbeFmt(worldZ) + "," +
                        ProbeFmt(terrainWorldY) + "," + ProbeFmt(solidWorldY) + "," +
                        ProbeFmt(rawWorldY) + "," + ProbeFmt(columnWorldY) + "," +
                        ProbeFmt(renderPreviousWorldY) + "," + ProbeFmt(renderCurrentWorldY) + "," +
                        ProbeFmt(renderWorldY) + "," + topY + "," + ProbeFmt(topFraction) + "," +
                        ProbeFmt(ProbeFinite(terrainWorldY) ? rawWorldY - terrainWorldY : float.NaN) + "," +
                        ProbeFmt(ProbeFinite(columnWorldY) ? columnWorldY - rawWorldY : float.NaN) + "," +
                        ProbeFmt(ProbeFinite(renderWorldY) && ProbeFinite(columnWorldY) ? renderWorldY - columnWorldY : float.NaN);
                    csv.Add(row);

                    representative.Add(
                        "PW_E3_PROBE row sim=" + ProbeFmt(sim) +
                        " x=" + x + " z=" + z +
                        " terrainY=" + ProbeFmt(terrainWorldY) +
                        " solidY=" + ProbeFmt(solidWorldY) +
                        " rawTopY=" + ProbeFmt(rawWorldY) +
                        " columnY=" + ProbeFmt(columnWorldY) +
                        " renderY=" + ProbeFmt(renderWorldY) +
                        " topCell=" + topY +
                        " topFrac=" + ProbeFmt(topFraction));
                }

                string diagnosticsDirectory = Path.Combine(
                    Path.GetDirectoryName(typeof(PhysicalWaterPlugin).Assembly.Location),
                    "Diagnostics");
                Directory.CreateDirectory(diagnosticsDirectory);
                string safeReason = reason.Replace(" ", "_").Replace(":", "_").Replace("/", "_").Replace("\\", "_");
                string csvPath = Path.Combine(
                    diagnosticsDirectory,
                    "E3Probe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
                    "-" + safeReason + "-sim" +
                    sim.ToString("000.00", CultureInfo.InvariantCulture).Replace(".", "_") + ".csv");
                File.WriteAllLines(csvPath, csv);

                float corrRawTerrain = ProbeCorrelation(rawTerrainA, rawTerrainB);
                float corrColumnTerrain = ProbeCorrelation(columnTerrainA, columnTerrainB);
                float corrRawColumn = ProbeCorrelation(rawColumnA, rawColumnB);
                float corrColumnRender = ProbeCorrelation(columnRenderA, columnRenderB);
                float rmseRawColumn = ProbeRmse(rawColumnA, rawColumnB);
                float rmseColumnRender = ProbeRmse(columnRenderA, columnRenderB);

                PhysicalWaterPlugin.Log.LogInfo(
                    "PW_E3_PROBE summary reason=" + reason +
                    " sim=" + ProbeFmt(sim) +
                    " wetColumns=" + wetColumns +
                    " rawStd=" + ProbeFmt(ProbeStdDev(allRaw)) +
                    " columnStd=" + ProbeFmt(ProbeStdDev(allColumn)) +
                    " terrainStd=" + ProbeFmt(ProbeStdDev(allTerrain)) +
                    " renderStd=" + ProbeFmt(ProbeStdDev(allRender)) +
                    " corrRawTerrain=" + ProbeFmt(corrRawTerrain) +
                    " corrColumnTerrain=" + ProbeFmt(corrColumnTerrain) +
                    " corrRawColumn=" + ProbeFmt(corrRawColumn) +
                    " corrColumnRender=" + ProbeFmt(corrColumnRender) +
                    " rmseRawColumn=" + ProbeFmt(rmseRawColumn) +
                    " rmseColumnRender=" + ProbeFmt(rmseColumnRender) +
                    " presentationAlpha=" + ProbeFmt(alpha) +
                    " csv=" + csvPath + ".");

                int rowsToLog = Mathf.Min(25, representative.Count);
                for (int i = 0; i < rowsToLog; i++)
                {
                    int index = rowsToLog <= 1
                        ? 0
                        : Mathf.RoundToInt(i * (representative.Count - 1f) / (rowsToLog - 1f));
                    PhysicalWaterPlugin.Log.LogInfo(representative[index]);
                }
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log.LogError("PW_E3_PROBE exception: " + ex);
            }
        }

        private static int ProbeCellIndex(int x, int y, int z, int nx, int ny)
        {
            return x + nx * (y + ny * z);
        }

        private static float ProbeSolidSurfaceBelow(
            float[] sdf, int x, int z, int topY, int nx, int ny, float dx)
        {
            int upper = Mathf.Clamp(topY, 0, ny - 1);
            for (int y = Mathf.Min(upper, ny - 2); y >= 0; y--)
            {
                float s0 = sdf[ProbeCellIndex(x, y, z, nx, ny)];
                float s1 = sdf[ProbeCellIndex(x, y + 1, z, nx, ny)];
                if (s0 <= 0f && s1 > 0f)
                {
                    float denominator = s1 - s0;
                    float t = Mathf.Abs(denominator) > 1e-6f ? Mathf.Clamp01(-s0 / denominator) : 0.5f;
                    return (y + 0.5f + t) * dx;
                }
            }

            for (int y = upper; y >= 0; y--)
            {
                if (sdf[ProbeCellIndex(x, y, z, nx, ny)] <= 0f)
                    return (y + 0.5f) * dx;
            }
            return float.NaN;
        }

        private static float ProbeRenderColumnY(
            VolumetricSurfaceReconstructor.SurfaceVertex[] vertices,
            int sx,
            int sz,
            int x,
            int z)
        {
            if (vertices == null || vertices.Length == 0 || sx < 2 || sz < 2) return float.NaN;
            int cellX = Mathf.Clamp(x, 0, sx - 2);
            int cellZ = Mathf.Clamp(z, 0, sz - 2);
            int cornerX = x - cellX;
            int cornerZ = z - cellZ;
            int offset;
            if (cornerX == 0 && cornerZ == 0) offset = 0;
            else if (cornerX == 0 && cornerZ == 1) offset = 1;
            else if (cornerX == 1 && cornerZ == 1) offset = 2;
            else offset = 5;

            int index = (cellX + (sx - 1) * cellZ) * 6 + offset;
            if (index < 0 || index >= vertices.Length) return float.NaN;
            return vertices[index].Position.y;
        }

        private static bool ProbeFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string ProbeFmt(float value)
        {
            return ProbeFinite(value)
                ? value.ToString("F6", CultureInfo.InvariantCulture)
                : "NaN";
        }

        private static float ProbeCorrelation(List<float> a, List<float> b)
        {
            int n = Mathf.Min(a.Count, b.Count);
            if (n < 2) return float.NaN;
            double meanA = 0.0, meanB = 0.0;
            for (int i = 0; i < n; i++) { meanA += a[i]; meanB += b[i]; }
            meanA /= n;
            meanB /= n;
            double covariance = 0.0, varianceA = 0.0, varianceB = 0.0;
            for (int i = 0; i < n; i++)
            {
                double da = a[i] - meanA;
                double db = b[i] - meanB;
                covariance += da * db;
                varianceA += da * da;
                varianceB += db * db;
            }
            double denominator = Math.Sqrt(varianceA * varianceB);
            return denominator > 1e-12 ? (float)(covariance / denominator) : float.NaN;
        }

        private static float ProbeStdDev(List<float> values)
        {
            if (values == null || values.Count < 2) return float.NaN;
            double mean = 0.0;
            for (int i = 0; i < values.Count; i++) mean += values[i];
            mean /= values.Count;
            double sum = 0.0;
            for (int i = 0; i < values.Count; i++)
            {
                double d = values[i] - mean;
                sum += d * d;
            }
            return (float)Math.Sqrt(sum / values.Count);
        }

        private static float ProbeRmse(List<float> a, List<float> b)
        {
            int n = Mathf.Min(a.Count, b.Count);
            if (n == 0) return float.NaN;
            double sum = 0.0;
            for (int i = 0; i < n; i++)
            {
                double d = a[i] - b[i];
                sum += d * d;
            }
            return (float)Math.Sqrt(sum / n);
        }

'@

$runtime = Replace-ExactOnce $runtime $anchor ($probeCode + $anchor) `
    "insert raw fraction / SDF / terrain / column / render probe" "PW_E3_PROBE summary reason="

# Version bump: diagnostic identity only, no solver claim.
if ($plugin.Contains('public const string PluginVersion = "0.6.0-devE3.2";')) {
    $plugin = $plugin.Replace(
        'public const string PluginVersion = "0.6.0-devE3.2";',
        'public const string PluginVersion = "0.6.0-devE3.2-probe1";')
}
if ($plugin.Contains('public const string PluginBepInExVersion = "0.6.0.16";')) {
    $plugin = $plugin.Replace(
        'public const string PluginBepInExVersion = "0.6.0.16";',
        'public const string PluginBepInExVersion = "0.6.0.17";')
}
if ($plugin.Contains('public const string PluginAssemblyVersion = "0.6.0.16";')) {
    $plugin = $plugin.Replace(
        'public const string PluginAssemblyVersion = "0.6.0.16";',
        'public const string PluginAssemblyVersion = "0.6.0.17";')
}

if (!$plugin.Contains('public const string PluginAssemblyVersion = "0.6.0.17";')) {
    Stop-Run "Could not establish diagnostic assembly 0.6.0.17."
}

$build = $build.Replace(
    'if ($dllVersion -ne "0.6.0.16") {',
    'if ($dllVersion -ne "0.6.0.17") {')
$build = $build.Replace(
    'throw "Fresh DLL version is $dllVersion, expected 0.6.0.16."',
    'throw "Fresh DLL version is $dllVersion, expected 0.6.0.17."')

if (!$build.Contains('expected 0.6.0.17')) {
    Stop-Run "Could not update build.ps1 guard to 0.6.0.17."
}

$install = $install.Replace(
    'PhysicalWater 0.6.0-devE3.2: fractional free-surface pressure correction on conservative E3 streaming; global replacement, vanilla suppression, player, ship, fish, swimming and buoyancy hooks remain disabled.',
    'PhysicalWater 0.6.0-devE3.2-probe1: diagnostic-only live field probe on the devE3.2 pressure candidate; global replacement, vanilla suppression, player, ship, fish, swimming and buoyancy hooks remain disabled.')

if (!$changelog.Contains("## 0.6.0-devE3.2-probe1")) {
    $entry = @'
## 0.6.0-devE3.2-probe1

- Diagnostic-only candidate. No fluid solver, pressure, APIC/FLIP, SDF, E3 streaming, damping, E2 smoothing, topology, or shader behavior is changed from devE3.2.
- Added four bounded blocking live field snapshots at approximately 2, 5, 20, and 60 simulated seconds after explicit fill, plus `F11` / `pw_e3_probe` for a manual snapshot.
- Each snapshot writes a CSV comparing the authoritative fractional top interface, Valheim Heightmap terrain height, solid-SDF zero crossing below the liquid, final E2 column height, previous/current persistent render vertex height, and the actual presentation-interpolated render height.
- Logs terrain correlations and RMSEs needed to classify the live-only terrain blanket without another speculative physics change.
- This build is evidence-gathering only and must not be marked live PASS from compilation or offline regressions.

'@
    $changelog = $entry + $changelog
}

# Write only after all patch points validated.
Write-Preserved $RuntimePath $runtime $runtimeCrlf
Write-Preserved $PluginPath $plugin $pluginCrlf
Write-Preserved $BuildPath $build $buildCrlf
Write-Preserved $InstallPath $install $installCrlf
Write-Preserved $ChangelogPath $changelog $changelogCrlf

Write-Host ""
Write-Host "Diagnostic source patch applied. Physics/shaders were not edited." -ForegroundColor Green

# Archive old probe CSVs so the next collection belongs to one run.
if (Test-Path -LiteralPath $DiagnosticsDir -PathType Container) {
    $oldProbes = @(Get-ChildItem -LiteralPath $DiagnosticsDir -Filter "E3Probe-*.csv" -File)
    if ($oldProbes.Count -gt 0) {
        $archive = Join-Path $DiagnosticsDir ("Archive-" + $Stamp)
        New-Item -ItemType Directory -Force -Path $archive | Out-Null
        foreach ($f in $oldProbes) { Move-Item -LiteralPath $f.FullName -Destination $archive -Force }
    }
}

# Provenance check before build.
$BundlePath = Join-Path $ProjectRoot "dist\physicalwater_assets"
Require-File $BundlePath
$bundleHashBefore = (Get-FileHash -Algorithm SHA256 -LiteralPath $BundlePath).Hash
Write-Host "Current devE3.2 AssetBundle SHA256: $bundleHashBefore"
if ($bundleHashBefore -ne $ExpectedBundleHash) {
    Stop-Run "The current AssetBundle does not match the documented devE3.2 bundle hash. This diagnostic must not silently change GPU provenance."
}

Write-Host ""
Write-Host "Building diagnostic assembly 0.6.0.17..." -ForegroundColor Cyan
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $BuildPath
if ($LASTEXITCODE -ne 0) {
    Stop-Run "build.ps1 returned exit code $LASTEXITCODE"
}

$DllPath = Join-Path $ProjectRoot "dist\PhysicalWater.dll"
Require-File $DllPath
$dllVersion = ([Reflection.AssemblyName]::GetAssemblyName($DllPath)).Version.ToString()
$dllHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $DllPath).Hash
$bundleHashAfter = (Get-FileHash -Algorithm SHA256 -LiteralPath $BundlePath).Hash

if ($dllVersion -ne "0.6.0.17") {
    Stop-Run "Build produced DLL $dllVersion, expected 0.6.0.17."
}
if ($bundleHashAfter -ne $bundleHashBefore) {
    Stop-Run "AssetBundle changed during a DLL-only diagnostic build. Refusing to install."
}

Write-Host ""
Write-Host "BUILD PASS" -ForegroundColor Green
Write-Host "DLL version: $dllVersion"
Write-Host "DLL SHA256: $dllHash"
Write-Host "AssetBundle unchanged: $bundleHashAfter"

$summary = Join-Path $ReportDir "prepare-summary.txt"
@"
PhysicalWater E3 Live Field Probe
=================================
Candidate: 0.6.0-devE3.2-probe1
Assembly: $dllVersion
DLL SHA256: $dllHash
AssetBundle SHA256: $bundleHashAfter

Purpose:
Evidence only. Determine where the live Valheim terrain shape first enters:
authoritative fraction -> solid/terrain -> E2 column -> persistent render buffer.

Automatic snapshots:
2s
5s
20s
60s

Manual snapshot:
F11
or console: pw_e3_probe

No solver/shader/damping/geometry-streaming behavior was modified.

Backups:
$BackupDir
Transcript:
$Transcript
"@ | Set-Content -LiteralPath $summary -Encoding UTF8

Write-Host ""
$answer = Read-Host "Install this diagnostic candidate into Valheim with E3 enabled? (Y/N)"
if ($answer -match '^[Yy]$') {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $InstallPath -EnableStageE3
    if ($LASTEXITCODE -ne 0) {
        Stop-Run "install.ps1 failed with exit code $LASTEXITCODE"
    }

    Require-File $InstalledDll
    Require-File $InstalledBundle
    $installedDllHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $InstalledDll).Hash
    $installedBundleHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $InstalledBundle).Hash
    if ($installedDllHash -ne $dllHash) {
        Stop-Run "Installed DLL hash does not match the built diagnostic DLL."
    }
    if ($installedBundleHash -ne $bundleHashAfter) {
        Stop-Run "Installed AssetBundle hash does not match the proven devE3.2 bundle."
    }

    Write-Host ""
    Write-Host "INSTALL VERIFIED" -ForegroundColor Green
    Write-Host "DLL:    $installedDllHash"
    Write-Host "Bundle: $installedBundleHash"
    Write-Host ""
    Write-Host "One focused run:" -ForegroundColor Cyan
    Write-Host "  F6 -> wait for geometry ready -> F7"
    Write-Host "  Leave it running about 65 simulated seconds."
    Write-Host "  The 2/5/20/60s probes happen automatically."
    Write-Host "  F11 is optional for an extra snapshot."
    Write-Host "  Close Valheim, then run this BAT again and choose option 2."
} else {
    Write-Host "Installation skipped. Diagnostic source/build remains prepared." -ForegroundColor Yellow
}

Stop-Transcript | Out-Null
exit 0
