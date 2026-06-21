using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ArkPlot.Tts.Model;
using ArkPlot.Tts.Models;
using SqlSugar;

namespace ArkPlot.Tts;

/// <summary>
/// 统一音色池管理器。
/// 根据角色名 + 性别确定音色，支持 SQLite 持久化保证跨运行一致。
/// 所有引擎（EdgeTTS、MiniMax、自定义）的音色汇入统一池，按引擎标记路由。
/// </summary>
public class VoiceManagerUnified
{
    private readonly ConcurrentDictionary<string, VoiceAssignment> _cache = new();
    private readonly SqlSugarClient? _db;
    private readonly object _dbLock = new();
    private readonly List<(VoiceEntry Entry, EngineType Engine, Guid? CustomEngineId)> _pool;
    private readonly string[] _femaleVoices;
    private readonly string[] _maleVoices;
    private readonly string _narratorVoice;
    private bool _tableReady;

    /// <summary>
    /// 创建统一音色池管理器。
    /// </summary>
    /// <param name="pool">统一音色池（VoicePoolBuilder.Build 的输出）。</param>
    /// <param name="defaultNarratorVoice">默认旁白音色 VoiceId。</param>
    /// <param name="db">可选的 SqlSugarClient，提供时启用 DB 持久化。</param>
    public VoiceManagerUnified(
        List<(VoiceEntry Entry, EngineType Engine, Guid? CustomEngineId)> pool,
        string defaultNarratorVoice,
        SqlSugarClient? db = null)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _db = db;
        _narratorVoice = defaultNarratorVoice;

        // 按性别分组
        _femaleVoices = pool
            .Where(v => v.Entry.Gender == "Female")
            .Select(v => v.Entry.VoiceId)
            .ToArray();

