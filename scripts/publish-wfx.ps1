param(
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "..\src\TcWfxPlugin\TcWfxPlugin.csproj"
$outputPath = Join-Path $PSScriptRoot "..\artifacts\TcWfxPlugin-$RuntimeIdentifier"

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

Write-Host "Publish completed successfully."
