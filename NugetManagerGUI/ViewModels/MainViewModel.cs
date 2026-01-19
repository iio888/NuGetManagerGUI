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
using NugetManagerGUI.Model;
using NuGet.Packaging;

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
    private Settings? _settings = null;

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
        p1.Versions.Add(new PackageVersionInfo("1.0.0"));
        p1.Versions.Add(new PackageVersionInfo("1.1.0"));
        p1.Versions.Add(new PackageVersionInfo("2.0.0"));

        var p2 = new PackageItem("Another.Package");
        p2.Versions.Add(new PackageVersionInfo("2.3.1"));

        Packages.Add(p1);
        Packages.Add(p2);

        LoadSettings();
        _service = new NuGetPackageService(_settings, new ConsoleLogger(AppendLog));
    }
    private NuGetPackageService _service;

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

    private string? GetRelativeProjectPath(string displayName)
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
            await _service.GetTopPackagesAsync(searchQuery);

        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"搜索失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Load all packages from configured NuGet V3 feed
    [RelayCommand]
    private async Task LoadAllPackages()
    {
        try
        {
            LoadSettings(); // ensure settings loaded
        }
        catch (Exception e)
        {
            AppendLog(e.Message);
        }

        var feedUrl = _settings.FeedUrl;
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            return;
        }

        try
        {
            var packages = await _service.GetTopPackagesAsync();
            Packages.Clear();
            Packages.AddRange(packages);
        }
        catch (Exception e)
        {
            AppendLog(e.Message);
        }
    }

    public async Task LoadAllPackagesPublic()
    {
        await LoadAllPackages();
    }

    [RelayCommand]
    private async Task PackSelected()
    {
        var toPack = SelectedProjects.Any() ? 
            SelectedProjects.ToList() : 
            (string.IsNullOrEmpty(SelectedProject) ? 
                new List<string>() : 
                new List<string> { SelectedProject });

        if (!toPack.Any())
        {
            MessageBox.Show("请先选择要打包的项目（可多选）或单个项目。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var proj in toPack)
        {
            // proj may be a display name; map to the relative path from the solution
            var rel = GetRelativeProjectPath(proj) ?? proj;
            var projPath = ResolveProjectPath(rel);
            await _service.PackAsync(projPath, CustomVersion, OutputDirectory);
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
            await _service.PackAsync(projPath, CustomVersion, OutputDirectory);
        }
    }

    public async Task PackageSelectionChanged()
    {
        if (SelectedPackage is null)
            return;

        try
        {
            SelectedPackage.Versions.Clear();
            var versions = await _service!.GetAllPackageVersionsAsync(SelectedPackage.Id);
            SelectedPackage.Versions.AddRange(versions);
        }
        catch (Exception e)
        {
            AppendLog("更新版本列表时出错：");
            AppendLog(e.Message);
        }
    }

    [RelayCommand]
    private async Task Upload()
    {
        // Upload .nupkg files to configured NuGet feed 
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

            foreach (var file in files)
            {
                if (!File.Exists(file))
                {
                    AppendLog($"文件未找到：{file}");
                    continue;
                }
            }
            AppendLog($"开始上传：{files}");
            await _service.PushPackageAsync(files);
            AppendLog("");

        }
        catch (System.Exception ex)
        {
            AppendLog($"上传失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task Delete()
    {
        // Collect selected versions
        var deletions = new List<(PackageItem pkg, List<PackageVersionInfo> versions)>();

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
            sb.Append(pkg.Id).Append(": \n");
            sb.AppendLine(string.Join(", ", versions.Select(v => v.Version)));
            sb.AppendLine();
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

            // perform remote deletions 
            foreach (var (pkg, versions) in deletions)
            {
                foreach (var v in versions)
                {
                    AppendLog($"开始删除：{pkg.Id} {v.Version}");
                    try
                    {
                        await _service.DeletePackageVersionAsync(pkg.Id, v);
                    }
                    catch (System.Exception ex)
                    {
                        AppendLog($"删除 {pkg.Id} 时发生异常：{ex.Message}");
                    }
                }
                await PackageSelectionChanged();

                if (!pkg.Versions.Any())
                    Packages.Remove(pkg);
                AppendLog("");
            }
        }
        catch (System.Exception ex)
        {
            AppendLog($"删除过程中发生错误：{ex.Message}");
        }

        AppendLog("");
    }

    public void LoadSettings()
    {
        if (_settings is not null && _settings.FeedUrl.StartsWith("http"))
        {
            return; // already loaded
        }

        _settings = Settings.Load();
        CurrentSource = _settings!.FeedUrl;
    }

    private string ResolveProjectPath(string projectPath)
    {
        if (Path.IsPathRooted(projectPath))
            return projectPath;

        if (!string.IsNullOrEmpty(SolutionDirectory))
            return Path.GetFullPath(Path.Combine(SolutionDirectory, projectPath));

        return projectPath;
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
