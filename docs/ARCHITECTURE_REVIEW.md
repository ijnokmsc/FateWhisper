# FateWhisper 架构审查报告

> 审查日期：2026-07-02 | 版本：v0.3.0.0 | 审查人：架构通

---

## 一、总体评价

**评分：7.0 / 10** — 架构整体合理，分层清晰，但对于当前复杂度已出现可维护性瓶颈。

FateWhisper 采用了经典的 **分层架构 + 事件驱动** 模式，对于一个 Dalamud 插件来说是恰当的选型。5 层分层（UI → 业务逻辑 → 通信 → 数据 → 框架）职责清晰，Models/Config/Services/UI/Commands 的目录结构符合直觉。但随着功能从最初的"MQTT 播报"扩展到"导航 + TTS + 跨服传送 + 状态机去重"，`Plugin.cs` 已膨胀为 God Object，成为最大的架构债务。

| 维度 | 评分 | 说明 |
|------|------|------|
| 分层清晰度 | 8.5 | 5 层分层明确，目录结构规范 |
| 单一职责 | 5.5 | Plugin.cs 承担过多职责，NotificationService 职责混合 |
| 可测试性 | 3.0 | 无接口抽象、无测试项目、静态属性注入 |
| 可扩展性 | 6.5 | 事件驱动模式可扩展，但事件连线集中在 Plugin.cs |
| 线程安全 | 5.5 | 部分使用 ConcurrentDictionary，但线程封送不一致 |
| 文档同步 | 4.0 | ARCHITECTURE.md 严重过时，仍为 SilverDasher 初版设计 |
| 资源管理 | 8.0 | Dispose 逆序释放 + 独立 try-catch，规范 |

---

## 二、架构优势（做得好的部分）

### 1. 分层架构清晰
```
UI 层 → 业务逻辑层 → 通信层 → 数据层 → Dalamud 框架层
```
依赖方向一致向下，无逆向依赖。UI 层不直接访问 MQTT，通信层不直接操作 ImGui。

### 2. 事件驱动的松耦合通信
服务间通过 `event Action<T>` 通信，而非直接调用。这使得：
- MobScannerService 和 NetworkInterceptor 互不感知，都通过事件向 NotificationService 报告
- MqttService 的接收事件可被任意订阅者消费
- 新增数据源（如未来的 PacketInterceptor）只需触发事件，无需修改现有服务

### 3. NotificationService 管线模式
通知处理采用 4 层过滤管线，职责明确：
```
大区过滤 → 订阅过滤 → 状态机去重 → 副本过滤 → 通知输出
```
每层独立判断，层间无副作用，易于理解和修改。

### 4. SubscriptionsStore 独立持久化
明智地绕过了 Dalamud PluginConfig 的序列化限制，将订阅列表（可能很大）存到独立 JSON 文件。避免了 PluginConfig 膨胀和序列化性能问题。

### 5. 双路猎怪检测的冗余设计
- **MobScannerService**：每 6 秒轮询 IObjectTable，可靠但延迟较高
- **NetworkInterceptor**：实时 Hook 网络包，快速但依赖 opcode 正确性

两者互为补充，NotificationService 的状态机去重确保不会重复通知。

### 6. Dispose 模式规范
逆序释放 + 每个服务独立 try-catch，确保单个服务释放失败不阻断其他服务。委托引用存储（`_frameworkUpdateHandler`）确保事件能正确注销。

### 7. 配置向后兼容
PluginConfig 的 `CopyFrom` + `LoadSafe` 机制、NotificationConfig 的 `PauseInDuty` → `MuteTtsInDuty` 迁移，体现了对配置演化的考虑。

---

## 三、架构问题（需要改进的部分）

### 问题 1：Plugin.cs God Object（严重）

**现状**：Plugin.cs 562 行，承担 6 重职责：

