# DrakesTestLab

Experimental Valheim mod sandbox for DrakeMods. **Not** part of the [DrakesWorkshop](https://github.com/drakethos/DrakeCustomizationSuite) customization suite.

## Dependencies

- BepInEx
- Jotunn

No DrakesWorkshopLibs requirement.

## Status

Stub — plugin loads and logs; features TBD.

## Build

Open `DrakesWorkshop.sln` (monorepo) or build this folder standalone after `nuget restore DrakesTestLab.csproj`.

Copy `environment.props.example` to suite-root `environment.props` (or local `environment.props`) with your Valheim / r2modman paths.

## Release

Tag `v*.*.*` to run `.github/workflows/release.yml` (GitHub pre-release + Thunderstore zip).
