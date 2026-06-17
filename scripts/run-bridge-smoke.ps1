param(
    [string]$BridgeRepoPath = "../dms-provider-bridge",
    [string]$BridgeHost = "127.0.0.1",
    [int]$BridgePort = 8765,
    [string]$PythonExe = "python",
    [int]$LargeUploadMB = 176
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

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
$uploadFileName = "bridge-smoke-large-upload.bin"
$uploadProviderPath = "$providerPath/$uploadFileName"
$uploadTargetLocalPath = Join-Path $pluginRoot $uploadFileName
$largeUploadSizeBytes = [int64]$LargeUploadMB * 1024 * 1024

$originalFsoConfigBytes = [System.IO.File]::ReadAllBytes($fsoConfigPath)
$tempFsoConfig = @{
    key = "fso"
    fso = @{
        allowedRoots = @($pluginRootPosix)
    }
} | ConvertTo-Json -Depth 5

$serverProcess = $null
$httpClient = $null
$uploadFileStream = $null
$multipartContent = $null
$streamContent = $null

try {
    [System.IO.File]::WriteAllText($fsoConfigPath, $tempFsoConfig, [System.Text.UTF8Encoding]::new($false))

    Write-Host "Starting bridge server for smoke test..."
    $serverProcess = Start-Process -FilePath $PythonExe -ArgumentList @(
        "-m",
        "uvicorn",
        "dms_provider_bridge.app.server:app",
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

    $connections = Invoke-RestMethod -Method Get -Uri "$bridgeUrl/bridge/wfx/connections"
    if (-not $connections.ok) {
        throw "Connections endpoint returned ok=false"
    }

    $hasFso = @($connections.data.connection_names) -contains "fso"
    if (-not $hasFso) {
        throw "Connections endpoint did not include fso connection."
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

    if ($LargeUploadMB -gt 0) {
        Write-Host "Running large raw upload smoke ($LargeUploadMB MB)..."

        if (Test-Path $uploadTargetLocalPath) {
            Remove-Item $uploadTargetLocalPath -Force -ErrorAction SilentlyContinue
        }

        $tempUploadSource = Join-Path $env:TEMP "$uploadFileName.src"
        try {
            $sourceStream = [System.IO.File]::Open($tempUploadSource, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
            try {
                $sourceStream.SetLength($largeUploadSizeBytes)
            }
            finally {
                $sourceStream.Dispose()
            }

            $authPayload = @{ mode = "winuser"; win_user = $env:USERNAME } | ConvertTo-Json -Compress

            $httpClient = [System.Net.Http.HttpClient]::new()
            $httpClient.Timeout = [TimeSpan]::FromMinutes(35)

            $uploadFileStream = [System.IO.File]::OpenRead($tempUploadSource)
            $multipartContent = [System.Net.Http.MultipartFormDataContent]::new()
            $multipartContent.Add([System.Net.Http.StringContent]::new($providerPath), "destination")
            $multipartContent.Add([System.Net.Http.StringContent]::new($uploadFileName), "file_name")
            $multipartContent.Add([System.Net.Http.StringContent]::new("true"), "overwrite")
            $multipartContent.Add([System.Net.Http.StringContent]::new($authPayload), "auth_json")

            $streamContent = [System.Net.Http.StreamContent]::new($uploadFileStream, 1024 * 1024)
            $streamContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/octet-stream")
            $multipartContent.Add($streamContent, "file", $uploadFileName)

            $uploadResponse = $httpClient.PostAsync("$bridgeUrl/bridge/wfx/upload-raw", $multipartContent).GetAwaiter().GetResult()
            $uploadBodyText = $uploadResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if (-not $uploadResponse.IsSuccessStatusCode) {
                throw "upload-raw returned HTTP $([int]$uploadResponse.StatusCode): $uploadBodyText"
            }

            $uploadBody = $uploadBodyText | ConvertFrom-Json
            if (-not $uploadBody.ok) {
                throw "upload-raw returned ok=false (error_code=$($uploadBody.error_code), message=$($uploadBody.message))"
            }

            $statBody = @{
                path = $uploadProviderPath
                auth = @{
                    mode = "winuser"
                    win_user = $env:USERNAME
                }
            } | ConvertTo-Json -Depth 6

            $statResult = Invoke-RestMethod -Method Post -Uri "$bridgeUrl/bridge/wfx/stat" -ContentType "application/json" -Body $statBody
            if (-not $statResult.ok) {
                throw "stat after upload returned ok=false (error_code=$($statResult.error_code), message=$($statResult.message))"
            }

            $remoteSize = [int64]$statResult.data.size
            if ($remoteSize -ne $largeUploadSizeBytes) {
                throw "Uploaded size mismatch. Expected $largeUploadSizeBytes B, got $remoteSize B."
            }
        }
        finally {
            if ($streamContent) { $streamContent.Dispose(); $streamContent = $null }
            if ($multipartContent) { $multipartContent.Dispose(); $multipartContent = $null }
            if ($uploadFileStream) { $uploadFileStream.Dispose(); $uploadFileStream = $null }
            if ($httpClient) { $httpClient.Dispose(); $httpClient = $null }
            if (Test-Path $tempUploadSource) {
                Remove-Item $tempUploadSource -Force -ErrorAction SilentlyContinue
            }
            if (Test-Path $uploadTargetLocalPath) {
                Remove-Item $uploadTargetLocalPath -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Write-Host "Smoke test passed. Connections, list endpoint, and large raw upload are operational."
}
finally {
    if ($serverProcess -and -not $serverProcess.HasExited) {
        Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
    }

    if ((Test-Path $fsoConfigPath) -and ($null -ne $originalFsoConfigBytes)) {
        [System.IO.File]::WriteAllBytes($fsoConfigPath, $originalFsoConfigBytes)
    }
}