| 职责 | 行数 | 说明 |
|------|------|------|
| DI 组合根 | ~80 行 | 创建 11 个服务实例 |
| 事件连线 | ~100 行 | 8 组事件订阅，每组带 try-catch |
| 生命周期管理 | ~60 行 | Dispose 逆序释放 |
| 玩家信息同步 | ~80 行 | OnZoneInit/TryConnectMqttIfLoggedIn/OnLogin 重复逻辑 |
| MQTT 连接管理 | ~40 行 | Connect/Disconnect/Reconnect |
| 启动流程 | ~30 行 | StartAsync 异步初始化 |

**问题**：
- 构造函数 280+ 行，违反 SRP
- 玩家信息更新逻辑在 OnZoneInit、TryConnectMqttIfLoggedIn、构造函数中三处重复
- 事件处理 lambda 内联业务逻辑（如 `Task.Run(async () => await _mqttService.PublishHuntAsync(...))`），无法复用、无法测试
- 新增功能时必须修改 Plugin.cs（违反 OCP）

**建议**：拆分为 3 个类：

```
Plugin                    — 仅 IDalamudPlugin 入口 + DI 组合根 + Dispose
├── ServiceOrchestrator   — 事件连线 + 生命周期协调
├── PlayerContextManager  — 玩家信息同步 + MQTT 连接管理
└── EventHandlers         — 事件处理逻辑（封装 try-catch + Task.Run）
```

### 问题 2：无接口抽象（中等）

**现状**：所有 12 个 Service 都是具体类，构造函数注入具体类型。

**影响**：
- 无法 Mock 测试 NotificationService 的过滤逻辑
- 无法替换实现（如换用不同 MQTT 库）
- 依赖关系在编译时固定

**权衡**：对于 Dalamud 插件生态，接口抽象的价值取决于测试需求。如果项目计划长期维护，建议至少为 NotificationService、MqttService、NavigationService 提取接口。如果只是个人维护的小项目，可接受现状。

### 问题 3：线程安全不一致（中等）

**现状**：
- MQTT 事件回调在 **MQTTnet 内部线程** 触发
- MobScanner 事件在 **Framework 线程**（游戏主线程）触发
- NetworkInterceptor 事件在 **Hook 线程** 触发
- NotificationService 的 `_trackedStates` 使用 ConcurrentDictionary（正确）
- 但 `OnHuntBroadcast` 中的 `message.MobName = mobName` 等赋值不是线程安全的
- 导航弹窗事件用了 `Framework.RunOnFrameworkThread()`（正确），但通知输出没有封送

**风险**：并发修改 HuntMessage 实例属性可能导致数据竞争。虽然 ImGui 操作必须封送到主线程，但 ChatGui.Print 和 ToastGui.ShowNormal 的线程安全性未明确。

**建议**：在 NotificationService 入口处统一封送到 Framework 线程，或使用锁保护消息实例的修改。

### 问题 4：ARCHITECTURE.md 严重过时（中等）

**现状**：docs/ARCHITECTURE.md 仍为 SilverDasher 初版设计文档：
- 标题写的是 "SilverDasher — 系统架构设计文档"
- 描述 4 个 Tab（实际 7 个）
- 无 NavigationService、MobScannerService、TtsService、SubscriptionsStore
- 无跨服传送、导航、TTS 功能描述
- 待明确事项（UNCLEAR）部分已被实际实现解答但未更新
- 文件列表中的 OverlayWindow.cs 不存在

**建议**：重写 ARCHITECTURE.md 以反映 v0.3.0.0 的实际架构。

### 问题 5：玩家信息同步逻辑重复（中等）

**现状**：玩家信息（CharacterName、WorldId、WorldName、Datacenter、DcLabel）的更新逻辑出现在 3 处：

| 位置 | 触发时机 |
|------|----------|
| Plugin 构造函数 (L157-171) | 插件加载时 |
| OnZoneInit (L349-408) | 换区时 |
| TryConnectMqttIfLoggedIn (L432-471) | 启动时检测已登录 |

每处都做了类似的：读取 LocalPlayer → 更新 Config → 更新 NavigationService → 更新 MqttService。

