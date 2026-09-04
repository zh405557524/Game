# Project Realm Simulation Code 导读

本文专门介绍 `com.projectrealm.simulation` 包中的模拟代码：代码从哪里进入、各层负责什么、一次 Tick 如何运行，以及账本、生产、政府决策等领域模型目前接入到了什么程度。

> 当前结论：`WorldRuntime`、14 阶段 Tick、命令状态机、确定性状态和持久化契约已经组成可运行的世界模拟骨架；规范模块仍由 `ScaffoldModuleExecutor` 空跑。账本、生产结算、政府决策和年度分层结算已经有领域代码与测试，但尚未接入主世界 Tick。

## 1. 先建立一个整体认识

Simulation Code 分成两条代码链：

```text
主运行链（已接入世界运行）
WorldRuntime
  └─ TickCoordinator
      ├─ CommandProcessor
      ├─ ModuleRegistry
      ├─ IModuleExecutor
      └─ CommittedState / WorkingState

领域结算链（已有模型和测试，尚未接入主 Tick）
SimulationDriverRecord
  └─ ProductionSettlementService
      └─ EconomicAccountingService
          └─ SettlementLedgerService
              └─ GovernmentReportService
                  └─ GovernmentDecisionService
```

这一区分很重要：

- “已接入”表示通过 `WorldRuntime.Advance()` 推进世界时会执行。
- “已有领域代码”表示对象、校验、算法和测试已经存在，但尚未由 `TickCoordinator` 调用。
- “Scaffold”表示调用链存在，但没有真实人口、库存、税收或经济结果。

## 2. 目录与程序集

```text
com.projectrealm.simulation/
├── Runtime/
│   ├── Domain/        # 纯 C# 领域状态、规则对象和确定性算法
│   ├── Ports/         # 数据库、存档、Codec、执行器等抽象边界
│   └── Application/   # 世界启动、推进、命令和诊断编排
└── Tests/             # Simulation Core 的 EditMode 测试
```

程序集依赖为：

```text
ProjectRealm.Application
        ↓
ProjectRealm.Ports
        ↓
ProjectRealm.Domain
```

- `ProjectRealm.Domain` 设置了 `noEngineReferences=true`，不能引用 Unity。
- `ProjectRealm.Ports` 只定义外部能力接口。
- `ProjectRealm.Application` 负责流程协调，不直接实现 SQLite 或 Unity 行为。
- SQLite 和 Unity 适配代码位于 `Assets/ProjectRealm/`，不属于本包的领域核心。

## 3. 推荐阅读顺序

第一次阅读时建议依次打开：

1. `Runtime/Application/WorldRuntime.cs`：模拟系统的公开入口。
2. `Runtime/Application/TickCoordinator.cs`：一个日 Tick 的完整执行过程。
3. `Runtime/Domain/WorldTimeAndPersistence.cs`：时钟、阶段、检查点和随机流描述。
4. `Runtime/Domain/SimulationState.cs`：权威状态、工作状态、结果和散列。
5. `Runtime/Domain/ModuleSystem.cs`：模块目录、实例、生命周期和执行器。
6. `Runtime/Application/CommandProcessor.cs`：命令校验、预留、分发和执行。
7. `Runtime/Domain/SettlementLedgers.cs`：结算账本及守恒校验。
8. `Runtime/Domain/SimulationDriversAndAccounting.cs`：驱动、生产和经济核算。
9. `Runtime/Domain/GovernmentDecisionCycle.cs`：报告、预算、计划和政策反馈。
10. `Runtime/Domain/LayeredSettlementYearProbe.cs`：分层年度结算验证场景。

## 4. 主入口：WorldRuntime

`WorldRuntime` 是 Application 层对 Unity、测试和其他调用方暴露的统一外观。它持有：

