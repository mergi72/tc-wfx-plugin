param(
    [string]$BridgeRepoPath = "../dms-provider-bridge",
    [string]$BridgeHost = "127.0.0.1",
    [int]$BridgePort = 8765,
    [string]$PythonExe = "python"
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$pluginRoot = Resolve-Path (Join-Path $scriptRoot "..")
$resolvedBridgeRepo = Resolve-Path $BridgeRepoPath

$bridgeUrl = "http://$BridgeHost`:$BridgePort"
$appDir = Join-Path $resolvedBridgeRepo "src"
$fsoConfigPath = Join-Path $resolvedBridgeRepo "config/fso.json"

if (-not (Test-Path $appDir)) {
    throw "Bridge src directory not found: $appDir"
}

if (-not (Test-Path $fsoConfigPath)) {
    throw "Bridge FSO config not found: $fsoConfigPath"
}

$pluginRootPosix = $pluginRoot.Path.Replace("\\", "/")
$providerPath = "fso:/$pluginRootPosix"

$originalFsoConfigBytes = [System.IO.File]::ReadAllBytes($fsoConfigPath)
$tempFsoConfig = @{
    key = "fso"
    fso = @{
        allowedRoots = @($pluginRootPosix)
    }
} | ConvertTo-Json -Depth 5

$serverProcess = $null

try {
    [System.IO.File]::WriteAllText($fsoConfigPath, $tempFsoConfig, [System.Text.UTF8Encoding]::new($false))

    Write-Host "Starting bridge server for smoke test..."
    $serverProcess = Start-Process -FilePath $PythonExe -ArgumentList @(
        "-m",
        "uvicorn",
        "edocat_bridge.app.server:app",
        "--app-dir",
        $appDir,
        "--host",
        $BridgeHost,
        "--port",
        "$BridgePort"
    ) -WorkingDirectory $resolvedBridgeRepo -PassThru

    Write-Host "Waiting for bridge health endpoint..."
    $healthy = $false
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 1
        try {
            $health = Invoke-RestMethod -Method Get -Uri "$bridgeUrl/health"
            if ($health.status -eq "ok") {
                $healthy = $true
                break
            }
        }
        catch {
            # Server can still be starting up.
        }
    }

    if (-not $healthy) {
        throw "Bridge did not become healthy in time."
    }

    $providers = Invoke-RestMethod -Method Get -Uri "$bridgeUrl/bridge/wfx/providers"
    if (-not $providers.ok) {
        throw "Providers endpoint returned ok=false"
    }

    $hasFso = @($providers.data.providers) -contains "fso"
    if (-not $hasFso) {
        throw "Providers endpoint did not include fso provider."
    }

    $listBody = @{
        path = $providerPath
        auth = @{
            mode = "winuser"
            win_user = $env:USERNAME
        }
    } | ConvertTo-Json -Depth 6

    $listResult = Invoke-RestMethod -Method Post -Uri "$bridgeUrl/bridge/wfx/list" -ContentType "application/json" -Body $listBody
    if (-not $listResult.ok) {
        $message = $listResult.message
        $errorCode = $listResult.error_code
        throw "List endpoint returned ok=false (error_code=$errorCode, message=$message)"
    }

    Write-Host "Smoke test passed. Providers and list endpoint are operational."
}
finally {
    if ($serverProcess -and -not $serverProcess.HasExited) {
        Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
    }

    if ((Test-Path $fsoConfigPath) -and ($null -ne $originalFsoConfigBytes)) {
        [System.IO.File]::WriteAllBytes($fsoConfigPath, $originalFsoConfigBytes)
    }
}
