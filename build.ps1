param(
    [switch]$Deploy,
    [string]$CelestePath = $env:CELESTE_DIR
)

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
$Dist = Join-Path $Root 'dist\BlackTheo'

if ([string]::IsNullOrWhiteSpace($CelestePath)) {
    $candidates = @(
        'K:\SteamLibrary\steamapps\common\Celeste',
        'C:\Program Files (x86)\Steam\steamapps\common\Celeste',
        'C:\Program Files\Steam\steamapps\common\Celeste'
    )
    $CelestePath = $candidates | Where-Object { Test-Path (Join-Path $_ 'Celeste.dll') } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($CelestePath) -or -not (Test-Path (Join-Path $CelestePath 'Celeste.dll'))) {
    throw 'Celeste not found. Pass -CelestePath <Celeste folder> or set CELESTE_DIR.'
}

Write-Host "Using Celeste at $CelestePath"
Write-Host 'Compiling HLSL effect...'
dotnet run --project (Join-Path $Root 'tools\EffectCompiler\EffectCompiler.csproj') -c Release -- `
    (Join-Path $Root 'src\BlackHole.fx') (Join-Path $Root 'src\BlackHole.cso')
if ($LASTEXITCODE -ne 0) { throw 'Effect compilation failed.' }

New-Item -ItemType Directory -Force (Join-Path $Root 'src\BlackTheo\Resources') | Out-Null
Copy-Item (Join-Path $Root 'src\BlackHole.cso') (Join-Path $Root 'src\BlackTheo\Resources\BlackHole.cso') -Force

# The runtime shader is embedded in the DLL; no Effects folder is needed.
Remove-Item (Join-Path $Dist 'Effects') -Recurse -Force -ErrorAction SilentlyContinue

$DialogDst = Join-Path $Dist 'Dialog'
New-Item -ItemType Directory -Force $DialogDst | Out-Null
Copy-Item (Join-Path $Root 'src\BlackTheo\Dialog\*') $DialogDst -Force

Write-Host 'Building mod...'
dotnet build (Join-Path $Root 'src\BlackTheo\BlackTheo.csproj') -c Release -p:CelesteDir="$CelestePath"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$Out = Join-Path $Root 'src\BlackTheo\bin\Release\net8.0'
New-Item -ItemType Directory -Force (Join-Path $Dist 'Code\bin') | Out-Null
Copy-Item (Join-Path $Out 'BlackTheo.dll') (Join-Path $Dist 'Code\bin\BlackTheo.dll') -Force
Copy-Item (Join-Path $Out 'BlackTheo.pdb') (Join-Path $Dist 'Code\bin\BlackTheo.pdb') -Force

Write-Host "Packaged mod at $Dist"

if ($Deploy) {
    $Target = Join-Path $CelestePath 'Mods\BlackTheo'
    New-Item -ItemType Directory -Force $Target | Out-Null
    Copy-Item (Join-Path $Dist '*') $Target -Recurse -Force
    Write-Host "Deployed to $Target"
}
