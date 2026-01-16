using NuGet.Versioning;

public class PackageVersionInfo
{
    public NuGetVersion Version { get; set; }
    public bool IsPrerelease { get; set; }
    public bool IsListed { get; set; }
    public DateTime Published { get; set; }
    public string Description { get; set; }
    public string Authors { get; set; }
    public long? DownloadCount { get; set; }
}
