# Release Notes v0.1.0-alpha

## Scope

This alpha release is intended for first external testers.

Provider support status:
- Alfresco provider: supported
- eDoCat provider: planned / not yet enabled in this alpha milestone
- FSO provider: experimental / planned

Current intended flow:
- TC WFX plugin -> bridge -> Alfresco provider

## Included in This Alpha

- WFX runtime operations for list, stat, mkdir, delete, move/copy, upload and download
- Bridge integration through `/bridge/wfx/*` endpoints
- Runtime progress policy and configuration support
- Native AOT publish flow and plugin artifact packaging

## Not Included Yet

- Full eDoCat provider branch enablement and validation
- Automatic Total Commander plugin registration (`wincmd.ini` modification)

## Next Milestone (v0.2.0-alpha)

Planned:
- enable eDoCat provider branch
- verify authentication flow
- map eDoCat paths
- validate upload, download, delete, list
