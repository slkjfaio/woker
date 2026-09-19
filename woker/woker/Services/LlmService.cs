using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using woker.Models;

namespace woker.Services
{
    /// <summary>
    /// 大模型服务（OpenAI 兼容）。密钥与请求由后端代理，客户端只传 configId，
    /// 通过 /api/ai/chat、/api/ai/chat/stream、/api/ai/models 接口实现。
    /// </summary>
    public static class LlmService
    {
        /// <summary>预置模型（OpenAI 兼容协议，填 Key 即用）</summary>
        public static readonly List<LlmConfig> Presets = new()
        {
            new LlmConfig { Name="DeepSeek",    BaseUrl="https://api.deepseek.com/v1",          ModelName="deepseek-chat" },
            new LlmConfig { Name="通义千问",     BaseUrl="https://dashscope.aliyuncs.com/compatible-mode/v1", ModelName="qwen-plus" },
            new LlmConfig { Name="Kimi",        BaseUrl="https://api.moonshot.cn/v1",           ModelName="moonshot-v1-8k" },
            new LlmConfig { Name="智谱 GLM",    BaseUrl="https://open.bigmodel.cn/api/paas/v4",  ModelName="glm-4-air" },
            new LlmConfig { Name="OpenAI",      BaseUrl="https://api.openai.com/v1",            ModelName="gpt-4o-mini" },
        };

        public sealed record ChatMsg(string Role, string Content);

        /// <summary>从 OpenAI 兼容服务获取可用模型列表（后端代理拉取）</summary>
        public static async Task<(List<string> models, string error)> GetModelsAsync(
            string baseUrl, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                return (new List<string>(), "请先填写 BaseURL");

            try
            {
                var url = $"ai/models?baseUrl={Uri.EscapeDataString(baseUrl.Trim())}"
                          + (string.IsNullOrWhiteSpace(apiKey) ? "" : $"&apiKey={Uri.EscapeDataString(apiKey.Trim())}");
                var list = await ApiService.GetAsync<List<string>>(url) ?? new List<string>();
                return list.Count > 0
                    ? (list, "")
                    : (list, "接口未返回可用模型，请手动填写模型 ID");
            }
            catch (Exception ex)
            {
                return (new List<string>(), Friendly(ex));
            }
        }

        /// <summary>非流式对话：后端代理一次性返回完整回复</summary>
        public static async Task<(string reply, string error)> ChatAsync(LlmConfig cfg, IList<ChatMsg> messages, double temperature = 0.7)
        {
            try
            {
                var body = new
                {
                    configId = cfg.Id,
                    messages = ToMsgArray(messages),
                    temperature,
                };
                var result = await ApiService.PostAsync<ChatData>("ai/chat", body);
                return (result?.Content ?? "", "");
            }
            catch (Exception ex)
            {
                return ("", Friendly(ex));
            }
        }

        /// <summary>流式对话：后端以 SSE 增量推送，回调 delta 文本，完成后返回完整文本</summary>
        public static async Task<(string reply, string error)> ChatStreamAsync(
            LlmConfig cfg, IList<ChatMsg> messages, Action<string> onDelta, double temperature = 0.7)
        {
            var body = new
            {
                configId = cfg.Id,
                messages = ToMsgArray(messages),
                temperature,
            };
            return await ApiService.PostStreamAsync("ai/chat/stream", body, onDelta);
        }

        private static object[] ToMsgArray(IList<ChatMsg> messages)
        {
            var arr = new object[messages.Count];
            for (var i = 0; i < messages.Count; i++)
                arr[i] = new { role = messages[i].Role, content = messages[i].Content };
            return arr;
        }

        private static string Friendly(Exception ex) =>
            ex is ApiException api
                ? api.Message
                : "无法连接服务器：" + ex.Message;

        private sealed class ChatData
        {
            public string Content { get; set; } = "";
        }
    }
}