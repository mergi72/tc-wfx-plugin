# Changelog

All notable changes to this project will be documented in this file.

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
