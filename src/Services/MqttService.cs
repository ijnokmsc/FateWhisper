using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using Newtonsoft.Json;
using SilverDasher.Models;

namespace SilverDasher.Services;

/// <summary>
/// MQTT 通信服务，基于 MQTTnet 4.x 管理 WebSocket+TLS 连接。
/// 负责 Connect/Disconnect/Subscribe/Publish/Reconnect。
/// </summary>
public class MqttService : IDisposable
{
    private readonly IPluginLog _log;
    private IMqttClient? _mqttClient;
    private MqttClientOptions? _mqttOptions;
    private readonly string _brokerUrl;

    /// <summary>
    /// MQTT 用户名。可通过 UpdateCredentials() 在运行时更新。
    /// </summary>
    private string _mqttUsername;

    /// <summary>
    /// MQTT 密码。可通过 UpdateCredentials() 在运行时更新。
    /// </summary>
    private string _mqttPassword;

    private CancellationTokenSource? _reconnectCts;
    private int _reconnectAttempt;
    private const int MaxReconnectDelay = 60;
    private const string Prefix = "[SilverDasher]";

    /// <summary>
    /// 收到远程猎怪播报时触发。
    /// </summary>
    public event Action<HuntMessage>? HuntReceived;

    /// <summary>
    /// 收到远程 FATE 播报时触发。
    /// </summary>
    public event Action<FateMessage>? FateReceived;

    /// <summary>
    /// MQTT 连接状态变更时触发。true=已连接。
    /// </summary>
    public event Action<bool>? ConnectionStateChanged;

    private bool _isConnected;
    /// <summary>
    /// 当前是否已连接。
    /// </summary>
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

    /// <summary>
    /// 初始化 MQTT 服务。
    /// </summary>
    /// <param name="log">日志服务。</param>
    /// <param name="brokerUrl">MQTT Broker WebSocket URL。</param>
    /// <param name="mqttUsername">MQTT 用户名。</param>
    /// <param name="mqttPassword">MQTT 密码。</param>
    public MqttService(IPluginLog log, string brokerUrl, string mqttUsername, string mqttPassword)
    {
        _log = log;
        _brokerUrl = brokerUrl;
        _mqttUsername = mqttUsername;
        _mqttPassword = mqttPassword;
    }

    /// <summary>
    /// 更新 MQTT 认证凭证。应在认证成功后调用，确保后续连接使用最新的凭证。
    /// 如果当前已连接，凭证将在下次重连时生效。
    /// </summary>
    /// <param name="username">新的 MQTT 用户名。</param>
    /// <param name="password">新的 MQTT 密码。</param>
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

    /// <summary>
    /// 连接到 MQTT Broker。
    /// </summary>
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
                .WithClientId($"SilverDasher_{Guid.NewGuid():N}"[..23])
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

    /// <summary>
    /// 断开 MQTT 连接。
    /// </summary>
    public async Task DisconnectAsync()
    {
        _reconnectCts?.Cancel();
        if (_mqttClient is not null && _mqttClient.IsConnected)
        {
            try
            {
                await _mqttClient.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _log.Warning($"{Prefix} MQTT 断开异常: {ex.Message}");
            }
        }

        IsConnected = false;
    }

