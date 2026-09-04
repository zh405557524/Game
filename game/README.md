# Project Realm Unity 工程

本目录是 Project Realm 的 Unity 工程根目录，不再增加同名子目录。

## 工具链基线

- Unity Editor：`6000.5.10f1`（macOS ARM64）
- 模板：Universal 3D / URP
- URP：`17.5.0`
- API 兼容级别：.NET Standard 2.1
- 当前构建与运行验收平台：macOS ARM64
- 后续商业目标平台：Windows x64；待 Windows 开发机或 CI 就绪后验证

外部 `.NET 10 LTS` SDK 仅用于后续的无 Unity 压力测试和数据工具，不影响首次打开 Unity 工程。

## 首次打开

1. 打开 Unity Hub。
2. 选择 `Projects` → `Add`。
3. 选择本目录：`/Users/soul/code/soul/Game/game`。
4. 确认 Editor 为 `6000.5.10f1` 后打开。

不要使用其他 Unity 版本打开并自动升级工程。升级版本前必须先更新 ADR，并完成存档、渲染、SQLite 和构建回归。

## 工程边界

```text
Packages/com.projectrealm.simulation/
  Runtime/Domain/       纯 C# 领域状态与规则
  Runtime/Ports/        存储、内容和外部服务抽象
  Runtime/Application/  用例与协调逻辑
  Tests/                Simulation Core 单元测试

Assets/ProjectRealm/
  Presentation/         地图、相机、UI、Shader 与表现
  ApplicationAdapters/  Unity 生命周期和输入适配
  Infrastructure/       SQLite、文件和资源适配
  Content/              发布内容与配置
  Editor/               导入器、验证器和编辑器工具
  Tests/EditMode/        Unity 集成测试
```

`ProjectRealm.Domain`、`ProjectRealm.Ports` 和 `ProjectRealm.Application` 的程序集定义启用了 `noEngineReferences`，不能引用 `UnityEngine`。

## 批处理编译

从仓库根目录运行：

```bash
'/Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/MacOS/Unity' \
  -batchmode -nographics -quit \
  -projectPath '/Users/soul/code/soul/Game/game' \
  -logFile '/Users/soul/code/soul/Game/builds/unity-compile.log'
```

## EditMode 测试

```bash
'/Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/MacOS/Unity' \
  -batchmode -nographics \
  -projectPath '/Users/soul/code/soul/Game/game' \
  -runTests -testPlatform EditMode \
  -testResults '/Users/soul/code/soul/Game/builds/unity-editmode-results.xml' \
  -logFile '/Users/soul/code/soul/Game/builds/unity-editmode.log'
```

Unity 已打开时，也可选择菜单 `Project Realm` → `Tests` → `Run All EditMode Tests`，结果会显示在 Test Runner 和 Console 中。

## macOS Development Build

```bash
'/Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/MacOS/Unity' \
  -batchmode -nographics -quit \
  -projectPath '/Users/soul/code/soul/Game/game' \
  -executeMethod ProjectRealm.EditorTools.ProjectRealmBuild.BuildMacOSDevelopment \
  -buildOutput '/Users/soul/code/soul/Game/builds/ProjectRealm-macOS/Project Realm.app' \
  -logFile '/Users/soul/code/soul/Game/builds/unity-build-macos.log'
```

必须纳入版本管理：`Assets/`、`Packages/`、`ProjectSettings/`。不得提交：`Library/`、`Temp/`、`Logs/`、`UserSettings/` 和本地构建产物。

## 县级地图样板

Unity 菜单选择 `Project Realm` → `Map` → `Create or Open County Map Prototype`，会创建并打开：

- 场景：`Assets/Scenes/Debug/Map/90_Integration/CountyMapPrototype/CountyMapPrototype.unity`
- 可编辑样板数据：`Assets/ProjectRealm/Development/TestData/Map/90_Integration/CountyMapPrototype/CountyMapPrototype.asset`

样板包含平原、丘陵、山地、盆地、主河/支流、道路、10 个聚落节点，以及县/乡/村三级边界。运行时可使用鼠标滚轮缩放，按住鼠标中键或右键拖动地图，也可使用 `WASD` 或方向键移动。聚落标签按县城、镇、村三级语义缩放显示。

## 场景与调试数据管理

入口：`Project Realm → Debug → Map Debug Workbench`。

- [场景分类](Assets/Scenes/README.md)：正式运行、手动练习、按十二图层划分的单项调试、组合对照和历史备份。
- [测试输入与生成输出](Assets/ProjectRealm/Development/README.md)：独立于场景和正式 `Content`。
- 练习场景：`Assets/Scenes/Learning/MapLearning/MapLearning.unity`。
- 练习数据：`Assets/ProjectRealm/Development/TestData/Learning/MapLearning/`。
- 地形先按平原、丘陵、山地、高原、盆地分别调试，人工确认后再组合；目录存在和自动测试通过均不表示美术验收通过。

## 山地原图引导小样

山地原图引导小样的旧记录见 [MountainLookdevV1](../docs/90_资料与归档/04_地图表现旧流程/旧流程产物/05_单项调试/Mountain/MountainLookdevV1/README.md)，菜单 `Project Realm → Debug → Mountain → Open Mountain Lookdev V1` 仍可用于历史候选对照。它不替换五阶段流程；当前正式山地Unity方案、材质和场景均为 `NotStarted`。

## 水系独立制作

水系分为河流、溪流、湖泊、池塘、湿地、海岸。六类现均有独立样板，不再只有空文件夹；均处于视觉调试阶段。

打开：`Project Realm → Debug → Water → Create or Open River Study`。点 Play 后，`1/2/3` 切换水面、流向、河床；WASD 平移，滚轮缩放，F 复位，Space 暂停/继续水纹。底部提供近景/全景按钮。

输入与输出独立保存在 `Development/TestData/Map/02_Water/01_River/` 和 `Development/Generated/Map/02_Water/01_River/<revision>/`。修改输入后，退出 Play，用 `Rebuild River Study From Input` 重建；旧场景先备份，旧输出保留。

其余五类：在同一 Water 菜单选择 `Open Stream/Lake/Pond/Wetland/Coast Study`，或打开 `Assets/Scenes/Debug/Map/02_Water/` 对应文件夹中的场景。点 Play 后，`1/2/3` 切换正常、水深分区、隐藏水面；底部提供近景、全景、俯视按钮，`V` 隐藏尺度参照物。

这五类输入、生成资源也按同名编号分别存放。修改输入后退出 Play，选择 `Rebuild Current Water Body Study`；打开场景不会重建，也不会覆盖练习场景。`Create Missing Five Water Studies` 只补不存在的场景；`Rebuild Five Water Body Studies` 会备份并重建这五类，不重建河流。

[水系设计方案](../docs/03_美术风格/01_地图设计方案/02_水系/README.md) · [旧六类水系与分项验收记录](../docs/90_资料与归档/04_地图表现旧流程/旧流程产物/05_单项调试/Water/README.md)。旧105/105 EditMode测试通过不等于当前五阶段美术验收通过；尚未连接成正式完整水系。
