# SilverDasher — 产品需求文档 (PRD)

## 1. 项目信息

| 字段 | 值 |
|------|-----|
| Language | 中文 |
| Programming Language | C# / .NET 10 / Dalamud API 15 |
| Project Name | silver_dasher |
| 原始需求 | 将 ACT 版 SilverDasher 猎怪+FATE 跨服播报插件移植为 Dalamud 插件，复刻核心播报与通知功能 |

## 2. 产品定义

### Product Goals

1. **跨服猎怪/FATE 实时播报**：玩家在同大区及跨大区范围内，零延迟接收猎怪与 FATE 触发信息，不再依赖手动喊话或第三方网站
2. **低干扰、可定制通知体验**：以 Windows Toast 为主要通知渠道，辅以游戏内聊天框打印；支持按区域/等级/类型精细订阅，进副本自动暂停
3. **ACT→Dalamud 无缝迁移**：保持与 ACT 版一致的数据模型和 MQTT 通信协议，降低用户迁移成本，确保 ACT 版与 Dalamud 版共用同一播报生态

### User Stories

1. As a **猎手**, I want to **在同大区内自动接收其他世界的 S/A 级猎怪触发通知** so that **不用切世界或蹲守就能第一时间参与击杀**
2. As a **猎手**, I want to **接收跨大区的猎怪信息** so that **即使目标在其他数据中心也能选择前往参与**
3. As a **FATE 农民**, I want to **订阅指定区域的 FATE 触发通知** so that **刷古武/优武时不用反复手动查看地图**
4. As a **副本玩家**, I want to **进入副本时自动暂停播报通知** so that **不会在打本时被无关通知打扰**
5. As a **ACT 迁移用户**, I want to **Dalamud 版与 ACT 版使用相同的订阅设置和播报来源** so that **迁移后体验一致，不需要重新适应**

## 3. 技术规范

### Requirements Pool

#### P0 — Must Have（首发版本）

| ID | 需求 | 说明 |
|----|------|------|
| P0-01 | 跨世界猎怪播报 (CWHunt) | 接收/发送同大区猎怪信息，支持 A/B/S/SS 四个等级 |
| P0-02 | 跨大区猎怪播报 (CDCHunt) | 接收/发送跨大区猎怪信息，支持 A/B/S/SS 四个等级 |
| P0-03 | 跨世界 FATE 播报 (CWFate) | 接收/发送 FATE 触发信息，支持 Common/Special 分组 |
| P0-04 | Toast 通知 | Windows 原生 Toast 通知，作为主通知渠道 |
| P0-05 | 聊天框打印 | 游戏内聊天框输出播报信息，作为辅助通知渠道 |
| P0-06 | 订阅管理 | 按区域/怪物等级/类型分组勾选，控制接收哪些播报 |
| P0-07 | 副本内暂停 (PauseInDuty) | 检测进入副本状态，自动暂停播报通知 |
| P0-08 | 认证系统 | 连接 MQTT 服务器前的身份认证，对接 `https://nest.silverdasher.com` |
| P0-09 | MQTT 通信层 | 基于 MQTTnet 实现 WebSocket+TLS 连接 `wss://tree.silverdasher.com/mqtt`，支持订阅/发布/断线重连 |
| P0-10 | 静态数据加载 | 加载 hunt.json / fates.json / territories.json / worlds.json 等静态数据文件 |
| P0-11 | 远程数据更新 | 从 `https://garlandtools.cn/silv/versions.json` 检查版本，按需下载更新数据文件 |
| P0-12 | 网络包拦截 | 通过 Dalamud 网络包拦截获取 InitZone / FateInfo / ActorControlSelf 事件 |
| P0-13 | Dalamud 插件基础设施 | 正确实现 IDalamudPlugin 接口、[PluginService] 注入、无参构造函数 |

#### P1 — Should Have（后续迭代）

| ID | 需求 | 说明 |
|----|------|------|
| P1-01 | TTS 语音通知 | 播报时语音播报猎怪/FATE 信息 |
| P1-02 | PvP 通知 — 被击杀提醒 (NotifyDied) | PvP 中被击杀时通知 |
| P1-03 | PvP 通知 — 被标记提醒 (NotifySpotted) | PvP 中被标记时通知 |
| P1-04 | PvP 通知 — 被嘲讽提醒 (NotifyTaunted) | PvP 中被嘲讽时通知 |
| P1-05 | PvP 通知 — 欺凌提醒 (NotifyBullying) | PvP 中被欺凌时通知 |
| P1-06 | 备用服务器支持 | 支持配置备用 MQTT 服务器，主服务器不可用时自动切换 |

