using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;

namespace FateWhisper.Services;

/// <summary>
/// 订阅持久化存储 — 独立 JSON 文件，绕过 Dalamud PluginConfig 序列化。
/// </summary>
public class SubscriptionsStore
{
    private readonly string _filePath;
    private readonly IPluginLog _log;
    private List<int> _huntIds = [];
    private List<int> _fateIds = [];

    public IReadOnlyList<int> HuntIds => _huntIds;
    public IReadOnlyList<int> FateIds => _fateIds;

    public SubscriptionsStore(string configDir, IPluginLog log)
    {
        _log = log;
        // 防坑：禁用/重启用时 ConfigDirectory 可能变化，用绝对路径
        if (string.IsNullOrEmpty(configDir) || !Path.IsPathRooted(configDir))
        {
            configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XIVLauncherCN", "pluginConfigs", "FateWhisper");
            _log.Warning($"[FateWhisper] SubscriptionsStore: ConfigDirectory 无效，回退到 {configDir}");
        }
        _filePath = Path.Combine(configDir, "FateWhisper_subs.json");
        _log.Information($"[FateWhisper] SubscriptionsStore: 文件路径={_filePath}");
        Load();
    }

    public bool IsHuntSubscribed(int mobId) => _huntIds.Contains(mobId);
    public bool IsFateSubscribed(int fateId) => _fateIds.Contains(fateId);

    public void AddHunt(int mobId)
    {
        if (!_huntIds.Contains(mobId))
        {
            _huntIds.Add(mobId);
            Save();
            _log.Information($"[FateWhisper] SubscriptionsStore: 添加猎怪 {mobId}, 总数={_huntIds.Count}");
        }
    }

    public void RemoveHunt(int mobId)
    {
        if (_huntIds.RemoveAll(i => i == mobId) > 0)
        {
            Save();
            _log.Information($"[FateWhisper] SubscriptionsStore: 移除猎怪 {mobId}, 总数={_huntIds.Count}");
        }
    }

    public void AddFate(int fateId)
    {
        if (!_fateIds.Contains(fateId))
        {
            _fateIds.Add(fateId);
            Save();
            _log.Debug($"[FateWhisper] SubscriptionsStore: 添加FATE {fateId}, 总数={_fateIds.Count}");
        }
    }

    public void RemoveFate(int fateId)
    {
        if (_fateIds.RemoveAll(i => i == fateId) > 0)
        {
            Save();
            _log.Debug($"[FateWhisper] SubscriptionsStore: 移除FATE {fateId}, 总数={_fateIds.Count}");
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(_filePath));
                if (json["hunt_subs"] is Newtonsoft.Json.Linq.JArray hArr)
                    _huntIds = hArr.Select(t => (int)t).ToList();
                if (json["fate_subs"] is Newtonsoft.Json.Linq.JArray fArr)
                    _fateIds = fArr.Select(t => (int)t).ToList();
                _log.Information($"[FateWhisper] SubscriptionsStore: 加载完成 hunt={_huntIds.Count} fate={_fateIds.Count} (path={_filePath})");
            }
            else
            {
                _log.Information($"[FateWhisper] SubscriptionsStore: 文件不存在，使用空列表 (path={_filePath})");
            }
        }
        catch (Exception ex)
        {
            _log.Error($"[FateWhisper] SubscriptionsStore: 加载失败 {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var data = new { hunt_subs = _huntIds, fate_subs = _fateIds };
            File.WriteAllText(_filePath, JsonConvert.SerializeObject(data));
            _log.Information($"[FateWhisper] SubscriptionsStore: 保存成功 hunt={_huntIds.Count} fate={_fateIds.Count}");
        }
        catch (Exception ex)
        {
            _log.Error($"[FateWhisper] SubscriptionsStore: 保存失败 {ex.Message}");
        }
    }
}