- `WorldClock`：当前世界日期和 Tick 序号；
- `WorldTopology`：地理、势力、管辖和所有权；
- `ModuleCatalog`：规范模块定义；
- `ModuleRegistry`：当前世界中的模块实例；
- `CommittedState`：最近一次成功 Tick 的权威状态；
- `CommandProcessor`：命令、状态历史和资源预留；
- `WorldCheckpoint`：可审计的状态散列检查点；
- 最近一次模块结果、节点结算结果和节点快照。

主要公开方法：

| 方法 | 用途 |
| --- | --- |
| `Advance(AdvanceRequest)` | 按日、月、季或年推进 |
| `AdvanceOneDay()` | 兼容旧 `SimulationSession` 的单日入口 |
| `SubmitCommand(CommandEnvelope)` | 把命令加入队列，等待后续 Tick 处理 |
| `Save()` | 通过 `ISaveGameStore` 保存闭合状态 |
| `ExportSaveData()` | 生成不依赖 SQLite 的存档 DTO |
| `CurrentStateHash` | 计算当前权威状态的 SHA-256 |

月、季、年推进不会直接跳日期。`Advance()` 会连续执行日 Tick，直到遇到相应的周期闭合边界。

## 5. 一次日 Tick 如何执行

`WorldRuntime.Advance()` 最终调用：

```text
WorldRuntime.Advance
  → AdvanceDayInternal
    → TickCoordinator.ExecuteDay
      → 创建候选时钟
      → 复制 WorkingState
      → 克隆 CommandProcessor
      → 冻结 TickTopologySnapshot
      → 顺序执行 14 个阶段
      → 成功：Commit + Hash + Snapshot + Checkpoint
      → 失败：Rollback，返回原时钟、原状态和原命令处理器
```

固定阶段如下：

| 阶段 | 当前框架职责 |
| --- | --- |
| `S00 FreezeTopology` | 冻结本 Tick 的节点和模块集合 |
| `S10 CollectDueWork` | 收集到期工作 |
| `S20 PrepareInputs` | 准备模块输入 |
| `S30 LocalFactSettlement` | 本地事实结算入口 |
| `S40 UpwardAggregation` | 向上聚合入口 |
| `S50 SnapshotClose` | 闭合事实快照入口 |
| `S60 PerceptionBuild` | 构建认知入口 |
| `S70 DecisionPlanning` | 决策规划入口 |
| `S80 CommandValidation` | 校验待处理命令 |
| `S90 ReservationCommit` | 提交资源预留 |
| `S100 CommandDispatch` | 分发已预留命令 |
| `S110 ImmediateExecution` | 执行即时命令 |
| `S120 EventCommit` | 事实提交后的事件入口 |
| `S130 AuditAndCheckpoint` | 审计与检查点入口 |

每个阶段中，`TickCoordinator` 会按稳定顺序遍历活动模块。只有模块定义声明参与该阶段时，才会创建执行器并调用 `Execute()`。

## 6. 状态与回滚

核心状态对象位于 `SimulationState.cs`：

- `CommittedState`：只读权威状态；
- `WorkingState`：单个 Tick 内的可写副本；
- `StateDelta`：记录本 Tick 的设置和删除；
- `StateRecord`：用 `codecId + byte[] payload` 保存模块状态；
- `WorldTickResult`：一次 Tick 的阶段、模块和节点结果。

事务边界不仅包含模块状态，也包含时钟和命令处理器：

```text
Tick 开始
  原 Clock ───────────────┐
  原 CommittedState ──────┼─ 保持不变
  原 CommandProcessor ────┘

  候选 Clock
  WorkingState
  CommandProcessor Clone
        │
        ├─ 全部成功 → 一次性替换权威字段
        └─ 任意失败 → 全部丢弃
```

因此失败 Tick 不会留下半完成的日期、预留或模块状态。

## 7. 模块系统

`ModuleSystem.cs` 中的主要对象：

