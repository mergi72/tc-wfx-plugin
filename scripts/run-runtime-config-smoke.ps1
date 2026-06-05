param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..")
$testsProject = Join-Path $repoRoot "tests\TcWfxPlugin.Tests\TcWfxPlugin.Tests.csproj"
$configTemplate = Join-Path $repoRoot "config\config.json"

if (-not (Test-Path $testsProject)) {
    throw "Test project not found: $testsProject"
}

if (-not (Test-Path $configTemplate)) {
    throw "Config template not found: $configTemplate"
}

Write-Host "Runtime config smoke: validating template JSON..."
$templateJson = Get-Content -Path $configTemplate -Raw | ConvertFrom-Json
if (-not $templateJson.bridge.url) {
    throw "config/config.json must include bridge.url"
}
if (-not $templateJson.logging.path) {
    throw "config/config.json must include logging.path"
}

Write-Host "Runtime config smoke: running focused tests..."
& dotnet test $testsProject --configuration $Configuration --filter "FullyQualifiedName~WfxRuntimeConfigTests"
if ($LASTEXITCODE -ne 0) {
    throw "Runtime config smoke tests failed with exit code $LASTEXITCODE"
}

Write-Host "Runtime config smoke passed (including logging.enabled=false scenario)."
