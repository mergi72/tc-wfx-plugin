# tc-wfx-plugin

[![Status](https://img.shields.io/badge/Status-Alpha-orange)](https://github.com/mergi72/tc-wfx-plugin)
[![Plugin Version](https://img.shields.io/badge/Plugin-v0.2.1-blue)](https://github.com/mergi72/tc-wfx-plugin)
[![Installer Release](https://img.shields.io/badge/Installer-v0.2.2--alpha-blueviolet)](https://github.com/mergi72/dms-provider-installer/releases/tag/v0.2.2-alpha)

Current development branch: `develop`  
Stable release branch: `main`

Separated C# repository for the Total Commander WFX plugin that integrates with `dms-provider-bridge`.

Current release mapping:

- Plugin repository latest changelog version: `0.2.1`
- Latest installer release that bundles bridge + plugin: `v0.2.2-alpha`

## Related Projects

- `dms-provider-bridge`
- `dms-provider-installer`

## Release Scope (v0.1.0-alpha)

Current status:
- Alfresco provider: supported
- eDoCat provider: planned / not yet enabled in this alpha milestone
- FSO provider: experimental / planned

Current intended flow:
- TC WFX plugin -> bridge -> Alfresco provider

This alpha milestone does not yet include full eDoCat branch enablement.

## Automation

- CI workflow: `.github/workflows/ci.yml` (restore, build, test on push/PR)
- Release artifact workflow: `.github/workflows/release-artifact.yml` (manual run or tag `v*`)
- Release workflow gating: when secret `BRIDGE_REPO_TOKEN` is set, release first runs bridge integration smoke and publishes artifact only if smoke succeeds.
- Integration smoke workflow job: starts local `dms-provider-bridge`, validates `GET /health`, `GET /bridge/wfx/providers`, `POST /bridge/wfx/list`, and performs streamed large upload smoke via `POST /bridge/wfx/upload-raw` (default 176 MB) using FSO path.
	- For cross-repo checkout in GitHub Actions, configure secret `BRIDGE_REPO_TOKEN` (read access to `mergi72/dms-provider-bridge`).
- Branch protection helper: CI publishes final job `protection-gate` that summarizes required job outcomes.
  - Recommended GitHub branch rule for `main`: require status check `protection-gate`.
	- If GitHub plan does not allow branch protection for private repositories, use process fallback: no direct pushes to `main`; merge only via PR after successful `protection-gate`.

## Release Notes

- See `CHANGELOG.md` for release history and `v0.2.1` notes.
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
- `EnvironmentAuthProvider` loads auth data from environment variables.
- `TotalCommanderPathMapper` translates Total Commander-style paths (`\provider\path`) to bridge provider paths (`provider:/path`).
- Root listing (`\` and `\*.*`) is served as provider folders resolved dynamically from `GET /bridge/wfx/providers`.
- Wildcard listing masks in paths (for example `\edocat\folder\*.*`) are normalized to directory provider paths.
- Root provider list is cached with TTL to reduce bridge calls during panel navigation.
- Root providers come from the bridge response. If `TC_WFX_PROVIDERS` is set, it overrides the bridge response. If the bridge is unavailable and no cache is present, the root listing stays empty instead of falling back to hardcoded providers.
- Find contexts are cleaned up automatically by idle TTL and bounded by maximum context count to prevent handle leaks.

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
- `TC_WFX_PROVIDERS` (optional comma/semicolon-separated root provider override; when missing, providers are resolved from bridge)
- `TC_WFX_PROVIDERS_CACHE_SECONDS` (optional TTL for cached root providers loaded from bridge; default `30`, `0` disables cache)
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

- `GET /bridge/wfx/providers`
- `POST /bridge/wfx/list`
- `POST /bridge/wfx/stat`
- `POST /bridge/wfx/mkdir`
- `POST /bridge/wfx/delete`
- `POST /bridge/wfx/move`
- `POST /bridge/wfx/copy`
- `POST /bridge/wfx/download`
- `POST /bridge/wfx/upload`
- `POST /bridge/wfx/upload-raw`

## Next Implementation Step

Publish the plugin as native AOT DLL and validate exact ABI compatibility with the Total Commander WFX specification.

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
