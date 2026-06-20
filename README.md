# FateWhisper — FF14 跨服猎怪+FATE 播报插件

基于 **SilverDasher（ACT 版）** 的 MQTT 消息网络，移植到 Dalamud API 15 / XIVLauncherCN 国服环境。

## 功能

- **跨服猎怪播报（S/A/B/SS 等级）** — 通过 MQTT 接收跨服猎怪消息
- **跨服 FATE 播报** — 普通/特殊 FATE 实时通知
- **本地猎怪检测** — 通过 IObjectTable 扫描本地怪物，自动上报到 MQTT 网络
- **逐状态通知** — 健康/已开怪/被暴打中/死亡，各自独立控制 TTS/Toast
- **TTS 语音播报** — EdgeTTS + 微软晓晓中文女声
- **游戏原生 Toast 通知** — 右下角弹出
- **副本内自动暂停**
- **树形订阅管理** — 按版本/地图勾选订阅
- **ACT 版兼容** — 可接收 ACT 版 SilverDasher 用户发出的猎怪/FATE 消息

## 技术栈

- C# / .NET 10
- Dalamud API 15 / XIVLauncherCN
- MQTTnet 4.2.1（跨服消息通信，Broker: `tree.garlandtools.cn`）
- edge_tts_sharp（TTS 语音合成）

## 消息来源

所有猎怪/FATE 数据来源于 **SilverDasher** MQTT 网络
([ACT 版](https://github.com/orgs/SilverDasher))。
认证服务器 `nest.garlandtools.cn` 已废弃，MQTT 使用默认凭证连接。

## 编译

```bash
dotnet build FateWhisper.csproj
```

编译后自动部署到 `%APPDATA%\XIVLauncherCN\devPlugins\FateWhisper\`。

## 安装到游戏

### 方式一：插件仓库（推荐）

1. 打开游戏，输入 `/xlsettings`
2. 左侧菜单 → **插件仓库** → **添加仓库**
3. 粘贴以下地址：
   ```
   https://raw.githubusercontent.com/ijnokmsc/DalamudPlugins/main/pluginmaster.json
   ```
4. 确定 → 重新整理插件列表
5. 搜索 **FateWhisper** → 安装

### 方式二：手动安装

1. 从 [Releases](https://github.com/ijnokmsc/DalamudPlugins/releases) 下载对应版本的 `latest.zip`
2. 解压到 `%APPDATA%\XIVLauncherCN\devPlugins\FateWhisper\`
3. 游戏内 `/xlplugins` → 开发者插件 → 刷新并启用

## 使用

| 命令 | 说明 |
|------|------|
| `/fw` | 打开/关闭设置窗口 |
| `/fatewhisper` | 同上（完整命令） |
| `/sd` | 同上（向下兼容） |

## 许可证

AGPL-3.0-or-later

## 致谢

- [SilverDasher (ACT版)](https://github.com/orgs/SilverDasher) — MQTT 消息网络和数据
- [garlandtools.cn](https://garlandtools.cn) — 静态数据源
