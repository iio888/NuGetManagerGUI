using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NugetManagerGUI.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class NuGetPackageService
{
    private readonly ILogger _logger;
    private readonly SourceRepository _repository;
    private readonly SourceCacheContext _cacheContext;

    public NuGetPackageService(string source)
    {
        _logger = NullLogger.Instance;
        _cacheContext = new SourceCacheContext();
        
        // 创建 NuGet 源
        var packageSource = new PackageSource(source);
        _repository = Repository.Factory.GetCoreV3(packageSource);
    }

    /// <summary>
    /// 获取包的所有版本（包含预发布版本）
    /// </summary>
    public async Task<List<NuGetVersion>> GetAllVersionsAsync(
        string packageId, 
        bool includeUnlisted = false,
        CancellationToken cancellationToken = default)
    {
        // 获取 FindPackageByIdResource
        var findPackageByIdResource = await _repository.GetResourceAsync<FindPackageByIdResource>();
        
        // 获取所有版本（包含预发布）
        var allVersions = await findPackageByIdResource.GetAllVersionsAsync(
            packageId,
            _cacheContext,
            _logger,
            cancellationToken
        );

        return allVersions.Reverse().ToList();
    }

    /// <summary>
    /// 获取包的所有版本详细信息（包含预发布）
    /// </summary>
    public async Task<List<PackageVersionInfo>> GetAllPackageVersionsAsync(
        string packageId,
        bool includeUnlisted = false,
        CancellationToken cancellationToken = default)
    {
        var result = new List<PackageVersionInfo>();

        // 方法1：使用 PackageMetadataResource
        var metadataResource = await _repository.GetResourceAsync<PackageMetadataResource>();
        
        var metadatas = await metadataResource.GetMetadataAsync(
            packageId,
            includePrerelease: true,
            includeUnlisted: includeUnlisted,
            _cacheContext,
            _logger,
            cancellationToken
        );

        foreach (var metadata in metadatas)
        {
            var versionInfo = new PackageVersionInfo
            {
                Version = metadata.Identity.Version,
                IsPrerelease = metadata.Identity.Version.IsPrerelease,
                IsListed = metadata.IsListed,
                Published = metadata.Published?.UtcDateTime ?? DateTime.MinValue,
                Description = metadata.Description,
                Authors = metadata.Authors,
                DownloadCount = metadata.DownloadCount
            };
            result.Add(versionInfo);
        }

        return result
            .OrderByDescending(v => v.Version)
            .ToList();
    }

    /// <summary>
    /// 获取所有版本（包括已列出和未列出的）
    /// </summary>
    public async Task<Dictionary<bool, List<NuGetVersion>>> GetAllVersionsWithListingStatusAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<bool, List<NuGetVersion>>
        {
            { true, new List<NuGetVersion>() },   // 已列出
            { false, new List<NuGetVersion>() }   // 未列出
        };

        // 获取已列出的版本
        var listedVersions = await GetAllVersionsAsync(packageId, false, cancellationToken);
        result[true].AddRange(listedVersions);

        // 获取未列出的版本（通过包含未列出）
        var metadataResource = await _repository.GetResourceAsync<PackageMetadataResource>();
        
        var allMetadata = await metadataResource.GetMetadataAsync(
            packageId,
            includePrerelease: true,
            includeUnlisted: true,
            _cacheContext,
            _logger,
            cancellationToken
        );

        foreach (var metadata in allMetadata)
        {
            var version = metadata.Identity.Version;
            if (!listedVersions.Contains(version) && !result[false].Contains(version))
            {
                result[false].Add(version);
            }
        }

        return result;
    }

    /// <summary>
    /// 搜索包含特定包的版本信息
    /// </summary>
    public async Task<List<PackageSearchResult>> SearchPackagesAsync(
        string searchTerm,
        bool includePrerelease = true,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PackageSearchResult>();
        
        // 获取搜索资源
        var searchResource = await _repository.GetResourceAsync<PackageSearchResource>();
        
        // 创建搜索过滤器
        var searchFilter = new SearchFilter(includePrerelease)
        {
            IncludeDelisted = false,
            OrderBy = SearchOrderBy.Id
        };

        // 执行搜索
        var searchResults = await searchResource.SearchAsync(
            searchTerm,
            searchFilter,
            skip: 0,
            take: take,
            _logger,
            cancellationToken
        );

        foreach (var package in searchResults)
        {
            var packageResult = new PackageSearchResult
            {
                PackageId = package.Identity.Id,
                LatestVersion = package.Identity.Version,
                Description = package.Description,
                Authors = package.Authors,
                TotalDownloads = package.DownloadCount,
                Versions = new List<NuGetVersion>()
            };

            // 获取该包的所有版本
            var versions = await GetAllVersionsAsync(package.Identity.Id, false, cancellationToken);
            packageResult.Versions.AddRange(versions);

            results.Add(packageResult);
        }

        return results;
    }

        /// <summary>
    /// 获取前N个包（按下载量排序）
    /// </summary>
    public async Task<List<PackageItem>> GetTopPackagesAsync(
        int count = 100,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        var result = new List<PackageItem>();
        
        // 获取搜索资源
        var searchResource = await _repository.GetResourceAsync<PackageSearchResource>();
        
        // 创建搜索过滤器
        var searchFilter = new SearchFilter(includePrerelease)
        {
            IncludeDelisted = false,
            OrderBy = SearchOrderBy.Id // 按下载量排序
        };

        int pageSize = Math.Min(count, 100); // 每页最多100个
        int pages = (int)Math.Ceiling(count / (double)pageSize);
        
        for (int page = 0; page < pages; page++)
        {
            int skip = page * pageSize;
            int take = Math.Min(pageSize, count - result.Count);
            
            if (take <= 0) break;
            
            var searchResults = await searchResource.SearchAsync(
                "*", // 搜索所有包
                searchFilter,
                skip: skip,
                take: take,
                _logger,
                cancellationToken
            );

            foreach (var package in searchResults)
            {
                var packageInfo = new PackageItem(package.Identity.Id)
                {
                    Description = package.Description,
                };
                result.Add(packageInfo);
            }
            
            // 如果返回的结果少于请求的数量，说明没有更多了
            if (searchResults.Count() < take) break;
            
            // 避免请求过快，添加延迟
            await Task.Delay(100, cancellationToken);
        }

        return result.Take(count).ToList();
    }
}

