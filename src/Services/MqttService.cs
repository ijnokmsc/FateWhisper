using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FateWhisper.Config;
using FateWhisper.Models;

namespace FateWhisper.Services;

/// <summary>
/// MQTT 通信服务，基于 MQTTnet 4.x 管理 WebSocket+TLS 连接。
/// 负责 Connect/Disconnect/Subscribe/Publish/Reconnect。
/// Topic 格式对齐 ACT 版: {dc_label}/{world_label}/{type}/{id}
/// </summary>
public class MqttService : IDisposable
{
    private readonly IPluginLog _log;
    private readonly DataManager _dataManager;
    private readonly PluginConfig _config;
    private IMqttClient? _mqttClient;
    private MqttClientOptions? _mqttOptions;
    private readonly string _brokerUrl;

    private string _mqttUsername;
    private string _mqttPassword;

    private CancellationTokenSource? _reconnectCts;
    private int _reconnectAttempt;
    private const int MaxReconnectDelay = 60;
    private const string Prefix = "[FateWhisper]";

    /// <summary>收到远程猎怪播报时触发。</summary>
    public event Action<HuntMessage>? HuntReceived;

    /// <summary>收到远程 FATE 播报时触发。</summary>
    public event Action<FateMessage>? FateReceived;

    /// <summary>MQTT 连接状态变更时触发。true=已连接。</summary>
    public event Action<bool>? ConnectionStateChanged;

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (_isConnected != value)
            {
                _isConnected = value;
                ConnectionStateChanged?.Invoke(value);
            }
        }
    }

    public MqttService(IPluginLog log, DataManager dataManager, PluginConfig config, string brokerUrl, string mqttUsername, string mqttPassword)
    {
        _log = log;
        _dataManager = dataManager;
        _config = config;
        _brokerUrl = brokerUrl;
        _mqttUsername = mqttUsername;
        _mqttPassword = mqttPassword;

        // 从配置恢复大区标签（持久化确保重启后立即可用）
        _playerDcLabel = config.PlayerDcLabel ?? "";
        if (!string.IsNullOrEmpty(_playerDcLabel))
            _log.Information($"{Prefix} 从配置恢复玩家大区: {_playerDcLabel}");
    }

    private string _playerDcLabel;

    /// <summary>
    /// 更新玩家所在大区标签（如 LuXingNiao），用于跨大区判断。
    /// </summary>
    public void UpdatePlayerDc(string? dcLabel)
    {
        _playerDcLabel = dcLabel ?? "";
        _config.PlayerDcLabel = _playerDcLabel;  // 持久化到配置
        _config.Save();
        _log.Information($"{Prefix} 玩家大区: {_playerDcLabel}");
    }

    public void UpdateCredentials(string username, string password)
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            _log.Warning($"{Prefix} UpdateCredentials: 用户名或密码为空，凭证未更新");
            return;
        }
        _mqttUsername = username;
        _mqttPassword = password;
        _log.Information($"{Prefix} MQTT 凭证已更新 (user={username[..Math.Min(username.Length, 4)]}...)");
    }

    public async Task ConnectAsync()
    {
        try
        {
            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();

            _mqttClient.ConnectedAsync += OnConnectedAsync;
            _mqttClient.DisconnectedAsync += OnDisconnectedAsync;
            _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

            var builder = new MqttClientOptionsBuilder()
                .WithWebSocketServer(_brokerUrl)
                .WithTls(new MqttClientOptionsBuilderTlsParameters
                {
                    UseTls = true,
                    CertificateValidationHandler = _ => true,
                    AllowUntrustedCertificates = true
                })
                .WithCredentials(_mqttUsername, _mqttPassword)
                .WithClientId($"FateWhisper_{Guid.NewGuid():N}"[..23])
                .WithCleanSession(false)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(30));

            _mqttOptions = builder.Build();

            _log.Information($"{Prefix} 正在连接 MQTT Broker: {_brokerUrl}");
            var result = await _mqttClient.ConnectAsync(_mqttOptions);

            if (result.ResultCode == MqttClientConnectResultCode.Success)
            {
                IsConnected = true;
                _reconnectAttempt = 0;
                _log.Information($"{Prefix} MQTT 连接成功");
                await SubscribeTopicsAsync();
            }
            else
            {
                _log.Error($"{Prefix} MQTT 连接失败: {result.ResultCode} - {result.ReasonString}");
                IsConnected = false;
                StartReconnect();
            }
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} MQTT 连接异常: {ex.Message}");
            IsConnected = false;
            StartReconnect();
        }
    }

    public async Task DisconnectAsync()
    {
        _reconnectCts?.Cancel();
        if (_mqttClient is not null && _mqttClient.IsConnected)
        {
            try { await _mqttClient.DisconnectAsync(); }
            catch (Exception ex) { _log.Warning($"{Prefix} MQTT 断开异常: {ex.Message}"); }
        }
        IsConnected = false;
    }

    /// <summary>
    /// 订阅 MQTT Topic — ACT 版格式: {dc_label}/{world_label}/{type}/{id}
    /// 使用通配符订阅所有大区/服务器/ID。
    /// </summary>
    private async Task SubscribeTopicsAsync()
    {
        if (_mqttClient is null || !_mqttClient.IsConnected) return;

        try
        {
            // ACT 版 Topic 格式: {DataCenterLabel}/{WorldLabel}/{type}/{id}
            // 例: LuXingNiao/HongYuHai/hunt/2957
            // 通配符: + = 单层, # = 多层
            var huntTopic = "+/+/hunt/+";
            var fateTopic = "+/+/fate/+";

            await _mqttClient.SubscribeAsync(new MqttTopicFilterBuilder()
                .WithTopic(huntTopic)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build());

            await _mqttClient.SubscribeAsync(new MqttTopicFilterBuilder()
                .WithTopic(fateTopic)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build());

            _log.Information($"{Prefix} MQTT Topic 订阅完成: {huntTopic}, {fateTopic}");
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} MQTT Topic 订阅失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 发布猎怪消息（本地检测 → 服务器）。
    /// ACT 版发布路径: upload/u/{session}，payload 经 Weave 编码。
    /// 当前无有效 session，暂发布到通用 topic。
    /// </summary>
    public async Task PublishHuntAsync(HuntMessage message, string datacenter, string world, string rank)
    {
        if (_mqttClient is null || !_mqttClient.IsConnected)
        {
            _log.Warning($"{Prefix} MQTT 未连接，无法发布猎怪消息");
            return;
        }

        try
        {
            var topic = $"upload/u/{_mqttUsername}";
            var payload = JsonConvert.SerializeObject(message);
            var mqttMessage = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _mqttClient.PublishAsync(mqttMessage);
            _log.Debug($"{Prefix} 已发布猎怪: {topic}");
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} 发布猎怪消息失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 发布 FATE 消息（本地检测 → 服务器）。
    /// </summary>
    public async Task PublishFateAsync(FateMessage message, string datacenter, string world, string type)
    {
        if (_mqttClient is null || !_mqttClient.IsConnected)
        {
            _log.Warning($"{Prefix} MQTT 未连接，无法发布 FATE 消息");
            return;
        }

        try
        {
            var topic = $"upload/u/{_mqttUsername}";
            var payload = JsonConvert.SerializeObject(message);
            var mqttMessage = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _mqttClient.PublishAsync(mqttMessage);
            _log.Debug($"{Prefix} 已发布 FATE: {topic}");
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} 发布 FATE 消息失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 连接成功回调。
    /// </summary>
    private Task OnConnectedAsync(MqttClientConnectedEventArgs args)
    {
        _log.Information($"{Prefix} MQTT Connected 事件触发");
        IsConnected = true;
        _reconnectAttempt = 0;
        _ = SubscribeTopicsAsync();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 连接断开回调，触发重连。
    /// </summary>
    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        _log.Warning($"{Prefix} MQTT 断开: {args.Reason}");
        IsConnected = false;
        StartReconnect();
        await Task.CompletedTask;
    }

    /// <summary>
    /// 收到 MQTT 消息回调。
    /// Topic 格式: {dc_label}/{world_label}/{type}/{id}
    /// Payload 格式: {"i":instance, "hp":health/progress, "m":map, "c":{"x":int,"y":int}}
    /// 对齐 ACT 版 Notifier.Unpack 逻辑。
    /// </summary>
    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        try
        {
            var topic = args.ApplicationMessage.Topic;
            var payload = Encoding.UTF8.GetString(args.ApplicationMessage.PayloadSegment);

            if (_config.Debug.MqttMessages)
                _log.Debug($"{Prefix} MQTT 收到消息 topic={topic} len={payload.Length} payload={payload}");

            // 解析 Topic: {dc_label}/{world_label}/{type}/{id}
            var segments = topic.Split('/');
            if (segments.Length < 4) return Task.CompletedTask;

            var dcLabel = segments[0];
            var worldLabel = segments[1];
            var msgType = segments[2];  // "hunt" 或 "fate"
            var msgIdStr = segments[3];

            if (!int.TryParse(msgIdStr, out var msgId)) return Task.CompletedTask;

            // 跨大区判断：Topic 的 DC 标签与玩家所在 DC 不同则为跨大区
            // 若 _playerDcLabel 未初始化（首次安装 WorldId=0），从 config 实时查询
            var playerDc = !string.IsNullOrEmpty(_playerDcLabel)
                ? _playerDcLabel
                : _dataManager.LookupDcLabel(_config.WorldId.ToString()) ?? "";
            var isCrossDc = !string.IsNullOrEmpty(playerDc) &&
                !string.Equals(dcLabel, playerDc, StringComparison.OrdinalIgnoreCase);

            // Debug 日志：帮助排查跨大区过滤问题
            if (_config.Debug.MqttMessages)
                _log.Debug($"{Prefix} [DC过滤] topicDC={dcLabel} playerDC={playerDc} isCrossDc={isCrossDc} storedDcLabel='{_playerDcLabel}'");

            // 持久化：如果这次从 config 查询到了有效的 playerDc，且 _playerDcLabel 为空，则更新
            if (string.IsNullOrEmpty(_playerDcLabel) && !string.IsNullOrEmpty(playerDc))
            {
                _playerDcLabel = playerDc;
                _config.PlayerDcLabel = playerDc;
                _config.Save();
                _log.Information($"{Prefix} 从消息处理中自动检测到玩家大区: {playerDc}");
            }

            // 解析 JSON payload
            var json = JObject.Parse(payload);

            // 共享字段
            var instance = json["i"]?.Value<uint>() ?? 0;
            var map = json["m"]?.Value<uint>() ?? 0;
            var hp = json["hp"]?.Value<int>() ?? 100;

            // 从数据管理器查世界和区域名称（worldLabel 是拼音如 HongYuHai）
            var worldName = _dataManager.LookupWorldNameByLabel(worldLabel);
            var territoryName = _dataManager.LookupTerritoryName(map.ToString());

            Coordinate? coord = null;
            if (json["c"] is JObject cObj)
            {
                coord = new Coordinate
                {
                    X = cObj["x"]?.Value<int>() ?? 0,
                    Y = cObj["y"]?.Value<int>() ?? 0
                };
            }

            if (msgType == "hunt")
            {
                var huntMsg = new HuntMessage
                {
                    Id = msgId,
                    Instance = instance,
                    Map = map,
                    Health = hp,
                    Coordinate = coord,
                    TerritoryName = territoryName,
                    WorldName = worldName,
                    Datacenter = dcLabel,
                    IsCrossDc = isCrossDc,
                    Rank = _dataManager.LookupHuntRank(msgIdStr),
                    MobName = _dataManager.LookupHuntName(msgIdStr),
                };

                if (_config.Debug.HuntTriggers)
                    _log.Information($"{Prefix} 猎怪播报触发: id={msgId} hp={hp}% map={map} ({territoryName}) world={worldName} dc={dcLabel} crossDc={isCrossDc}");
                HuntReceived?.Invoke(huntMsg);
            }
            else if (msgType == "fate")
            {
                // ACT 版: FATE payload 的 hp = 剩余 HP (100=刚开始)，需要 100-hp 得进度
                var progress = Math.Max(0, 100 - hp);

                var fateMsg = new FateMessage
                {
                    Id = msgId,
                    Instance = instance,
                    Map = map,
                    Progress = progress,
                    Coordinate = coord,
                    TerritoryName = territoryName,
                    WorldName = worldName,
                    Datacenter = dcLabel,
                    IsCrossDc = isCrossDc,
                    FateName = _dataManager.LookupFateName(msgIdStr),
                    IsSpecial = _dataManager.IsFateSpecial(msgIdStr),
                    EventType = progress >= 100 ? "end" : (progress == 0 ? "start" : "progress"),
                };

                if (_config.Debug.FateTriggers)
                    _log.Information($"{Prefix} FATE 播报触发: id={msgId} progress={progress}% map={map} ({territoryName}) world={worldName} dc={dcLabel} crossDc={isCrossDc} special={fateMsg.IsSpecial}");
                FateReceived?.Invoke(fateMsg);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} 处理 MQTT 消息异常: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 启动指数退避重连。
    /// </summary>
    private void StartReconnect()
    {
        _reconnectCts?.Cancel();
        _reconnectCts = new CancellationTokenSource();
        var token = _reconnectCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var delay = Math.Min(
                        (int)Math.Pow(2, _reconnectAttempt),
                        MaxReconnectDelay);
                    _log.Information($"{Prefix} 将在 {delay}s 后尝试重连 (尝试 #{_reconnectAttempt + 1})");

                    await Task.Delay(TimeSpan.FromSeconds(delay), token);

                    if (_mqttClient is not null && _mqttOptions is not null)
                    {
                        _reconnectAttempt++;

                        var rebuiltOptions = new MqttClientOptionsBuilder()
                            .WithWebSocketServer(_brokerUrl)
                            .WithTls(new MqttClientOptionsBuilderTlsParameters
                            {
                                UseTls = true,
                                CertificateValidationHandler = _ => true,
                                AllowUntrustedCertificates = true
                            })
                            .WithCredentials(_mqttUsername, _mqttPassword)
                            .WithClientId($"FateWhisper_{Guid.NewGuid():N}"[..23])
                            .WithCleanSession(false)
                            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                            .Build();

                        await _mqttClient.ConnectAsync(rebuiltOptions, token);

                        if (_mqttClient.IsConnected)
                        {
                            IsConnected = true;
                            _reconnectAttempt = 0;
                            _log.Information($"{Prefix} 重连成功");
                            await SubscribeTopicsAsync();
                            return;
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _reconnectAttempt++;
                    _log.Warning($"{Prefix} 重连尝试失败: {ex.Message}");
                }
            }
        }, token);
    }

    public void Dispose()
    {
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();

        if (_mqttClient is not null)
        {
            _mqttClient.ConnectedAsync -= OnConnectedAsync;
            _mqttClient.DisconnectedAsync -= OnDisconnectedAsync;
            _mqttClient.ApplicationMessageReceivedAsync -= OnMessageReceivedAsync;

            if (_mqttClient.IsConnected)
            {
                try
                {
                    var disconnectTask = Task.Run(async () =>
                    {
                        try { await _mqttClient.DisconnectAsync(); }
                        catch { }
                    });
                    if (!disconnectTask.Wait(TimeSpan.FromSeconds(3)))
                        _log.Warning($"{Prefix} MQTT 断开超时 (3s)，强制释放");
                }
                catch { }
            }
            _mqttClient.Dispose();
        }

        _log.Information($"{Prefix} MQTT 服务已释放");
    }
}
