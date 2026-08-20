param(
    [string]$ValheimDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
)

$ManagedDir = Join-Path $ValheimDir "valheim_Data\Managed"
$JotunnPath = Join-Path $ValheimDir "BepInEx\plugins\Jotunn.dll"
$ValheimAssemblyPath = Join-Path $ManagedDir "assembly_valheim.dll"

Add-Type -Path $JotunnPath
Add-Type -Path $ValheimAssemblyPath

function Dump-Type {
    param([string]$TypeName, [string[]]$MemberNameFilter = $null)

    Write-Host "`n===== $TypeName =====" -ForegroundColor Cyan
    $type = [System.AppDomain]::CurrentDomain.GetAssemblies() |
        ForEach-Object { $_.GetType($TypeName, $false) } |
        Where-Object { $_ -ne $null } |
        Select-Object -First 1

    if (-not $type) {
        Write-Host "TYPE NOT FOUND" -ForegroundColor Red
        return
    }

    $members = $type.GetMembers([System.Reflection.BindingFlags]::Public -bor
        [System.Reflection.BindingFlags]::Static -bor
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::DeclaredOnly)

    foreach ($m in $members) {
        if ($MemberNameFilter -and ($MemberNameFilter | Where-Object { $m.Name -like "*$_*" }).Count -eq 0) {
            continue
        }
        Write-Host "$($m.MemberType): $m"
    }
}

# Jotunn config sync surface
Dump-Type "Jotunn.Managers.SynchronizationManager"
Dump-Type "BepInEx.Configuration.ConfigurationManagerAttributes" @("IsAdminOnly")

# Valheim networking surface used by the new RPC code
Dump-Type "ZNet" @("GetUID", "GetOwner")
Dump-Type "ZDO" @("GetOwner")
Dump-Type "ZDOMan" @("GetZDO", "instance")
Dump-Type "ZRoutedRpc" @("Register", "InvokeRoutedRPC", "instance", "Everybody")
Dump-Type "ZNetView" @("Register", "InvokeRPC", "IsOwner", "HasOwner", "IsValid", "GetZDO")

# Inventory surface used by the item filter and fuel move/refund logic
Dump-Type "Inventory" @("AddItem", "CanAddItem", "MoveItemToThis", "MoveAll", "RemoveItem", "CountItems", "GetAllItems")
Dump-Type "ItemDrop" @("DropItem")
Dump-Type "Fireplace" @("AddFuel", "m_maxFuel", "m_fuelItem", "m_canRefill", "m_infiniteFuel")

Write-Host "`nDone. Copy this entire output back to Claude." -ForegroundColor Green
