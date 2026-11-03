using System.Text.Json;
using ArkPlot.Avalonia.Services;
using SqlSugar;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

public class TestCacheSourceTests : IDisposable
{
    private readonly SqlSugarClient _db;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arkplot-cache-test-{Guid.NewGuid():N}");
    private readonly TestCacheSource _source;

    public TestCacheSourceTests()
    {
        _db = new SqlSugarClient(new ConnectionConfig { ConnectionString = "Data Source=:memory:", DbType = DbType.Sqlite, IsAutoCloseConnection = false });
        _db.Ado.ExecuteCommand("""
            CREATE TABLE Acts (Id INTEGER, Name TEXT);
            CREATE TABLE StoryChapters (Id INTEGER, StorySort INTEGER);
            CREATE TABLE Plot (Id INTEGER, ActId INTEGER, Title TEXT, StoryChapterId INTEGER, Status INTEGER);
            CREATE TABLE FormattedTextEntry (Id INTEGER, PlotId INTEGER, "Index" INTEGER, CharacterCode TEXT,
              CharacterName TEXT, Dialog TEXT, MdText TEXT, Portraits TEXT, Bg TEXT, PortraitFocus INTEGER);
            CREATE TABLE PicDescriptions (ImageUrl TEXT, PicDesc TEXT);
            INSERT INTO Acts VALUES (1,'已缓存活动'),(2,'未解析活动');
            INSERT INTO StoryChapters VALUES (10,1),(11,2);
            INSERT INTO Plot VALUES (1,1,'第一章',10,2),(2,2,'未解析',11,1);
            INSERT INTO FormattedTextEntry VALUES
              (1,1,1,'char_a','甲','你好','', '["https://example.invalid/a.png","https://example.invalid/b.png"]','https://example.invalid/bg.png',0),
              (2,1,2,'char_a','甲','后续对白','', '["https://example.invalid/a.png"]','https://example.invalid/bg.png',0),
              (3,2,1,'char_x','未解析角色','原始对白','', '["https://example.invalid/x.png"]','',0);
            INSERT INTO PicDescriptions VALUES ('https://example.invalid/a.png','已有立绘描述');
            """);
        _source = new TestCacheSource(() => _db, _root);
    }

    [Fact]
    public void Source_ReadsParsedChaptersAndOrderedDialogueOnly()
    {
        Assert.Equal("已缓存活动", Assert.Single(_source.GetActivities(false)).Name);
        Assert.Equal("第一章", Assert.Single(_source.GetChapters(1)).Name);
        Assert.Equal("甲：你好\n甲：后续对白", _source.GetText(1));
        Assert.Equal(-1, _source.GetActivities(true)[0].Id);
    }

    [Fact]
    public void Text_StripsMediaMarkupAndRetainsStoryContent()
    {
        _db.Ado.ExecuteCommand("""
            UPDATE FormattedTextEntry SET Dialog = '',
            MdText = '<img src="https://example.invalid/bg.png"/><audio controls></audio>风吹过。' WHERE Id = 2
            """);
        var text = _source.GetText(1);
        Assert.Contains("甲：你好", text);
        Assert.Contains("风吹过。", text);
        Assert.DoesNotContain("<img", text);
        Assert.DoesNotContain("<audio", text);
    }

    [Fact]
    public void Images_AreDeduplicatedAndPreferLocalFiles()
    {
        var file = Path.Combine(_root, "已缓存活动", "example.invalid", "a.png");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "local fixture");
        var images = _source.GetImages(1);
        Assert.Equal(3, images.Count);
        var portrait = images.Single(i => i.Url.EndsWith("/a.png"));
        Assert.Equal("已有立绘描述", portrait.Description);
        Assert.Equal(file, portrait.PreviewSource);
        Assert.Single(_source.GetImages(-1));
    }

    [Fact]
    public void Characters_AreDeduplicatedAndUseFocusedPortrait()
    {
        var character = Assert.Single(_source.GetCharacters());
        Assert.Equal("甲", character.Name);
        Assert.EndsWith("/a.png", character.PortraitUrl);
        Assert.Contains("第一章", character.Source);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
