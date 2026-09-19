using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using woker.Services;

var bytes = Encoding.UTF8.GetBytes("test installer bytes");
var hash = Convert.ToHexString(SHA256.HashData(bytes));
var filename = "WorkBench-2.2.0-win-x64-Setup.exe";
var downloadUrl = "https://github.com/example/workbench/releases/download/v2.2.0/" + filename;
var checks = 0;
string Release(string tag = "v2.2.0", bool prerelease = false, string? overrideUrl = null, bool includeManifest = true) => JsonSerializer.Serialize(new
{
    tag_name = tag, draft = false, prerelease,
    assets = new[]
    {
        new { name = includeManifest ? "update-win-x64.json" : "update-win-arm64.json", size = 1000,
              browser_download_url = "https://github.com/example/workbench/releases/download/v2.2.0/update-win-x64.json" },
        new { name = filename, size = bytes.Length, browser_download_url = overrideUrl ?? downloadUrl }
    }
});
string Manifest(string sha = "", string arch = "x64", string version = "2.2.0") => JsonSerializer.Serialize(new
{
    version, architecture = arch, installer = filename, sha256 = sha == "" ? hash : sha, size = bytes.Length
});
DesktopUpdateService Service(string release, string? manifest = null, byte[]? installer = null, HttpStatusCode status = HttpStatusCode.OK) =>
    new(new HttpClient(new FakeHandler(request =>
    {
        if (request.Headers.Authorization != null) throw new Exception("Credentials must not be sent to GitHub");
        if (request.Headers.UserAgent.Count == 0) throw new Exception("GitHub requires user agent");
        return new HttpResponseMessage(status)
        {
            Content = request.RequestUri!.Host == "api.github.com" ? new StringContent(release)
                : request.RequestUri.AbsolutePath.EndsWith(".json") ? new StringContent(manifest ?? Manifest())
                : new ByteArrayContent(installer ?? bytes)
        };
    })));
void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); checks++; }
async Task Reject(Func<Task> action, string message)
{
    try { await action(); }
    catch (InvalidOperationException e) { Assert(e.Message.Contains(message)); return; }
    throw new Exception("Expected rejection: " + message);
}
var current = new Version(2, 1, 0, 0);
var service = Service(Release());
var update = await service.CheckAsync("example/workbench", current, "x64");
Assert(update?.Version == new Version(2, 2, 0, 0));
Assert(await Service(Release()).CheckAsync("example/workbench", new Version(2, 2, 0), "x64") == null);
Assert(await Service(Release()).CheckAsync("example/workbench", new Version(3, 0, 0), "x64") == null);
Assert(await Service(Release(prerelease: true)).CheckAsync("example/workbench", current, "x64") == null);
await Reject(() => Service(Release()).CheckAsync("https://github.com/example/workbench", current, "x64"), "用户名/仓库名");
await Reject(() => Service(Release(overrideUrl: "https://evil.example/installer.exe")).CheckAsync("example/workbench", current, "x64"), "不属于");
await Reject(() => Service(Release(), Manifest(arch: "arm64")).CheckAsync("example/workbench", current, "x64"), "架构");
await Reject(() => Service(Release(), Manifest(version: "2.3.0")).CheckAsync("example/workbench", current, "x64"), "版本");
await Reject(() => Service(Release(), Manifest(sha: "invalid")).CheckAsync("example/workbench", current, "x64"), "校验");
await Reject(() => Service(Release(includeManifest: false)).CheckAsync("example/workbench", current, "x64"), "缺少");
await Reject(() => Service(Release(), status: HttpStatusCode.NotFound).CheckAsync("example/workbench", current, "x64"), "未找到");
await Reject(() => Service(Release(), status: HttpStatusCode.Forbidden).CheckAsync("example/workbench", current, "x64"), "限制");
var cache = Path.Combine(Path.GetTempPath(), "workbench-update-tests-" + Guid.NewGuid().ToString("N"));
try
{
    var path = await service.DownloadAsync(update!, cache);
    Assert(File.ReadAllBytes(path).SequenceEqual(bytes));
    File.Delete(path);
    await Reject(() => Service(Release(), installer: Encoding.UTF8.GetBytes("wrong installer byte")).DownloadAsync(update!, cache), "校验失败");
    await Reject(() => Service(Release(), installer: new byte[bytes.Length + 1]).DownloadAsync(update!, cache), "大小超过");
    Assert(!Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories).Any());
    using var cancel = new CancellationTokenSource();
    cancel.Cancel();
    try { await service.DownloadAsync(update!, cache, token: cancel.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { checks++; }
    Assert(!Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories).Any());
}
finally { if (Directory.Exists(cache)) Directory.Delete(cache, true); }
Console.WriteLine($"Update checks passed: {checks}");

sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(handler(request));
    }
}
