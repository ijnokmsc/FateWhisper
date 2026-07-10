# 副本检测漏检 & TTS 偶发不播 — 诊断与修复记录

> 关联需求：检查副本过滤/副本检测代码（副本中收到通知）、检测 TTS 代码（有几率 TTS 未播报）
> 日期：2026-07-02
> 涉及文件：`DutyMonitor.cs`、`NotificationService.cs`、`TtsService.cs`、`DataManager.cs`、`TerritoryInfo.cs`、`ServiceOrchestrator.cs`

---

## 一、问题 1：副本中收到了通知信息（副本检测漏检）

### 1.1 症状
玩家在副本/实例内仍然收到猎怪 / FATE 的 ChatLog / Toast / TTS 播报。

### 1.2 根因分析

`DutyMonitor.IsInDuty` 的实现为：

```csharp
private const uint DutyTerritoryThreshold = 1000;
public bool IsInDuty => _clientState.TerritoryType >= DutyTerritoryThreshold;
```

即「区域 ID ≥ 1000 视为副本」。但通过对 `territories.json`（1266 条区域）的核查：

| 指标 | 数量 |
|------|------|
| 带 `content` 字段（= 游戏 `ContentFinderCondition` 链接）的区域 | **778 / 1266** |
| 其中 `content != 0` 且 **ID < 1000** 的真实副本 | **559** |
| 其中 `content != 0` 且 ID ≥ 1000 的副本 | 219 |
| 野外/城镇（128 利姆萨、130 乌尔达哈、137 东拉诺西亚…） | 无 `content`，正确判为非副本 |

典型被漏检的副本（ID < 1000）：

- `142` 日影地修炼所（`content=81`）
- `149` 狼狱演习场（`content=128`）
- `159` 放浪神古神殿（`content=10`）
- `167` 无限城古堡（`content=14`）

**结论**：`≥ 1000` 阈值是错误的启发式。游戏判定「是否为副本/实例」的权威信号是 `TerritoryType` 表里的 `ContentFinderCondition` 字段是否为 0 —— 我们的 `territories.json` 已携带该字段（`content`）。旧阈值导致 **559 个副本被误判为野外**，于是 `MuteNotificationInDuty` / `MuteTtsInDuty` 这些静音开关根本不会生效，通知与 TTS 直接泄漏进副本。

> 补充：`NotificationService.OnHuntBroadcast/OnFateBroadcast` 中「第4层：副本状态过滤」处的 `var isInDuty = _dutyMonitor.IsInDuty;` 是**死代码**——计算后从未用于提前 return，实际静音逻辑只在 `SendNotification` 内按 `Mute*InDuty` 生效。这也是为什么即便检测对了，原代码看起来「好像有副本过滤却没生效」。

### 1.3 ADR：改用 `content` 字段判定副本

```markdown
# ADR-010: 副本判定改用 territories.json 的 content 字段

## Status
Accepted

## Context
旧实现用 TerritoryType >= 1000 作为副本阈值，但 559 个真实副本 ID < 1000，
导致副本内静音失效、通知/TTS 泄漏。territories.json 已含 content 字段
（游戏 ContentFinderCondition 链接），content != 0 即副本。

## Decision
- TerritoryInfo 增加 int Content 属性；
- DataManager 增加 IsDutyTerritory(uint)：content != 0 ⇒ 副本；
- DutyMonitor 注入 DataManager，IsInDuty 改用该判定，移除 >= 1000 常量；
- 未知区域（不在数据中）保守返回 false（不静音），避免误伤野外通知。

## Consequences
+ 副本判定准确率从「部分」提升到「与游戏数据一致」（778 个 content 区域全识别）；
+ 修复 559 个 <1000 副本的静音失效；
+ 极少数带 content 的野外/特殊区（如 190 黑衣森林中央林区 content=43、500 库尔札斯 content=437）
  会被判为副本并静音——这是「少漏报」对「偶尔多静音」的可接受权衡；
- 依赖 territories.json 的 content 字段准确性（远程热更新会覆盖，数据完整性由数据源保证）。
```

