using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using woker.Models;

namespace woker.Services
{
    /// <summary>
    /// 工作台数据仓储：全部经由用户配置的后端 REST API 读写，
    /// 不再直连数据库。方法签名与旧版 MySQL 实现保持一致，页面调用方无需改动。
    /// </summary>
    public static class LogRepository
    {
        // ================= 项目管理 =================
        public static async Task<List<Project>> GetProjectsAsync(long userId)
        {
            return await ApiService.GetAsync<List<Project>>("projects") ?? new List<Project>();
        }

        public static async Task<Project?> GetProjectAsync(long userId, long projectId)
        {
            var all = await GetProjectsAsync(userId);
            return all.FirstOrDefault(p => p.Id == projectId);
        }

        public static async Task<long> CreateProjectAsync(long userId, Project project)
        {
            var id = await ApiService.PostAsync<IdData>("projects", new
            {
                name = project.Name,
                description = project.Description,
                color = project.Color,
                isDefault = project.IsDefault,
            });
            return id?.Id ?? 0;
        }

        public static async Task UpdateProjectAsync(long userId, Project project)
        {
            await ApiService.PutAsync<object>($"projects/{project.Id}", new
            {
                name = project.Name,
                description = project.Description,
                color = project.Color,
            });
        }

        public static async Task SetDefaultProjectAsync(long userId, long projectId)
        {
            await ApiService.PutAsync<object>($"projects/{projectId}/default", null);
        }

        public static async Task ArchiveProjectAsync(long userId, long projectId)
        {
            await ApiService.PutAsync<object>($"projects/{projectId}/archive", null);
        }

        public static async Task<Project?> GetOrCreateDefaultProjectAsync(long userId)
        {
            var projects = await GetProjectsAsync(userId);

            var defaultProj = projects.FirstOrDefault(p => p.IsDefault);
            if (defaultProj != null) return defaultProj;

            if (projects.Count == 0)
            {
                var newId = await CreateProjectAsync(userId, new Project
                {
                    Name = "默认项目",
                    Description = "自动创建的默认项目",
                    Color = "#0078D4",
                    IsDefault = true,
                });
                return await GetProjectAsync(userId, newId);
            }

            await SetDefaultProjectAsync(userId, projects[0].Id);
            projects[0].IsDefault = true;
            return projects[0];
        }

        // ================= 日报主表 =================
        public static async Task<DailyLog> GetOrCreateLogAsync(long userId, long projectId, DateTime date)
        {
            var query = $"logs?date={date:yyyy-MM-dd}&projectId={projectId}";
            return await ApiService.GetAsync<DailyLog>(query)
                   ?? new DailyLog { Id = 0, UserId = userId, ProjectId = projectId, LogDate = date };
        }

        // ================= 模块1 · 工作成果 =================
        public static async Task<long> AddTaskAsync(long logId, WorkTask t)
        {
            var id = await ApiService.PostAsync<IdData>($"logs/{logId}/tasks", TaskBody(t));
            return id?.Id ?? 0;
        }

        public static async Task UpdateTaskAsync(long logId, WorkTask t)
        {
            await ApiService.PutAsync<object>($"logs/{logId}/tasks/{t.Id}", TaskBody(t));
        }

        public static async Task ToggleTaskAsync(long logId, long id, bool done)
        {
            await ApiService.PatchAsync<object>($"logs/{logId}/tasks/{id}/toggle", new { isDone = done });
        }

        public static async Task DeleteTaskAsync(long logId, long id)
        {
            await ApiService.DeleteAsync<object>($"logs/{logId}/tasks/{id}");
        }

        private static object TaskBody(WorkTask t) => new
        {
            title = t.Title,
            result = string.IsNullOrEmpty(t.Result) ? null : t.Result,
            type = string.IsNullOrEmpty(t.Type) ? null : t.Type,
            costHours = t.CostHours,
            execCount = t.ExecCount,
            passCount = t.PassCount,
            bugCount = t.BugCount,
            isDone = t.IsDone,
        };

