using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArkPlot.Avalonia.ViewModels;
using ArkPlot.Tts.Models;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MiniMax;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

public class MiniMaxVoiceManagerHeadlessTests
{
    [AvaloniaFact]
    public void MiniMaxVoiceManagerWindow_CanBeCreatedAndShown()
    {
        using var client = CreateMiniMaxClient("sk-placeholder", new RecordingHttpMessageHandler(_ => CreateVoiceResponse()));
        var vm = CreateMiniMaxViewModel(client, [new VoiceEntry("existing-1", "现有音色", "Female", "zh-CN")]);
        var window = new global::ArkPlot.Avalonia.VoiceManagerWindow
        {
            DataContext = vm
        };

        window.Show();

        Assert.True(window.IsVisible);
        Assert.Equal("音色管理 · MiniMax", window.Title);
        window.Close();
    }

    [AvaloniaFact]
    public void MiniMaxVoiceManagerWindow_ContainsExpectedButtonsAndGrid()
    {
        using var client = CreateMiniMaxClient("sk-placeholder", new RecordingHttpMessageHandler(_ => CreateVoiceResponse()));
        var vm = CreateMiniMaxViewModel(client, [new VoiceEntry("existing-1", "现有音色", "Female", "zh-CN")]);
        var window = new global::ArkPlot.Avalonia.VoiceManagerWindow
        {
            DataContext = vm
        };

        window.Show();

        var buttons = window.GetVisualDescendants()
            .OfType<Button>()
            .ToList();
        var dataGrid = window.GetVisualDescendants()
            .OfType<DataGrid>()
            .FirstOrDefault();

        Assert.Contains(buttons, b => (b.Content?.ToString() ?? "") == "获取音色" && b.IsEnabled);
        Assert.Contains(buttons, b => (b.Content?.ToString() ?? "") == "＋ 添加一行");
        Assert.Contains(buttons, b => (b.Content?.ToString() ?? "") == "保存");
        Assert.Contains(buttons, b => (b.Content?.ToString() ?? "") == "取消");

        Assert.NotNull(dataGrid);
        Assert.Equal(6, dataGrid!.Columns.Count);
        Assert.Equal("选中", dataGrid.Columns[0].Header?.ToString());
        Assert.Equal("Voice ID", dataGrid.Columns[1].Header?.ToString());
        Assert.Equal("显示名", dataGrid.Columns[2].Header?.ToString());
        Assert.Equal("性别", dataGrid.Columns[3].Header?.ToString());
        Assert.Equal("Locale", dataGrid.Columns[4].Header?.ToString());

        window.Close();
    }

    [AvaloniaFact]
    public async Task MiniMaxVoiceManagerWindow_GenderList_RemainsStableAfterScrollingBackToTop()
    {
        using var client = CreateMiniMaxClient("sk-placeholder", new RecordingHttpMessageHandler(_ => CreateVoiceResponse()));
        var rows = Enumerable.Range(0, 60)
            .Select(i => new VoiceEntry($"voice-{i:D3}", $"音色 {i:D3}", i % 2 == 0 ? "Female" : "Male", "zh-CN"))
            .ToArray();
        var vm = CreateMiniMaxViewModel(client, rows);
        var window = new global::ArkPlot.Avalonia.VoiceManagerWindow
        {
            DataContext = vm
        };

        window.Show();
        await FlushUiAsync();

        var dataGrid = AssertSingleDataGrid(window);
        dataGrid.ScrollIntoView(vm.Voices[0], dataGrid.Columns[3]);
        await FlushUiAsync();

        var originalSnapshot = await ReadGenderCellSnapshotAsync(window, dataGrid, vm.Voices[0]);
        Assert.Equal(vm.GenderOptions, originalSnapshot.Options);

        foreach (var rowIndex in new[] { 15, 30, 45, 59 })
        {
            dataGrid.ScrollIntoView(vm.Voices[rowIndex], dataGrid.Columns[3]);
            await FlushUiAsync();
            var snapshot = await ReadGenderCellSnapshotAsync(window, dataGrid, vm.Voices[rowIndex]);
            Assert.Equal(vm.GenderOptions, snapshot.Options);
        }

        dataGrid.ScrollIntoView(vm.Voices[0], dataGrid.Columns[3]);
        await FlushUiAsync();

        var afterScrollBackSnapshot = await ReadGenderCellSnapshotAsync(window, dataGrid, vm.Voices[0]);
        Assert.Equal(originalSnapshot.Options, afterScrollBackSnapshot.Options);

        window.Close();
    }

