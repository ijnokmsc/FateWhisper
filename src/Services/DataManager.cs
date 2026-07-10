using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using FateWhisper.Models;

namespace FateWhisper.Services;

/// <summary>
/// 静态数据管理器，负责加载本地 JSON 数据文件并提供名称/区域/世界翻译功能。
/// 支持远程版本检查和热更新下载。
/// </summary>
public class DataManager : IDisposable
{
    private readonly IPluginLog _log;
    private readonly string _dataDir;
    private readonly HttpClient _httpClient;
    private const string RemoteBaseUrl = "https://tree.silverdasher.com/data/";
    private const string Prefix = "[FateWhisper]";

    /// <summary>
    /// 猎怪数据 (hunt.json 或 hunts.json)：mobId → HuntMob。
    /// 优先加载 hunt.json，若不存在则回退到 hunts.json。
    /// </summary>
    public Dictionary<string, HuntMob> Hunts { get; private set; } = [];

    /// <summary>
    /// 猎怪分组数据 (sphunts.json)。
    /// </summary>
    public List<HuntGroup> SpecialHuntGroups { get; private set; } = [];

    /// <summary>
    /// FATE 数据 (fates.json)：fateId → FateInfo。
    /// </summary>
    public Dictionary<string, FateInfo> Fates { get; private set; } = [];

    /// <summary>
    /// 特殊 FATE 分组数据 (spfates.json)。
    /// </summary>
    public List<HuntGroup> SpecialFateGroups { get; private set; } = [];

    /// <summary>
    /// 区域数据 (territories.json)：territoryId → TerritoryInfo。
    /// </summary>
    public Dictionary<string, TerritoryInfo> Territories { get; private set; } = [];

    /// <summary>
    /// 世界/服务器数据 (worlds.json)：worldId → WorldInfo。
    /// </summary>
    public Dictionary<string, WorldInfo> Worlds { get; private set; } = [];

    /// <summary>
    /// 版本数据 (patches.json)。
    /// </summary>
    public List<PatchInfo> Patches { get; private set; } = [];

    /// <summary>
    /// Opcode 数据 (opcodes.json)。
    /// </summary>
    public List<OpcodeEntry> Opcodes { get; private set; } = [];

    /// <summary>
    /// 各数据文件的本地版本号 (versions.json)。
    /// </summary>
    public Dictionary<string, long> LocalVersions { get; private set; } = [];

    /// <summary>
    /// 所有可用的猎怪等级。
    /// </summary>
    public HashSet<string> AvailableRanks { get; private set; } = [];

    /// <summary>
    /// 初始化数据管理器并加载所有本地数据文件。
    /// </summary>
    /// <param name="pluginInterface">Dalamud 插件接口，用于定位插件目录。</param>
    /// <param name="log">日志服务。</param>
    public DataManager(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        _log = log;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _dataDir = Path.Combine(
            Path.GetDirectoryName(pluginInterface.AssemblyLocation.FullName) ?? ".",
            "data");
        LoadAllData();
    }

