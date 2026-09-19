using System;
using System.Collections.Generic;

using System.Linq;

namespace woker.Models
{
    public class User
    {
        public long Id { get; set; }
        public string Username { get; set; } = "";
        public string? PasswordHash { get; set; }
        public string DisplayName { get; set; } = "";
        public string? Email { get; set; }
        public DateTime? LastLoginAt { get; set; }
    }

    public class Project
    {
        public long Id { get; set; }
        public long UserId { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Color { get; set; } = "#0078D4";
        public bool IsDefault { get; set; }
        public bool IsArchived { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // UI 绑定属性
        public string DisplayName => IsDefault ? $"{Name} (默认)" : Name;
        public Microsoft.UI.Xaml.Visibility DefaultBadgeVisibility =>
            IsDefault ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    public class WorkTask
    {
        public long Id { get; set; }
        public string Title { get; set; } = "";
        public string Result { get; set; } = "";
        public string Type { get; set; } = "其他";
        public double CostHours { get; set; }
        public int ExecCount { get; set; }
        public int PassCount { get; set; }
        public int BugCount { get; set; }
        public bool IsDone { get; set; }
        public int SortOrder { get; set; }
    }

    public class Reflection
    {
        public long Id { get; set; }
        public string Title { get; set; } = "";
        public string Category { get; set; } = "其他";
        public string TimeStr { get; set; } = "";
        public string Phenomenon { get; set; } = "";
        public string Investigation { get; set; } = "";
        public string Solution { get; set; } = "";
        public string Prevention { get; set; } = "";
        public int SortOrder { get; set; }
    }

    public class GrowthEntry
    {
        public long Id { get; set; }
        public string DimKey { get; set; } = "business";
        public string DimName => DimKey switch
        {
            "business" => "业务认知",
            "thinking" => "测试思维",
            "debug" => "问题排查",
            "tool" => "工具技能",
            "comm" => "沟通协作",
            _ => DimKey,
        };
        public string TimeStr { get; set; } = "";
        public string Content { get; set; } = "";
        public int SortOrder { get; set; }
    }

    public class DimLevel
    {
        public string DimKey { get; set; } = "";
        public int Level { get; set; }
    }

    public class TodoItem
    {
        public long Id { get; set; }
        public string Category { get; set; } = "pending"; // pending/retest/doc/connect
        public string Title { get; set; } = "";
        public string Priority { get; set; } = "medium";
        public string PriorityText => Priority switch
        {
            "high" => "紧急",
            "medium" => "中",
            _ => "低",
        };
        public string Deadline { get; set; } = "—";
        public string Meta { get; set; } = "—";
        public Microsoft.UI.Xaml.Visibility MetaVisibility =>
            string.IsNullOrWhiteSpace(Meta) || Meta == "—"
                ? Microsoft.UI.Xaml.Visibility.Collapsed
                : Microsoft.UI.Xaml.Visibility.Visible;
        public bool IsDone { get; set; }
        public int SortOrder { get; set; }
    }

    public class PlanItem
    {
        public long Id { get; set; }
        public int Prio { get; set; } // 0 紧急优先 / 1 常规迭代 / 2 能力提升
        public string Title { get; set; } = "";
        public string Note { get; set; } = "—";
        public Microsoft.UI.Xaml.Visibility NoteVisibility =>
            string.IsNullOrWhiteSpace(Note) || Note == "—"
                ? Microsoft.UI.Xaml.Visibility.Collapsed
                : Microsoft.UI.Xaml.Visibility.Visible;
        public double EstHours { get; set; } = 1;
        public int SortOrder { get; set; }
    }

    public class Material
    {
        public long Id { get; set; }
        public string Category { get; set; } = "核心成果"; // 核心成果/问题改进/工作亮点
        public string Icon { get; set; } = "target";
        public string CategoryIcon => Category switch
        {
            "核心成果" => "\uEC47",
            "问题改进" => "\uE7BA",
            _ => "\uE945",
        };
        public string DateLabel { get; set; } = "本日";
        public string Content { get; set; } = "";
        public bool IsPicked { get; set; }
        public string PickText => IsPicked ? "已纳入" : "纳入素材";
        public int SortOrder { get; set; }
    }

    /// <summary>某用户某天的完整台账（聚合模型）</summary>
    public class DailyLog
    {
        public long Id { get; set; }
        public long UserId { get; set; }
        public long ProjectId { get; set; }
        public DateTime LogDate { get; set; }
        public List<WorkTask> Tasks { get; set; } = new();
        public List<Reflection> Reflections { get; set; } = new();
        public List<GrowthEntry> Growth { get; set; } = new();
        public List<TodoItem> Todos { get; set; } = new();
        public List<PlanItem> Plans { get; set; } = new();
        public List<Material> Materials { get; set; } = new();
    }

    public class LlmConfig
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string ModelName { get; set; } = "";
        public bool IsDefault { get; set; }
        public Microsoft.UI.Xaml.Visibility DefaultVisibility =>
            IsDefault ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    public class ChatMessageItem
    {
        public long Id { get; set; }
        public string Role { get; set; } = "user"; // user / assistant
        public string Content { get; set; } = "";
        public string? ModelName { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>应用更新日志条目（版本 + 日期 + 变更列表）</summary>
    public class UpdateLogEntry
    {
        public string Version { get; set; } = "";
        public string Date { get; set; } = "";
        public string Title { get; set; } = "";
        public List<string> Changes { get; set; } = new();
        public string ChangesText => string.Join("\n", Changes.Select(c => "· " + c));
    }
}