    [AvaloniaFact]
    public async Task MiniMaxVoiceManagerWindow_FetchVoices_KeepsGenderOptionsAndSelectedGenderStable()
    {
        var handler = new RecordingHttpMessageHandler(_ => CreateVoiceResponse(
            new SystemVoiceInfo("mm-f01", "服务端少女", null),
            new SystemVoiceInfo("mm-m01", "服务端青年", null)));
        using var client = CreateMiniMaxClient("sk-test", handler);
        var vm = CreateMiniMaxViewModel(client,
        [
            new VoiceEntry("mm-f01", "旧显示名", "Female", "zh-CN")
        ]);
        var window = new global::ArkPlot.Avalonia.VoiceManagerWindow
        {
            DataContext = vm
        };

        window.Show();
        await FlushUiAsync();

        var dataGrid = AssertSingleDataGrid(window);
        dataGrid.ScrollIntoView(vm.Voices[0], dataGrid.Columns[3]);
        await FlushUiAsync();

        var beforeFetchSnapshot = await ReadGenderCellSnapshotAsync(window, dataGrid, vm.Voices[0]);
        var selectedGenderBeforeFetch = vm.Voices[0].Gender;

        await vm.FetchVoicesCommand.ExecuteAsync(null);
        await FlushUiAsync();

        dataGrid = AssertSingleDataGrid(window);
        dataGrid.ScrollIntoView(vm.Voices[0], dataGrid.Columns[3]);
        await FlushUiAsync();

        var afterFetchSnapshot = await ReadGenderCellSnapshotAsync(window, dataGrid, vm.Voices[0]);

        Assert.Equal(beforeFetchSnapshot.Options, afterFetchSnapshot.Options);
        Assert.Equal(selectedGenderBeforeFetch, vm.Voices[0].Gender);
        Assert.Equal("mm-f01", vm.Voices[0].VoiceId);

        dataGrid.ScrollIntoView(vm.Voices[1], dataGrid.Columns[3]);
        await FlushUiAsync();

        var newRowSnapshot = await ReadGenderCellSnapshotAsync(window, dataGrid, vm.Voices[1]);
        Assert.Equal(beforeFetchSnapshot.Options, newRowSnapshot.Options);
        Assert.Equal("mm-m01", vm.Voices[1].VoiceId);

        window.Close();
    }

    [Fact]
    public void VoiceManagerViewModel_SaveOnlySelectedRowsWithNonEmptyVoiceId()
    {
        var vm = CreateMiniMaxViewModel(client: null,
        [
            new VoiceEntry("mm-f01", "少女", "Female", "zh-CN"),
            new VoiceEntry("mm-m01", "青年", "Male", "zh-CN")
        ]);
        var closeRequested = false;
        vm.RequestClose += () => closeRequested = true;

        vm.Voices[0].IsSelected = true;
        vm.Voices[1].IsSelected = false;
        vm.Voices.Add(new VoiceRowViewModel
        {
            VoiceId = "",
            DisplayName = "空 ID",
            Gender = "Unknown",
            Locale = "zh-CN",
            IsSelected = false
        });

        vm.SaveCommand.Execute(null);

        Assert.True(vm.Confirmed);
        Assert.True(closeRequested);
        Assert.NotNull(vm.Result);
        Assert.Single(vm.Result!);
        Assert.Equal("mm-f01", vm.Result[0].VoiceId);
    }

    [Fact]
    public void VoiceManagerViewModel_Save_FailsWhenSelectedRowHasEmptyVoiceId()
    {
        var vm = CreateMiniMaxViewModel(client: null, []);
        var closeRequested = false;
        vm.RequestClose += () => closeRequested = true;

        vm.Voices.Add(new VoiceRowViewModel
        {
            VoiceId = "",
            DisplayName = "未填写 ID",
            Gender = "Female",
            Locale = "zh-CN",
            IsSelected = true
        });

        vm.SaveCommand.Execute(null);

        Assert.False(vm.Confirmed);
        Assert.False(closeRequested);
        Assert.Null(vm.Result);
        Assert.Equal("❌ 保存失败：选中的音色必须填写 Voice ID", vm.FetchStatus);
    }