    /// <summary>
    /// 加载所有本地 JSON 数据文件。
    /// </summary>
    private void LoadAllData()
    {
        try
        {
            // 优先加载 hunt.json，若不存在或为空则回退到 hunts.json
            Hunts = LoadHuntData();

            SpecialHuntGroups = LoadListJson<HuntGroup>("sphunts.json");
            Fates = LoadDictJson<FateInfo>("fates.json");
            SpecialFateGroups = LoadListJson<HuntGroup>("spfates.json");
            Territories = LoadDictJson<TerritoryInfo>("territories.json");
            Worlds = LoadDictJson<WorldInfo>("worlds.json");
            Patches = LoadListJson<PatchInfo>("patches.json");
            Opcodes = LoadListJson<OpcodeEntry>("opcodes.json");

            // 加载版本文件
            var versionPath = Path.Combine(_dataDir, "versions.json");
            if (File.Exists(versionPath))
            {
                var versionJson = File.ReadAllText(versionPath);
                LocalVersions = JsonConvert.DeserializeObject<Dictionary<string, long>>(versionJson) ?? [];
            }

            // 收集所有可用的猎怪等级
            AvailableRanks = Hunts.Values
                .Select(h => h.Rank)
                .Where(r => !string.IsNullOrEmpty(r))
                .Distinct()
                .ToHashSet();

            // 构建世界 label → 中文名索引
            _worldNameByLabel = new Dictionary<string, string>();
            foreach (var (id, info) in Worlds)
            {
                if (!string.IsNullOrEmpty(info.NameLabel) && !string.IsNullOrEmpty(info.Name))
                    _worldNameByLabel[info.NameLabel] = info.Name;
            }

            _log.Information($"{Prefix} 数据加载完成: Hunts={Hunts.Count}, Fates={Fates.Count}, " +
                $"Territories={Territories.Count}, Worlds={Worlds.Count}, WorldLabels={_worldNameByLabel.Count}");
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} 数据加载失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载猎怪数据。优先使用 hunt.json，若不存在或为空则回退到 hunts.json。
    /// 统一文件策略：两个文件格式相同（mobId → HuntMob），服务器端可能使用不同文件名。
    /// </summary>
    private Dictionary<string, HuntMob> LoadHuntData()
    {
        // 优先加载 hunt.json
        var primaryPath = Path.Combine(_dataDir, "hunt.json");
        if (File.Exists(primaryPath))
        {
            try
            {
                var json = File.ReadAllText(primaryPath);
                var result = JsonConvert.DeserializeObject<Dictionary<string, HuntMob>>(json);
                if (result is not null && result.Count > 0)
                {
                    _log.Debug($"{Prefix} 从 hunt.json 加载了 {result.Count} 条猎怪数据");
                    return result;
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"{Prefix} hunt.json 解析失败: {ex.Message}，尝试回退到 hunts.json");
            }
        }

        // 回退到 hunts.json
        var fallbackPath = Path.Combine(_dataDir, "hunts.json");
        if (File.Exists(fallbackPath))
        {
            try
            {
                var json = File.ReadAllText(fallbackPath);
                var result = JsonConvert.DeserializeObject<Dictionary<string, HuntMob>>(json) ?? [];
                _log.Information($"{Prefix} 从 hunts.json 加载了 {result.Count} 条猎怪数据（回退）");
                return result;
            }
            catch (Exception ex)
            {
                _log.Error($"{Prefix} hunts.json 解析也失败: {ex.Message}");
            }
        }

        _log.Warning($"{Prefix} hunt.json 和 hunts.json 均未找到或无效，猎怪数据为空");
        return [];
    }

    /// <summary>
    /// 加载 JSON 文件为字典类型（含 "data" 包裹层）。
    /// </summary>
    private Dictionary<string, T> LoadDictJson<T>(string fileName) where T : class
    {
        var path = Path.Combine(_dataDir, fileName);
        if (!File.Exists(path))
        {
            _log.Warning($"{Prefix} 数据文件不存在: {path}");
            return [];
        }

        try
        {
            var json = File.ReadAllText(path);
            var wrapper = JsonConvert.DeserializeObject<DataWrapper<Dictionary<string, T>>>(json);
            return wrapper?.Data ?? [];
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} 解析 {fileName} 失败: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// 加载无包裹层的 JSON 文件直接反序列化为字典（hunt.json 使用此格式）。
    /// </summary>
    private Dictionary<string, T> LoadRawDictJson<T>(string fileName) where T : class
    {
        var path = Path.Combine(_dataDir, fileName);
        if (!File.Exists(path))
        {
            _log.Warning($"{Prefix} 数据文件不存在: {path}");
            return [];
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<Dictionary<string, T>>(json) ?? [];
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} 解析 {fileName} 失败: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// 加载 JSON 文件为列表类型（含 "data" 包裹层）。
    /// </summary>
    private List<T> LoadListJson<T>(string fileName) where T : class
    {
        var path = Path.Combine(_dataDir, fileName);
        if (!File.Exists(path))
        {
            _log.Warning($"{Prefix} 数据文件不存在: {path}");
            return [];
        }

        try
        {
            var json = File.ReadAllText(path);
            var wrapper = JsonConvert.DeserializeObject<DataWrapper<List<T>>>(json);
            return wrapper?.Data ?? [];
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} 解析 {fileName} 失败: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// 检查远程版本更新，下载需要更新的数据文件。
    /// </summary>
    public async Task CheckRemoteUpdatesAsync()
    {
        try
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var url = $"https://tree.silverdasher.com/data/versions.json?t={ts}";
            var response = await _httpClient.GetStringAsync(url);
            var remoteVersions = JsonConvert.DeserializeObject<Dictionary<string, long>>(response);

            if (remoteVersions is null || remoteVersions.Count == 0)
            {
                _log.Debug($"{Prefix} 远程版本数据为空，跳过更新检查");
                return;
            }

            foreach (var (key, remoteVersion) in remoteVersions)
            {
                var fileName = $"{key}.json";
                if (LocalVersions.TryGetValue(key, out var localVersion) && localVersion >= remoteVersion)
                {
                    continue;
                }

                try
                {
                    var fileUrl = $"{RemoteBaseUrl}{fileName}?t={ts}";
                    var fileContent = await _httpClient.GetStringAsync(fileUrl);
                    var localPath = Path.Combine(_dataDir, fileName);
                    await File.WriteAllTextAsync(localPath, fileContent);
                    LocalVersions[key] = remoteVersion;
                    _log.Information($"{Prefix} 数据文件已更新: {fileName} (v{remoteVersion})");
                }
                catch (Exception ex)
                {
                    _log.Warning($"{Prefix} 下载 {fileName} 失败: {ex.Message}");
                }
            }

            // 重新加载所有数据
            LoadAllData();
        }
        catch (Exception ex)
        {
            _log.Warning($"{Prefix} 远程版本检查失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 根据 mobId 查找猎怪的中文名称。
    /// </summary>
    public string LookupHuntName(string mobId)
    {
        if (Hunts.TryGetValue(mobId, out var mob))
            return mob.NameChs;
        return mobId;
    }

    /// <summary>
    /// 根据 territoryId 查找区域名称。
    /// </summary>
    public string LookupTerritoryName(string territoryId)
    {
        if (Territories.TryGetValue(territoryId, out var info) && !string.IsNullOrEmpty(info.Name))
            return info.Name;
        return $"Zone {territoryId}";
    }

    /// <summary>
    /// 判断指定区域是否为副本/实例内容（用于副本内静音）。
    /// 依据 territories.json 的 content 字段（对应游戏 ContentFinderCondition）。
    /// content != 0 ⇒ 该区域链接了副本内容查找器 ⇒ 视为副本。
    /// 此判定远比旧的 TerritoryType >= 1000 阈值准确：
    /// 大量真实副本的 ID &lt; 1000（如 142=日影地修炼所、159=放浪神古神殿），
    /// 旧阈值会漏检这些副本，导致副本内仍收到通知/TTS。
    /// 未知区域（不在数据中）保守返回 false，不静音，避免误伤野外通知。
    /// </summary>
    public bool IsDutyTerritory(uint territoryType)
    {
        if (Territories.TryGetValue(territoryType.ToString(), out var info))
            return info.Content != 0;
        return false;
    }

    /// <summary>
    /// 根据 worldId 查找服务器中文名称。
    /// </summary>
    public string LookupWorldName(string worldId)
    {
        if (Worlds.TryGetValue(worldId, out var info) && !string.IsNullOrEmpty(info.Name))
            return info.Name;
        return worldId;
    }

    /// <summary>
    /// 根据 fateId 查找 FATE 的中文名称。
    /// </summary>
    public string LookupFateName(string fateId)
    {
        if (Fates.TryGetValue(fateId, out var fate))
            return fate.NameChs;
        return fateId;
    }

    /// <summary>
    /// 根据 worldId 查找大区名称。
    /// </summary>
    public string LookupDatacenter(string worldId)
    {
        if (Worlds.TryGetValue(worldId, out var info) && !string.IsNullOrEmpty(info.Dc))
            return info.Dc;
        return "";
    }

    /// <summary>
    /// 根据 opcode 名称查找 OpcodeEntry。
    /// </summary>
    public OpcodeEntry? GetOpcode(string name)
    {
        return Opcodes.FirstOrDefault(o => o.Name == name);
    }

    // ===== 状态判定系统（对齐 ACT 版 MobStorage/FateStorage） =====

    /// <summary>
    /// 根据血量百分比获取猎怪状态。
    /// ACT 版 MobStorage.GetState(health)。
    /// </summary>
    public static HuntState GetHuntState(int healthPercent)
    {
        return healthPercent switch
        {
            100 => HuntState.Healthy,
            > 95 => HuntState.Taunted,
            > 0 => HuntState.Dying,
            _ => HuntState.Died
        };
    }

    /// <summary>
    /// 根据进度获取 FATE 状态。
    /// ACT 版 FateStorage.GetState(progress)。
    /// </summary>
    public static HuntState GetFateState(int progress)
    {
        return progress switch
        {
            0 => HuntState.Healthy,
            < 20 => HuntState.Taunted,
            < 100 => HuntState.Dying,
            _ => HuntState.Died
        };
    }

    /// <summary>
    /// 获取状态的中文名称。
    /// ACT 版 MobStorage.GetStateName(state)。
    /// </summary>
    public static string GetStateName(HuntState state)
    {
        return state switch
        {
            HuntState.Healthy => "健康",
            HuntState.Taunted => "已开怪",
            HuntState.Dying => "被暴打中",
            HuntState.Died => "挂了",
            HuntState.Unknown => "",
            _ => "不见了"
        };
    }

    /// <summary>
    /// 释放 HttpClient 资源。
    /// </summary>
    public void Dispose()
    {
        _httpClient.Dispose();
    }

    // ===== 坐标转换（对齐 ACT 版 Negotiator.ScanMobs） =====

    /// <summary>
    /// 游戏坐标 → 传输坐标。
    /// ACT 版公式: x = (gamePos * 0.02 + 21.5) * 100
    /// 结果以 int 存储，显示时除以 100。
    /// 注：Coordinate.Y 存储的是游戏 Z 轴（北-南），不含高度。
    /// </summary>
    public static Coordinate GamePosToTransmissionCoord(float gameX, float gameZ)
    {
        return new Coordinate
        {
            X = (int)((gameX * 0.02f + 21.5f) * 100.0),
            Y = (int)((gameZ * 0.02f + 21.5f) * 100.0)
        };
    }

    // ===== 世界 label 反向查找（MQTT Topic 中 worldLabel → 中文名） =====

    /// <summary>
    /// 世界名称 label → 中文名 索引（如 HongYuHai → 红玉海）。
    /// 在 LoadAllData 中构建。
    /// </summary>
    private Dictionary<string, string> _worldNameByLabel = new();

    /// <summary>
    /// 通过世界 label（拼音）查找中文名称。
    /// MQTT Topic 中 worldLabel 是拼音格式（如 HongYuHai）。
    /// </summary>
    public string LookupWorldNameByLabel(string label)
    {
        if (_worldNameByLabel.TryGetValue(label, out var name))
            return name;
        return label;
    }

    /// <summary>
    /// 查询猎怪等级（Rank），用于 MQTT 接收时补齐。
    /// </summary>
    public string LookupHuntRank(string mobId)
    {
        if (Hunts.TryGetValue(mobId, out var mob) && !string.IsNullOrEmpty(mob.Rank))
            return mob.Rank.ToUpperInvariant();
        return "";
    }

    /// <summary>
    /// 判断 FATE 是否为特殊 FATE（从 spfates.json 查询）。
    /// </summary>
    public bool IsFateSpecial(string fateId)
    {
        foreach (var group in SpecialFateGroups)
        {
            if (group.Items?.Contains(fateId) == true)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 根据世界 ID 获取大区拼音标签（如 1060 → LuXingNiao）。
    /// </summary>
    public string? LookupDcLabel(string worldId)
    {
        if (Worlds.TryGetValue(worldId, out var info) && !string.IsNullOrEmpty(info.DcLabel))
            return info.DcLabel;
        return null;
    }

    /// <summary>
    /// JSON 数据文件包装结构。
    /// </summary>
    private class DataWrapper<T>
    {
        [JsonProperty("version")]
        public string? Version { get; set; }

        [JsonProperty("data")]
        public T? Data { get; set; }
    }
}
