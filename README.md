# tc-wfx-plugin

[![CI](https://github.com/mergi72/tc-wfx-plugin/actions/workflows/ci.yml/badge.svg)](https://github.com/mergi72/tc-wfx-plugin/actions/workflows/ci.yml)
[![Status](https://img.shields.io/badge/Status-Beta-yellowgreen)](https://github.com/mergi72/tc-wfx-plugin)
[![Plugin Version](https://img.shields.io/badge/Plugin-v0.5.1--beta-blue)](https://github.com/mergi72/tc-wfx-plugin)
[![Installer Release](https://img.shields.io/badge/Installer-v0.5.0--beta-blueviolet)](https://github.com/mergi72/dms-provider-installer/releases/tag/v0.5.0-beta)

Total Commander x64 filesystem plugin for browsing and transferring files through a local `dms-provider-bridge` instance.

Use it to access document repositories exposed by the bridge, such as Alfresco or eDoCat when those providers are configured in the bridge.

## Features

- Browse connection roots and folders from Total Commander.
- Download files.
- Upload files, including large streamed uploads.
- Rename, move, copy, delete, and create folders.
- Use version-aware uploads when the bridge reports that an existing document needs a new version.
- Use the same version choice flow for connection-to-connection copy/move into an existing versioned document.
- Localize plugin prompts from `config/localize.json` using the current Total Commander `LanguageIni` id, with fallback text when the TC language id is not configured.
- Resolve user credentials through `credential-broker` when configured.
- Load runtime settings from `config.json` next to the plugin.

## Requirements

- Windows x64.
- Total Commander x64.
- `dms-provider-installer` for the easiest full-stack installation.
- A running local `dms-provider-bridge`.
- Provider and credential configuration in the bridge/broker stack.

License: MIT.

## Quick Start

1. Install `dms-provider-installer`.
2. Make sure the bridge is running at `http://127.0.0.1:8765/health`.
3. Configure bridge connections and credentials using the bridge/broker setup for your environment.
4. Open Total Commander.
5. Open the DMS Provider filesystem plugin from Network Neighborhood / WFX plugins.
6. Select a connection folder and work with files normally.

## How It Works For Users

Total Commander talks to `TcWfxPlugin.wfx64`. The plugin translates Total Commander paths like `\connection\folder` into bridge paths like `connection:/folder`, then calls the local bridge API. The bridge owns driver-specific behavior and talks to the remote DMS.

This repository does not ship provider implementations. Connection roots are loaded dynamically from:

- `GET /bridge/wfx/connections`

## What This Project Does Not Own

- Bridge installation and Windows service setup.
- Provider configuration.
- Provider implementations.
- Credential Broker installation.
- Windows Credential Manager entries.
- Total Commander itself.

Those responsibilities belong to `dms-provider-bridge`, `credential-broker`, `dms-provider-installer`, or Total Commander.

## Developer Overview

Separated C# repository for the Total Commander WFX plugin that integrates with `dms-provider-bridge`.

Current development branch: `develop`  
Stable release branch: `main`

Current release mapping:

- Plugin repository latest changelog version: `0.5.1-beta`
- Latest orchestrator installer release: `dms-provider-installer v0.5.0-beta`
- Current tested bridge release: `dms-provider-bridge v0.7.1-beta`
- Current credential broker release: `credential-broker v0.5.0-beta`

## Related Projects

- `dms-provider-bridge`
- `credential-broker`
- `dms-provider-installer`

## Developer Scope

Current status:
- Provider support is owned by `dms-provider-bridge`.
- Root connections are resolved dynamically from `GET /bridge/wfx/connections`.
- This repository does not hardcode or ship provider implementations.
- Credential resolution can use `credential-broker` when configured.

Current intended flow:
- Total Commander WFX plugin -> credential broker for user credentials when needed -> bridge -> provider.
- Bridge and broker installation are owned by their dedicated installers; the orchestrator installer bundles them with the WFX plugin.

## Automation

- CI workflow: `.github/workflows/ci.yml` (restore, build, test on push/PR)
- Release artifact workflow: `.github/workflows/release-artifact.yml` (manual run or tag `v*`)
- Release workflow gating: when secret `BRIDGE_REPO_TOKEN` is set, release first runs bridge integration smoke and publishes artifact only if smoke succeeds.
- Integration smoke workflow job starts local `dms-provider-bridge`, validates `GET /health`, `GET /bridge/wfx/connections`, `POST /bridge/wfx/list`, and can perform streamed large upload smoke via `POST /bridge/wfx/upload-raw` when a suitable bridge connection/path is configured.
	- For cross-repo checkout in GitHub Actions, configure secret `BRIDGE_REPO_TOKEN` (read access to `mergi72/dms-provider-bridge`).
- Branch protection helper: CI publishes final job `protection-gate` that summarizes required job outcomes.
  - Recommended GitHub branch rule for `main`: require status check `protection-gate`.
	- If GitHub plan does not allow branch protection for private repositories, use process fallback: no direct pushes to `main`; merge only via PR after successful `protection-gate`.

## Release Notes

- See `CHANGELOG.md` for release history and `v0.5.1-beta` notes.
- For first external testing scope, see `RELEASE_NOTES_v0.1.0-alpha.md`.

## License

- MIT (`LICENSE`)

## Structure

- `src/TcWfxPlugin/Contracts/` - request/response DTO contracts aligned with bridge endpoints
- `src/TcWfxPlugin/Core/` - shared domain helpers (provider path parsing/validation)
- `src/TcWfxPlugin/Bridge/` - bridge client abstraction interfaces
- `src/TcWfxPlugin/Wfx/` - WFX-oriented facade for plugin operations
- `src/TcWfxPlugin/WfxBridgeClient.cs` - concrete HTTP client implementation for `/bridge/wfx/*`
- `tests/TcWfxPlugin.Tests/` - xUnit tests for core parsing and facade behavior

## WFX Runtime Skeleton

- `WfxPluginRuntime` implements WFX-like operations for listing, iterating, mkdir, delete, rename, copy, download, and upload.
- `WfxEntryPoints` exposes sync wrappers (`FsFindFirst`, `FsFindNext`, `FsFindClose`, `FsMkDir`, `FsDeleteFile`, `FsRenMovFile`, `FsGetFile`, `FsPutFile`).
- `IWfxAuthProvider` decouples credential retrieval from runtime operations.
- `TcDialogAuthProvider` resolves credentials through environment defaults, credential broker, or Total Commander prompts.
- `EnvironmentAuthProvider` remains available for tests and direct embedding scenarios.
- `TotalCommanderPathMapper` translates Total Commander-style paths (`\provider\path`) to bridge provider paths (`provider:/path`).
- Root listing (`\` and `\*.*`) is served as connection folders resolved dynamically from `GET /bridge/wfx/connections`.
- Wildcard listing masks in paths (for example `\provider\folder\*.*`) are normalized to directory provider paths.
- Root connection list is cached with TTL to reduce bridge calls during panel navigation.
- Root connections come from the bridge response. If `TC_WFX_PROVIDERS` is set, it overrides the bridge response. If the bridge is unavailable and no cache is present, the root listing stays empty instead of falling back to hardcoded connections.
- Find contexts are cleaned up automatically by idle TTL and bounded by maximum context count to prevent handle leaks.
- Existing-document upload can handle bridge `version_required` responses and ask for major/minor version selection.
- Large uploads use streamed multipart `upload-raw`; downloads use raw streaming where available.

## Native Exports

- `WfxNativeExports` defines unmanaged entrypoints with `UnmanagedCallersOnly`:
	- `FsInitW`
	- `FsFindFirstW`
	- `FsFindNextW`
	- `FsFindClose`
	- `FsMkDirW`
	- `FsDeleteFileW`
	- `FsRenMovFileW`
	- `FsGetFileW`
	- `FsPutFileW`

## Environment Configuration

- `TC_WFX_BRIDGE_URL` (default: `http://127.0.0.1:8765/`)
- `TC_WFX_AUTH_MODE` (`winuser` or `credentials`)
- `TC_WFX_WIN_USER`
- `TC_WFX_CREDENTIAL_ID`
- `TC_WFX_USERNAME`
- `TC_WFX_PASSWORD`
- `TC_WFX_TOKEN`
- `TC_WFX_CREDENTIAL_BROKER_URL` (default credential broker endpoint)
- `TC_WFX_CREDENTIAL_BROKER_TIMEOUT_MS` (credential broker request timeout)
- `TC_WFX_PROVIDERS` (optional comma/semicolon-separated root connection override; when missing, connections are resolved from bridge)
- `TC_WFX_PROVIDERS_CACHE_SECONDS` (optional TTL for cached root connections loaded from bridge; default `30`, `0` disables cache)
- `TC_WFX_FIND_CONTEXT_TTL_SECONDS` (optional TTL for inactive find contexts; default `600`, `0` disables TTL cleanup)
- `TC_WFX_MAX_FIND_CONTEXTS` (optional hard limit for concurrently tracked find contexts; default `512`)
- `TC_WFX_PROGRESS_STEPS` (optional fallback number of progress buckets for TC callback throttling; default `10`, allowed range `1..100`)

Additional optional env fallbacks:

- `TC_WFX_BRIDGE_TIMEOUT_SECONDS` (fallback bridge HTTP timeout in seconds)
- `TC_WFX_LOGGING_ENABLED` (fallback `true/false` toggle for diagnostic logs)
- `TC_WFX_LOG_DIR` (fallback log directory; absolute or relative to plugin directory)

Optional runtime file configuration (preferred for progress):

- Place `config.json` next to `TcWfxPlugin.wfx64`.
- Repository ships a ready template at `config/config.json`.
- Configure progress buckets via:

```json
{
	"progress": {
		"steps": 10
	}
}
```

Resolution order for progress steps:

1. `config.json` -> `progress.steps`
2. `TC_WFX_PROGRESS_STEPS`
3. default `10`

Resolution order for runtime settings in general:

1. `config.json`
2. corresponding environment variable
3. built-in default

Supported `config.json` keys:

```json
{
	"bridge": {
		"url": "http://127.0.0.1:8765/",
		"timeoutSeconds": 900
	},
	"progress": {
		"steps": 10
	},
	"logging": {
		"enabled": true,
		"path": "logs"
	}
}
```

`config.json` can be placed either next to `TcWfxPlugin.wfx64` or in `config/config.json` under the plugin directory.

The publish script automatically copies repository template `config/config.json` to artifact output `config/config.json`.

Cache can also be invalidated explicitly through `WfxEntryPoints.InvalidateProvidersCache()`.

## Bridge Endpoints Targeted

- `GET /bridge/wfx/connections`
- `POST /bridge/wfx/list`
- `POST /bridge/wfx/stat`
- `POST /bridge/wfx/mkdir`
- `POST /bridge/wfx/delete`
- `POST /bridge/wfx/move`
- `POST /bridge/wfx/copy`
- `POST /bridge/wfx/download`
- `POST /bridge/wfx/download-raw`
- `POST /bridge/wfx/upload`
- `POST /bridge/wfx/upload-raw`
- `POST /bridge/wfx/upload-stream` (bridge alias for streamed raw upload)

Upload requests can include bridge `versioning` data with `majorVersion` when the bridge asks for a new version decision.

## Notes

- Build and test:

```powershell
dotnet build
dotnet test
```

- Native AOT publish (WFX DLL):

```powershell
./scripts/publish-wfx.ps1
```

or directly:

```powershell
dotnet publish src/TcWfxPlugin/TcWfxPlugin.csproj --configuration Release -r win-x64 /p:PublishAot=true /p:NativeLib=Shared --output artifacts/TcWfxPlugin-win-x64
```

- Bridge integration smoke (local):

```powershell
./scripts/run-bridge-smoke.ps1 -BridgeRepoPath ../dms-provider-bridge
```

- Runtime config smoke (local):

```powershell
./scripts/run-runtime-config-smoke.ps1
```

This smoke validates config template presence/shape and runs focused tests that verify runtime config loading, including `logging.enabled=false`.

Optional parameters:

- `-LargeUploadMB 176` enables large streamed upload smoke (set `0` to skip)
