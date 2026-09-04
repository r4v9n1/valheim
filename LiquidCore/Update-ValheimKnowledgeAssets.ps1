param(
    [Parameter(Mandatory = $true)]
    [string]$InventoryPath,
    [string]$DatabasePath = (Join-Path $PSScriptRoot 'knowledge\valheim-knowledge-v1.json')
)

$ErrorActionPreference = 'Stop'
$inventory = Get-Content -Raw -LiteralPath $InventoryPath | ConvertFrom-Json
$database = Get-Content -Raw -LiteralPath $DatabasePath | ConvertFrom-Json

if ($inventory.schemaVersion -ne $database.schemaVersion) { throw 'Inventory schema does not match the canonical database.' }
if ($inventory.valheim.assemblySha256 -ne $database.valheim.assemblySha256) { throw 'Inventory Valheim assembly fingerprint does not match.' }
if ($inventory.modSet.fingerprint -ne $database.modSet.fingerprint) { throw 'Inventory mod-set fingerprint does not match.' }

$feedCategories = @('SolidBarrier', 'DynamicSolid', 'ThinBlockingBarrier')
$recognizedRoots = @('Piece', 'WearNTear', 'Destructible', 'MineRock', 'MineRock5', 'Door')
$selected = @($inventory.assets | Where-Object {
    $_.colliderCount -gt $_.triggerColliderCount -and
    $_.componentTypes -contains 'ZNetView' -and
    $_.category -in $feedCategories -and
    ($_.observedRootType -in $recognizedRoots -or
        ($_.observedRootType -eq 'ZNetView' -and
         $_.componentTypes -notcontains 'Ragdoll' -and
         $_.componentTypes -notcontains 'Rigidbody' -and
         $_.componentTypes -notcontains 'Animator'))
})

$assets = [Collections.Generic.List[object]]::new()
foreach ($record in $selected) {
    $requiresStateInspection = $record.door -or $record.observedRootType -in @('Door', 'MineRock', 'MineRock5')
    $assets.Add([ordered]@{
        assetId = $record.assetId
        observedRootType = $record.observedRootType
        category = $record.category
        geometryKind = $record.geometryKind
        staticClass = $record.staticClass
        source = '2026-09-04 fingerprinted automated Valheim resource inventory'
        precompute = $true
        runtimeInspectionRequired = [bool]$requiresStateInspection
        colliderCount = [int]$record.colliderCount
        colliderTypes = @($record.colliderTypes)
        colliderRecipes = @($record.colliderRecipes)
        meshColliderCount = [int]$record.meshColliderCount
        primitiveColliderCount = [int]$record.primitiveColliderCount
        triggerColliderCount = [int]$record.triggerColliderCount
        meshVertices = [int]$record.meshVertices
        meshTriangles = [int]$record.meshTriangles
        localBoundsCenter = @($record.localBoundsCenter)
        localBoundsSize = @($record.localBoundsSize)
        geometrySignature = $record.geometrySignature
        componentTypes = @($record.componentTypes)
        destructible = [bool]$record.destructible
        buildPiece = [bool]$record.buildPiece
        door = [bool]$record.door
        stateFields = @($record.stateFields)
        authoritativeCallbacks = @($record.authoritativeCallbacks)
        pceRule = $record.pceRule
        liquidCoreRule = $record.liquidCoreRule
    })
}

# LocationProxy expands a location hierarchy at runtime. Its prefab template has
# no collider geometry, so the database must classify it but must not pretend a
# reusable collider descriptor exists.
$assets.Add([ordered]@{
    assetId = 'LocationProxy'
    observedRootType = 'Location'
    category = 'SolidBarrier'
    geometryKind = 'RuntimeExpandedLocationHierarchy'
    staticClass = 'static-solid'
    source = '2026-09-04 fingerprinted automated Valheim resource inventory'
    precompute = $false
    runtimeInspectionRequired = $true
    colliderCount = 0
    colliderTypes = @()
    colliderRecipes = @()
    meshColliderCount = 0
    primitiveColliderCount = 0
    triggerColliderCount = 0
    meshVertices = 0
    meshTriangles = 0
    localBoundsCenter = @(0.0, 0.0, 0.0)
    localBoundsSize = @(0.0, 0.0, 0.0)
    geometrySignature = 'runtime-expanded-location'
    componentTypes = @('Location', 'LocationProxy', 'ZNetView')
    destructible = $false
    buildPiece = $false
    door = $false
    stateFields = @('location prefab', 'runtime expansion state', 'transform')
    authoritativeCallbacks = @('ZNetScene.CreateObject', 'ZNetView.ResetZDO')
    pceRule = 'classify from database; inspect the expanded runtime hierarchy once per authoritative revision'
    liquidCoreRule = 'prepare the expanded instance geometry and cache by SourceID/revision'
})

$orderedAssets = @($assets | Sort-Object { $_.assetId })
$database.observedAssets = $orderedAssets
$database.generatedUtc = [DateTime]::UtcNow.ToString('O')
$json = $database | ConvertTo-Json -Depth 16
[IO.File]::WriteAllText($DatabasePath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))

[pscustomobject]@{
    Result = 'PASS'
    SourceRegisteredPrefabs = [int]$inventory.registeredPrefabCount
    SourceColliderPrefabs = [int]$inventory.colliderPrefabCount
    EmbeddedAssetDescriptors = $orderedAssets.Count
    ReusableGeometryDescriptors = @($orderedAssets | Where-Object { $_.precompute -and !$_.runtimeInspectionRequired }).Count
    StatefulDescriptors = @($orderedAssets | Where-Object { $_.runtimeInspectionRequired }).Count
    DatabasePath = $DatabasePath
} | Format-List
