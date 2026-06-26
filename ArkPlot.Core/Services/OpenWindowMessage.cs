namespace ArkPlot.Core.Services;

public class OpenWindowMessage
{
    public string WindowName { get; }
    public string? JsonPath { get; }
    public int? SelectedTabIndex { get; }
    public string? ActName { get; }

    public OpenWindowMessage(
        string windowName,
        string? jsonPath = null,
        int? selectedTabIndex = null,
        string? currentActName = null
    )
    {
        WindowName = windowName;
        JsonPath = jsonPath;
        SelectedTabIndex = selectedTabIndex;
        ActName = currentActName;
    }
}