        _maleVoices = pool
            .Where(v => v.Entry.Gender == "Male")
            .Select(v => v.Entry.VoiceId)
            .ToArray();
    }

    /// <summary>统一音色池。</summary>
    public IReadOnlyList<(VoiceEntry Entry, EngineType Engine, Guid? CustomEngineId)> Pool => _pool.AsReadOnly();

    /// <summary>池中音色总数。</summary>
    public int PoolCount => _pool.Count;

    /// <summary>
    /// 根据角色名获取音色（带引擎标记）。
    /// 优先查 DB（如已配置）→ 内存缓存 → 计算分配 → 写入 DB。
    /// </summary>
    public VoiceAssignment GetVoiceForCharacter(string characterName, string? gender = null)
    {
        characterName = NormalizeCharacterName(characterName);
        if (string.IsNullOrWhiteSpace(characterName))
            return GetNarratorAssignment();

        var cacheKey = BuildCacheKey(characterName, gender);

        return _cache.GetOrAdd(cacheKey, _ =>
        {
            // 1. 查 DB
            EnsureTableReady();
            var dbVoice = LoadFromDb(characterName);
            if (dbVoice != null)
                return dbVoice;

            // 2. 计算分配
            var hash = GetStableHash(characterName);
            bool isFemale = !string.IsNullOrWhiteSpace(gender)
                ? gender.Contains("女")
                : hash % 2 == 0;

            var voices = isFemale ? _femaleVoices : _maleVoices;
            if (voices.Length == 0)
            {
                // 如果该性别池为空，从全部池中选
                voices = _pool.Select(v => v.Entry.VoiceId).ToArray();
            }

            if (voices.Length == 0)
            {
                // 极端情况：池为空，返回默认旁白
                return GetNarratorAssignment();
            }

            var index = Math.Abs(hash) % voices.Length;
            var voiceId = voices[index];
            var assignment = FindAssignment(voiceId);

            // 3. 写入 DB
            SaveToDb(characterName, gender, voiceId);

            return assignment;
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

        var assignment = FindAssignment(voiceId);
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

        _cache[cacheKey] = assignment;
        EnsureTableReady();
        SaveToDb(characterName, gender, voiceId);
    }

    /// <summary>获取旁白专用音色（带引擎标记）。</summary>
    public VoiceAssignment GetNarratorAssignment()
    {
        return FindAssignment(_narratorVoice);
    }

    /// <summary>获取性别无法识别时的 fallback 音色。</summary>
    public VoiceAssignment GetFallbackAssignment()
    {
        if (_maleVoices.Length > 0)
            return FindAssignment(_maleVoices[0]);
        if (_pool.Count > 0)
            return new VoiceAssignment(_pool[0].Entry, _pool[0].Engine, _pool[0].CustomEngineId);
        return new VoiceAssignment(new VoiceEntry("fallback", "Fallback", "Unknown"), EngineType.EdgeTts, null);
    }

    /// <summary>
    /// 根据性别获取音色（固定选第一个，稳定可预测）。
    /// </summary>
    public VoiceAssignment GetVoiceForGender(string? gender)
    {
        if (string.IsNullOrWhiteSpace(gender))
            return GetNarratorAssignment();

        var voices = gender.Contains("女") ? _femaleVoices : _maleVoices;
        if (voices.Length > 0)
            return FindAssignment(voices[0]);
        return GetNarratorAssignment();
    }

    /// <summary>
    /// 根据 VoiceId 查找对应的引擎标记。
    /// </summary>
    public VoiceAssignment FindAssignment(string voiceId)
    {
        var match = _pool.FirstOrDefault(v => v.Entry.VoiceId == voiceId);
        if (match.Entry != null)
            return new VoiceAssignment(match.Entry, match.Engine, match.CustomEngineId);

        // 未找到时返回默认
        return new VoiceAssignment(
            new VoiceEntry(voiceId, voiceId, "Unknown"),
            EngineType.EdgeTts,
            null);
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
        lock (_dbLock)
        {
            if (_tableReady) return;
            try
            {
                _db.CodeFirst.SetStringDefaultLength(200).InitTables(typeof(CharacterVoiceMap));
                _tableReady = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[diag] EnsureTableReady failed: {ex.Message} | " +
                    $"conn={_db.CurrentConnectionConfig?.ConnectionString}");
                throw;
            }
        }
    }

    private VoiceAssignment? LoadFromDb(string characterName)
    {
        if (_db == null) return null;
        characterName = NormalizeCharacterName(characterName);
        lock (_dbLock)
        {
            string? voiceId;
            try
            {
                var entity = _db.Queryable<CharacterVoiceMap>()
                    .Where(it => it.CharacterName == characterName)
                    .OrderByDescending(it => it.AssignedAt)
                    .OrderByDescending(it => it.Id)
                    .First();
                voiceId = entity?.Voice;
            }
            catch
            {
                return null;
            }

            if (string.IsNullOrEmpty(voiceId)) return null;
            return FindAssignment(voiceId);
        }
    }

    /// <summary>
    /// 保存角色-音色映射到数据库，实现持久化存储。
    /// 如果数据库未初始化则直接返回，先删除该角色的所有旧记录，再插入新的映射记录。
    /// </summary>
    /// <param name="characterName">角色名称，会自动做归一化处理</param>
    /// <param name="gender">角色的性别标识，允许为空</param>
    /// <param name="voiceId">要绑定的音色ID，必须存在于音色池中</param>
    private void SaveToDb(string characterName, string? gender, string voiceId)
    {
        if (_db == null) return;
        characterName = NormalizeCharacterName(characterName);
        lock (_dbLock)
        {
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
                    Voice = voiceId,
                    AssignedAt = DateTime.UtcNow
                }).ExecuteCommand();
                return;
            }

            existing.Gender = gender;
            existing.Voice = voiceId;
            existing.AssignedAt = DateTime.UtcNow;
            _db.Updateable(existing).ExecuteCommand();
        }
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

/// <summary>
/// 音色分配结果（带引擎标记），用于路由到正确的合成引擎。
/// </summary>
public record VoiceAssignment(VoiceEntry Entry, EngineType Engine, Guid? CustomEngineId)
{
    /// <summary>音色 ID（便捷访问）。</summary>
    public string VoiceId => Entry.VoiceId;

    /// <summary>显示名称（便捷访问）。</summary>
    public string DisplayName => Entry.DisplayName;
}