        // ================= 模块2 · 问题复盘 =================
        public static async Task<long> AddReflectionAsync(long logId, Reflection r)
        {
            var id = await ApiService.PostAsync<IdData>($"logs/{logId}/reflections", ReflectionBody(r));
            return id?.Id ?? 0;
        }

        public static async Task UpdateReflectionAsync(long logId, Reflection r)
        {
            await ApiService.PutAsync<object>($"logs/{logId}/reflections/{r.Id}", ReflectionBody(r));
        }

        public static async Task DeleteReflectionAsync(long logId, long id)
        {
            await ApiService.DeleteAsync<object>($"logs/{logId}/reflections/{id}");
        }

        private static object ReflectionBody(Reflection r) => new
        {
            title = r.Title,
            category = r.Category,
            timeStr = string.IsNullOrEmpty(r.TimeStr) ? null : r.TimeStr,
            phenomenon = string.IsNullOrEmpty(r.Phenomenon) ? null : r.Phenomenon,
            investigation = string.IsNullOrEmpty(r.Investigation) ? null : r.Investigation,
            solution = string.IsNullOrEmpty(r.Solution) ? null : r.Solution,
            prevention = string.IsNullOrEmpty(r.Prevention) ? null : r.Prevention,
        };

        // ================= 模块3 · 能力成长 =================
        public static async Task<long> AddGrowthAsync(long logId, GrowthEntry g)
        {
            var id = await ApiService.PostAsync<IdData>($"logs/{logId}/growth", new
            {
                dimKey = g.DimKey,
                timeStr = string.IsNullOrEmpty(g.TimeStr) ? null : g.TimeStr,
                content = g.Content,
            });
            return id?.Id ?? 0;
        }

        public static async Task DeleteGrowthAsync(long logId, long id)
        {
            await ApiService.DeleteAsync<object>($"logs/{logId}/growth/{id}");
        }

        public static async Task<Dictionary<string, int>> GetDimLevelsAsync(long userId)
        {
            var list = await ApiService.GetAsync<List<DimLevelData>>("growth/levels") ?? new List<DimLevelData>();
            var dict = new Dictionary<string, int>();
            foreach (var item in list)
                if (!string.IsNullOrEmpty(item.DimKey))
                    dict[item.DimKey] = item.Level;
            return dict;
        }

        public static async Task BumpDimLevelAsync(long userId, string dimKey, int delta)
        {
            await ApiService.PostAsync<object>("growth/levels/bump", new { dimKey, delta });
        }

        // ================= 模块4 · 遗留待办 =================
        public static async Task<long> AddTodoAsync(long logId, TodoItem t)
        {
            var id = await ApiService.PostAsync<IdData>($"logs/{logId}/todos", TodoBody(t));
            return id?.Id ?? 0;
        }

        public static async Task UpdateTodoAsync(long logId, TodoItem t)
        {
            await ApiService.PutAsync<object>($"logs/{logId}/todos/{t.Id}", TodoBody(t));
        }

        public static async Task ToggleTodoAsync(long logId, long id, bool done)
        {
            await ApiService.PatchAsync<object>($"logs/{logId}/todos/{id}/toggle", new { isDone = done });
        }

        public static async Task DeleteTodoAsync(long logId, long id)
        {
            await ApiService.DeleteAsync<object>($"logs/{logId}/todos/{id}");
        }

        private static object TodoBody(TodoItem t) => new
        {
            category = t.Category,
            title = t.Title,
            priority = t.Priority,
            deadline = string.IsNullOrEmpty(t.Deadline) ? null : t.Deadline,
            meta = string.IsNullOrEmpty(t.Meta) || t.Meta == "—" ? null : t.Meta,
            isDone = t.IsDone,
        };

        // ================= 模块5 · 次日计划 =================
        public static async Task<long> AddPlanAsync(long logId, PlanItem p)
        {
            var id = await ApiService.PostAsync<IdData>($"logs/{logId}/plans", PlanBody(p));
            return id?.Id ?? 0;
        }

