# Project Realm repository rules

These rules apply to the whole repository unless a deeper `AGENTS.md` says otherwise.

## Protect the workspace

- Preserve existing uncommitted work. Never reset, discard, or stage unrelated changes.
- Do not move, rewrite, reimport, or add images, models, materials, textures, audio, generated databases, or prototype assets unless the user explicitly asks.
- Keep Unity at `6000.5.10f1`, URP at `17.5.0`, and existing package pins. Do not upgrade packages or introduce a DI container without explicit approval.

## Framework boundaries

- `RealmApplication` is the only Unity bootstrap and `RealmSystemServer` is the only runtime composition root.
- Do not add static `Current`, `Instance`, `Get<T>()`, or another service locator. Inject `IRealmContext` or a narrower dependency.
- UI, Presenter, ViewModel, and Unity View code must not reference `ProjectRealm.SystemServer`, SQLite implementations, or writable world state.
- Only System Server may own `WorldRuntime`. UI changes the world only through Manager requests and reads immutable snapshots.
- Never advance authoritative simulation from `Update()`. Time moves only through an explicit deterministic `SimulationManager.Advance` request.
- Preserve the fixed `S00` through `S130` Tick order, Working State whole-Tick rollback, committed-event boundary, PCG32 random addressing, and canonical SHA-256 hashing.
- Unimplemented authoritative modules must remain `Scaffold` and report `Unavailable` / `implementation_unavailable`; never invent zero-valued population, inventory, tax, or economy facts.

## Verification and Git

- Pure C# assemblies must not reference `UnityEngine`; keep asmdef dependencies acyclic.
- Opening or refreshing a Screen, Inspector, or diagnostic query must not change Tick, random state, or state hash.
- Before staging, inspect the exact diff. Stage only task-owned code, asmdefs, scenes, UI documents/styles, matching `.meta` files, required package/build settings, and architecture documentation.
- Never stage generated Definition/Save databases or art/resource files.
