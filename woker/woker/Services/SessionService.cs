using System;
using System.Threading.Tasks;
using woker.Models;

namespace woker.Services
{
    /// <summary>全局会话：当前用户 + 访问令牌 + 当前项目 + 当前日报 + 保持登录</summary>
    public static class SessionService
    {
        public const string RememberKey = "session_remember";
        public const string LastProjectKey = "last_project_id";
        public const string TokenKey = "session_access_token";
        public const string RefreshTokenKey = "session_refresh_token";

        public static User? CurrentUser { get; set; }
        public static Project? CurrentProject { get; set; }
        public static DailyLog? CurrentLog { get; set; }
        public static string Theme { get; set; } = "Light";

        /// <summary>当前访问令牌（JWT）</summary>
        public static string? AccessToken { get; set; }

        /// <summary>当前刷新令牌</summary>
        public static string? RefreshToken { get; set; }

        public static bool IsLoggedIn => CurrentUser != null;

        public static void SetUser(User user, bool remember)
        {
            CurrentUser = user;
            if (remember) LocalStore.SetString(RememberKey, user.Id.ToString());
            else LocalStore.SetString(RememberKey, "");
            PersistTokens(remember);
        }

        /// <summary>持久化令牌：remember 时保存，否则仅内存。</summary>
        public static void PersistTokens(bool persist)
        {
            ApiService.AccessToken = AccessToken;
            ApiService.RefreshToken = RefreshToken;
            if (persist)
            {
                if (!string.IsNullOrEmpty(AccessToken)) LocalStore.SetString(TokenKey, AccessToken);
                if (!string.IsNullOrEmpty(RefreshToken)) LocalStore.SetString(RefreshTokenKey, RefreshToken);
            }
            else
            {
                LocalStore.SetString(TokenKey, "");
                LocalStore.SetString(RefreshTokenKey, "");
            }
        }

        public static void SetCurrentProject(Project project)
        {
            CurrentProject = project;
            if (project != null)
            {
                LocalStore.SetString(LastProjectKey, project.Id.ToString());
            }
        }

        public static long GetLastProjectId()
        {
            var idStr = LocalStore.GetString(LastProjectKey);
            return long.TryParse(idStr, out var id) ? id : 0;
        }

        public static void Clear()
        {
            CurrentUser = null;
            CurrentProject = null;
            CurrentLog = null;
            AccessToken = null;
            RefreshToken = null;
            ApiService.AccessToken = null;
            ApiService.RefreshToken = null;
            LocalStore.SetString(RememberKey, "");
            LocalStore.SetString(TokenKey, "");
            LocalStore.SetString(RefreshTokenKey, "");
        }

        /// <summary>尝试恢复保持登录的账号：本地令牌 + 后端验证，令牌过期自动刷新。</summary>
        public static async Task<bool> TryRestoreAsync()
        {
            var idStr = LocalStore.GetString(RememberKey);
            var token = LocalStore.GetString(TokenKey);
            var refresh = LocalStore.GetString(RefreshTokenKey);
            if (string.IsNullOrEmpty(idStr)
                || !long.TryParse(idStr, out var uid)
                || (string.IsNullOrEmpty(token) && string.IsNullOrEmpty(refresh)))
                return false;

            AccessToken = token;
            RefreshToken = refresh;
            ApiService.AccessToken = AccessToken;
            ApiService.RefreshToken = RefreshToken;

            try
            {
                var user = await AuthService.MeAsync();
                if (user == null) return false;
                // 若通过刷新恢复了令牌，重新持久化
                PersistTokens(true);
                CurrentUser = user;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>轻量 Toast 通知（Shell 订阅显示）</summary>
    public static class Toast
    {
        public static event Action<string>? Requested;

        public static void Show(string message) => Requested?.Invoke(message);
    }
}