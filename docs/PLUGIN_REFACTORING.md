# Plugin.cs 拆分重构报告

## 执行摘要

将 562 行的 God Object `Plugin.cs` 拆分为 3 个职责单一的类，编译通过并部署。

## 拆分结果

| 文件 | 行数 | 职责 |
|------|------|------|
| `Plugin.cs` | 50 | IDalamudPlugin 入口 + [PluginService] 声明 |
| `ServiceOrchestrator.cs` | 325 | 服务实例化 + 事件连线 + Dispose |
| `PlayerContextManager.cs` | 259 | 玩家信息同步 + MQTT 连接管理 |

**总计**：634 行（原 562 行）。行数增加 13%，但每个类职责单一，可独立修改。

## 关键改进

### 1. 消除玩家信息同步重复（3→1）

原代码在 3 处重复读取玩家信息（构造函数 / OnZoneInit / TryConnectMqttIfLoggedIn），合并为 `ReadAndSyncPlayerInfo()` 统一方法，返回 `PlayerSyncResult` record struct。

### 2. 8 组 inline lambda → 6 个命名方法

| 命名方法 | 替代的 inline lambda |
|----------|---------------------|
| `OnHuntBroadcast` | mqttService.HuntReceived + mobScanner.HuntStatusChanged + mobScanner.HuntVanished |
| `OnFateBroadcast` | mqttService.FateReceived |
| `OnHuntDetectedAndPublish` | mobScanner.HuntDetected + networkInterceptor.HuntDetected |
| `OnFateDetectedAndPublish` | networkInterceptor.FateDetected |
| `OnHuntNavPopup` | notificationService.HuntNavigationPopupRequested |
| `OnFateNavPopup` | notificationService.FateNavigationPopupRequested |

### 3. SafeDispose 辅助方法

Dispose 中 14 段重复的 `try { x.Dispose(); } catch (Exception ex) { Log.Error(...); }` 替换为 `SafeDispose(x, "label", log)` 一行调用。

### 4. 修复 world change 日志 bug

原代码在 `OnZoneInit` 中先同步玩家信息（覆盖 `_config.WorldId`），再日志输出 "世界变更: {_config.WorldId} → {homeWorldId}"，两者相同。修复为使用 `OldWorldId` 记录变更前值。

## 行为保持

- 服务创建顺序、事件连线顺序、Dispose 逆序完全不变
- `QuickSyncWorldName()` 在构造期间立即调用（保持原构造函数初始世界名读取时机）
- `TryConnectMqttIfLoggedIn` 预检逻辑（CurrentWorld 名非空才继续）保持不变
- `_mqttLoggedIn` 标志语义不变

## 编译

```
dotnet build → 0 errors, 2 warnings (pre-existing NU1601)
部署到 devPlugins: ✓
```
