using System;
using System.Text.Json.Serialization;
using Dalamud.Configuration;
using Dalamud.Plugin;
using Newtonsoft.Json;

namespace SilverDasher.Config;

/// <summary>
/// 插件主配置类，实现 IPluginConfiguration 接口。
/// 作为所有子配置的容器，通过 Dalamud PluginInterface 持久化。
/// </summary>
[Serializable]
public class PluginConfig : IPluginConfiguration
{
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    private readonly IDalamudPluginInterface _pluginInterface;

    /// <summary>
    /// 配置版本号，用于 Dalamud IPluginConfiguration 接口。
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// 同大区猎怪订阅配置。
    /// </summary>
    [JsonProperty("cw_hunt")]
    public CWHuntConfig CWHunt { get; set; } = new();

    /// <summary>
    /// 跨大区猎怪订阅配置。
    /// </summary>
    [JsonProperty("cdc_hunt")]
    public CDCHuntConfig CDCHunt { get; set; } = new();

    /// <summary>
    /// 同大区 FATE 订阅配置。
    /// </summary>
    [JsonProperty("cw_fate")]
    public CWFateConfig CWFate { get; set; } = new();

    /// <summary>
    /// 通知配置。
    /// </summary>
    [JsonProperty("notification")]
    public NotificationConfig Notification { get; set; } = new();

    /// <summary>
    /// 认证 token（持久化以支持重启后免重新认证）。
    /// </summary>
    [JsonProperty("auth_token")]
    public string? AuthToken { get; set; }

    /// <summary>
    /// 角色名称。
    /// </summary>
    [JsonProperty("character_name")]
    public string CharacterName { get; set; } = "";

    /// <summary>
    /// 世界/服务器名称。
    /// </summary>
    [JsonProperty("world_name")]
    public string WorldName { get; set; } = "";

    /// <summary>
    /// 世界 ID（用于认证）。
    /// </summary>
    [JsonProperty("world_id")]
    public uint WorldId { get; set; }

    /// <summary>
    /// 大区名称。
    /// </summary>
    [JsonProperty("datacenter")]
    public string Datacenter { get; set; } = "";

    /// <summary>
    /// 主窗口是否可见。
    /// </summary>
    [JsonProperty("window_visible")]
    public bool WindowVisible { get; set; } = false;

    /// <summary>
    /// 上次使用的 MQTT 用户名。
    /// </summary>
    [JsonProperty("mqtt_username")]
    public string? MqttUsername { get; set; }

    /// <summary>
    /// 上次使用的 MQTT 密码。
    /// </summary>
    [JsonProperty("mqtt_password")]
    public string? MqttPassword { get; set; }

    // ===== ACT 版风格订阅管理 =====

    /// <summary>
    /// 已订阅的猎怪 ID 列表（ACT 版兼容）。
    /// </summary>
    [JsonProperty("hunt_subs")]
    public List<int> HuntSubscriptions { get; set; } = [];

    /// <summary>
    /// 已订阅的 FATE ID 列表（ACT 版兼容）。
    /// </summary>
    [JsonProperty("fate_subs")]
    public List<int> FateSubscriptions { get; set; } = [];

    /// <summary>
    /// 初始化配置实例并从持久化存储加载。
    /// </summary>
    /// <param name="pluginInterface">Dalamud 插件接口。</param>
    public PluginConfig(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        LoadSafe();
    }

    /// <summary>
    /// 无参构造器（Dalamud JSON 反序列化必需）。
    /// 显式初始化所有属性为安全默认值。
    /// </summary>
    public PluginConfig()
    {
        _pluginInterface = null!;
        Version = 1;
        CWHunt = new();
        CDCHunt = new();
        CWFate = new();
        Notification = new();
        CharacterName = "";
        WorldName = "";
        Datacenter = "";
        WindowVisible = false;
    }

    /// <summary>
    /// 将当前配置保存到持久化存储。
    /// </summary>
    public void Save()
    {
        if (_pluginInterface is null) return;
        try
        {
            _pluginInterface.SavePluginConfig(this);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SilverDasher] 配置保存失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 安全地从持久化存储加载配置，异常时使用默认值。
    /// </summary>
    private void LoadSafe()
    {
        if (_pluginInterface is null) return;

        try
        {
            var saved = _pluginInterface.GetPluginConfig() as PluginConfig;
            if (saved is null) return;
            CopyFrom(saved);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SilverDasher] 配置加载失败，使用默认值: {ex.Message}");
        }
    }

    /// <summary>
    /// 从已保存配置复制所有属性（异常安全）。
    /// </summary>
    private void CopyFrom(PluginConfig saved)
    {
        if (saved is null) return;

        try
        {
            Version = saved.Version;
            CWHunt = saved.CWHunt ?? new CWHuntConfig();
            CDCHunt = saved.CDCHunt ?? new CDCHuntConfig();
            CWFate = saved.CWFate ?? new CWFateConfig();
            Notification = saved.Notification ?? new NotificationConfig();
            AuthToken = saved.AuthToken;
            CharacterName = saved.CharacterName ?? "";
            WorldName = saved.WorldName ?? "";
            WorldId = saved.WorldId;
            Datacenter = saved.Datacenter ?? "";
            WindowVisible = saved.WindowVisible;
            MqttUsername = saved.MqttUsername;
            MqttPassword = saved.MqttPassword;
            HuntSubscriptions = saved.HuntSubscriptions ?? [];
            FateSubscriptions = saved.FateSubscriptions ?? [];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SilverDasher] 配置复制失败: {ex.Message}");
        }
    }
}