#### P2 — Nice to Have

| ID | 需求 | 说明 |
|----|------|------|
| P2-01 | ACT 版设置导入 | 自动检测并导入 ACT 版 SilverDasher 的配置 |
| P2-02 | 播报历史记录 | 保留最近的播报记录，可回看 |
| P2-03 | 自定义通知模板 | 用户可自定义 Toast/聊天框的通知格式 |

### UI Design Draft

#### 整体布局

插件采用 Dalamud 标准的 `/sd` 命令打开 ImGui 设置窗口，主窗口使用 Tab 式布局：

```
┌──────────────────────────────────────────────────┐
│  SilverDasher 设置                          [×]  │
├──────────────────────────────────────────────────┤
│  [猎怪订阅] [FATE订阅] [通知设置] [系统设置]      │
├──────────────────────────────────────────────────┤
│                                                    │
│  ── 当前 Tab 内容区 ──                             │
│                                                    │
│  猎怪订阅 Tab：                                    │
│  ┌─────────────────────────────────────────────┐  │
│  │ 同大区猎怪 (CWHunt)          跨大区猎怪 (CDCHunt) │  │
│  │  ☑ A 级猎怪   ☑ A 级猎怪                     │  │
│  │  ☑ B 级猎怪   ☑ B 级猎怪                     │  │
│  │  ☑ S 级猎怪   ☑ S 级猎怪                     │  │
│  │  ☐ SS 级猎怪  ☐ SS 级猎怪                    │  │
│  │                                              │  │
│  │  区域筛选：                                   │  │
│  │  ☑ 6.0 区域  ☑ 7.0 区域  ☐ 旧版区域          │  │
│  └─────────────────────────────────────────────┘  │
│                                                    │
│  FATE订阅 Tab：                                    │
│  ☑ 普通 FATE (Common)                             │
│  ☑ 特殊 FATE (Special/博兹雅等)                    │
│  区域/等级筛选...                                  │
│                                                    │
│  通知设置 Tab：                                    │
│  ☑ 启用 Toast 通知                                │
│  ☑ 启用聊天框打印                                  │
│  ☐ 启用 TTS (P1)                                  │
│  ☑ 进副本自动暂停 (PauseInDuty)                    │
│                                                    │
│  系统设置 Tab：                                    │
│  服务器状态：● 已连接                              │
│  MQTT 服务器：wss://tree.silverdasher.com/mqtt     │
│  认证状态：● 已认证                                │
│  数据版本：hunt.json vXX / fates.json vXX          │
│  [检查更新]  [重新认证]  [重新连接]                 │
│                                                    │
└──────────────────────────────────────────────────┘
```

#### 通知展示

- **Toast 通知**：Windows 原生弹窗，标题显示猎怪等级/FATE 类型，正文显示名称+位置+世界
  - 示例：`[S级猎怪] 阿拉米格獒 — 仲夏叹歌 @ 萌芽池 (7.2, 12.4)`
- **聊天框打印**：以 `[SilverDasher]` 前缀输出到游戏聊天框
  - 示例：`[SilverDasher] [S级] 阿拉米格獒 @ 萌芽池 (7.2, 12.4) [仲夏叹歌]`

### Open Questions

1. **MQTT Topic 精确格式**：ACT 版 DLL 逆向推测为 `silverdasher/hunt/{world}/{rank}` 和 `silverdasher/fate/{world}/{type}`，但需连接服务器后抓包确认实际 topic 结构
2. **认证协议细节**：认证 API (`https://nest.silverdasher.com`) 的请求/响应格式需要进一步逆向或向原作者确认
3. **opcodes 兼容性**：opcodes.json 中 cn/global 版 opcode 是否与当前国服客户端版本匹配，需实机验证
4. **MQTTnet 版本**：ACT 版使用 MQTTnet 4.2.1，Dalamud 环境下是否有版本兼容问题（如与 Dalamud 自带的 MQTTnet 冲突）
5. **Toast 通知在游戏全屏模式下的行为**：需验证 Windows Toast 在 FF14 独占全屏/窗口化全屏模式下的可见性
6. **数据文件分发方式**：静态数据文件是打包在插件内还是首次启动时下载？需要确认 Dalamud 插件的打包规范
