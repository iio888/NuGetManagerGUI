using CommunityToolkit.Mvvm.ComponentModel;

namespace NugetManagerGUI.ViewModels;

public partial class VersionItem : ObservableObject
{
    public string Version { get; }
    public string Time { get; }

    [ObservableProperty]
    private bool isSelected;

    public VersionItem(string version, string time = "")
    {
        Version = version;
        Time = time;
    }
}
