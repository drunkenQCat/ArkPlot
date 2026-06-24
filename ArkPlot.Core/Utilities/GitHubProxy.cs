namespace ArkPlot.Core.Utilities;

/// <summary>
/// GitHub 代理前缀辅助类。
/// 所有 GitHub URL 构造处调用 GetUrl() 即可自动套用用户配置的镜像前缀。
/// 前缀在应用启动时从 AppSettings.GitHubProxyPrefix 初始化。
/// </summary>
public static class GitHubProxy
{
    private static string _prefix = "";

    /// <summary>代理前缀，如 "https://gh-proxy.com/"。空字符串 = 直连。</summary>
    public static string Prefix
    {
        get => _prefix;
        set => _prefix = string.IsNullOrWhiteSpace(value) ? "" : EnsureTrailingSlash(value.Trim());
    }

    /// <summary>静态构造时从环境变量读取一次（兜底，AppSettings 加载后会覆盖）。</summary>
    static GitHubProxy()
    {
        var env = Environment.GetEnvironmentVariable("GITHUB_PROXY_PREFIX");
        if (!string.IsNullOrWhiteSpace(env))
            _prefix = EnsureTrailingSlash(env.Trim());
    }

    /// <summary>
    /// 对原始 GitHub URL 套用代理前缀。
    /// 空前缀 = 返回原 URL。
    /// </summary>
    public static string GetUrl(string originalUrl)
    {
        if (string.IsNullOrEmpty(_prefix) || string.IsNullOrEmpty(originalUrl))
            return originalUrl;
        return _prefix + originalUrl;
    }

    /// <summary>
    /// 当直连 GitHub 失败时触发（UI 可订阅此事件弹出引导对话框）。
    /// 事件参数为出错的 URL。
    /// </summary>
    public static event Action<string>? ConnectionFailed;

    /// <summary>
    /// 检查 HTTP 响应是否代表 GitHub 连接失败。
    /// 如果是且未配置代理，则触发 <see cref="ConnectionFailed"/> 事件。
    /// </summary>
    /// <param name="url">请求的 GitHub URL</param>
    /// <param name="exception">捕获的异常（如有）</param>
    /// <param name="statusCode">HTTP 状态码（如有）</param>
    public static void CheckConnectionError(string url, Exception? exception = null, int? statusCode = null)
    {
        // 只在直连模式下触发（已配置代理就不弹了）
        if (!string.IsNullOrEmpty(_prefix))
            return;

        // 只对 GitHub URL 触发
        if (!url.Contains("github.com"))
            return;

        // 网络错误、超时、5xx、403 都算连接失败
        bool isError = exception != null
            || (statusCode >= 500)
            || statusCode == 403
            || statusCode == 0;

        if (isError)
        {
            NotifyConnectionFailed(url);
        }
    }

    /// <summary>
    /// 供测试和内部代码触发连接失败事件。
    /// </summary>
    public static void NotifyConnectionFailed(string url)
    {
        ConnectionFailed?.Invoke(url);
    }

    /// <summary>
    /// 根据语言代码返回数据仓库名称。
    /// zh_CN 使用 Kengxxiao/ArknightsGameData，其余语言使用 ArknightsAssets/ArknightsGamedata。
    /// </summary>
    public static string GetRepoName(string lang)
    {
        return lang == "zh_CN"
            ? "Kengxxiao/ArknightsGameData"
            : "ArknightsAssets/ArknightsGamedata";
    }

    /// <summary>
    /// 将 UI 层语言代码（如 zh_CN, en_US）映射为数据仓库中的目录名。
    /// Kengxxiao 使用 zh_CN / en_US，ArknightsAssets 使用 cn / en / jp / kr / tw。
    /// </summary>
    public static string MapLangToDir(string lang)
    {
        return lang switch
        {
            "zh_CN" => "zh_CN",
            "en_US" => "en",
            "ja_JP" => "jp",
            "ko_KR" => "kr",
            "zh_TW" => "tw",
            _ => lang
        };
    }

    /// <summary>
    /// 获取 story_review_table.json 的下载 URL。
    /// </summary>
    public static string GetStoryTableUrl(string lang)
    {
        return GetUrl($"https://raw.githubusercontent.com/{GetRepoName(lang)}/master/{MapLangToDir(lang)}/gamedata/excel/story_review_table.json");
    }

    /// <summary>
    /// 获取剧情文本目录的 base URL（后续拼接章节路径）。
    /// </summary>
    public static string GetStoryBaseUrl(string lang)
    {
        return GetUrl($"https://raw.githubusercontent.com/{GetRepoName(lang)}/master/{MapLangToDir(lang)}/gamedata/story/");
    }

    /// <summary>
    /// 获取 GitHub API commit SHA 查询 URL。
    /// </summary>
    public static string GetCommitApiUrl(string repo)
    {
        return GetUrl($"https://api.github.com/repos/{repo}/commits?per_page=1");
    }

    private static string EnsureTrailingSlash(string url)
    {
        return url.EndsWith('/') ? url : url + '/';
    }
}
