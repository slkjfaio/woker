using System;
using System.IO;
using System.Text.Json;

namespace woker.Services
{
    /// <summary>本地持久化。统一保存到 %LOCALAPPDATA%\WorkBench，便于查找和备份。</summary>
    public static class LocalStore
    {
        private static readonly string Dir = GetBaseDir();
        private static readonly JsonSerializerOptions JsonOpt = new() { WriteIndented = true };

        private static string GetBaseDir()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WorkBench");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static T? Load<T>(string name) where T : class
        {
            try
            {
                var path = Path.Combine(Dir, name);
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
            }
            catch { return null; }
        }

        public static void Save<T>(string name, T value)
        {
            try
            {
                File.WriteAllText(Path.Combine(Dir, name), JsonSerializer.Serialize(value, JsonOpt));
            }
            catch { /* 忽略持久化失败 */ }
        }

        public static ServerConfig GetServerConfig()
        {
            var config = Load<ServerConfig>("server.config.json");
            if (config != null && !string.IsNullOrWhiteSpace(config.BaseUrl)) return config;

            config = new ServerConfig();
            SaveServerConfig(config);
            return config;
        }

        public static void SaveServerConfig(ServerConfig cfg) => Save("server.config.json", cfg);

        public static string? GetString(string key) => Load<string>(key + ".json");

        public static void SetString(string key, string value) => Save(key + ".json", value);
    }
}