| 对象 | 责任 |
| --- | --- |
| `ModuleDefinition` | 模块 ID、版本、阶段、依赖和能力声明 |
| `ModuleInstance` | 模块在某个节点上的实例和生命周期 |
| `ModuleCatalog` | 规范定义、别名解析和目录查询 |
| `ModuleRegistry` | 实例集合、依赖检查和唯一权威能力检查 |
| `ModuleExecutionContext` | Tick、阶段、时钟、实例和 Working State |
| `IModuleExecutor` | 模块执行协议 |

当前 `DefaultModuleExecutorFactory` 为规范模块返回 `ScaffoldModuleExecutor`。它会稳定返回：

```text
Succeeded          = true
ImplementationTier = Scaffold
DataQuality         = Unavailable
ReasonCode          = implementation_unavailable
```

这里的 `Succeeded` 只表示框架调用没有异常，不能解释成业务结算成功，更不能把 `Unavailable` 当成数值零。

## 8. 命令与权限

命令分为两层：

### 运行时命令状态机

`CommandAndEventContracts.cs` 与 `CommandProcessor.cs` 管理：

```text
Drafted → Submitted → Validating → Accepted / Rejected
Accepted → Reserving → Reserved → Dispatched
Dispatched → Executing → Completed → Settled
```

命令使用“权限作用域 + 幂等键”去重。当前 scaffold-only 模式会在校验阶段以 `implementation_unavailable` 稳定拒绝领域命令。

### 领域权限判断

`IdentityAuthority.cs` 和 `CommandAuthority.cs` 描述：

- 人物身份和职位；
- 职位在某个范围内的有效授权；
- 人物对目标的认知与影响关系；
- 直接命令、请求协商或不可用三种判断结果。

`CommandAuthorityEvaluator` 当前由独立测试覆盖，尚未接入 `CommandProcessor.ValidatePending()`。

## 9. 确定性

### 状态散列

`DeterministicStateHasher` 会稳定排序并写入世界 ID、种子、时钟、拓扑、模块和状态载荷，最后计算 SHA-256。

### 随机数

`Pcg32` 提供版本化随机算法。随机流由以下键共同定位：

```text
世界种子 + Tick + 节点 + 模块 + 用途 + 实体 + 算法版本
```

权威模拟不应使用 `UnityEngine.Random`、系统当前时间或不稳定集合遍历顺序。

## 10. 结算、生产与经济核算

这一部分已有领域实现和测试，但尚未挂入 `TickCoordinator`。

### SimulationDriversAndAccounting.cs

- `SimulationDriverRecord`：天气、灾害、战争、政策等驱动；
- `SimulationDriverEffect`：产量乘数、损耗率、劳动力和运输能力等效果；
- `SectorProductionActivity`：某个经营者在特定范围内的生产活动；
- `ProductionSettlementService`：应用有效驱动，生成生产结算；
- `EconomicAccountingService`：形成部门核算和产出法/支出法差额。

政策或人物行动产生的驱动只能影响之后的完整月度周期，不能回写已经闭合的事实。

### SettlementLedgers.cs

账本支持：

- 月度流量、期初、流入、生产、消费、损耗和期末；
- 财政应收、实收、在途、欠款、债务和现金；
- 产能、库存、经济部门和军事物资；
- 子节点账本、父级残差账本和合并抵消；
- 12 个月闭合为年度账本；
- 粮食、财政和物资等守恒误差检查。

`SettlementLedgerService` 的主要调用顺序是：

```text
CloseMonth
  → ConsolidateChildLedgers
    → CloseYear
```

## 11. 政府报告与决策

`GovernmentDecisionCycle.cs` 把“真实账本”和“政府看到的报告”分开：

```text
AnnualClosingLedger（权威事实）
  → GovernmentReportService
  → GovernmentReport（延迟、不完整、带置信度）
  → GovernmentDecisionService.BuildDecisionPacket
  → PlanNextYear
  → AdjustMonthlyPlan
  → ScheduleFundedEdictEffect
  → 下一完整周期的 SimulationDriverRecord
```

