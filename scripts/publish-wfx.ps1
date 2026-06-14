param(
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "..\src\TcWfxPlugin\TcWfxPlugin.csproj"
$outputPath = Join-Path $PSScriptRoot "..\artifacts\TcWfxPlugin-$RuntimeIdentifier"
$configTemplatePath = Join-Path $PSScriptRoot "..\config\config.json"
$localizePath = Join-Path $PSScriptRoot "..\config\localize.json"

Write-Host "Publishing WFX Native AOT library..."
Write-Host "Project: $projectPath"
Write-Host "Output:  $outputPath"

& dotnet publish $projectPath `
    --configuration $Configuration `
    -r $RuntimeIdentifier `
    /p:PublishAot=true `
    /p:NativeLib=Shared `
    --output $outputPath

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$publishedDllPath = Join-Path $outputPath "TcWfxPlugin.dll"
$publishedWfxPath = Join-Path $outputPath "TcWfxPlugin.wfx64"
if (-not (Test-Path $publishedDllPath)) {
    throw "Published plugin DLL not found: $publishedDllPath"
}
Copy-Item -Path $publishedDllPath -Destination $publishedWfxPath -Force
Write-Host "Copied native plugin to: $publishedWfxPath"

if (Test-Path $configTemplatePath) {
    $outputRootConfigPath = Join-Path $outputPath "config.json"
    Copy-Item -Path $configTemplatePath -Destination $outputRootConfigPath -Force
    Write-Host "Copied runtime config template to: $outputRootConfigPath"

    $outputConfigDir = Join-Path $outputPath "config"
    New-Item -ItemType Directory -Path $outputConfigDir -Force | Out-Null
    Copy-Item -Path $configTemplatePath -Destination (Join-Path $outputConfigDir "config.json") -Force
    Write-Host "Copied runtime config template to: $outputConfigDir\config.json"
}

if (Test-Path $localizePath) {
    $outputConfigDir = Join-Path $outputPath "config"
    New-Item -ItemType Directory -Path $outputConfigDir -Force | Out-Null
    Copy-Item -Path $localizePath -Destination (Join-Path $outputConfigDir "localize.json") -Force
    Write-Host "Copied localization file to: $outputConfigDir\localize.json"
}

Write-Host "Publish completed successfully."
