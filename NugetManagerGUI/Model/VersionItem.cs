using CommunityToolkit.Mvvm.ComponentModel;
using NuGet.Versioning;

public partial class VersionItem : ObservableObject
{
    [ObservableProperty]
    private bool isSelected;

    public NuGetVersion Version { get; set; }
    public bool IsPrerelease { get; set; }
    public DateTime Published { get; set; }
    public string? Description { get; set; }
    public string? Authors { get; set; }
    public long? DownloadCount { get; set; }
    public VersionItem()
    {
        
    }
    public VersionItem(string version)
    {
        Version = NuGetVersion.Parse(version);
    }

    public override string ToString()
    {
        return Version.ToString();
    }
}
