# Changelog

All notable changes to this project will be documented in this file.

## [0.2.2] - 2026-06-09

### Added

- Download progress diagnostics now log whether transfer size came from HTTP `ContentLength`, bridge stat, or unknown-size fallback.

### Changed

- Default progress step filtering increased to 100 steps so transfers stay smooth even without runtime config.

## [0.2.1] - 2026-06-09

### Added

- Credential broker client integration for resolving user credentials outside the bridge service process.
- Runtime config support for progress/logging diagnostics used while testing Total Commander transfers.
- Progress diagnostics consolidated into `progress-debug.log` for local troubleshooting.

### Changed

- Download progress reporting now uses normalized Total Commander paths and raw streamed byte progress from the bridge.
- Transfer progress callback pumping is throttled to avoid flooding the Total Commander progress dialog.
- Upload progress keeps a heartbeat while the bridge call is active.

## [0.2.0] - 2026-06-02

### Added

- Compatibility gate enforcing minimum bridge version for plugin operations.
- Structured bridge smoke validation wiring in CI release flow (when bridge token is configured).
- Capabilities cache for provider feature flags with reconnect invalidation.
- Improved auth recovery path after access denied to force credential re-prompt.

### Changed

- Credential persistence scope switched to Windows CurrentUser.
- Explicit HTTP timeout handling in bridge client operations.

## [0.1.0] - 2026-06-02

### Added

- Initial standalone repository setup for Total Commander WFX plugin.
- Bridge client abstraction and concrete HTTP bridge client for `/bridge/wfx/*` endpoints.
- Contracts layer for auth context, request DTOs, and response DTOs.
- Core path parsing and mapping for provider paths and Total Commander path format.
- WFX runtime operations for list/stat/mkdir/delete/rename/copy/download/upload flows.
- WFX sync entrypoint facade with methods analogous to plugin operations.
- Native export skeleton using `UnmanagedCallersOnly` for WFX-compatible exported functions.
- Environment-based auth provider and bridge URL configuration.
- Unit test suite for path mapping, facade behavior, and runtime entrypoint behavior.
- GitHub Actions CI workflow for restore/build/test.
- GitHub Actions release-artifact workflow for publish + artifact upload.

### Notes

- First tagged release intended as technical baseline and integration skeleton.
- Native AOT packaging and ABI validation are planned next.
