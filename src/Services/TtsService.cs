using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Edge_tts_sharp;
using Edge_tts_sharp.Model;

namespace FateWhisper.Services;

/// <summary>
/// TTS 语音播报 — edge_tts_sharp (内置 NAudio 播放)。
///
/// 关键修复：旧实现在每次播报时调用 _tts.Stop() 后立即 fire-and-forget 启动新的
/// Edge_tts.PlayText，多个 PlayText 并发争用同一个 NAudio 输出设备，导致
/// "有几率 TTS 未播报"（设备被占用 → 播放静默失败 / 被取消）。
///
/// 新实现改为串行单消费者队列：同一时刻只有一个 PlayText 在运行，新文本入队排队，
/// 从根本上消除设备争用。Edge_tts.PlayText 为阻塞式播放，故放在线程池执行，
/// 由 _currentCts 在 shutdown 时取消调度。
/// </summary>
public class TtsService : IDisposable
{
    private readonly eVoice? _voice;
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>();
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly Task _consumer;
    private CancellationTokenSource? _currentCts;
    private readonly object _gate = new();
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

        _consumer = Task.Run(ConsumeAsync);
    }

    /// <summary>
    /// 入队一段要朗读的文本。串行播放，不会因并发而静音。
    /// 入队为同步操作（极快），调用方无需 await；重复/高频文本由队列顺序消费。
    /// </summary>
    public Task SpeakAsync(string text)
    {
        if (_disposed || _voice == null || string.IsNullOrWhiteSpace(text))
            return Task.CompletedTask;

        _queue.Writer.TryWrite(text);
        return Task.CompletedTask;
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var text in _queue.Reader.ReadAllAsync(_shutdownCts.Token))
            {
                var cts = new CancellationTokenSource();
                lock (_gate) _currentCts = cts;

                try
                {
                    // 阻塞式播放放到线程池；token 用于 shutdown 时取消调度
                    await Task.Run(() => Edge_tts.PlayText(new PlayOption { Text = text }, _voice!), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // 被 shutdown 取消，属正常流程
                }
                catch (Exception ex)
                {
                    Plugin.SharedLog?.Error($"[TTS] 播放失败: {ex.Message}");
                }
                finally
                {
                    lock (_gate) { if (_currentCts == cts) _currentCts = null; }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown 取消 ReadAllAsync，正常退出
        }
    }

    /// <summary>
    /// 取消当前正在调度的播放项。
    /// 注意：Edge_tts.PlayText 为阻塞式且非 token 感知，已开始的音频不会被中断，
    /// 但串行模型保证一次只有一处占用设备，新请求会自然排队其后，不会静音。
    /// </summary>
    public void Stop() => _currentCts?.Cancel();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _queue.Writer.TryComplete(); } catch { }
        try { _shutdownCts.Cancel(); } catch { }
        try { _currentCts?.Cancel(); } catch { }
    }
}
