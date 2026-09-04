# Project Realm 游戏技术框架说明

本文介绍 Project Realm 当前的世界模拟代码、运行流程和调试入口。框架基于 Unity 6.5，核心规则使用纯 C# 实现，并通过 Ports 与 Unity、SQLite 隔离。

> 当前状态：技术框架可以加载世界、执行 Tick、保存、重载和验证确定性；领域模块仍是 `Scaffold / Unavailable`。这表示调用链已打通，但不代表人口、库存、税收或经济公式已经实现。

如果要逐文件阅读 Simulation Core，请先看 [`Packages/com.projectrealm.simulation/README.md`](Packages/com.projectrealm.simulation/README.md)。该文档还会区分已经接入主 Tick 的运行骨架，以及尚未接入主 Tick 的账本、生产和政府决策领域代码。

## 1. 从哪里开始看

建议按下面的顺序阅读：

1. `Packages/com.projectrealm.simulation/Runtime/Application/WorldRuntime.cs`
   世界运行总入口，负责推进、命令、检查点和存档。
2. `Packages/com.projectrealm.simulation/Runtime/Application/TickCoordinator.cs`
   按固定顺序执行 14 个 Tick 阶段，并处理整 Tick 回滚。
3. `Packages/com.projectrealm.simulation/Runtime/Domain/SimulationState.cs`
   `CommittedState`、`WorkingState`、结果、快照、散列和 PCG32。
4. `Packages/com.projectrealm.simulation/Runtime/Domain/ModuleSystem.cs`
   模块定义、实例、依赖、生命周期和权威能力约束。
5. `Assets/ProjectRealm/Infrastructure/Sqlite/`
   Definition 只读库和 Save 读写库。
6. `Assets/ProjectRealm/Framework/UnitySimulationHost.cs`
   Unity 生命周期适配器和显式推进入口。
7. `Assets/ProjectRealm/Framework/Editor/FrameworkInspectorWindow.cs`
   Unity Editor 调试窗口。

## 2. 目录和分层

```text
game/
├── Packages/com.projectrealm.simulation/
│   ├── Runtime/Domain/          # 纯 C# 领域对象、状态和确定性算法
│   ├── Runtime/Ports/           # Definition、Save、Codec、Executor 抽象
│   ├── Runtime/Application/     # WorldRuntime、Tick、命令和启动流程
│   └── Tests/                   # 核心 EditMode 单元测试
├── Assets/ProjectRealm/
│   ├── Framework/               # Unity Host、调试窗口、Player 冒烟入口
│   ├── Infrastructure/Sqlite/   # SQLite 适配器
│   └── Tests/                   # Unity 集成和编辑器测试
├── Packages/
│   ├── manifest.json            # Unity 包依赖
│   └── packages-lock.json       # 锁定后的依赖版本
└── ProjectSettings/             # Unity 工程必要配置
```

依赖方向固定为：

```text
Unity / SQLite / Editor
          ↓
      Application
          ↓
         Ports
          ↓
         Domain
```

- `Domain` 不引用 Unity、SQLite、文件系统或网络。
- `Application` 只协调领域对象和 Ports。
- `Infrastructure` 实现数据库、文件等外部能力。
- `UnityFramework` 只管理 Unity 生命周期和显式用户动作。
- 展示和诊断代码不能直接修改权威状态。

## 3. WorldRuntime 如何运行

`WorldRuntime` 是外部调用世界模拟的统一入口，主要方法如下：

| 方法 | 作用 |
| --- | --- |
| `Advance(AdvanceRequest)` | 按日、月、季或年推进世界 |
| `SubmitCommand(CommandEnvelope)` | 提交带权限作用域和幂等键的命令 |
| `Save()` | 保存最近闭合 Tick |
| `ExportSaveData()` | 生成与具体数据库无关的存档 DTO |
| `CurrentStateHash` | 计算当前权威世界的确定性 SHA-256 |

月、季、年推进不是直接修改日期。框架会连续执行日 Tick，直到遇到对应的周期闭合标记。任何一个日 Tick 失败，推进会立即停止。

## 4. 一个 Tick 的事务边界

每个 Tick 开始时会创建：

- 下一日的候选时钟；
- 从 `CommittedState` 复制出的 `WorkingState`；
- 命令处理器的事务副本；
- 冻结的节点和模块 ID 快照。

随后严格执行下面 14 个阶段：

| 阶段 | 主要职责 |
| --- | --- |
| `S00 FreezeTopology` | 冻结本 Tick 的拓扑和模块集合 |
| `S10 CollectDueWork` | 收集到期任务和持续行动 |
| `S20 PrepareInputs` | 准备模块输入 |
| `S30 LocalFactSettlement` | 节点本地事实结算 |
| `S40 UpwardAggregation` | 向上级节点聚合 |
| `S50 SnapshotClose` | 闭合事实快照 |
| `S60 PerceptionBuild` | 构建可见信息和认知 |
| `S70 DecisionPlanning` | 规划决策 |
| `S80 CommandValidation` | 校验命令和权限 |
| `S90 ReservationCommit` | 提交资源预留 |
| `S100 CommandDispatch` | 分发已预留命令 |
| `S110 ImmediateExecution` | 执行即时命令 |
| `S120 EventCommit` | 在事实提交后发布事件 |
| `S130 AuditAndCheckpoint` | 审计、散列和检查点 |

只有全部阶段成功后，Working State、候选时钟和命令副本才会一起成为新权威状态。模块异常、失败结果或守恒校验失败都会放弃整个 Tick。

