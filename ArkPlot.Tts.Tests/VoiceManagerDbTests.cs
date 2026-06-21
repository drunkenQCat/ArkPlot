using ArkPlot.Core.Infrastructure;

namespace ArkPlot.Tts.Tests;

public class VoiceManagerDbTests : IDisposable
{
    private readonly string _connString;

    public VoiceManagerDbTests()
    {
        _connString = $"Data Source=:memory:";
        DbFactory.ConfigureForTesting(_connString);
    }

    [Fact]
    public void GetVoiceForCharacter_WithDb_PersistsToDatabase()
    {
        var db = DbFactory.GetClient();
        var vm = new VoiceManager(db);

        var voice = vm.GetVoiceForCharacter("测试角色A", "女");

        // 验证写入了 DB
        var dbVoice = db.Queryable<Model.CharacterVoiceMap>()
            .Where(m => m.CharacterName == "测试角色A")
            .Select(m => m.Voice)
            .First();
        Assert.Equal(voice, dbVoice);
    }

    [Fact]
    public void GetVoiceForCharacter_SecondInstance_LoadsFromDb()
    {
        var db = DbFactory.GetClient();

        // 第一个实例分配
        var vm1 = new VoiceManager(db);
        var voice1 = vm1.GetVoiceForCharacter("持久化角色", "女");

        // 第二个实例（新缓存）应该从 DB 读取相同音色
        var vm2 = new VoiceManager(db);
        var voice2 = vm2.GetVoiceForCharacter("持久化角色", "女");

        Assert.Equal(voice1, voice2);
    }

    [Fact]
    public void GetVoiceForCharacter_NoDb_StillWorks()
    {
        var vm = new VoiceManager(); // 无 DB

        var voice = vm.GetVoiceForCharacter("无DB角色", "女");
        Assert.True(VoicePool.IsFemaleVoice(voice));
    }

    [Fact]
    public void GetVoiceForCharacter_NoDb_ConsistentAcrossInstances()
    {
        // 无 DB 时靠 SHA256 哈希，也应一致
        var vm1 = new VoiceManager();
        var vm2 = new VoiceManager();

        var v1 = vm1.GetVoiceForCharacter("哈希角色", "男");
        var v2 = vm2.GetVoiceForCharacter("哈希角色", "男");
        Assert.Equal(v1, v2);
    }

    [Fact]
    public void GetVoiceForCharacter_NormalizesWhitespaceVariants()
    {
        var db = DbFactory.GetClient();
        var vm1 = new VoiceManager(db);
        var voice1 = vm1.GetVoiceForCharacter("桑 尼");

        var vm2 = new VoiceManager(db);
        var voice2 = vm2.GetVoiceForCharacter("桑尼");

        var maps = db.Queryable<Model.CharacterVoiceMap>()
            .OrderBy(m => m.Id)
            .ToList();

        Assert.Equal(voice1, voice2);
        Assert.Single(maps);
        Assert.Equal("桑尼", maps[0].CharacterName);
    }

    public void Dispose()
    {
        DbFactory.Reset();
    }
}
