using NuGet.Versioning;

public class PackageSearchResult
{
    public string PackageId { get; set; }
    public NuGetVersion LatestVersion { get; set; }
    public string Description { get; set; }
    public string Authors { get; set; }
    public long? TotalDownloads { get; set; }
    public List<NuGetVersion> Versions { get; set; }
}