## 5. 世界拓扑

框架把不同关系拆成独立结构：

- `GeographicTree`：世界、区域、县、基层区划和聚落的父子树；
- `FactionGraph`：势力之间的关系；
- `JurisdictionGraph`：势力对区域的管辖关系；
- `SettlementOwner`：聚落或设施的所有权。

地理父子关系、政治控制和所有权不能互相替代。构造拓扑时会校验重复 ID、缺失节点和地理循环。

## 6. 模块系统

`FrameworkModuleCatalog` 保存架构文档定义的规范模块。模块由三层对象组成：

- `ModuleDefinition`：模块元数据、版本、阶段、依赖和能力；
- `ModuleInstance`：模块在具体节点上的实例和生命周期；
- `ModuleRegistry`：世界中的实例集合，并校验硬依赖和唯一权威提供者。

当前规范模块统一由 `ScaffoldModuleExecutor` 执行。返回结果为：

```text
ImplementationTier = Scaffold
DataQuality          = Unavailable
ReasonCode           = implementation_unavailable
```

这里的 `Succeeded=true` 只表示框架执行器正常返回，不表示业务能力已经实现，也不能把 `Unavailable` 解释为人口、库存或流量为零。

`PersonModule` 和 `TaxModule` 只是旧名称兼容别名，不是第二个权威模块。

## 7. 状态、散列和随机数

### CommittedState 与 WorkingState

- `CommittedState`：最近成功闭合 Tick 的只读权威状态；
- `WorkingState`：单 Tick 可写副本；
- `StateDelta`：本 Tick 的设置或删除记录；
- `Commit()`：生成新的 `CommittedState`；
- `Rollback()`：关闭并丢弃候选状态。

状态载荷使用 `codecId + byte[] payload` 表达，领域层不依赖具体序列化库。

### 确定性散列

`DeterministicStateHasher` 会按稳定 ID 排序，规范写入：

- 世界 ID、种子和时钟；
- 地理、势力、管辖和所有权；
- 模块实例及生命周期；
- 所有状态记录和二进制载荷。

最后计算 SHA-256。相同输入必须得到相同散列。

### PCG32

随机流由以下信息共同寻址：

```text
世界种子 + Tick + 节点 + 模块 + 用途 + 实体 + 算法版本
```

框架不使用 `UnityEngine.Random` 或当前系统时间生成权威随机结果。

## 8. 命令状态机

命令使用 `CommandEnvelope` 传递，包含：

- 命令实例和定义 ID；
- actor、target 和 authority scope；
- 作用域内幂等键；
- 二进制载荷和提交 Tick。

主要状态为：

```text
Drafted → Submitted → Validating → Accepted
                                ↘ Rejected
Accepted → Reserving → Reserved → Dispatched
         → Executing → Completed → Settled
```

当前框架运行在 scaffold-only 模式，领域命令会稳定进入 `Rejected`，原因是 `implementation_unavailable`。这比伪造一次成功执行更容易审计。

## 9. Definition DB 和 Save DB

### Definition DB

- 作为 Unity `SQLiteAsset` 只读打开；
- 启用 `query_only`、外键和受限 schema；
- 保存拓扑、规则清单和节点模块组合；
- 加载时校验模块数量、名称、阶段和兼容别名；
- 内容散列必须与存档规则清单一致。

开发数据库由仓库根目录执行：

```bash
python3 tools/framework/build_runtime_definition.py
python3 tools/framework/validate_module_catalog.py
```

生成的 `.sqlite` 文件被 Git 忽略，不上传 GitHub。

### Save DB

存档路径：

```text
Application.persistentDataPath/ProjectRealm/Saves/<saveId>/save_<saveId>.sqlite
```

Save 使用：

- `WAL`；
- `synchronous=FULL`；
- 外键；
- 单写者事务；
- `integrity_check`；
- SQLite backup API 创建迁移前备份。

写入时最后更新 `save_manifest` 和当前检查点指针，避免出现可见的半套快照。

## 10. Unity 中如何使用

1. 用 Unity `6000.5.10f1` 打开 `game/`。
2. 生成开发 Definition DB。
3. 打开菜单：`Project Realm → Simulation → Framework Inspector`。
4. 在场景中创建 `UnitySimulationHost`。
5. 使用 `New` 或 `Load`。
6. 使用 `+Day`、`+Month`、`+Season`、`+Year` 显式推进。
7. 使用 `Save` 保存，再使用 `Load` 验证重载。

`UnitySimulationHost` 没有在 `Update()` 中推进世界。打开窗口、刷新窗口、搜索、分页和切换页签都不会改变世界状态。

## 11. 测试与验证入口

模块目录检查：

```bash
python3 tools/framework/validate_module_catalog.py
```

Unity EditMode 测试：

```text
Project Realm → Tests → Run All EditMode Tests
```

测试重点包括：

- 模块目录、别名、依赖环和唯一权威能力；
- 14 阶段顺序和失败回滚；
- 命令状态机、幂等键和 scaffold 拒绝；
- PCG32 与状态散列确定性；
- Definition 加载和 Save 保存/重载；
- 中断续跑与不中断运行的散列一致性。

## 12. 当前明确不包含的内容

- 真实人口和经济公式；
- 真实库存、资产、税收和市场结算；
- 历史精确控制关系；
- 商业数据授权结论；
- 玩家正式 UI；
- 后台多线程模拟和后台保存；
- Windows 构建验收和百年性能结论。

在上述能力真正实现并通过规则、数据和守恒测试之前，应继续显示 `Scaffold / Unavailable`。
