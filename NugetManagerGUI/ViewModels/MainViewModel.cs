using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Net.Http;
using NuGet.Protocol;
using System.Text.Json;
using Microsoft.Win32;
using NuGet.Protocol.Core.Types;
using NuGet.Common;

namespace NugetManagerGUI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<string> Projects { get; } = new();

    public ObservableCollection<PackageItem> Packages { get; } = new();

    public ObservableCollection<string> SelectedProjects { get; } = new();

    // map of display name -> project relative path (as found in solution)
    private readonly Dictionary<string, string> _projectMap = new();

    [ObservableProperty]
    private string? searchQuery;

    [ObservableProperty]
    private PackageItem? selectedPackage;

    [ObservableProperty]
    private string? selectedProject;

    [ObservableProperty]
    private string? customVersion;

    [ObservableProperty]
    private string? outputDirectory;

    [ObservableProperty]
    private string? packLog;

    [ObservableProperty]
    private string? currentSource;

    private string? SolutionDirectory;

    // persisted settings
    private Settings _settings = new();

    public MainViewModel()
    {
        // sample data with multiple versions
        _projectMap.Clear();
        Projects.Clear();

        // sample entries: display name -> relative path
        _projectMap["ProjectA"] = "ProjectA\\ProjectA.csproj";
        _projectMap["ProjectB"] = "ProjectB\\ProjectB.csproj";
        _projectMap["ProjectC"] = "ProjectB\\ProjectB.csproj";
        _projectMap["ProjectD"] = "ProjectB\\ProjectB.csproj";
        _projectMap["ProjectE"] = "ProjectB\\ProjectB.csproj";

        Projects.Add("ProjectA");
        Projects.Add("ProjectB");
        Projects.Add("ProjectC");
        Projects.Add("ProjectD");
        Projects.Add("ProjectE");

        var p1 = new PackageItem("Example.Package");
        p1.Versions.Add(new VersionItem("1.0.0"));
        p1.Versions.Add(new VersionItem("1.1.0"));
        p1.Versions.Add(new VersionItem("2.0.0"));

        var p2 = new PackageItem("Another.Package");
        p2.Versions.Add(new VersionItem("2.3.1"));

        Packages.Add(p1);
        Packages.Add(p2);
    }
    private NuGetPackageService? _service;

    public void LoadSolution(string solutionPath)
    {
        if (string.IsNullOrEmpty(solutionPath) || !File.Exists(solutionPath))
            return;

        SolutionDirectory = Path.GetDirectoryName(solutionPath);

        Projects.Clear();
        SelectedProjects.Clear();
        _projectMap.Clear();

        var lines = File.ReadAllLines(solutionPath);
        foreach (var line in lines)
        {
            // simple parse: lines like: Project("{...}") = "ProjectName", "path\\to\\project.csproj", "{GUID}"
            if (line.StartsWith("Project(") && line.Contains(", \""))
            {
                var parts = line.Split('=');
                if (parts.Length >= 2)
                {
                    var right = parts[1].Trim();
                    // format: "ProjectName", "path\\to\\project.csproj", "{GUID}"
                    var items = right.Split(',');
                    if (items.Length >= 2)
                    {
                        var projectPath = items[1].Trim().Trim('"');

                        // Only include actual project files (filter out solution folders and other non-csproj entries)
                        if (!projectPath.EndsWith(".csproj", System.StringComparison.OrdinalIgnoreCase))
                            continue;

                        // display only the project file name without extension
                        var displayName = Path.GetFileNameWithoutExtension(projectPath);

                        // ensure unique display name; if duplicate, append a suffix to keep names unique
                        var finalName = displayName;
                        int dup = 1;
                        while (_projectMap.ContainsKey(finalName))
                        {
                            dup++;
                            finalName = displayName + " (" + dup + ")";
                        }

                        _projectMap[finalName] = projectPath;
                        Projects.Add(finalName);
                    }
                }
            }
        }
    }

    public string? GetRelativeProjectPath(string displayName)
    {
        if (string.IsNullOrEmpty(displayName))
            return null;

        if (_projectMap.TryGetValue(displayName, out var path))
            return path;

        // fallback: if the displayName looks like a path, return it
        return displayName;
    }

    [RelayCommand]
    private async Task Search()
    {
        // Search packages from configured NuGet V3 feed using the SearchQueryService
        // Requires that Settings.FeedUrl is set to the feed root (or a URL that serves /v3/index.json)
        try
        {
            LoadSettings(); // ensure settings loaded

            var feedUrl = _settings.FeedUrl;
            if (string.IsNullOrWhiteSpace(feedUrl))
            {
                MessageBox.Show("请先在 Settings 中配置 NuGet 源。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var query = SearchQuery ?? string.Empty;

            Packages.Clear();

            using var http = new HttpClient();

            var indexUrl = feedUrl.EndsWith("/v3/index.json") ? feedUrl : feedUrl.TrimEnd('/') + "/v3/index.json";
            var idxResp = await http.GetStringAsync(indexUrl);
            using var idxDoc = JsonDocument.Parse(idxResp);

            string? searchService = null;
            if (idxDoc.RootElement.TryGetProperty("resources", out var resources))
            {
                foreach (var r in resources.EnumerateArray())
                {
                    if (r.TryGetProperty("@type", out var t))
                    {
                        var tstr = t.GetString() ?? string.Empty;
                        if (tstr.Contains("SearchQueryService"))
                        {
                            if (r.TryGetProperty("@id", out var idp))
                            {
                                searchService = idp.GetString();
                                break;
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(searchService))
            {
                MessageBox.Show("在 NuGet 源中找不到搜索服务 (SearchQueryService)。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var q = Uri.EscapeDataString(query);
            var url = searchService + (searchService.Contains("?") ? "&" : "?") + $"q={q}&skip=0&take=50&prerelease=true";
            var resp = await http.GetStringAsync(url);

            using var doc = JsonDocument.Parse(resp);
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var item in data.EnumerateArray())
                {
                    var id = item.GetProperty("id").GetString() ?? string.Empty;
                    var description = item.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty;

                    var pkg = new PackageItem(id) { Description = description };

                    if (item.TryGetProperty("versions", out var versions))
                    {
                        foreach (var v in versions.EnumerateArray())
                        {
                            string ver = string.Empty;
                            if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("version", out var vp))
                                ver = vp.GetString() ?? string.Empty;
                            else if (v.ValueKind == JsonValueKind.String)
                                ver = v.GetString() ?? string.Empty;

                            if (!string.IsNullOrEmpty(ver))
                                pkg.Versions.Add(new VersionItem(ver));
                        }
                    }

                    // Add to UI thread
                    if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                        Application.Current.Dispatcher.Invoke(() => Packages.Add(pkg));
                    else
                        Packages.Add(pkg);
                }
            }
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"搜索失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task LoadAllPackages()
    {
        // Load all packages from configured NuGet V3 feed
        try
        {
            LoadSettings(); // ensure settings loaded
            var feedUrl = _settings.FeedUrl;
            if (string.IsNullOrWhiteSpace(feedUrl))
            {
                return;
            }

            _service = new(feedUrl);
            var ps = await _service.GetTopPackagesAsync(includePrerelease: true);
            Console.WriteLine(ps);

            Packages.Clear();

            using var http = new HttpClient();

            var indexUrl = feedUrl.EndsWith("/v3/index.json") ? feedUrl : feedUrl.TrimEnd('/') + "/v3/index.json";
            var idxResp = await http.GetStringAsync(indexUrl);
            using var idxDoc = JsonDocument.Parse(idxResp);

            string? searchService = null;
            if (idxDoc.RootElement.TryGetProperty("resources", out var resources))
            {
                foreach (var r in resources.EnumerateArray())
                {
                    if (r.TryGetProperty("@type", out var t))
                    {
                        var tstr = t.GetString() ?? string.Empty;
                        if (tstr.Contains("SearchQueryService"))
                        {
                            if (r.TryGetProperty("@id", out var idp))
                            {
                                searchService = idp.GetString();
                                break;
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(searchService))
            {
                return;
            }

            // Load all packages without search query (empty query loads all)
            var url = searchService + (searchService.Contains("?") ? "&" : "?") + "q=&skip=0&take=100&prerelease=true";
            var resp = await http.GetStringAsync(url);

            using var doc = JsonDocument.Parse(resp);
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var item in data.EnumerateArray())
                {
                    var id = item.GetProperty("id").GetString() ?? string.Empty;
                    var description = item.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty;

                    var pkg = new PackageItem(id) { Description = description };
                    var vs = await _service.GetAllVersionsAsync(id);
                    pkg.Versions = new(vs.Select(v => new VersionItem(v.ToString())));

                    //if (item.TryGetProperty("versions", out var versions))
                    //{
                    //    foreach (var v in versions.EnumerateArray())
                    //    {
                    //        string ver = string.Empty;
                    //        if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("version", out var vp))
                    //            ver = vp.GetString() ?? string.Empty;
                    //        else if (v.ValueKind == JsonValueKind.String)
                    //            ver = v.GetString() ?? string.Empty;

                    //        if (!string.IsNullOrEmpty(ver))
                    //            pkg.Versions.Add(new VersionItem(ver));
                    //    }
                    //pkg.Versions = new(pkg.Versions.Reverse());
                    //}

                    // Add to UI thread
                    if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                        Application.Current.Dispatcher.Invoke(() => Packages.Add(pkg));
                    else
                        Packages.Add(pkg);
                }
            }
        }
        catch (System.Exception ex)
        {
            // silently fail for initial load
        }
    }

    public async Task LoadAllPackagesPublic()
    {
        await LoadAllPackages();
    }

    [RelayCommand]
    private async Task PackSelected()
    {
        var toPack = SelectedProjects.Any() ? SelectedProjects.ToList() : (string.IsNullOrEmpty(SelectedProject) ? new List<string>() : new List<string> { SelectedProject });

        if (!toPack.Any())
        {
            MessageBox.Show("请先选择要打包的项目（可多选）或单个项目。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        //PackLog = "";

        foreach (var proj in toPack)
        {
            // proj may be a display name; map to the relative path from the solution
            var rel = GetRelativeProjectPath(proj) ?? proj;
            var projPath = ResolveProjectPath(rel);
            await RunDotnetPackAsync(projPath, CustomVersion);
        }
    }

    [RelayCommand]
    private async Task PackAll()
    {
        if (Projects.Count == 0)
        {
            MessageBox.Show("没有找到项目可打包。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        //PackLog = "";

        // iterate over mapped project paths
        foreach (var display in Projects)
        {
            var rel = GetRelativeProjectPath(display);
            if (string.IsNullOrEmpty(rel))
                continue;

            var projPath = ResolveProjectPath(rel);
            await RunDotnetPackAsync(projPath, CustomVersion);
        }
    }

    [RelayCommand]
    private async Task Upload()
    {
        // Upload .nupkg files to configured NuGet feed using `dotnet nuget push`.
        try
        {
            LoadSettings();

            var feedUrl = _settings.FeedUrl;
            if (string.IsNullOrWhiteSpace(feedUrl))
            {
                MessageBox.Show("请先在 Settings 中配置 NuGet 源。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var initialDir = !string.IsNullOrEmpty(OutputDirectory) ? OutputDirectory : SolutionDirectory ?? Environment.CurrentDirectory;

            var dlg = new OpenFileDialog()
            {
                Multiselect = true,
                Filter = "NuGet packages (*.nupkg)|*.nupkg",
                InitialDirectory = initialDir
            };

            var ok = dlg.ShowDialog();
            if (ok != true)
                return;

            var files = dlg.FileNames;
            if (files == null || files.Length == 0)
            {
                MessageBox.Show("没有选择任何 .nupkg 文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            //PackLog = "";

            var results = new List<string>();
            foreach (var file in files)
            {
                AppendLog($"开始上传：{file}");
                var res = await PushNupkgAsync(file, feedUrl, _settings.ApiKey);
                results.Add($"{Path.GetFileName(file)}: {(res ? "成功" : "失败")}");
            }

            var msg = string.Join("\n", results);
            AppendLog(msg);
            //MessageBox.Show($"{msg}", "上传结果", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (System.Exception ex)
        {
            AppendLog($"上传失败：{ex.Message}");
            //MessageBox.Show($"上传失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<bool> PushNupkgAsync(string packageFile, string feedUrl, string? apiKey)
    {
        if (!File.Exists(packageFile))
        {
            AppendLog($"文件未找到：{packageFile}");
            return false;
        }

        var args = new StringBuilder();
        args.Append("nuget push ");
        args.Append('"').Append(packageFile).Append('"');
        args.Append(" --source ");
        args.Append('"').Append(feedUrl).Append('"');
        args.Append(" --allow-insecure-connections");
        // include api key if provided
        if (!string.IsNullOrEmpty(apiKey))
        {
            args.Append(" --api-key ");
            args.Append('"').Append(apiKey).Append('"');
        }
        // skip duplicates to avoid errors when package already exists
        //args.Append(" --skip-duplicate");

        var psi = new ProcessStartInfo("dotnet", args.ToString())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            var process = new Process() { StartInfo = psi };
            var output = new StringBuilder();
            var error = new StringBuilder();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) { output.AppendLine(e.Data); AppendLog(e.Data); } };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) { error.AppendLine(e.Data); AppendLog(e.Data); } };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await Task.Run(() => process.WaitForExit());

            if (process.ExitCode == 0)
            {
                AppendLog($"上传成功：{packageFile}");
                return true;
            }
            else
            {
                AppendLog($"上传失败：{packageFile}\n{error}");
                return false;
            }
        }
        catch (System.Exception ex)
        {
            AppendLog($"上传时发生异常：{ex.Message}");
            return false;
        }
    }

    [RelayCommand]
    private async Task Delete()
    {
        AppendLog($"");
        // Collect selected versions
        var deletions = new List<(PackageItem pkg, List<VersionItem> versions)>();

        foreach (var pkg in Packages.ToList())
        {
            var sel = pkg.Versions.Where(v => v.IsSelected).ToList();
            if (sel.Any())
                deletions.Add((pkg, sel));
        }

        if (!deletions.Any())
        {
            MessageBox.Show("请先选择要删除的版本（通过复选框）。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // confirmation
        var sb = new StringBuilder();
        sb.AppendLine("将要删除以下包的指定版本：");
        foreach (var (pkg, versions) in deletions)
        {
            sb.Append(pkg.Id).Append(": ");
            sb.AppendLine(string.Join(", ", versions.Select(v => v.Version)));
        }
        sb.AppendLine();
        sb.AppendLine("此操作将尝试从配置的 NuGet 源中删除这些版本，可能不可逆。是否继续？");

        var answer = MessageBox.Show(sb.ToString(), "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            LoadSettings();
            var feedUrl = _settings.FeedUrl;
            if (string.IsNullOrWhiteSpace(feedUrl))
            {
                MessageBox.Show("请先在 Settings 中配置 NuGet 源。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var apiKey = _settings.ApiKey;

            // perform remote deletions using `dotnet nuget delete` and remove locally when successful
            foreach (var (pkg, versions) in deletions)
            {
                var toRemove = new List<VersionItem>();
                foreach (var v in versions)
                {
                    AppendLog($"开始删除：{pkg.Id} {v.Version}");
                    var ok = await DeletePackageVersionAsync(pkg.Id, v.Version, feedUrl, apiKey);
                    if (ok)
                    {
                        AppendLog($"删除成功：{pkg.Id} {v.Version}");
                        toRemove.Add(v);
                    }
                    else
                    {
                        AppendLog($"删除失败：{pkg.Id} {v.Version}");
                    }
                }

                foreach (var r in toRemove)
                    pkg.Versions.Remove(r);

                if (!pkg.Versions.Any())
                    Packages.Remove(pkg);
            }

            //MessageBox.Show("删除操作已完成。请查看日志以获取详细信息。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (System.Exception ex)
        {
            AppendLog($"删除过程中发生错误：{ex.Message}");
            //MessageBox.Show($"删除过程中发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<bool> DeletePackageVersionAsync(string packageId, string version, string feedUrl, string? apiKey)
    {
        // Use `dotnet nuget delete` to support various server implementations
        var args = new StringBuilder();
        args.Append("nuget delete ");
        args.Append('"').Append(packageId).Append('"').Append(' ');
        args.Append('"').Append(version).Append('"');
        args.Append(" --source ");
        args.Append('"').Append(feedUrl).Append('"');
        if (!string.IsNullOrEmpty(apiKey))
        {
            args.Append(" --api-key ");
            args.Append('"').Append(apiKey).Append('"');
        }
        args.Append(" --non-interactive");

        var psi = new ProcessStartInfo("dotnet", args.ToString())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            var process = new Process() { StartInfo = psi };
            var output = new StringBuilder();
            var error = new StringBuilder();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) { output.AppendLine(e.Data); AppendLog(e.Data); } };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) { error.AppendLine(e.Data); AppendLog(e.Data); } };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await Task.Run(() => process.WaitForExit());

            if (process.ExitCode == 0)
            {
                return true;
            }
            else
            {
                AppendLog($"删除命令退出码：{process.ExitCode} 错误：{error}");
                return false;
            }
        }
        catch (System.Exception ex)
        {
            AppendLog($"删除时发生异常：{ex.Message}");
            return false;
        }
    }

    public void LoadSettings()
    {
        _settings = Settings.Load();
        //// For now just expose feed url to search query for demo purposes
        //if (!string.IsNullOrEmpty(_settings.FeedUrl))
        //    SearchQuery = _settings.FeedUrl;

        // expose current source for UI binding
        CurrentSource = _settings.FeedUrl ?? string.Empty;
    }

    public Settings GetSettings() => _settings;

    private string ResolveProjectPath(string projectPath)
    {
        if (Path.IsPathRooted(projectPath))
            return projectPath;

        if (!string.IsNullOrEmpty(SolutionDirectory))
            return Path.GetFullPath(Path.Combine(SolutionDirectory, projectPath));

        return projectPath;
    }

    private async Task RunDotnetPackAsync(string projectFilePath, string? versionOverride)
    {
        if (!File.Exists(projectFilePath))
        {
            AppendLog($"项目文件未找到：{projectFilePath}");
            return;
        }

        var outDir = string.IsNullOrEmpty(OutputDirectory) ? Path.Combine(Path.GetDirectoryName(projectFilePath) ?? ".", "../nupkgs") : OutputDirectory;
        Directory.CreateDirectory(outDir);

        var args = $"pack \"{projectFilePath}\" -c Release -o \"{outDir}\"";
        if (!string.IsNullOrEmpty(versionOverride))
        {
            args += $" /p:PackageVersion={versionOverride}";
        }

        var psi = new ProcessStartInfo("dotnet", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            var process = new Process() { StartInfo = psi };
            var output = new StringBuilder();
            var error = new StringBuilder();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) { output.AppendLine(e.Data); AppendLog(e.Data); } };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) { error.AppendLine(e.Data); AppendLog(e.Data); } };

            AppendLog($"开始打包：{projectFilePath}");

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await Task.Run(() => process.WaitForExit());

            if (process.ExitCode == 0)
            {
                AppendLog($"打包成功：{projectFilePath} 输出目录：{outDir}");
                //MessageBox.Show($"打包成功：{projectFilePath}\n输出目录：{outDir}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                AppendLog($"打包失败：{projectFilePath}\n{error}");
                //MessageBox.Show($"打包失败：{projectFilePath}\n\n{error}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (System.Exception ex)
        {
            AppendLog($"执行打包时发生异常：{ex.Message}");
            //MessageBox.Show($"执行打包时发生异常：{ex.Message}", "异常", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AppendLog(string line)
    {
        try
        {
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() => PackLog = (PackLog ?? "") + line + "\n");
            }
            else
            {
                PackLog = (PackLog ?? "") + line + "\n";
            }
        }
        catch
        {
            // ignore logging errors
        }
    }
}

