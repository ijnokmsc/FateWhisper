using System;
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

    /// <summary>
    /// 初始化配置实例并从持久化存储加载。
    /// </summary>
    /// <param name="pluginInterface">Dalamud 插件接口。</param>
    public PluginConfig(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        Load();
    }

    /// <summary>
    /// 将当前配置保存到持久化存储。
    /// </summary>
    public void Save()
    {
        _pluginInterface.SavePluginConfig(this);
    }

    /// <summary>
    /// 从持久化存储加载配置，若无则使用默认值。
    /// </summary>
    private void Load()
    {
        var saved = _pluginInterface.GetPluginConfig() as PluginConfig;
        if (saved is not null)
        {
            CWHunt = saved.CWHunt ?? new CWHuntConfig();
            CDCHunt = saved.CDCHunt ?? new CDCHuntConfig();
            CWFate = saved.CWFate ?? new CWFateConfig();
            Notification = saved.Notification ?? new NotificationConfig();
            AuthToken = saved.AuthToken;
            CharacterName = saved.CharacterName;
            WorldName = saved.WorldName;
            WorldId = saved.WorldId;
            Datacenter = saved.Datacenter;
            WindowVisible = saved.WindowVisible;
        }
    }
}
