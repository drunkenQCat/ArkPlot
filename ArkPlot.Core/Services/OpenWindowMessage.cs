namespace ArkPlot.Core.Services;

public class OpenWindowMessage
{
    public string WindowName { get; }
    public string JsonPath { get; }
    public int? SelectedTabIndex { get; }

    public OpenWindowMessage(string windowName, string jsonPath, int? selectedTabIndex = null)
    {
        WindowName = windowName;
        JsonPath = jsonPath;
        SelectedTabIndex = selectedTabIndex;
    }
}

