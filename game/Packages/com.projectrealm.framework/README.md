# Project Realm Framework 代码导读

这套代码把 Unity 游戏运行框架组织成接近 Android Framework 的样子。它没有重写世界模拟算法，而是在原有确定性 Simulation 内核外建立了清晰的启动、服务、Manager、Presenter 和 Unity View 边界。

当前结论：框架可以启动、创建世界、显式推进日/月/季/年、保存、读取和查询诊断；101 个规范模块仍是 `Scaffold`，会明确显示 `Unavailable / implementation_unavailable`，不会生成虚假的人口、库存、税收或经济数据。

## 1. Unity 里相当于 `main()` 的入口

从这里开始读：

```text
Assets/Scenes/Runtime/00_Bootstrap.unity
  └─ RealmApplication.Awake()                 // 类似 Java main() + Android Application.onCreate()
       └─ new RealmSystemServer(...).Start()  // 类似 system_server
            └─ IRealmContext                  // 类似受控 Context
                 └─ *Manager                  // 类似 Android XxxManager
```

入口文件是：

```text
Assets/ProjectRealm/Bootstrap/RealmApplication.cs
```

`RealmApplication.Awake()` 只做四件事：

1. 拒绝第二个 `RealmApplication`；
2. 加载只读 Definition 数据库并创建 Save Store；
3. 构造并启动 `RealmSystemServer`；
4. 进入主菜单。

它不会自动创建世界，也不会在 `Update()` 中推进时间。

## 2. Android / Java 对照表

| Android / Java 概念 | Project Realm 对应物 | 责任 |
| --- | --- | --- |
| `main()` / `Application.onCreate()` | `RealmApplication.Awake()` | Unity 唯一启动入口 |
| `system_server` | `RealmSystemServer` | 构造服务、按顺序启动、逆序停止 |
| `Context` | `IRealmContext` | 只暴露受控 Manager 和只读事件流 |
| `SystemService` | `WorldService`、`SimulationService`、`SaveService` | 拥有用例和内部状态 |
| `XxxManager` | `WorldManager`、`SimulationManager`、`SaveManager` | 给上层使用的窄代理 |
| Activity / Screen Controller | `MainMenuPresenter`、`GameplayPresenter` | 协调请求和 View |
| View | `MainMenuScreenView`、`GameplayScreenView` | UI Toolkit 显示和输入 |
| Room / SQLite adapter | `SqliteWorldDefinitionStore`、`SqliteSaveGameStore` | 双数据库实现 |
| Framework internal state | `WorldRuntime` | System Server 内唯一可写世界运行时 |

这里没有 `Manager.Instance`，也没有静态 `Context.Current`。上层拿到的是注入的 `IRealmContext`，服务实现只对 `SystemServer` 可见。

## 3. 程序集依赖

```text
ProjectRealm.Foundation
        ↓
ProjectRealm.World
        ↓
ProjectRealm.Framework
        ↓
ProjectRealm.SystemServer
        ↓
ProjectRealm.Persistence.Sqlite

ProjectRealm.Framework → ProjectRealm.Presentation → ProjectRealm.UnityPresentation
ProjectRealm.Framework → ProjectRealm.UnityAdapter

以上运行时实现最终只在 ProjectRealm.Bootstrap 组装。
```

- `Foundation`：稳定 ID、`RealmResult<T>`、错误类别等最小基础类型。
- `World`：拓扑、模块、时钟、状态、命令、账本和存档 DTO。
- `Framework`：Context、Manager、请求、不可变快照、事件和存储端口。
- `SystemServer`：`WorldRuntime`、Tick、命令和系统服务。
- `Persistence.Sqlite`：Definition/Save SQLite 实现。
- `Presentation`：不依赖 Unity 的 Presenter 和 View 契约。
- `UnityAdapter`：场景导航和 Unity 生命周期适配。
- `Bootstrap`：唯一 Composition Root。
- `UnityPresentation`：UI Toolkit、输入、相机和地图 View。

纯 C# 层不引用 `UnityEngine`。UI 不引用 `SystemServer` 或 SQLite。

## 4. 三条完整调用链

### 新建世界

```text
MainMenuScreenView
  → MainMenuPresenter.CreateWorld
  → IRealmContext.World.Create(request)
  → WorldManager
  → WorldService
  → WorldRuntimeFactory.CreateNew
  → 只创建拓扑、空模块状态和初始检查点
  → 返回 WorldSessionSnapshot
  → NavigationManager.ShowGameplay
```

Definition 中的历史聚合行不会被偷偷转换成当前人口或库存。

### 推进一个 Tick

