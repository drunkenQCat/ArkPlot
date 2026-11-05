using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ArkPlot.Novelizer;
using Microsoft.Data.Sqlite;
using SqlSugar;

namespace ArkPlot.Avalonia.Services;

public class CachedChoice
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public override string ToString() => Name;
}

public record CachedImage(string Url, string Name, string Description, string LocalPath = "")
{
    public string PreviewSource => string.IsNullOrEmpty(LocalPath) ? Url : LocalPath;
    public override string ToString() => Name;
}

public record CachedCharacter(string Key, string Name, string PortraitUrl, string Source);

public interface ITestCacheSource
{
    List<CachedChoice> GetActivities(bool images);
    List<CachedChoice> GetChapters(long activityId);
    string GetText(long plotId);
    List<CachedImage> GetImages(long plotId);
    List<CachedCharacter> GetCharacters();
}

/// <summary>Reads existing plot and image-description caches without generating or modifying content.</summary>
public class TestCacheSource : ITestCacheSource
{
    private readonly Func<SqlSugarClient> _getDb;
    private readonly string _outputRoot;
    private readonly bool _ownsClients;
    public TestCacheSource() : this(Path.Combine(AppContext.BaseDirectory, "arkplot.db"),
        Path.Combine(AppContext.BaseDirectory, "output")) { }
    internal TestCacheSource(string databasePath, string outputRoot)
    {
        _outputRoot = outputRoot;
        _ownsClients = true;
        // Each read owns its connection: background tab loading must not share the UI's SqlSugarClient.
        // An empty cache is valid, and opening the test panel must not create or migrate a database.
        _getDb = () =>
        {
            var exists = File.Exists(databasePath);
            return new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = exists ? databasePath : ":memory:",
                    Mode = exists ? SqliteOpenMode.ReadOnly : SqliteOpenMode.Memory,
                    Pooling = false
                }.ToString(),
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false
            });
        };
    }
    public TestCacheSource(Func<SqlSugarClient> getDb, string outputRoot)
    { _getDb = getDb; _outputRoot = outputRoot; }

    public List<CachedChoice> GetActivities(bool images)
    {
        var db = _getDb();
        using var ownedDb = _ownsClients ? db : null;
        // Materialize the chapter IDs once. A correlated EXISTS scans the entire unindexed
        // text table again for every Plot in older caches (tens of seconds on real data).
        var activities = HasEntries(db) ? db.Ado.SqlQuery<CachedChoice>("""
            SELECT DISTINCT a.Id, a.Name FROM Acts a JOIN Plot p ON p.ActId = a.Id
            WHERE p.Status = 2 AND p.Id IN (SELECT DISTINCT PlotId FROM FormattedTextEntry)
            ORDER BY a.Id DESC
            """) : [];
        if (images && db.DbMaintenance.IsAnyTable("PicDescriptions", false) &&
            db.Ado.GetInt("SELECT EXISTS(SELECT 1 FROM PicDescriptions WHERE ImageUrl <> '')") > 0)
            activities.Insert(0, new CachedChoice { Id = -1, Name = "历史图片描述缓存" });
        return activities;
    }

    public List<CachedChoice> GetChapters(long activityId)
    {
        if (activityId == -1) return [new() { Id = -1, Name = "全部已描述图片" }];
        var db = _getDb();
        using var ownedDb = _ownsClients ? db : null;
        if (!HasEntries(db)) return [];
        return db.Ado.SqlQuery<CachedChoice>("""
            SELECT p.Id, p.Title AS Name FROM Plot p LEFT JOIN StoryChapters c ON p.StoryChapterId = c.Id
            WHERE p.ActId = @act AND p.Status = 2
            AND p.Id IN (SELECT DISTINCT PlotId FROM FormattedTextEntry)
            ORDER BY c.StorySort, p.Id
            """, new SugarParameter("@act", activityId));
    }

    public string GetText(long plotId)
    {
        var db = _getDb();
        using var ownedDb = _ownsClients ? db : null;
        var rows = db.Ado.SqlQuery<CachedTextRow>("""
            SELECT CharacterName, Dialog, MdText FROM FormattedTextEntry
            WHERE PlotId = @plot ORDER BY "Index", Id
            """, new SugarParameter("@plot", plotId));
        return MarkdownBuilder.PreprocessMdContent(string.Join("\n", rows.Select(r => !string.IsNullOrWhiteSpace(r.Dialog)
            ? (string.IsNullOrWhiteSpace(r.CharacterName) ? r.Dialog : $"{r.CharacterName}：{r.Dialog}")
            : r.MdText).Where(t => !string.IsNullOrWhiteSpace(t))));
    }

    public List<CachedImage> GetImages(long plotId)
    {
        var db = _getDb();
        using var ownedDb = _ownsClients ? db : null;
        var descriptions = db.DbMaintenance.IsAnyTable("PicDescriptions", false)
            ? db.Ado.SqlQuery<DescriptionRow>("SELECT ImageUrl, PicDesc FROM PicDescriptions WHERE ImageUrl <> ''") : [];
        var descriptionMap = descriptions.GroupBy(d => d.ImageUrl).ToDictionary(g => g.Key, g => g.First().PicDesc);
        if (plotId == -1)
            return descriptions.DistinctBy(d => d.ImageUrl).Where(d => IsImageUrl(d.ImageUrl))
                .Select(d => new CachedImage(d.ImageUrl, FileName(d.ImageUrl), d.PicDesc)).ToList();
        var rows = db.Ado.SqlQuery<ImageRow>("""
            SELECT f.Portraits, f.Bg, f.CharacterName, a.Name AS ActivityName FROM FormattedTextEntry f
            JOIN Plot p ON f.PlotId = p.Id JOIN Acts a ON p.ActId = a.Id
            WHERE f.PlotId = @plot ORDER BY f."Index", f.Id
            """, new SugarParameter("@plot", plotId));
        var images = new List<CachedImage>();
        foreach (var row in rows)
        {
            foreach (var url in ParsePortraits(row.Portraits))
                AddImage(url, string.IsNullOrWhiteSpace(row.CharacterName) ? "立绘" : row.CharacterName, row.ActivityName);
            AddImage(row.Bg, "场景", row.ActivityName);
        }
        return images.DistinctBy(i => i.Url).ToList();

        void AddImage(string url, string label, string activity)
        {
            if (!IsImageUrl(url)) return;
            images.Add(new CachedImage(url, $"{label} · {FileName(url)}",
                descriptionMap.GetValueOrDefault(url, ""), FindLocalImage(activity, url)));
        }
    }

    public List<CachedCharacter> GetCharacters()
    {
        var db = _getDb();
        using var ownedDb = _ownsClients ? db : null;
        if (!HasEntries(db)) return [];
        var rows = db.Ado.SqlQuery<CharacterRow>("""
            SELECT COALESCE(NULLIF(f.CharacterCode, ''), f.CharacterName) AS CharacterKey,
                   f.CharacterName, f.Portraits, f.PortraitFocus, a.Name AS ActivityName, p.Title AS ChapterName
            FROM FormattedTextEntry f JOIN Plot p ON f.PlotId = p.Id JOIN Acts a ON p.ActId = a.Id
            WHERE p.Status = 2 AND f.CharacterName <> '' AND f.Portraits <> '[]' AND f.PortraitFocus >= 0
            GROUP BY COALESCE(NULLIF(f.CharacterCode, ''), f.CharacterName)
            """);
        return rows.Select(r =>
        {
            var portraits = ParsePortraits(r.Portraits);
            if (r.PortraitFocus >= portraits.Length) return null;
            var url = portraits[r.PortraitFocus];
            var local = FindLocalImage(r.ActivityName, url);
            return new CachedCharacter(r.CharacterKey, r.CharacterName,
                string.IsNullOrEmpty(local) ? url : local, $"{r.ActivityName} / {r.ChapterName}");
        }).OfType<CachedCharacter>().ToList();
    }

    private string FindLocalImage(string activity, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "";
        var root = Path.GetFullPath(_outputRoot) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, activity, uri.Host, Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/')));
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate) ? candidate : "";
    }

    internal static string[] ParsePortraits(string json)
    {
        try { return (JsonSerializer.Deserialize<string[]>(json) ?? []).Where(IsImageUrl).ToArray(); }
        catch (JsonException) { return []; }
    }
    private static bool IsImageUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
    private static string FileName(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath)) : url;
    private static bool HasEntries(SqlSugarClient db) => db.DbMaintenance.IsAnyTable("FormattedTextEntry", false);

    public class CachedTextRow { public string CharacterName { get; set; } = ""; public string Dialog { get; set; } = ""; public string MdText { get; set; } = ""; }
    public class DescriptionRow { public string ImageUrl { get; set; } = ""; public string PicDesc { get; set; } = ""; }
    public class ImageRow { public string Portraits { get; set; } = "[]"; public string Bg { get; set; } = ""; public string CharacterName { get; set; } = ""; public string ActivityName { get; set; } = ""; }
    public class CharacterRow : ImageRow { public string CharacterKey { get; set; } = ""; public int PortraitFocus { get; set; } public string ChapterName { get; set; } = ""; }
}
