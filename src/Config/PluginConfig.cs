using System;
using System.Linq;
using System.Text.Json.Serialization;
using Dalamud.Configuration;
using Dalamud.Plugin;
using Newtonsoft.Json;

namespace FateWhisper.Config;

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

    /// <summary>玩家大区标签（用于跨大区判断），持久化以避免登录前 MQTT 消息误判</summary>
    [JsonProperty("player_dc_label")]
    public string PlayerDcLabel { get; set; } = "";

    /// <summary>
    /// 主窗口是否可见。
    /// </summary>
    [JsonProperty("window_visible")]
    public bool WindowVisible { get; set; } = false;

    /// <summary>
    /// 上次选中的 Tab 索引。
    /// </summary>
    [JsonProperty("active_tab")]
    public int ActiveTab { get; set; } = 0;

    /// <summary>
    /// 是否启用本地检测（扫描对象表 / Hook 网络包以检测猎怪与 FATE）。
    /// 关闭后为纯 MQTT 接收模式：插件只接收远端推送，不主动检测本地事件，
    /// 也不会把本地结果发布到 MQTT。
    /// 默认关闭。修改后需重载插件生效。
    /// </summary>
    [JsonProperty("enable_local_detection")]
    public bool EnableLocalDetection { get; set; } = false;

    /// <summary>
    /// 调试开关配置。
    /// </summary>
    [JsonProperty("debug")]
    public DebugConfig Debug { get; set; } = new();

    /// <summary>
    /// 跨服导航配置。
    /// </summary>
    [JsonProperty("cross_server")]
    public CrossServerConfig CrossServer { get; set; } = new();

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
    /// 导航测试保存的地点列表（内存中管理，持久化到独立文件）。
    /// </summary>
    [Newtonsoft.Json.JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public List<Models.NavTestLocation> NavTestLocations { get; set; } = [];

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
        Debug = new();
        CrossServer = new();
    }

    /// <summary>
    /// 将当前配置保存到持久化存储。
    /// </summary>
    public void Save()
    {
        if (_pluginInterface is null) { System.Diagnostics.Debug.WriteLine("[FateWhisper] Save: _pluginInterface is null"); return; }
        try
        {
            _pluginInterface.SavePluginConfig(this);
            System.Diagnostics.Debug.WriteLine($"[FateWhisper] Save: 主配置已保存");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FateWhisper] 配置保存失败: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"[FateWhisper] LoadSafe: CopyFrom 完成");

            // 加载导航测试地点列表
            LoadNavTestLocations();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FateWhisper] 配置加载失败，使用默认值: {ex.Message}");
        }
    }

    /// <summary>
    /// 保存导航测试地点列表到独立文件。
    /// </summary>
    public void SaveNavTestLocations()
    {
        try
        {
            var configDir = _pluginInterface.ConfigDirectory;
            if (configDir is null) return;
            var path = System.IO.Path.Combine(configDir.FullName, "FateWhisper_navtest.json");
            System.IO.File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(NavTestLocations));
        }
        catch { }
    }

    /// <summary>
    /// 从独立文件加载导航测试地点列表。
    /// </summary>
    public void LoadNavTestLocations()
    {
        try
        {
            var configDir = _pluginInterface.ConfigDirectory;
            if (configDir is null) return;
            var path = System.IO.Path.Combine(configDir.FullName, "FateWhisper_navtest.json");
            if (!System.IO.File.Exists(path)) return;
            var raw = System.IO.File.ReadAllText(path);
            var list = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Models.NavTestLocation>>(raw);
            if (list != null) NavTestLocations = list;
        }
        catch { }
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
            Notification = new NotificationConfig();
            if (saved.Notification is not null)
                Notification.CopyFrom(saved.Notification);
            AuthToken = saved.AuthToken;
            CharacterName = saved.CharacterName ?? "";
            WorldName = saved.WorldName ?? "";
            WorldId = saved.WorldId;
            Datacenter = saved.Datacenter ?? "";
            PlayerDcLabel = saved.PlayerDcLabel ?? "";
            WindowVisible = saved.WindowVisible;
            MqttUsername = saved.MqttUsername;
            MqttPassword = saved.MqttPassword;
            System.Diagnostics.Debug.WriteLine($"[FateWhisper] CopyFrom: 配置复制完成");
            ActiveTab = saved.ActiveTab;
            EnableLocalDetection = saved.EnableLocalDetection;
            Debug = saved.Debug ?? new DebugConfig();
            CrossServer = saved.CrossServer ?? new CrossServerConfig();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FateWhisper] 配置复制失败: {ex.Message}");
        }
    }
}

/// <summary>
/// 调试开关配置。
/// </summary>
[Serializable]
public class DebugConfig
{
    [JsonProperty("mqtt_messages")]
    public bool MqttMessages { get; set; } = false;

    [JsonProperty("hunt_triggers")]
    public bool HuntTriggers { get; set; } = false;

    [JsonProperty("fate_triggers")]
    public bool FateTriggers { get; set; } = false;

    [JsonProperty("navigation")]
    public bool Navigation { get; set; } = false;
}

/// <summary>
/// 跨服导航配置。
/// </summary>
[Serializable]
public class CrossServerConfig
{
    /// <summary>
    /// 是否优先使用 Lifestream（默认开启）。
    /// </summary>
    [JsonProperty("prefer_lifestream")]
    public bool PreferLifestream { get; set; } = true;
}