### 1.4 未改动项（需用户确认）
`MuteNotificationInDuty` 默认值为 **false**（ChatLog/Toast 默认副本内不静音），`MuteTtsInDuty` 默认为 **true**（TTS 默认副本内静音，本次检测修复后已正确生效）。
若用户希望「副本内完全无通知」，需要把 `MuteNotificationInDuty` 默认值改为 `true`，或在 UI 中开启该开关。本次**未改动默认行为**。

---

## 二、问题 2：TTS 有几率未播报

### 2.1 症状
猎怪 / FATE 通知触发时，TTS 偶发完全不发声，无明显规律。

### 2.2 根因分析

`NotificationService.SendNotification` 的 TTS 分支：

```csharp
_tts.Stop();
_ = Task.Run(() => _tts.SpeakAsync(ttsText));
```

而 `TtsService.SpeakAsync` 内部：

```csharp
_cts?.Cancel();
_cts = new CancellationTokenSource();
await Task.Run(() => Edge_tts.PlayText(option, _voice), token);
```

**竞态 + 设备争用**：
1. `Edge_tts.PlayText` 内部使用 NAudio 的单一输出设备。
2. 当多条通知短时间内到达（同目标 healthy→taunted→dying→died，或多个目标并发），`Stop()` 取消上一段后立刻 `Task.Run` 启动新的 `PlayText`，**多个 `PlayText` 并发争用同一个 NAudio 设备**。
3. 设备被占用时新 `PlayText` 静默失败（被 catch 吞掉或取消），表现为「有几率不播」。
4. 高频场景下，上一段尚未结束就被取消，导致大量播报被丢弃。

### 2.3 ADR：TTS 串行单消费者队列

```markdown
# ADR-011: TTS 改用串行单消费者队列

## Status
Accepted

## Context
并发 PlayText 争用单 NAudio 设备导致间歇性静音与播报丢弃。

## Decision
TtsService 重写为 Channel<string> 单消费者模型：
- SpeakAsync 仅入队（同步、极快），不再自行 Task.Run；
- 后台消费者串行 await PlayText，同一时刻只有一个在播放；
- 新文本入队排队，消除设备争用；
- SpeakAsync 仍返回 Task（兼容 TestTts 的 await）。

## Consequences
+ 彻底消除设备争用 → 不再有「有几率不播」；
+ 播报按到达顺序串行播放，不丢条目；
- 高频状态变更会连续播报多条（可接受；最新状态最有价值）；
- 已开始的音频不会被 Stop() 中断（PlayText 非 token 感知），但串行模型保证不争用。
```

### 2.4 调用方改动
`NotificationService.SendNotification` 移除 `_tts.Stop()` 与 fire-and-forget `Task.Run`，改为：

```csharp
var ttsText = fullText.Replace("★", "").Replace("●", "").Replace("○", "").Replace("→", ",");
_ts.SpeakAsync(ttsText);
```

---

## 三、改动清单

| 文件 | 改动 |
|------|------|
| `Models/TerritoryInfo.cs` | 新增 `int Content`（映射 `content` 字段） |
| `Services/DataManager.cs` | 新增 `IsDutyTerritory(uint)` 判定方法 |
| `Services/DutyMonitor.cs` | 注入 `DataManager`；`IsInDuty` / `OnFrameworkUpdate` 改用 `IsDutyTerritory`；移除 `DutyTerritoryThreshold` |
| `ServiceOrchestrator.cs` | `new DutyMonitor(..., _dataManager)` 传入数据层（第2步已建，顺序正确） |
| `Services/TtsService.cs` | 重写为 `Channel<string>` 串行消费者 |
| `Services/NotificationService.cs` | TTS 调用改为直接 `SpeakAsync`；删除 `OnHuntBroadcast/OnFateBroadcast` 中的死代码副本过滤变量 |

## 四、验证
- `dotnet build -c Debug`：**0 错误**（仅有与本次无关的 MQTTnet NU1601 版本告警）。
- 自动 PostBuild 部署到 `XIVLauncherCN/devPlugins/FateWhisper/`（DLL 时间戳与产物一致）。
- 行为验证需在游戏内：进入 142/159 等 <1000 副本确认 TTS（默认 `MuteTtsInDuty=true`）不再泄漏；连续触发多条通知确认 TTS 逐条播报无静音。