    [Fact]
    public void VoiceManagerViewModel_Save_FailsWhenVoiceIdsAreDuplicated()
    {
        var vm = CreateMiniMaxViewModel(client: null, []);
        var closeRequested = false;
        vm.RequestClose += () => closeRequested = true;

        vm.Voices.Add(new VoiceRowViewModel
        {
            VoiceId = "dup-1",
            DisplayName = "音色 A",
            Gender = "Female",
            Locale = "zh-CN",
            IsSelected = true
        });
        vm.Voices.Add(new VoiceRowViewModel
        {
            VoiceId = "dup-1",
            DisplayName = "音色 B",
            Gender = "Male",
            Locale = "zh-CN",
            IsSelected = true
        });

        vm.SaveCommand.Execute(null);

        Assert.False(vm.Confirmed);
        Assert.False(closeRequested);
        Assert.Null(vm.Result);
        Assert.Equal("❌ 保存失败：存在重复的 Voice ID：dup-1", vm.FetchStatus);
    }

    [Fact]
    public void VoiceManagerViewModel_Save_FailsWhenDisplayNameIsEmpty()
    {
        var vm = CreateMiniMaxViewModel(client: null, []);
        var closeRequested = false;
        vm.RequestClose += () => closeRequested = true;

        vm.Voices.Add(new VoiceRowViewModel
        {
            VoiceId = "mm-empty-name",
            DisplayName = "",
            Gender = "Female",
            Locale = "zh-CN",
            IsSelected = true
        });

        vm.SaveCommand.Execute(null);

        Assert.False(vm.Confirmed);
        Assert.False(closeRequested);
        Assert.Null(vm.Result);
        Assert.Equal("❌ 保存失败：选中的音色必须填写显示名", vm.FetchStatus);
    }

    [Fact]
    public async Task VoiceManagerViewModel_FetchVoices_UsesMiniMaxEndpointAndPreservesSelectedExistingRows()
    {
        var handler = new RecordingHttpMessageHandler(_ => CreateVoiceResponse(
            new SystemVoiceInfo("mm-f01", "服务端少女", null),
            new SystemVoiceInfo("mm-m01", "服务端青年", null)));
        using var client = CreateMiniMaxClient("sk-test", handler);
        var vm = CreateMiniMaxViewModel(client,
        [
            new VoiceEntry("mm-f01", "旧显示名", "Female", "zh-CN")
        ]);

        await vm.FetchVoicesCommand.ExecuteAsync(null);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v1/get_voice", request.RequestUri.AbsolutePath);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal("sk-test", request.AuthorizationParameter);
        Assert.Contains("\"voice_type\":\"system\"", request.Body);

        Assert.Equal("✅ 获取成功：2 个音色", vm.FetchStatus);
        Assert.False(vm.IsFetching);
        Assert.Equal(2, vm.Voices.Count);
        Assert.Equal("服务端少女", vm.Voices[0].DisplayName);
        Assert.True(vm.Voices[0].IsSelected);
        Assert.Equal("Female", vm.Voices[0].Gender);
        Assert.Equal("服务端青年", vm.Voices[1].DisplayName);
        Assert.False(vm.Voices[1].IsSelected);
        Assert.Equal("Unknown", vm.Voices[1].Gender);
    }

    [Fact]
    public async Task VoiceManagerViewModel_FetchVoices_PreservesLocallyAddedRows()
    {
        var handler = new RecordingHttpMessageHandler(_ => CreateVoiceResponse(
            new SystemVoiceInfo("mm-f01", "服务端少女", null)));
        using var client = CreateMiniMaxClient("sk-test", handler);
        var vm = CreateMiniMaxViewModel(client,
        [
            new VoiceEntry("mm-local-only", "仅本地存在", "Female", "zh-CN"),
            new VoiceEntry("mm-f01", "旧显示名", "Female", "zh-CN")
        ]);

        await vm.FetchVoicesCommand.ExecuteAsync(null);

        Assert.Contains(vm.Voices, row => row.VoiceId == "mm-local-only");
        Assert.Contains(vm.Voices, row => row.VoiceId == "mm-f01");
    }

