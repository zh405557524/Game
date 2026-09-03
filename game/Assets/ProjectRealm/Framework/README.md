# Project Realm simulation framework

This folder contains the Unity-facing host and Editor tooling for the deterministic simulation core in
`Packages/com.projectrealm.simulation`.

## Generate the development Definition database

From the repository root:

```sh
python3 tools/framework/build_runtime_definition.py
python3 tools/framework/validate_module_catalog.py
```

The generated SQLite asset is intentionally ignored. It contains 1,168 county aggregates plus the
萧县 → 南江桥乡 → 七里村 sample path. Its manifest is development-only and blocks non-development builds.

## Use in Unity

Open `Project Realm → Simulation → Framework Inspector`. Add a `UnitySimulationHost` to the open scene,
then explicitly create or load a development world. Opening or repainting the window is read-only; only
the action buttons advance or persist the world.

All domain modules are currently `Scaffold / Unavailable`. They exercise composition, lifecycle, Tick,
snapshots, checkpoints, commands and persistence without claiming that gameplay formulas or current-state
historical facts exist.

## Validation

Run all EditMode tests from `Project Realm → Tests → Run All EditMode Tests`. The explicit built-player
smoke hook is inactive unless both `-projectRealmFrameworkSmokeResult` and
`-projectRealmFrameworkSmokeRoot` are provided on the player command line.