```text
GameplayScreenView 按钮
  → GameplayPresenter.Advance(Day / Month / Season / Year)
  → SimulationManager
  → SimulationService
  → WorldRuntime.Advance
  → TickCoordinator.ExecuteDay
  → S00 ... S130
  → 全部成功：一次性提交 Clock + State + Command + Checkpoint
  → 任意失败：丢弃 Working State 和命令副本
  → 返回 SimulationStepSnapshot
```

月、季、年不是直接修改日期，而是连续执行完整日 Tick，直到相应闭合边界。

### 保存与读取

```text
GameplayPresenter.Save
  → SaveManager.Save
  → SaveService
  → WorldRuntime.ExportSaveData
  → SqliteSaveGameStore 单写者事务
  → 最后更新当前检查点指针

MainMenuPresenter.LoadWorld
  → SaveManager.Load
  → SaveService / WorldService
  → 校验 Definition 内容散列和检查点散列
  → WorldRuntimeFactory.Restore
  → 返回不可变 WorldSessionSnapshot
```

Definition DB 是只读资料库，Save DB 位于 `Application.persistentDataPath/ProjectRealm/Saves/<saveId>/`。两者不建立跨库外键，而是用规则清单和 Definition 内容散列验证兼容性。

## 5. Tick 的权威事务边界

固定阶段不能改序：

```text
S00  FreezeTopology
S10  CollectDueWork
S20  PrepareInputs
S30  LocalFactSettlement
S40  UpwardAggregation
S50  SnapshotClose
S60  PerceptionBuild
S70  DecisionPlanning
S80  CommandValidation
S90  ReservationCommit
S100 CommandDispatch
S110 ImmediateExecution
S120 EventCommit
S130 AuditAndCheckpoint
```

一次日 Tick 的候选对象包括：候选时钟、`WorkingState`、克隆的命令处理器和候选检查点。只有 14 阶段全部完成后才替换权威字段；模块失败、守恒校验异常或提交异常都不能留下半个 Tick。

已提交的 Framework 事件只在事实提交后发布。Inspector、Screen 刷新和诊断查询只读，不得消耗随机数或改变状态散列。

## 6. 推荐阅读顺序（适合 Java / Android 开发者）

1. `Assets/ProjectRealm/Bootstrap/RealmApplication.cs`：相当于 `main()`。
2. `Runtime/SystemServer/RealmSystemServer.cs`：相当于 `system_server`。
3. `Runtime/Framework/IRealmContext.cs`：上层能够看到什么。
4. `Runtime/Framework/WorldManager.cs` 与其他 Manager：公开代理。
5. `Runtime/SystemServer/WorldService.cs`：新建、载入、关闭世界。
6. `Runtime/SystemServer/WorldRuntime.cs`：唯一权威运行时。
7. `Runtime/SystemServer/TickCoordinator.cs`：整 Tick 事务。
8. `Runtime/SystemServer/CommandProcessor.cs`：命令、预留和幂等。
9. `Runtime/World/SimulationState.cs`：Committed/Working State 与散列。
10. `Runtime/World/ModuleSystem.cs` 和 `FrameworkModuleCatalog.cs`：模块目录与 Scaffold。
11. `Runtime/Persistence/Sqlite/SqliteSaveGameStore.cs`：存档事务。
12. `Runtime/Presentation/*Presenter.cs`：上层如何只通过 Manager 工作。
13. `Assets/ProjectRealm/Presentation/Screens/`：Unity View。

在 Rider 中建议使用 `Navigate → Symbol`（macOS 默认 `⌘O`）搜索类名，或按住 `⌘` 点击类型跳转。不要从 `.csproj` 开始读；`.csproj` 是 Unity 自动生成的工程索引。

## 7. 真实实现与 Scaffold

当前已经真实实现：

- 应用/服务生命周期和受控 Context；
- 世界拓扑、时钟、模块目录、模块实例与生命周期；
- `S00–S130` 固定 Tick、Working State 回滚；
- 命令状态机、作用域幂等键和资源预留框架；
- PCG32 随机流寻址和规范 SHA-256 状态散列；
- Definition SQLite 与 Save SQLite、WAL、检查点和恢复校验；
- Framework Inspector 与开发用 MainMenu/Gameplay Shell；
- 账本、生产、政府决策等独立领域模型及测试。

仍为 Scaffold 或尚未接入主 Tick：

- 101 个规范业务模块的真实人口、农业、市场、税收、军事等公式；
- 历史精确控制关系和 Current State 初始化；
- 正式玩家 UI、美术整合与后台实时模拟；
- 部分已有领域结算模型到主 Tick 的权威挂接。

`ScaffoldModuleExecutor` 返回 `Succeeded=true` 仅表示框架调用没有崩溃，同时必须返回：

```text
ImplementationTier = Scaffold
DataQuality         = Unavailable
ReasonCode          = implementation_unavailable
```

这绝不代表业务结果为零或已经完成。

更严格的边界和设计决策见 `Documentation~/ARCHITECTURE.md`。
