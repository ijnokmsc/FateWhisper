using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Edge_tts_sharp;
using Edge_tts_sharp.Model;

namespace SilverDasher.Services;

/// <summary>
/// TTS 语音播报 — edge_tts_sharp (内置 NAudio 播放)。
/// </summary>
public class TtsService : IDisposable
{
    private eVoice? _voice;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public TtsService()
    {
        try
        {
            var voices = Edge_tts.GetVoice();
            _voice = voices.FirstOrDefault(v => v.Name == "zh-CN-XiaoxiaoNeural")
                  ?? voices.FirstOrDefault(v => v.Locale == "zh-CN")
                  ?? voices.FirstOrDefault();
            Plugin.SharedLog?.Information($"[TTS] 语音: {_voice?.Name ?? "无"}");
        }
        catch (Exception ex)
        {
            Plugin.SharedLog?.Error($"[TTS] 获取语音列表失败: {ex.Message}");
        }
    }

    public async Task SpeakAsync(string text)
    {
        if (_disposed || _voice == null || string.IsNullOrWhiteSpace(text)) return;
        try
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            var option = new PlayOption { Text = text };
            await Task.Run(() => Edge_tts.PlayText(option, _voice), token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Plugin.SharedLog?.Error($"[TTS] 播放失败: {ex.Message}");
        }
    }

    public void Stop() => _cts?.Cancel();
    public void Dispose() { _disposed = true; Stop(); }
}
