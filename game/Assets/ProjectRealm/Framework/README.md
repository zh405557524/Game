# Project Realm Framework Unity integration

完整的中文 Android/Java 代码导读位于
[`Packages/com.projectrealm.framework/README.md`](../../../Packages/com.projectrealm.framework/README.md)，
架构约束位于该包的 `Documentation~/ARCHITECTURE.md`。

本目录只保留 Unity Editor Inspector 和集成测试；运行时唯一入口已经迁移到
`Assets/ProjectRealm/Bootstrap/RealmApplication.cs`，世界内核位于
`Packages/com.projectrealm.framework/Runtime/SystemServer/`。

## Generate the development Definition database

From the repository root:

```sh
python3 tools/framework/build_runtime_definition.py
python3 tools/framework/validate_module_catalog.py
```

The generated SQLite asset is intentionally ignored. It contains 1,168 county aggregates plus the
萧县 → 南江桥乡 → 七里村 sample path. Its manifest is development-only and blocks non-development builds.

## Use in Unity

从 `00_Bootstrap.unity` 启动，或打开 `Project Realm → Simulation → Framework Inspector`。
Bootstrap 只进入主菜单，不自动创建世界。Inspector 的打开、刷新和分页都是只读操作；
只有通过 Manager 发出的显式按钮请求才会推进或保存世界。

All domain modules are currently `Scaffold / Unavailable`. They exercise composition, lifecycle, Tick,
snapshots, checkpoints, commands and persistence without claiming that gameplay formulas or current-state
historical facts exist.

## Validation

使用 `Project Realm → Tests → Run All EditMode Tests` 和 `Run All PlayMode Tests`。
构建产物 smoke hook 只有同时传入 `-projectRealmFrameworkSmokeResult` 与
`-projectRealmFrameworkSmokeRoot` 时才启用。
