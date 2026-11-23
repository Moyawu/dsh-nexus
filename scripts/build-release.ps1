param(
    [string]$Runtime = "win-x64",
    [switch]$SelfContained = $true
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $repoRoot "src\DshNexus\dsh-tray-launcher.csproj"
$outDir = Join-Path $repoRoot "publish\$Runtime"

dotnet publish $csproj `
    -c Release `
    -r $Runtime `
    --self-contained $SelfContained.ToString().ToLower() `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $outDir

Copy-Item (Join-Path $repoRoot "README.md"), (Join-Path $repoRoot "LICENSE") $outDir -Force
Compress-Archive -Path "$outDir\*" -DestinationPath (Join-Path $repoRoot "publish\DSH-Nexus-$Runtime.zip") -Force
Write-Host "Архив готов: $repoRoot\publish\DSH-Nexus-$Runtime.zip" -ForegroundColor Green