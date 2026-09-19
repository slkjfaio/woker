using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace woker.Services
{
    /// <summary>后端服务器地址配置</summary>
    public class ServerConfig
    {
        public string BaseUrl { get; set; } = Environment.GetEnvironmentVariable("WOKER_API_BASE_URL") ?? "";

        /// <summary>从用户保存的地址或环境变量生成 API 基址。</summary>
        public string ApiBaseUrl
        {
            get
            {
                var url = (BaseUrl ?? "").Trim().TrimEnd('/');
                if (url.Length == 0)
                    throw new InvalidOperationException("请在登录页或设置页填写服务器地址，也可设置 WOKER_API_BASE_URL 环境变量");
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    url = "https://" + url;
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                    || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)
                    || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                    throw new InvalidOperationException("请输入有效的 HTTP 或 HTTPS 服务器地址，不要包含账号、查询参数或片段");
                return url.EndsWith("/api", StringComparison.OrdinalIgnoreCase) ? url : url + "/api";
            }
        }
    }

    /// <summary>业务异常（携带后端返回的错误信息）</summary>
    public class ApiException : Exception
    {
        public int Code { get; }
        public ApiException(string message, int code = 0) : base(message) => Code = code;
    }

    /// <summary>统一响应包装体</summary>
    public sealed class ApiResult<T>
    {
        public int Code { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }
        public bool Ok => Code == 200;
    }

    /// <summary>
    /// 后端 API 客户端：统一 HTTP 调用、Bearer 鉴权、401 时 refreshToken 续期、
    /// 统一响应体 {code,message,data} 解析、SSE 流式读取。
    /// </summary>
    public static class ApiService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

        private static readonly JsonSerializerOptions JsonOpt = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public static ServerConfig Config { get; private set; } = LocalStore.GetServerConfig();

        /// <summary>当前访问令牌（由 SessionService 在读请求时填充）</summary>
        public static string? AccessToken { get; set; }

        /// <summary>当前刷新令牌</summary>
        public static string? RefreshToken { get; set; }

        public static void UpdateConfig(ServerConfig cfg)
        {
            Config = cfg;
            LocalStore.SaveServerConfig(cfg);
        }

        /// <summary>检测服务器是否可达（无需登录态）</summary>
        public static async Task<(bool ok, string message)> TestAsync()
        {
            return await TestAsyncWith(Config);
        }

        /// <summary>用指定服务器地址检测可达性（不修改当前配置）</summary>
        public static async Task<(bool ok, string message)> TestAsyncWith(ServerConfig cfg)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, JoinWith(cfg, "auth/me"));
                using var resp = await Http.SendAsync(req);
                return (true, resp.StatusCode == HttpStatusCode.OK ? "服务器连接正常" : $"服务器连接正常（HTTP {(int)resp.StatusCode}）");
            }
            catch (Exception ex)
            {
                return (false, "无法连接服务器：" + ex.Message);
            }
        }

        // ================= 通用 HTTP 方法（自动解析统一响应体） =================
        public static Task<T?> GetAsync<T>(string path) => SendAsync<T>(HttpMethod.Get, path, null);
        public static Task<T?> PostAsync<T>(string path, object? body) => SendAsync<T>(HttpMethod.Post, path, body);
        public static Task<T?> PutAsync<T>(string path, object? body) => SendAsync<T>(HttpMethod.Put, path, body);
        public static Task<T?> PatchAsync<T>(string path, object? body) => SendAsync<T>(HttpMethod.Patch, path, body);
        public static Task<T?> DeleteAsync<T>(string path) => SendAsync<T>(HttpMethod.Delete, path, null);

        private static async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, bool allowRetry = true)
        {
            var (code, payload) = await RawAsync(method, path, body);
            if (code == HttpStatusCode.Unauthorized)
            {
                if (await TryRefreshAsync())
                {
                    var retried = await RawAsync(method, path, body);
                    if (retried.Item1 != HttpStatusCode.Unauthorized)
                    {
                        code = retried.Item1;
                        payload = retried.Item2;
                    }
                }
            }

            var result = Deserialize<T>(payload);
            if (!result.Ok)
                throw new ApiException(result.Message ?? "请求失败", result.Code);
            return result.Data;
        }

        /// <summary>发起 HTTP 请求并返回 (状态码, 响应文本)。401 由调用方决定是否刷新重试。</summary>
        private static async Task<(HttpStatusCode, string)> RawAsync(HttpMethod method, string path, object? body)
        {
            using var req = new HttpRequestMessage(method, Join(path));
            if (!string.IsNullOrWhiteSpace(AccessToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            if (body != null)
            {
                var text = JsonSerializer.Serialize(body, JsonOpt);
                req.Content = new StringContent(text, Encoding.UTF8, "application/json");
            }
            using var resp = await Http.SendAsync(req);
            var text2 = await resp.Content.ReadAsStringAsync();
            return (resp.StatusCode, text2);
        }

        /// <summary>用 refreshToken 续期访问令牌。成功返回 true 并更新 SessionService 中的令牌。</summary>
        public static async Task<bool> TryRefreshAsync()
        {
            var refresh = RefreshToken ?? SessionService.RefreshToken;
            if (string.IsNullOrWhiteSpace(refresh)) return false;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, Join("auth/refresh"))
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { refreshToken = refresh }), Encoding.UTF8, "application/json"),
                };
                using var resp = await Http.SendAsync(req);
                var text = await resp.Content.ReadAsStringAsync();
                var result = Deserialize<RefreshData>(text);
                if (!result.Ok || result.Data == null) return false;
                AccessToken = result.Data.Token;
                SessionService.AccessToken = result.Data.Token;
                SessionService.RefreshToken = result.Data.RefreshToken;
                RefreshToken = result.Data.RefreshToken;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ================= 流式对话（SSE） =================
        /// <summary>POST 流式接口，逐块回调 delta 文本，返回完整回复。提供可感知鉴权的错误。</summary>
        public static async Task<(string reply, string error)> PostStreamAsync(
            string path, object body, Action<string> onDelta, int maxRetryOn401 = 1)
        {
            return await PostStreamCoreAsync(path, body, onDelta, maxRetryOn401);
        }

        private static async Task<(string reply, string error)> PostStreamCoreAsync(
            string path, object body, Action<string> onDelta, int maxRetry)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, Join(path));
                if (!string.IsNullOrWhiteSpace(AccessToken))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
                req.Content = new StringContent(JsonSerializer.Serialize(body, JsonOpt), Encoding.UTF8, "application/json");

                using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                if (resp.StatusCode == HttpStatusCode.Unauthorized)
                {
                    if (maxRetry > 0 && await TryRefreshAsync())
                        return await PostStreamCoreAsync(path, body, onDelta, maxRetry - 1);
                    return ("", "登录已过期，请重新登录");
                }
                if (!resp.IsSuccessStatusCode)
                {
                    var errBody = await resp.Content.ReadAsStringAsync();
                    return ("", $"HTTP {(int)resp.StatusCode}: {Summarize(errBody)}");
                }

                await using var stream = await resp.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);
                var sb = new StringBuilder();
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:")) continue;
                    var data = line["data:".Length..].Trim();
                    if (data == "[DONE]") break;
                    try
                    {
                        using var doc = JsonDocument.Parse(data);
                        if (doc.RootElement.TryGetProperty("delta", out var delta)
                            && delta.ValueKind == JsonValueKind.String)
                        {
                            var piece = delta.GetString();
                            if (!string.IsNullOrEmpty(piece))
                            {
                                sb.Append(piece);
                                onDelta(piece);
                            }
                        }
                    }
                    catch { /* 跳过无法解析的行 */ }
                }
                return (sb.ToString(), "");
            }
            catch (Exception ex)
            {
                return ("", ex.Message);
            }
        }

        // ================= 工具 =================
        private static ApiResult<T> Deserialize<T>(string text)
        {
            try
            {
                return JsonSerializer.Deserialize<ApiResult<T>>(text, JsonOpt) ?? new ApiResult<T> { Code = 0, Message = "响应为空" };
            }
            catch
            {
                return new ApiResult<T> { Code = 0, Message = Summarize(text) };
            }
        }

        private static string Join(string path)
        {
            return JoinWith(Config, path);
        }

        private static string JoinWith(ServerConfig cfg, string path)
        {
            var api = cfg.ApiBaseUrl.TrimEnd('/');
            path = path.TrimStart('/');
            return api + "/" + path;
        }

        private static string Summarize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "空响应";
            return text.Length > 300 ? text[..300] : text;
        }

        public sealed class RefreshData
        {
            public string Token { get; set; } = "";
            public string RefreshToken { get; set; } = "";
        }
    }
}
