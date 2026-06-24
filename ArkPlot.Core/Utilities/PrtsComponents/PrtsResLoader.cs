using System.IO;
using System.Net.Http;
using System.Threading;
using ArkPlot.Core.Services;
using PreloadSet = System.Collections.Generic.HashSet<System.Collections.Generic.KeyValuePair<string, string>>;

namespace ArkPlot.Core.Utilities.PrtsComponents;

public class PrtsResLoader
{
    // 下载 assets 里面�?所�?assets。要求他们放�?output �?件夹�?
    // 保存的时候要按照链接,按文件夹保存。比如说一个链接是 https://example.com/1.png,当前活动名是“阴云火花”，那么就要保存�??output/阴云火花/example.com/1.png
    public static async Task DownloadAssets(string storyName, PreloadSet assets, CancellationToken ct = default)
    {
        var httpClient = new HttpClient();

        foreach (var asset in assets)
        {
            ct.ThrowIfCancellationRequested();
            var url = asset.Value;
            var fullPath = GetLocalPathFromUrl(storyName, url);
            var directoryPath = Path.GetDirectoryName(fullPath);
            EnsureDirectoryExists(directoryPath!);
            if (!File.Exists(fullPath)) await DownloadFileAsync(httpClient, url, fullPath, ct);
        }
    }

    private static string GetLocalPathFromUrl(string storyName, string url)
    {
        var uri = new Uri(url);
        var localPath = Path.Join(uri.Host, uri.AbsolutePath.TrimStart('/'));
        return Path.Join("output", storyName, localPath);
    }


    public static string GetRelativePathFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        var uri = new Uri(url);
        var localPath = Path.Combine(uri.Host, uri.AbsolutePath.TrimStart('/'));
        return localPath;
    }

    private static void EnsureDirectoryExists(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) _ = Directory.CreateDirectory(directoryPath);
    }

    private static async Task DownloadFileAsync(HttpClient httpClient, string url, string fullPath, CancellationToken ct)
    {
        var notice = NotificationBlock.Instance;
        try
        {
            var content = await httpClient.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(fullPath, content, ct);
            notice.RaiseCommonEvent($"Downloaded: {url} to {fullPath}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException httpEx)
        {
            // �?�?网络请求相关�?异�?
            notice.OnNetErrorHappen(new NetworkErrorEventArgs(
                $"An error occurred while downloading {url}. Error: {httpEx.Message}"
            ));
        }
        catch (IOException ioEx)
        {
            // �?�?�?件写�?�相关�?异�?
            notice.OnNetErrorHappen(new NetworkErrorEventArgs(
                $"An error occurred while writing to {fullPath}. Error: {ioEx.Message}"
            ));
        }
        catch (Exception ex)
        {
            // �?�?其他可能发生�?异�?
            notice.OnNetErrorHappen(new NetworkErrorEventArgs(
                $"An unexpected error occurred. Error: {ex.Message}"
            ));
        }
    }
}