        public static async Task UpdatePlanAsync(long logId, PlanItem p)
        {
            await ApiService.PutAsync<object>($"logs/{logId}/plans/{p.Id}", PlanBody(p));
        }

        public static async Task DeletePlanAsync(long logId, long id)
        {
            await ApiService.DeleteAsync<object>($"logs/{logId}/plans/{id}");
        }

        private static object PlanBody(PlanItem p) => new
        {
            prio = p.Prio,
            title = p.Title,
            note = string.IsNullOrEmpty(p.Note) || p.Note == "—" ? null : p.Note,
            estHours = p.EstHours,
        };

        // ================= 模块6 · 总结素材 =================
        public static async Task<long> AddMaterialAsync(long logId, Material m)
        {
            var id = await ApiService.PostAsync<IdData>($"logs/{logId}/materials", MaterialBody(m));
            return id?.Id ?? 0;
        }

        public static async Task UpdateMaterialAsync(long logId, Material m)
        {
            await ApiService.PutAsync<object>($"logs/{logId}/materials/{m.Id}", MaterialBody(m));
        }

        public static async Task ToggleMaterialAsync(long logId, long id, bool picked)
        {
            await ApiService.PatchAsync<object>($"logs/{logId}/materials/{id}/pick", new { isPicked = picked });
        }

        public static async Task DeleteMaterialAsync(long logId, long id)
        {
            await ApiService.DeleteAsync<object>($"logs/{logId}/materials/{id}");
        }

        private static object MaterialBody(Material m) => new
        {
            category = m.Category,
            icon = m.Icon,
            dateLabel = string.IsNullOrEmpty(m.DateLabel) ? null : m.DateLabel,
            content = m.Content,
            isPicked = m.IsPicked,
        };

        // ================= 大模型配置 =================
        public static async Task<List<LlmConfig>> GetLlmConfigsAsync(long userId)
        {
            return await ApiService.GetAsync<List<LlmConfig>>("ai/llm-configs") ?? new List<LlmConfig>();
        }

        public static async Task<long> SaveLlmConfigAsync(long userId, LlmConfig c)
        {
            if (c.Id > 0)
            {
                var body = new Dictionary<string, object?>
                {
                    ["name"] = c.Name,
                    ["baseUrl"] = c.BaseUrl,
                    ["modelName"] = c.ModelName,
                };
                if (!string.IsNullOrWhiteSpace(c.ApiKey))
                    body["apiKey"] = c.ApiKey;
                await ApiService.PutAsync<object>($"ai/llm-configs/{c.Id}", body);
                return c.Id;
            }

            var id = await ApiService.PostAsync<IdData>("ai/llm-configs", new
            {
                name = c.Name,
                baseUrl = c.BaseUrl,
                apiKey = c.ApiKey,
                modelName = c.ModelName,
                isDefault = c.IsDefault,
            });
            return id?.Id ?? 0;
        }

        public static async Task SetDefaultLlmAsync(long userId, long id)
        {
            await ApiService.PutAsync<object>($"ai/llm-configs/{id}/default", null);
        }

        public static async Task DeleteLlmConfigAsync(long userId, long id)
        {
            await ApiService.DeleteAsync<object>($"ai/llm-configs/{id}");
        }

        // ================= AI 对话记录 =================
        /// <summary>聊天消息由后端在对话接口中自动落库，本地不再直写。</summary>
        public static async Task SaveChatMessageAsync(long userId, string role, string content, string? model)
        {
            await Task.CompletedTask;
        }

        public static async Task<List<ChatMessageItem>> GetChatHistoryAsync(long userId, int limit = 200)
        {
            return await ApiService.GetAsync<List<ChatMessageItem>>($"ai/history?limit={limit}") ?? new List<ChatMessageItem>();
        }

        public static async Task ClearChatAsync(long userId)
        {
            await ApiService.DeleteAsync<object>("ai/history");
        }

        // ---------------- 内部 DTO ----------------
        private sealed class IdData
        {
            public long Id { get; set; }
        }

        private sealed class DimLevelData
        {
            public string? DimKey { get; set; }
            public int Level { get; set; }
        }
    }
}