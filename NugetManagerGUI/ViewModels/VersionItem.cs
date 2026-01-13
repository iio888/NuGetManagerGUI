using CommunityToolkit.Mvvm.ComponentModel;

namespace NugetManagerGUI.ViewModels;

public partial class VersionItem : ObservableObject
{
    public string Version { get; }

    [ObservableProperty]
    private bool isSelected;

    public VersionItem(string version)
    {
        Version = version;
    }
}
