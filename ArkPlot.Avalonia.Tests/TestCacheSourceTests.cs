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

    [Fact]
    public void Lists_DoNotRescanTheTextTableForEveryChapterInUnindexedCaches()
    {
        _db.Ado.ExecuteCommand("""
            INSERT INTO Plot VALUES (3,1,'无文本的已解析章节',11,2),(4,2,'未解析但有文本',11,1);
            INSERT INTO FormattedTextEntry (Id,PlotId) VALUES (4,4);
            """);
        var queries = new List<string>();
        _db.Aop.OnLogExecuting = (sql, _) => queries.Add(sql);
        Assert.Equal(1, Assert.Single(_source.GetActivities(false)).Id);
        Assert.Equal(1, Assert.Single(_source.GetChapters(1)).Id);
        _db.Aop.OnLogExecuting = null;
        foreach (var query in queries.Where(q => q.Contains("SELECT p.Id") || q.Contains("SELECT DISTINCT a.Id")))
        {
            var plan = _db.Ado.SqlQuery<QueryPlan>("EXPLAIN QUERY PLAN " + query, new SugarParameter("@act", 1));
            Assert.DoesNotContain(plan, row => row.Detail.Contains("CORRELATED", StringComparison.OrdinalIgnoreCase));
        }
        Assert.Equal(2, queries.Count(q => q.Contains("SELECT p.Id") || q.Contains("SELECT DISTINCT a.Id")));
    }

    [Fact]
    public async Task FileCache_UsesIndependentReadOnlyConnectionsForConcurrentTabs()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "cache.db");
        _db.Ado.ExecuteCommand("VACUUM INTO @path", new SugarParameter("@path", path));
        var before = File.ReadAllBytes(path);
        var source = new TestCacheSource(path, _root);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            Assert.Single(source.GetActivities(false));
            Assert.Single(source.GetChapters(1));
            Assert.Contains("你好", source.GetText(1));
            Assert.Equal(3, source.GetImages(1).Count);
            Assert.Single(source.GetCharacters());
        })));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void MissingCache_DoesNotCreateDatabase()
    {
        var path = Path.Combine(_root, "missing.db");
        var source = new TestCacheSource(path, _root);
        Assert.Empty(source.GetActivities(false));
        Assert.Empty(source.GetActivities(true));
        Assert.Empty(source.GetCharacters());
        Assert.False(File.Exists(path));
    }

    public class QueryPlan { public string Detail { get; set; } = ""; }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