**建议**：提取为 `PlayerContextManager.UpdatePlayerInfo()` 方法，3 处调用统一委托。

### 问题 6：事件处理代码重复（轻微）

**现状**：Plugin.cs 中 8 组事件订阅，每组都遵循相同模式：
```csharp
_service.Event += msg => {
    try { _notificationService.OnXxxBroadcast(msg); }
    catch (Exception ex) { Log.Error(...); }
    _ = Task.Run(async () => {
        try { await _mqttService.PublishXxxAsync(...); }
        catch (Exception ex) { Log.Error(...); }
    });
};
```

**建议**：提取通用事件订阅辅助方法：
```csharp
private static Action<T> Wrap<T>(Action<T> handler, string label) => msg => {
    try { handler(msg); }
    catch (Exception ex) { Log.Error($"{Prefix} {label}异常: {ex.Message}"); }
};
```

### 问题 7：NavigationService 依赖混乱（轻微）

**现状**：NavigationService 构造函数同时接收 `IDataManager`（Dalamud 游戏数据）和 `DataManager`（自定义静态数据）：
```csharp
public NavigationService(
    ...,
    IDataManager dataManager,      // Dalamud 游戏数据
    DataManager _fateDataManager,  // 自定义数据管理 ← 命名冲突
    ...
)
```
字段名 `_fateDataManager` 暗示只用于 FATE，但实际用途更广。命名容易混淆。

**建议**：重命名参数为 `gameDataManager` 和 `staticDataManager` 以区分用途。

### 问题 8：无测试（轻微，视项目定位而定）

**现状**：无 C# 测试项目。10 个 Python 脚本用于逆向验证，非自动化测试。

**影响**：
- NotificationService 的过滤管线（大区/订阅/去重/副本）是最适合单元测试的逻辑，但完全无覆盖
- 配置迁移逻辑（CopyFrom、向后兼容）无回归测试
- 坐标转换公式（`x_int = (gameCoord * 0.02 + 21.5) * 100`）无验证测试

**建议**：如果项目计划长期维护，至少为 NotificationService 和 DataManager 的纯逻辑方法添加单元测试。

### 问题 9：项目根目录混杂 Python 脚本（轻微）

**现状**：10 个 test_*.py 脚本散落在项目根目录，与 C# 源码混合。

**建议**：移动到 `scripts/` 或 `tools/reverse-engineering/` 目录，保持 C# 项目根目录整洁。

---

## 四、架构决策评估

### ADR-001：Dalamud API 15 静态属性注入

**评估：正确决策**

使用 `[PluginService] internal static` 是 API 15 的标准模式。虽然 static 属性允许任意代码通过 `Plugin.ClientState` 绕过构造函数注入，但所有 Service 实际上都通过构造函数接收依赖，没有滥用 static 访问器。这是 Dalamud 生态的最佳实践。

**权衡**：牺牲了可测试性（static 属性难以 Mock），换取了 API 15 的简洁性。对于插件生态可接受。

### ADR-002：MQTTnet 4.2.1 而非最新版

**评估：合理决策**

与 ACT 版 SilverDasher 保持版本一致，确保 MQTT 协议行为一致。MQTTnet 4.x 的 API 在 4.2-4.x 间稳定，无升级压力。

**风险**：MQTTnet 5.0 已发布，未来可能需要迁移。但当前版本稳定可用。

### ADR-003：双路猎怪检测

**评估：合理但需协调**

MobScanner（轮询）+ NetworkInterceptor（Hook）的冗余设计提供了可靠性保障。但两个服务之间无协调机制，完全依赖 NotificationService 的状态机去重。如果去重逻辑出现 bug，会导致重复通知。

**建议**：考虑在 MobScanner 中检查 NetworkInterceptor 是否已检测到同一猎怪，避免重复扫描和事件触发。

### ADR-004：独立订阅持久化

**评估：正确决策**

