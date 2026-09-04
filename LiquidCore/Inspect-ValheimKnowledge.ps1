param(
    [string]$Query = "",
    [string]$DatabasePath = (Join-Path $PSScriptRoot "knowledge\valheim-knowledge-v1.json")
)

$ErrorActionPreference = "Stop"
$database = Get-Content -LiteralPath $DatabasePath -Raw | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($Query)) {
    [pscustomobject]@{
        Database = $database.databaseId
        Schema = $database.schemaVersion
        SteamBuild = $database.valheim.steamBuildId
        AssemblySha256 = $database.valheim.assemblySha256
        ModSetFingerprint = $database.modSet.fingerprint
        AssemblyTypeContracts = @($database.assemblyContracts.types).Count
        AssemblyCallbacks = @($database.assemblyContracts.callbacks).Count
        TypeRules = @($database.typeRules).Count
        SignalRules = @($database.signalRules).Count
        ObservedAssets = @($database.observedAssets).Count
    } | Format-List
    return
}

$needle = $Query.Trim()
$tokens = @([regex]::Matches($needle.ToLowerInvariant(), '[a-z0-9_]+') |
    ForEach-Object { $_.Value } |
    Where-Object { $_.Length -ge 4 -and $_ -notin @('what', 'does', 'when', 'which', 'where', 'with', 'from', 'valheim') } |
    Select-Object -Unique)
if ($tokens.Count -eq 0) { $tokens = @($needle.ToLowerInvariant()) }
function Matches-Query {
    param([object]$Record)
    $text = ($Record | ConvertTo-Json -Depth 8 -Compress).ToLowerInvariant()
    foreach ($token in $tokens) { if ($text.Contains($token)) { return $true } }
    return $false
}
$matches = @()
$matches += @($database.assemblyContracts.callbacks | Where-Object {
    Matches-Query $_
}) | ForEach-Object { [pscustomobject]@{ Kind = "assembly-callback"; Key = "$($_.type).$($_.method)"; Record = $_ } }
$matches += @($database.assemblyContracts.types | Where-Object {
    Matches-Query $_
}) | ForEach-Object { [pscustomobject]@{ Kind = "assembly-type"; Key = $_.type; Record = $_ } }
$matches += @($database.signalRules | Where-Object {
    Matches-Query $_
}) | ForEach-Object { [pscustomobject]@{ Kind = "signal"; Key = $_.eventLabel; Record = $_ } }
$matches += @($database.typeRules | Where-Object {
    Matches-Query $_
}) | ForEach-Object { [pscustomobject]@{ Kind = "type"; Key = $_.type; Record = $_ } }
$matches += @($database.observedAssets | Where-Object {
    Matches-Query $_
}) | ForEach-Object { [pscustomobject]@{ Kind = "asset"; Key = $_.assetId; Record = $_ } }

if ($matches.Count -eq 0) {
    Write-Host "No Valheim knowledge record matched '$needle'."
    exit 1
}

$matches | ForEach-Object {
    Write-Host "[$($_.Kind)] $($_.Key)" -ForegroundColor Cyan
    $_.Record | Format-List | Out-String | Write-Host
}
