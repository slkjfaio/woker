using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace woker.Services;

public sealed record DesktopUpdate(Version Version, string AssetName, Uri DownloadUrl, string Sha256, long Size);

/// <summary>Public GitHub Releases only. No application token or GitHub credential is sent.</summary>
public sealed class DesktopUpdateService
{
    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
    public static string Architecture => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
    public const long MaxInstallerSize = 1024L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient http;

    public DesktopUpdateService(HttpClient http) => this.http = http;

    public static string ValidateRepository(string repository)
    {
        repository = repository.Trim();
        if (!Regex.IsMatch(repository, @"^[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9_][A-Za-z0-9_.-]*$"))
            throw new InvalidOperationException("更新仓库请填写 GitHub 的 用户名/仓库名，不要填写网址或 Token。");
        return repository;
    }

    private static Version ParseVersion(string value)
    {
        if (!Regex.IsMatch(value, @"^[0-9]+\.[0-9]+\.[0-9]+$") || !Version.TryParse(value, out var version))
            throw new InvalidOperationException("发布版本必须为三段数字，例如 2.1.0。");
        return new Version(version.Major, version.Minor, version.Build, 0);
    }

    private async Task<byte[]> GetSmallAsync(Uri url, int maximum, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("WorkBench-Desktop/" + CurrentVersion.ToString(3));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("未找到公开的正式 Release，请检查仓库名以及版本是否已发布。");
        if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
            throw new InvalidOperationException("GitHub 暂时限制了请求，请稍后再检查更新。");
        response.EnsureSuccessStatusCode();
        using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + count > maximum) throw new InvalidOperationException("更新信息超过允许大小。");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static Uri AssetUri(JsonElement asset, string repository, string tag)
    {
        var name = asset.GetProperty("name").GetString()!;
        var url = new Uri(asset.GetProperty("browser_download_url").GetString()!);
        var prefix = $"https://github.com/{repository}/releases/download/{Uri.EscapeDataString(tag)}/";
        if (!url.AbsoluteUri.Equals(prefix + Uri.EscapeDataString(name), StringComparison.Ordinal)
            || !string.IsNullOrEmpty(url.UserInfo))
            throw new InvalidOperationException("更新文件地址不属于配置的 GitHub 发布仓库。");
        return url;
    }

    public async Task<DesktopUpdate?> CheckAsync(string repository, Version current, string architecture, CancellationToken token = default)
    {
        repository = ValidateRepository(repository);
        if (architecture is not ("x64" or "x86" or "arm64")) throw new InvalidOperationException("不支持的处理器架构。");
        using var release = JsonDocument.Parse(await GetSmallAsync(new Uri($"https://api.github.com/repos/{repository}/releases/latest"), 2 * 1024 * 1024, token));
        var root = release.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString()!;
        var version = ParseVersion(tag.StartsWith('v') ? tag[1..] : tag);
        var normalizedCurrent = new Version(current.Major, current.Minor, Math.Max(0, current.Build), Math.Max(0, current.Revision));
        if (version <= normalizedCurrent) return null;
        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        var manifests = assets.Where(a => a.GetProperty("name").GetString() == $"update-win-{architecture}.json").ToArray();
        if (manifests.Length != 1) throw new InvalidOperationException("此版本缺少适合当前架构的更新清单。");
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(await GetSmallAsync(AssetUri(manifests[0], repository, tag), 64 * 1024, token), JsonOptions)
            ?? throw new InvalidOperationException("更新清单为空。");
        var expectedName = $"WorkBench-{version.ToString(3)}-win-{architecture}-Setup.exe";
        if (ParseVersion(manifest.Version) != version || manifest.Architecture != architecture || manifest.Installer != expectedName
            || manifest.Size <= 0 || manifest.Size > MaxInstallerSize || !Regex.IsMatch(manifest.Sha256, "^[a-fA-F0-9]{64}$"))
            throw new InvalidOperationException("更新清单的版本、架构或校验信息不正确。");
        var installers = assets.Where(a => a.GetProperty("name").GetString() == expectedName).ToArray();
        if (installers.Length != 1 || installers[0].GetProperty("size").GetInt64() != manifest.Size)
            throw new InvalidOperationException("Release 安装包缺失或大小与清单不一致。");
        return new DesktopUpdate(version, expectedName, AssetUri(installers[0], repository, tag), manifest.Sha256, manifest.Size);
    }

    public async Task<string> DownloadAsync(DesktopUpdate update, string cacheDirectory, IProgress<int>? progress = null, CancellationToken token = default)
    {
        // Unique directory prevents concurrent checks or interrupted downloads from reusing an executable.
        var directory = Path.Combine(cacheDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var partial = Path.Combine(directory, "installer.partial");
        var destination = Path.Combine(directory, "WorkBench-Setup.exe");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUrl);
            request.Headers.UserAgent.ParseAdd("WorkBench-Desktop/" + CurrentVersion.ToString(3));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            using var input = await response.Content.ReadAsStreamAsync(token);
            using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    total += count;
                    if (total > update.Size || total > MaxInstallerSize) throw new InvalidOperationException("安装包大小超过清单记录。");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    progress?.Report((int)(total * 100 / update.Size));
                }
                if (total != update.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("安装包校验失败，文件已丢弃，请重试。");
            }
            File.Move(partial, destination);
            return destination;
        }
        catch
        {
            if (File.Exists(partial)) File.Delete(partial);
            throw;
        }
    }

    private sealed class UpdateManifest
    {
        public string Version { get; set; } = "";
        public string Architecture { get; set; } = "";
        public string Installer { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long Size { get; set; }
    }
}
