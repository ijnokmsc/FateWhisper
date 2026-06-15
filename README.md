# SilverDasher — FF14 Dalamud 猎怪+FATE 跨服播报插件

照搬 ACT 版 [SilverDasher](https://github.com/orgs/SilverDasher) 的核心功能，基于 **Dalamud API 15** + **XIVLauncherCN** 国服环境。

## 功能

- 跨服猎怪播报（S/A/B/SS 等级）
- 跨服 FATE 播报（普通/特殊）
- 游戏原生 Toast 右下角通知
- TTS 语音播报（EdgeTTS + 微软晓晓中文女声）
- 聊天框通知
- 副本内自动暂停
- 按等级/区域订阅过滤
- 静态数据自动更新（garlandtools.cn）

## 技术栈

- C# / .NET 10
- Dalamud API 15 / XIVLauncherCN
- MQTTnet 4.2.1（跨服消息通信）
- edge_tts_sharp（TTS 语音合成）
- MQTT 服务器: `tree.garlandtools.cn/mqtt`

## 编译

```bash
dotnet build
```

编译后自动部署到 `%APPDATA%\XIVLauncherCN\devPlugins\SilverDasher\`。

## 使用

1. 在 XIVLauncherCN 中启用插件
2. 输入 `/sd` 打开设置窗口
3. 选择需要订阅的猎怪等级和区域
4. 切换地图后自动连接 MQTT 开始接收播报

## 已知问题

- 认证功能需 Weaver.dll 加密签名（当前不可用）
- 本地 FATE 检测需更新国服客户端签名
- hunts.json 数据格式需对齐
