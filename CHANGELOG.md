# Changelog

All notable changes to this project will be documented in this file.

## [0.7.0-beta] - 2026-06-17

### Changed

- Root listing now resolves bridge connections from `GET /bridge/wfx/connections`.
- Bridge smoke validation now checks the connection discovery contract used by `dms-provider-bridge` `0.7.1-beta`.

## [0.5.0-beta] - 2026-06-14

### Changed

- Beta release candidate for external Total Commander testing.
- Includes version-aware local/provider uploads, provider-to-provider copy/move version prompts, localized dialogs, and broker-backed credential flow.

## [0.2.7] - 2026-06-13

### Added

- Added German and Polish dialog text entries to `config/localize.json`.

### Changed

- Localization now resolves dialog text by Total Commander `LanguageIni` id from `localize.json`, with a generic `fallback` entry when the TC language id is not configured.
- Removed hardcoded language mapping from the WFX localization layer.

## [0.2.6] - 2026-06-13

### Added

- Localized WFX dialog text loaded from `config/localize.json`, with Czech and English entries and English fallback.
- Total Commander language detection now reads `LanguageIni` from `wincmd.ini`, including the common `fsplugin.ini` default-params path.

### Changed

- Version conflict cancel/close now returns Total Commander's user-abort transfer result instead of showing a generic copy/download error.
- Version conflict, provider login, remember-login, and overwrite prompts now use localized text.

## [0.2.5] - 2026-06-13

### Added

- Provider-to-provider copy/move now handles bridge `version_required` responses with the same version choice flow as local-to-provider uploads.

### Changed

- Copy/move requests can send upload `versioning` data when retrying after a destination document version prompt.

## [0.2.4] - 2026-06-12

### Changed

- Updated README to describe the current plugin stack and release mapping.
- Clarified that provider implementations are owned by `dms-provider-bridge`; the WFX plugin resolves provider roots dynamically and does not hardcode provider support.
- Documented current bridge, credential broker, raw transfer, and versioning integration notes.

## [0.2.3] - 2026-06-10

### Added

- Alfresco existing-document upload flow now asks for a new version choice instead of treating DMS updates as filesystem overwrite.
- Version choice dialog shows the current version and calculated next major/minor targets, for example `Yes = 2.0` and `No = 1.5`.
- Upload requests can send bridge `versioning` data with Alfresco-compatible `majorVersion` through both JSON and raw multipart upload paths.

### Changed

- Alfresco upload skips the old overwrite confirmation and lets the bridge return `version_required`, then retries with the selected major/minor version.

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
