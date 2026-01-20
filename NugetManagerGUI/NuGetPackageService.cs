using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NugetManagerGUI;
using NugetManagerGUI.Model;
using NugetManagerGUI.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Settings = NugetManagerGUI.Settings;

public class NuGetPackageService
{
    private readonly ILogger _logger;
    private readonly SourceRepository _repository;
    private readonly Settings _source;

    public NuGetPackageService(Settings feed, ILogger logger)
    {
        _logger = logger;

        // 创建 NuGet 源
        var packageSource = new PackageSource(feed.FeedUrl);
        _repository = Repository.Factory.GetCoreV3(packageSource);
        this._source = feed;
    }

    /// <summary>
    /// 获取包的所有版本详细信息（包含预发布）
    /// </summary>
    public async Task<List<VersionItem>> GetAllPackageVersionsAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        var result = new List<VersionItem>();

        // 获取搜索资源
        var searchResource = await _repository.GetResourceAsync<PackageSearchResource>();

        // 创建搜索过滤器
        var searchFilter = new SearchFilter(true)
        {
            OrderBy = SearchOrderBy.Id,
            IncludeDelisted = true,
        };

        var searchResults = await searchResource.SearchAsync(
            packageId, // 搜索所有包
            searchFilter,
            skip: 0,
            take: 10,
            _logger,
            cancellationToken
        );

        var metadatas = await searchResults.First().GetVersionsAsync();

        foreach (var metadata in metadatas)
        {
            var versionInfo = new VersionItem
            {
                Version = metadata.Version,
                IsPrerelease = metadata.Version.IsPrerelease,
                //Published = metadata.PackageSearchMetadata.Published,
                //Description = metadata.Description,
                //Authors = metadata.Version.au,
                DownloadCount = metadata.DownloadCount
            };
            result.Add(versionInfo);
        }

        return result
            .OrderByDescending(v => v.Version)
            .ToList();
    }


    /// <summary>
    /// 获取前N个包
    /// </summary>
    public async Task<IEnumerable<PackageItem>> GetTopPackagesAsync(
        string search = "",
        int count = 100,
        bool includePrerelease = true,
        CancellationToken cancellationToken = default)
    {
        var result = new List<PackageItem>();

        // 获取搜索资源
        var searchResource = await _repository.GetResourceAsync<PackageSearchResource>();

        // 创建搜索过滤器
        var searchFilter = new SearchFilter(includePrerelease)
        {
            OrderBy = SearchOrderBy.Id,
            IncludeDelisted = true,
        };


        var searchResults = await searchResource.SearchAsync(
            search, // 搜索所有包
            searchFilter,
            skip: 0,
            take: count,
            _logger,
            cancellationToken
        );

        _logger.LogInformation($"加载 {searchResults.Count()} 个包");

        foreach (var package in searchResults)
        {
            var packageInfo = new PackageItem(package.Identity.Id)
            {
                Description = package.Description,
            };
            result.Add(packageInfo);
        }


        return result;
    }

    public async Task DeletePackageVersionAsync(
        string package,
        VersionItem version)
    {
        // 获取更新资源（包含删除功能）
        var updateResource = await _repository.GetResourceAsync<PackageUpdateResource>();

        // 执行删除
        await updateResource.Delete(
            package,
            version.Version.ToString(),
            getApiKey: _ => _source.FeedUrl,
            confirm: _ => true, // 确认删除，可以自定义确认逻辑
            noServiceEndpoint: false, // 如果是V2源设为true
            allowInsecureConnections: true,
            log: _logger);
    }

    public async Task PushPackageAsync(IList<string> packages)
    {
        // 获取更新资源（包含删除功能）
        var updateResource = await _repository.GetResourceAsync<PackageUpdateResource>();
        await updateResource.PushAsync(
            packages,
            symbolSource: null,
            timeoutInSecond: 10,
            disableBuffering: false,
            getApiKey: _ => _source.ApiKey,
            getSymbolApiKey: _ => null,
            noServiceEndpoint: false,
            skipDuplicate: false,
            allowSnupkg: true,
            allowInsecureConnections: true,
            log: _logger);

        foreach (var item in packages)
        {
            _logger.LogInformation($"成功推送包: {item}");
        }
    }

    public async Task PackAsync(string project, string? version, string? output)
    {
        if (!File.Exists(project))
        {
            _logger.LogInformation($"项目文件未找到：{project}");
            return;
        }

        var outDir = string.IsNullOrEmpty(output) ? Path.Combine(Path.GetDirectoryName(project) ?? ".", "../nupkgs") : output;
        Directory.CreateDirectory(outDir);

        var args = $"pack \"{project}\" -c Release -o \"{outDir}\"";
        if (!string.IsNullOrEmpty(version))
        {
            args += $" /p:PackageVersion={version}";
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
            var result = new StringBuilder();
            var error = new StringBuilder();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) { result.AppendLine(e.Data); _logger.LogInformation(e.Data); } };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) { error.AppendLine(e.Data); _logger.LogInformation(e.Data); } };

            _logger.LogInformation($"开始打包：{project}");

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await Task.Run(() => process.WaitForExit());

            if (process.ExitCode == 0)
            {
                _logger.LogInformation($"打包成功：{project} \n输出目录：{outDir}");
            }
            else
            {
                _logger.LogInformation($"打包失败：{project}\n{error}");
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning($"执行打包时发生异常：{ex.Message}");
        }
    }
}