设计意图是政府不能直接读取全知视角。报告可能延迟、缺失或失真，决策只能基于报告和政府真实掌握的财政资源。

这套代码目前在领域测试和年度验证构造器中使用，尚未成为 `S60`、`S70` 阶段的正式模块执行器。

## 12. LayeredSettlementYearProbe 是什么

`LayeredSettlementYearProbe` 是确定性的年度验证场景，不是正式玩法入口。它用固定情景验证：

- 村级详细结算；
- 县级 cohort/residual 结算；
- 区域级 aggregate/residual 结算；
- 粮食生产、消费、再生产、租赋、市场购买和损耗；
- 白银、债务、税收应收与上缴的对应关系；
- 月度账本合并和年度闭合；
- 相同输入得到相同 `StateFingerprint`。

它的价值是证明分层账本和守恒规则可以工作，但不能证明全国 1,168 个县已经在主世界中执行了这些经济公式。

## 13. Ports 与外部实现

`Runtime/Ports/` 只定义边界：

| 接口 | 外部实现职责 |
| --- | --- |
| `IWorldDefinitionReader` / `IWorldDefinitionStore` | 读取世界 Definition |
| `ISaveGameStore` | 保存和读取世界存档 |
| `IModuleStateCodec` | 编解码版本化模块状态 |
| `IModuleExecutorFactory` | 为模块选择执行器 |
| `ISimulationDiagnosticsSink` | 接收阶段、模块和命令诊断 |

SQLite 实现在 `Assets/ProjectRealm/Infrastructure/Sqlite/`，Unity 生命周期入口在 `Assets/ProjectRealm/Framework/`。

## 14. 测试对应关系

| 测试文件 | 主要覆盖内容 |
| --- | --- |
| `FrameworkKernelTests.cs` | 模块目录、Tick、回滚、命令、散列和随机数 |
| `SimulationCoreTests.cs` | 旧 API、稳定 ID、种子等基础契约 |
| `CommandAuthorityTests.cs` | 身份、职位、直接授权与协商路径 |
| `SettlementLedgerAndDecisionTests.cs` | 月/年账本、生产核算、报告和政府决策 |
| `LayeredSettlementYearProbeTests.cs` | 12 个月分层结算、守恒和确定性 |

模块目录校验命令：

```bash
python3 tools/framework/validate_module_catalog.py
```

完整 EditMode 测试应通过 Unity Test Runner 执行。

## 15. 新增模拟规则时应该放在哪里

- 新的纯领域值对象或守恒规则：`Runtime/Domain/`。
- 新的世界用例和跨对象编排：`Runtime/Application/`。
- 数据库、文件或平台能力：先在 `Runtime/Ports/` 定义接口，再到 `Assets/ProjectRealm/Infrastructure/` 实现。
- Unity 生命周期和编辑器工具：`Assets/ProjectRealm/Framework/`。
- 新规范模块：先进入模块目录和组合校验，再实现对应 `IModuleExecutor`。

接入正式 Tick 前至少需要确认：

1. 输入只来自闭合状态或明确的阶段产物；
2. 写入只发生在 `WorkingState`；
3. 失败能触发整个 Tick 回滚；
4. 遍历顺序、ID 和随机键稳定；
5. 输出携带正确的 `DataQuality`；
6. 状态 Codec 有明确版本；
7. 保存重载后散列与不中断运行一致。

## 16. 当前边界

当前代码已经提供可编译、可推进、可保存和可审计的 Simulation 骨架，也提供了若干经济与决策领域模型。它目前不代表：

- 正式人口演化已经实现；
- 全国节点已经执行真实经济结算；
- 账本和政府决策已经接入 14 阶段主 Tick；
- Scaffold 模块能够处理真实玩家命令；
- 历史数据、商业授权或玩家 UI 已经完成。

判断功能是否真正生效时，应从 `WorldRuntime → TickCoordinator → IModuleExecutor` 追踪实际调用，不要只根据某个领域类存在就下结论。
