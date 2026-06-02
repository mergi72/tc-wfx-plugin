# tc-wfx-plugin

Separated C# repository for the Total Commander WFX plugin that integrates with `dms-provider-bridge`.

## Automation

- CI workflow: `.github/workflows/ci.yml` (restore, build, test on push/PR)
- Release artifact workflow: `.github/workflows/release-artifact.yml` (manual run or tag `v*`)

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

## Bridge Endpoints Targeted

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