    [Fact]
    public async Task VoiceManagerViewModel_FetchVoices_PreservesUncheckedRowsNotReturnedByServer()
    {
        var handler = new RecordingHttpMessageHandler(_ => CreateVoiceResponse(
            new SystemVoiceInfo("mm-f01", "服务端少女", null)));
        using var client = CreateMiniMaxClient("sk-test", handler);
        var vm = CreateMiniMaxViewModel(client,
        [
            new VoiceEntry("mm-f01", "旧显示名", "Female", "zh-CN"),
            new VoiceEntry("mm-unchecked", "未选中但想保留", "Male", "zh-CN")
        ]);
        vm.Voices[1].IsSelected = false;

        await vm.FetchVoicesCommand.ExecuteAsync(null);

        Assert.Contains(vm.Voices, row => row.VoiceId == "mm-unchecked" && !row.IsSelected);
        Assert.Equal(2, vm.Voices.Count);
    }

    [Fact]
    public async Task MiniMaxLiveFetch_ReturnsVoices_WhenApiKeyPresent()
    {
        var apiKey = Environment.GetEnvironmentVariable("MINIMAX_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            return;

        var baseUrl = Environment.GetEnvironmentVariable("MINIMAX_BASE_URL") ?? "https://api.minimax.io/";
        using var client = new MiniMaxClient(
            apiKey,
            baseUri: new Uri(baseUrl));
        var vm = CreateMiniMaxViewModel(client, []);

        await vm.FetchVoicesCommand.ExecuteAsync(null);

        Assert.False(vm.IsFetching);
        Assert.NotEmpty(vm.Voices);
        Assert.StartsWith("✅ 获取成功：", vm.FetchStatus);
        Assert.All(vm.Voices, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.VoiceId));
            Assert.False(string.IsNullOrWhiteSpace(row.DisplayName));
        });
    }

    private static VoiceManagerViewModel CreateMiniMaxViewModel(MiniMaxClient? client, VoiceEntry[] voices) =>
        new("音色管理 · MiniMax", voices, miniMaxClient: client);

    private static MiniMaxClient CreateMiniMaxClient(string apiKey, HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://unit.test/")
        };

        return new MiniMaxClient(
            apiKey,
            httpClient: httpClient,
            baseUri: httpClient.BaseAddress,
            disposeHttpClient: true);
    }

    private static HttpResponseMessage CreateVoiceResponse(params SystemVoiceInfo[] voices)
    {
        var json = JsonSerializer.Serialize(new GetVoicesResponse
        {
            SystemVoice = voices.ToList()
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static async Task FlushUiAsync()
    {
        await Task.Delay(30);
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(30);
        Dispatcher.UIThread.RunJobs();
    }

    private static DataGrid AssertSingleDataGrid(Window window) =>
        Assert.Single(window.GetVisualDescendants().OfType<DataGrid>());

    private static async Task<GenderCellSnapshot> ReadGenderCellSnapshotAsync(Window window, DataGrid dataGrid, VoiceRowViewModel row)
    {
        var host = Assert.IsType<DockPanel>(window.Content);
        var genderColumn = Assert.IsType<DataGridTemplateColumn>(dataGrid.Columns[3]);
        var presenter = new ContentPresenter
        {
            DataContext = row,
            Content = row,
            ContentTemplate = Assert.IsAssignableFrom<IDataTemplate>(genderColumn.CellTemplate)
        };
        host.Children.Add(presenter);
        await FlushUiAsync();

        var comboBox = Assert.Single(presenter.GetVisualDescendants().OfType<ComboBox>());
        var items = Assert.IsAssignableFrom<IEnumerable<object>>(comboBox.ItemsSource);
        var snapshot = new GenderCellSnapshot(
            items.Select(item => item?.ToString() ?? "").ToArray(),
            row.Gender);

        host.Children.Remove(presenter);
        await FlushUiAsync();
        return snapshot;
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri ?? new Uri("https://invalid.local/"),
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                body));

            return responder(request);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri RequestUri,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string Body);

    private sealed record GenderCellSnapshot(IReadOnlyList<string> Options, string? SelectedGender);
}
