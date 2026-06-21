using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ArkPlot.Tts.Model;
using SqlSugar;

namespace ArkPlot.Tts;

/// <summary>
/// 音色分配管理器。
/// 根据角色名 + 性别确定音色，支持 SQLite 持久化保证跨运行一致。
/// 支持 EdgeTTS 和 MiniMax 双音色池。
/// </summary>
public class VoiceManager
{
    private readonly ConcurrentDictionary<string, string> _cache = new();
    private readonly SqlSugarClient? _db;
    private readonly EngineType _engineType;
    private bool _tableReady;

    /// <summary>
    /// 创建 VoiceManager。
    /// </summary>
    /// <param name="db">可选的 SqlSugarClient，提供时启用 DB 持久化。</param>
    /// <param name="engineType">TTS 引擎类型，默认 EdgeTTS。</param>
    public VoiceManager(SqlSugarClient? db = null, EngineType engineType = EngineType.EdgeTts)
    {
        _db = db;
        _engineType = engineType;
    }

    /// <summary>当前引擎类型。</summary>
    public EngineType EngineType => _engineType;

    /// <summary>当前女声音色池。</summary>
    private string[] FemalePool => _engineType == EngineType.MiniMax
        ? MiniMaxVoicePool.Female
        : VoicePool.Female;

    /// <summary>当前男声音色池。</summary>
    private string[] MalePool => _engineType == EngineType.MiniMax
        ? MiniMaxVoicePool.Male
        : VoicePool.Male;

    /// <summary>当前旁白专用音色。</summary>
    private string NarratorVoice => _engineType == EngineType.MiniMax
        ? MiniMaxVoicePool.Narrator
        : VoicePool.Narrator;

    /// <summary>当前用于 fallback 的男声。</summary>
    private string FallbackMaleVoice => _engineType == EngineType.MiniMax
        ? MiniMaxVoicePool.Male[0]
        : VoicePool.Male[0];

    /// <summary>
    /// 根据角色名获取音色。
    /// 优先查 DB（如已配置）→ 内存缓存 → 计算分配 → 写入 DB。
    /// </summary>
    public string GetVoiceForCharacter(string characterName, string? gender = null)
    {
        characterName = NormalizeCharacterName(characterName);
        if (string.IsNullOrWhiteSpace(characterName))
            return NarratorVoice;

        var cacheKey = BuildCacheKey(characterName, gender);

        return _cache.GetOrAdd(cacheKey, _ =>
        {
            // 1. 查 DB
            EnsureTableReady();
            var dbVoice = LoadFromDb(characterName);
            if (!string.IsNullOrEmpty(dbVoice))
                return dbVoice;

            // 2. 计算分配
            var hash = GetStableHash(characterName);
            bool isFemale = !string.IsNullOrWhiteSpace(gender)
                ? gender.Contains("女")
                : hash % 2 == 0;

            var pool = isFemale ? FemalePool : MalePool;
            var index = Math.Abs(hash) % pool.Length;
            var voice = pool[index];

            // 3. 写入 DB
            SaveToDb(characterName, gender, voice);

            return voice;
        });
    }

    /// <summary>
    /// 手动指定角色音色，并同步更新缓存和持久化记录。
    /// </summary>
    public void SetVoiceForCharacter(string characterName, string? gender, string voiceId)
    {
        characterName = NormalizeCharacterName(characterName);
        if (string.IsNullOrWhiteSpace(characterName) || string.IsNullOrWhiteSpace(voiceId))
            return;

        var cacheKey = BuildCacheKey(characterName, gender);

        foreach (var key in _cache.Keys
                     .Where(k => string.Equals(
                                     NormalizeCharacterName(k.Split('|')[0]),
                                     characterName,
                                     StringComparison.Ordinal))
                     .ToArray())
        {
            _cache.TryRemove(key, out _);
        }

        _cache[cacheKey] = voiceId;
        EnsureTableReady();
        SaveToDb(characterName, gender, voiceId);
    }

    /// <summary>获取旁白专用音色。</summary>
    public string GetNarratorVoice() => NarratorVoice;

    /// <summary>获取性别无法识别时的 fallback 音色。</summary>
    public string GetFallbackVoice() => FallbackMaleVoice;

    /// <summary>
    /// 根据性别获取音色（固定选第一个，稳定可预测）。
    /// </summary>
    public string GetVoiceForGender(string? gender)
    {
        if (string.IsNullOrWhiteSpace(gender))
            return NarratorVoice;

        var pool = gender.Contains("女") ? FemalePool : MalePool;
        return pool[0];
    }

    /// <summary>已分配音色的角色数量（内存缓存）。</summary>
    public int AssignedCount => _cache.Count;

    /// <summary>
    /// 计算字符串的稳定哈希值（跨进程、跨平台一致，基于 SHA256）。
    /// </summary>
    internal static int GetStableHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToInt32(bytes, 0);
    }

    private void EnsureTableReady()
    {
        if (_db == null || _tableReady) return;
        _db.CodeFirst.SetStringDefaultLength(200).InitTables(typeof(CharacterVoiceMap));
        _tableReady = true;
    }

    private string? LoadFromDb(string characterName)
    {
        if (_db == null) return null;
        characterName = NormalizeCharacterName(characterName);
        var entity = _db.Queryable<CharacterVoiceMap>()
            .Where(it => it.CharacterName == characterName)
            .OrderByDescending(it => it.AssignedAt)
            .OrderByDescending(it => it.Id)
            .First();
        return entity?.Voice;
    }

    private void SaveToDb(string characterName, string? gender, string voice)
    {
        if (_db == null) return;
        characterName = NormalizeCharacterName(characterName);

        var existing = _db.Queryable<CharacterVoiceMap>()
            .Where(it => it.CharacterName == characterName)
            .OrderByDescending(it => it.AssignedAt)
            .OrderByDescending(it => it.Id)
            .First();

        if (existing == null)
        {
            _db.Insertable(new CharacterVoiceMap
            {
                CharacterName = characterName,
                Gender = gender,
                Voice = voice,
                AssignedAt = DateTime.UtcNow
            }).ExecuteCommand();
            return;
        }

        existing.Gender = gender;
        existing.Voice = voice;
        existing.AssignedAt = DateTime.UtcNow;
        _db.Updateable(existing).ExecuteCommand();
    }

    private static string BuildCacheKey(string characterName, string? gender) =>
        string.IsNullOrWhiteSpace(NormalizeCharacterName(characterName))
            ? string.Empty
            : string.IsNullOrWhiteSpace(gender)
                ? NormalizeCharacterName(characterName)
                : $"{NormalizeCharacterName(characterName)}|{gender}";

    private static string NormalizeCharacterName(string? characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName))
            return string.Empty;

        return new string(characterName.Where(c => !char.IsWhiteSpace(c)).ToArray());
    }
}
