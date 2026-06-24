using System.Net.Http;
using ArkPlot.Core.Services;

namespace ArkPlot.Core.Utilities;

public static class NetworkUtility
{
    public static async Task<string> GetAsync(string url)
    {
        try
        {
            // 发送一个request请求
            using var client = new HttpClient();
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                GitHubProxy.CheckConnectionError(url, statusCode: (int)response.StatusCode);
                if (response.ReasonPhrase != null)
                    NotificationBlock.Instance.OnNetErrorHappen(new NetworkErrorEventArgs(response));
                return "";
            }

            var fileContent = await response.Content.ReadAsStringAsync();
            return fileContent;
        }
        catch (Exception e)
        {
            GitHubProxy.CheckConnectionError(url, exception: e);
            NotificationBlock.Instance.OnNetErrorHappen(new NetworkErrorEventArgs(e.Message));
            return "";
        }
    }

    // 获取查询的json
    public static async Task<string> GetJsonContent(string plotsJsonRequestUrl)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage();
        request.RequestUri = new Uri(plotsJsonRequestUrl);
        request.Method = HttpMethod.Get;
        request.Headers.Add("Accept", "application/json");
        try
        {
            var response = await client.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            return content;
        }
        catch (HttpRequestException e)
        {
            GitHubProxy.CheckConnectionError(plotsJsonRequestUrl, exception: e);
            NotificationBlock.Instance.OnNetErrorHappen(new NetworkErrorEventArgs(e.Message));
            return "";
        }
    }
}