    /// <summary>
    /// 订阅 MQTT Topic。
    /// </summary>
    private async Task SubscribeTopicsAsync()
    {
        if (_mqttClient is null || !_mqttClient.IsConnected)
        {
            return;
        }

        try
        {
            var huntTopic = "sd/hunt/#";
            var fateTopic = "sd/fate/#";

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
    /// 发布猎怪消息到 MQTT。
    /// </summary>
    /// <param name="message">猎怪消息。</param>
    /// <param name="datacenter">大区。</param>
    /// <param name="world">服务器。</param>
    /// <param name="rank">等级。</param>
    public async Task PublishHuntAsync(HuntMessage message, string datacenter, string world, string rank)
    {
        if (_mqttClient is null || !_mqttClient.IsConnected)
        {
            _log.Warning($"{Prefix} MQTT 未连接，无法发布猎怪消息");
            return;
        }

        try
        {
            var topic = $"sd/hunt/{datacenter}/{world}/{rank.ToLowerInvariant()}";
            var payload = JsonConvert.SerializeObject(message);
            var mqttMessage = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _mqttClient.PublishAsync(mqttMessage);
            _log.Debug($"{Prefix} 已发布猎怪: {topic} -> {message}");
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} 发布猎怪消息失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 发布 FATE 消息到 MQTT。
    /// </summary>
    /// <param name="message">FATE 消息。</param>
    /// <param name="datacenter">大区。</param>
    /// <param name="world">服务器。</param>
    /// <param name="type">FATE 类型。</param>
    public async Task PublishFateAsync(FateMessage message, string datacenter, string world, string type)
    {
        if (_mqttClient is null || !_mqttClient.IsConnected)
        {
            _log.Warning($"{Prefix} MQTT 未连接，无法发布 FATE 消息");
            return;
        }

        try
        {
            var topic = $"sd/fate/{datacenter}/{world}/{type}";
            var payload = JsonConvert.SerializeObject(message);
            var mqttMessage = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _mqttClient.PublishAsync(mqttMessage);
            _log.Debug($"{Prefix} 已发布 FATE: {topic} -> {message}");
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
    /// </summary>
    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        try
        {
            var topic = args.ApplicationMessage.Topic;
            var payload = Encoding.UTF8.GetString(args.ApplicationMessage.PayloadSegment);

            _log.Debug($"{Prefix} MQTT 收到消息 topic={topic} len={payload.Length}");
            _log.Information($"{Prefix} MQTT 消息: {topic} -> {payload[..Math.Min(payload.Length, 200)]}");

            if (topic.Contains("/hunt/"))
            {
                var huntMsg = JsonConvert.DeserializeObject<HuntMessage>(payload);
                if (huntMsg is not null)
                {
                    _log.Information($"{Prefix} 猎怪播报触发: {huntMsg}");
                    HuntReceived?.Invoke(huntMsg);
                }
                else
                    _log.Warning($"{Prefix} 猎怪消息反序列化失败");
            }
            else if (topic.Contains("/fate/"))
            {
                var fateMsg = JsonConvert.DeserializeObject<FateMessage>(payload);
                if (fateMsg is not null)
                {
                    _log.Information($"{Prefix} FATE 播报触发: {fateMsg}");
                    FateReceived?.Invoke(fateMsg);
                }
                else
                    _log.Warning($"{Prefix} FATE 消息反序列化失败");
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

                        // 重建 MqttOptions 以使用最新的凭证
                        var rebuiltOptions = new MqttClientOptionsBuilder()
                            .WithWebSocketServer(_brokerUrl)
                            .WithTls(new MqttClientOptionsBuilderTlsParameters
                            {
                                UseTls = true,
                                CertificateValidationHandler = _ => true,
                                AllowUntrustedCertificates = true
                            })
                            .WithCredentials(_mqttUsername, _mqttPassword)
                            .WithClientId($"SilverDasher_{Guid.NewGuid():N}"[..23])
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
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _reconnectAttempt++;
                    _log.Warning($"{Prefix} 重连尝试失败: {ex.Message}");
                }
            }
        }, token);
    }

    /// <summary>
    /// 释放 MQTT 客户端和重连资源。
    /// 使用带超时保护的异步断开，避免阻塞 Dispose 调用。
    /// </summary>
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
                    // 带超时保护的异步断开，避免 Dispose 同步阻塞无限等待
                    var disconnectTask = Task.Run(async () =>
                    {
                        try
                        {
                            await _mqttClient.DisconnectAsync();
                        }
                        catch
                        {
                            // 忽略断开异常
                        }
                    });

                    if (!disconnectTask.Wait(TimeSpan.FromSeconds(3)))
                    {
                        _log.Warning($"{Prefix} MQTT 断开超时 (3s)，强制释放");
                    }
                }
                catch
                {
                    // 忽略断开异常
                }
            }

            _mqttClient.Dispose();
        }

        _log.Information($"{Prefix} MQTT 服务已释放");
    }
}
