using ArkPlot.Tts.Models;

namespace ArkPlot.Tts;

/// <summary>
/// 统一音色池构建器，合并所有引擎的音色
/// </summary>
public static class VoicePoolBuilder
{
    /// <summary>
    /// 构建统一音色池
    /// </summary>
    /// <param name="settings">TTS 配置</param>
    /// <returns>带引擎标记的音色列表，重名音色后加入者覆盖先加入者</returns>
    public static List<(VoiceEntry Entry, EngineType Engine, Guid? CustomEngineId)> Build(TtsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var pool = new Dictionary<string, (VoiceEntry Entry, EngineType Engine, Guid? CustomEngineId)>();

        // 优先级 1：EdgeTTS 内置
        if (settings.EdgeTtsEnabled)
        {
            foreach (var voice in GetEdgeTtsVoices())
            {
                pool[voice.VoiceId] = (voice, EngineType.EdgeTts, null);
            }
        }

        // 优先级 2：MiniMax（需要启用且有 API Key）
        if (settings.MiniMaxEnabled && !string.IsNullOrEmpty(settings.MiniMaxApiKey))
        {
            foreach (var voice in settings.MiniMaxVoices)
            {
                pool[voice.VoiceId] = (voice, EngineType.MiniMax, null);
            }
        }

        // 优先级 3：自定义引擎
        foreach (var engine in settings.CustomEngines)
        {
            foreach (var voice in engine.Voices)
            {
                pool[voice.VoiceId] = (voice, EngineType.Custom, engine.Id);
            }
        }

        return pool.Values.ToList();
    }

    /// <summary>
    /// 获取 EdgeTTS 内置音色（从现有 VoicePool 转换）
    /// </summary>
    private static IEnumerable<VoiceEntry> GetEdgeTtsVoices()
    {
        // 女声
        foreach (var voice in VoicePool.Female)
        {
            yield return new VoiceEntry(voice, voice, "Female", "zh-CN");
        }

        // 男声
        foreach (var voice in VoicePool.Male)
        {
            yield return new VoiceEntry(voice, voice, "Male", "zh-CN");
        }

        // 旁白
        yield return new VoiceEntry(VoicePool.Narrator, VoicePool.Narrator, "Female", "zh-CN");
    }
}
