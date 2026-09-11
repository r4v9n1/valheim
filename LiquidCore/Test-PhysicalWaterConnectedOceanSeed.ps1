param(
    [string]$DllPath = "$env:LOCALAPPDATA\R4V9N1\LiquidCore\dist\LiquidCore.dll"
)

$ErrorActionPreference = "Stop"
if (!(Test-Path -LiteralPath $DllPath -PathType Leaf)) {
    throw "Compiled LiquidCore production DLL not found: $DllPath"
}

$valheimManaged = "C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed"
[AppDomain]::CurrentDomain.add_AssemblyResolve({
    param($sender, $args)
    $name = ([Reflection.AssemblyName]$args.Name).Name + ".dll"
    $candidate = Join-Path $valheimManaged $name
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        return [Reflection.Assembly]::LoadFrom($candidate)
    }
    return $null
})

foreach ($dependency in @(
    "UnityEngine.CoreModule.dll",
    "UnityEngine.dll",
    "UnityEngine.PhysicsModule.dll",
    "assembly_valheim.dll"
)) {
    $dependencyPath = Join-Path $valheimManaged $dependency
    if (!(Test-Path -LiteralPath $dependencyPath -PathType Leaf)) {
        throw "Required Valheim managed dependency not found: $dependencyPath"
    }
    try { [Reflection.Assembly]::LoadFrom($dependencyPath) | Out-Null } catch { }
}

$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $DllPath).Path)
$type = $assembly.GetType("PhysicalWater.PhysicalWaterSystem", $true)
$method = $type.GetMethod("ShouldSeedConnectedOceanCell", [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::NonPublic)
if ($method -eq $null) {
    throw "Production connected-ocean seed predicate is missing."
}

function Invoke-SeedPredicate([bool]$candidateFluid, [bool]$solidCell, [bool]$connectedOceanWet) {
    return [bool]$method.Invoke($null, @($candidateFluid, $solidCell, $connectedOceanWet))
}

$cases = @(
    @{ Name = "interior connected ocean cell"; Candidate = $true; Solid = $false; DomainWet = $true; Expected = $true },
    @{ Name = "solid connected-domain cell"; Candidate = $true; Solid = $true; DomainWet = $true; Expected = $false },
    @{ Name = "dry candidate cell"; Candidate = $true; Solid = $false; DomainWet = $false; Expected = $false },
    @{ Name = "non-fluid connected-domain cell"; Candidate = $false; Solid = $false; DomainWet = $true; Expected = $false }
)

foreach ($case in $cases) {
    $actual = Invoke-SeedPredicate $case.Candidate $case.Solid $case.DomainWet
    if ($actual -ne $case.Expected) {
        throw "Connected-ocean seed regression failed for '$($case.Name)': expected=$($case.Expected), actual=$actual"
    }
    Write-Output "PASS $($case.Name): $actual"
}

Write-Output "CONNECTED-OCEAN SEED PRODUCTION REGRESSION: PASS"
