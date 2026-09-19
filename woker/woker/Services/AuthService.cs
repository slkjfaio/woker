using System;
using System.Linq;
using System.Threading.Tasks;
using woker.Models;

namespace woker.Services
{
    /// <summary>账号密码登录与注册（后端 API，PBKDF2 密码散列由后端完成，与 Web 端兼容）</summary>
    public static class AuthService
    {
        /// <summary>后端动态验证码（captchaId + PNG Data URL）</summary>
        public sealed class CaptchaData
        {
            public string CaptchaId { get; set; } = "";
            public string Image { get; set; } = "";
            public int ExpiresInSeconds { get; set; }
        }

        // ---------------- 验证码 ----------------
        public static async Task<CaptchaData?> GetCaptchaAsync()
        {
            return await ApiService.GetAsync<CaptchaData>("auth/captcha");
        }

        // ---------------- 账号密码登录 ----------------
        public static async Task<(User? user, string error)> LoginAsync(string username, string password,
            string captchaId, string captchaAnswer)
        {
            if (string.IsNullOrWhiteSpace(username)) return (null, "请输入用户名");
            if (string.IsNullOrEmpty(password)) return (null, "请输入密码");
            if (string.IsNullOrWhiteSpace(captchaAnswer)) return (null, "请输入验证码");

            try
            {
                var result = await ApiService.PostAsync<LoginData>("auth/login",
                    new { username = username.Trim(), password, captchaId, captchaAnswer });
                if (result == null) return (null, "登录失败：服务异常");

                SessionService.AccessToken = result.Token;
                SessionService.RefreshToken = result.RefreshToken;
                SessionService.PersistTokens(false);

                var user = result.User;
                return (user == null ? null : new User
                {
                    Id = user.Id,
                    Username = user.Username ?? "",
                    DisplayName = user.DisplayName ?? "",
                    Email = user.Email,
                }, "");
            }
            catch (Exception ex)
            {
                return (null, Friendly(ex));
            }
        }

        /// <summary>获取当前登录用户（令牌校验失败时由 ApiService 自动刷新并重试）</summary>
        public static async Task<User?> MeAsync()
        {
            var data = await ApiService.GetAsync<MeData>("auth/me");
            if (data == null) return null;
            return new User
            {
                Id = data.Id,
                Username = data.Username ?? "",
                DisplayName = data.DisplayName ?? "",
                Email = data.Email,
                LastLoginAt = data.LastLoginAt,
            };
        }

        // ---------------- 注册 ----------------
        public static async Task<bool> IsRegistrationEnabledAsync() => true;

        public static async Task<(bool ok, string error)> RegisterAsync(string username, string password,
            string confirmPassword, string displayName, string email, string captchaId, string captchaAnswer)
        {
            if (string.IsNullOrWhiteSpace(username)) return (false, "请输入用户名");
            if (username.Trim().Length < 2) return (false, "用户名至少 2 个字符");
            if (string.IsNullOrEmpty(password)) return (false, "请输入密码");
            if (password.Length < 8) return (false, "密码至少 8 位");
            if (!password.Any(char.IsUpper)) return (false, "密码必须包含大写字母");
            if (!password.Any(char.IsLower)) return (false, "密码必须包含小写字母");
            if (!password.Any(char.IsDigit)) return (false, "密码必须包含数字");
            if (string.IsNullOrEmpty(confirmPassword)) return (false, "请再次输入密码");
            if (password != confirmPassword) return (false, "两次输入的密码不一致");
            if (string.IsNullOrWhiteSpace(captchaAnswer)) return (false, "请输入验证码");

            try
            {
                var result = await ApiService.PostAsync<RegisterData>("auth/register", new
                {
                    username = username.Trim(),
                    password,
                    confirmPassword,
                    displayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
                    email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
                    captchaId,
                    captchaAnswer,
                });
                if (result == null) return (false, "注册失败：服务异常");
                return (true, "");
            }
            catch (Exception ex)
            {
                return (false, Friendly(ex));
            }
        }

        // ---------------- 工具 ----------------
        private static string Friendly(Exception ex) =>
            ex is ApiException api
                ? api.Message
                : "无法连接服务器，请检查网络或服务器地址：" + ex.Message;

        // ---------------- 后端响应 DTO ----------------
        private sealed class LoginData
        {
            public string Token { get; set; } = "";
            public string RefreshToken { get; set; } = "";
            public UserData? User { get; set; }
        }

        private sealed class UserData
        {
            public long Id { get; set; }
            public string? Username { get; set; }
            public string? DisplayName { get; set; }
            public string? Email { get; set; }
        }

        private sealed class RegisterData
        {
            public long Id { get; set; }
        }

        private sealed class MeData
        {
            public long Id { get; set; }
            public string? Username { get; set; }
            public string? DisplayName { get; set; }
            public string? Email { get; set; }
            public DateTime? LastLoginAt { get; set; }
        }
    }
}