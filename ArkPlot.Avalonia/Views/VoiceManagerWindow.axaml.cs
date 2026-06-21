using System;
using ArkPlot.Avalonia.ViewModels;
using Avalonia.Controls;

namespace ArkPlot.Avalonia;

public partial class VoiceManagerWindow : Window
{
    public VoiceManagerWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is VoiceManagerViewModel vm)
        {
            vm.RequestClose += () => Close();
        }
    }
}
