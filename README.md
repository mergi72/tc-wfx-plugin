# tc-wfx-plugin

Separated C# repository for the Total Commander WFX plugin that integrates with `dms-provider-bridge`.

## Automation

- CI workflow: `.github/workflows/ci.yml` (restore, build, test on push/PR)
- Release artifact workflow: `.github/workflows/release-artifact.yml` (manual run or tag `v*`)

## Release Notes

- See `CHANGELOG.md` for release history and `v0.1.0` notes.

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
- On bridge fetch failure, last known cached provider list is reused (stale fallback) before default fallback is used.
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
- `TC_WFX_PROVIDERS` (optional comma/semicolon-separated root provider override; when missing, providers are resolved from bridge, then fallback to `edocat,alfresco,fso`)
- `TC_WFX_PROVIDERS_CACHE_SECONDS` (optional TTL for cached root providers loaded from bridge; default `30`, `0` disables cache)
- `TC_WFX_FIND_CONTEXT_TTL_SECONDS` (optional TTL for inactive find contexts; default `600`, `0` disables TTL cleanup)
- `TC_WFX_MAX_FIND_CONTEXTS` (optional hard limit for concurrently tracked find contexts; default `512`)

Cache can also be invalidated explicitly through `WfxEntryPoints.InvalidateProvidersCache()`.

## Bridge Endpoints Targeted

- `GET /bridge/wfx/providers`
- `POST /bridge/wfx/list`
- `POST /bridge/wfx/stat`
- `POST /bridge/wfx/mkdir`
- `POST /bridge/wfx/delete`
- `POST /bridge/wfx/rename`
- `POST /bridge/wfx/copy`
- `POST /bridge/wfx/download`
- `POST /bridge/wfx/upload`

## Next Implementation Step

Publish the plugin as native AOT DLL and validate exact ABI compatibility with the Total Commander WFX specification.

## Notes

- Build and test:

```powershell
dotnet build
dotnet test
```
