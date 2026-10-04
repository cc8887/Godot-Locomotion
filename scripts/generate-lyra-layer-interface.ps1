param([switch]$Check)
$ErrorActionPreference='Stop'
$layerCodegenRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
# Resolve the installed SDK from the parent directory; this checkout retains
# its original SDK pin. Never change global.json or system environment values.
Push-Location (Split-Path $layerCodegenRoot -Parent)
try {
    $layerCodegenMode=if($Check){'check'}else{'generate'}
    & dotnet run --project (Join-Path $layerCodegenRoot 'tools/Als.LayerCodegen/Als.LayerCodegen.csproj') -c Release -- $layerCodegenMode `
        (Join-Path $layerCodegenRoot 'assets/generated/lyra_als/linked_layer_contracts.json') `
        (Join-Path $layerCodegenRoot 'tools/contracts/lyra-layer-codegen.json') `
        (Join-Path $layerCodegenRoot 'tools/contracts/lyra-layer-function-ids.json') `
        (Join-Path $layerCodegenRoot 'src/Als.Godot/Animation/Lyra/LyraGeneratedLayerContract.g.cs') `
        (Join-Path $layerCodegenRoot 'assets/generated/lyra_als/linked_layer_inventory.json')
    if($LASTEXITCODE -ne 0){throw 'Animation layer codegen failed.'}
} finally {Pop-Location}
