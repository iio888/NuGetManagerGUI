using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NugetManagerGUI.Model;

public partial class PackageItem : ObservableObject
{
    public string Id { get; }

    public string Description { get; set; } = string.Empty;

    public ObservableCollection<VersionItem> Versions { get; set;  } = new();

    public PackageItem(string id)
    {
        Id = id;
    }
}