SubscriptionsStore 使用独立 JSON 文件绕过 Dalamud PluginConfig 序列化，避免了：
- PluginConfig 序列化大列表的性能问题
- 配置版本迁移时的订阅数据丢失风险
- 订阅数据与配置数据的耦合

### ADR-005：导航状态机集成多插件 IPC

**评估：功能正确但耦合度高**

NavigationService 同时依赖 3 个外部插件的 IPC：
- vnavmesh（寻路）
- Lifestream（跨服传送）
- DailyRoutines（FastWorldTravel 管理）

每个 IPC 调用都需要可用性检查 + 错误处理。状态机逻辑中穿插大量 IPC 可用性判断。

**建议**：提取 `NavigationIpcAdapter` 封装所有 IPC 交互，NavigationService 只依赖抽象接口。

---

## 五、改进优先级

| 优先级 | 问题 | 建议动作 | 影响范围 |
|--------|------|----------|----------|
| P0 | Plugin.cs God Object | 拆分为 Plugin + ServiceOrchestrator + PlayerContextManager | Plugin.cs 全文件 |
| P1 | 玩家信息同步重复 | 提取 PlayerContextManager.UpdatePlayerInfo() | Plugin.cs 3 处 |
| P1 | ARCHITECTURE.md 过时 | 重写架构文档 | docs/ |
| P2 | 事件处理代码重复 | 提取通用事件订阅辅助方法 | Plugin.cs 事件连线段 |
| P2 | 线程安全不一致 | NotificationService 入口统一封送 | NotificationService |
| P2 | NavigationService 依赖命名 | 重命名参数消除歧义 | NavigationService |
| P3 | 无接口抽象 | 为核心服务提取接口（可选） | 全项目 |
| P3 | 无测试 | 为 NotificationService 添加单元测试（可选） | 新增测试项目 |
| P3 | Python 脚本散落 | 移动到 scripts/ 目录 | 项目根目录 |

---

## 六、推荐的重构路径

### 阶段 1：Plugin.cs 瘦身（P0 + P1）

```
当前：
Plugin (562 行)
├── DI 组合根
├── 事件连线 (100+ 行)
├── 玩家信息同步 (80+ 行 × 3 处)
├── MQTT 连接管理
├── Dispose (60 行)
└── 启动流程

目标：
Plugin (150 行)
├── [PluginService] 声明
├── DI 组合根（创建 ServiceOrchestrator）
├── Dispose（委托给 ServiceOrchestrator）
└── 入口委托

ServiceOrchestrator (200 行)
├── 服务实例创建
├── 事件连线（使用辅助方法）
├── 生命周期协调
└── Dispose 逆序释放

PlayerContextManager (100 行)
├── UpdatePlayerInfo() — 统一玩家信息同步
├── OnZoneInit / OnLogin / OnLogout — 调用 UpdatePlayerInfo
└── MQTT 连接管理
```

### 阶段 2：文档更新 + 代码清理（P1 + P2）

- 重写 ARCHITECTURE.md 反映 v0.3.0.0 架构
- 提取事件订阅辅助方法消除重复
- 重命名 NavigationService 参数
- 移动 Python 脚本到 scripts/

### 阶段 3：可测试性提升（P3，可选）

- 为 NotificationService、MqttService、NavigationService 提取接口
- 创建 FateWhisper.Tests 项目
- 为 NotificationService 过滤管线编写单元测试
- 为坐标转换公式编写验证测试

---

## 七、总结

FateWhisper 的架构在 Dalamud 插件生态中属于 **中上水平**。分层清晰、事件驱动、Dispose 规范，这些基础打得很扎实。主要的架构债务集中在 Plugin.cs 的 God Object 问题上——这是一个典型的"从简单项目有机生长到中等复杂度"的案例，入口类自然膨胀但没有及时拆分。

**核心建议**：优先执行 Plugin.cs 瘦身（阶段 1），这是投入产出比最高的改进。拆分后每个类职责单一，后续添加功能时修改面缩小，可维护性显著提升。其余改进可根据项目维护节奏逐步推进。
