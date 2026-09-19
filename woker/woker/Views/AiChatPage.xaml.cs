using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using woker.Models;
using woker.Services;

namespace woker.Views
{
    public sealed partial class AiChatPage : Page
    {
        private List<LlmConfig> _configs = new();
        private LlmConfig? _current;
        private bool _busy;
        private bool _ignoreStream;
        private TextBlock? _streamBlock;

        public AiChatPage()
        {
            InitializeComponent();
            InputBox.KeyDown += (s, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Enter && !ModifiersHasShift())
                {
                    SendAsync();
                }
            };
            Loaded += async (s, e) => await InitAsync();
        }

        private bool ModifiersHasShift() =>
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        private async Task InitAsync()
        {
            try
            {
                _configs = await LogRepository.GetLlmConfigsAsync(SessionService.CurrentUser!.Id);
            }
            catch (Exception ex) { Toast.Show("加载模型配置失败：" + ex.Message); }

            ModelBox.ItemsSource = _configs.Select(c => c.Name).ToList();
            var def = _configs.FirstOrDefault(c => c.IsDefault) ?? _configs.FirstOrDefault();
            if (def != null) ModelBox.SelectedIndex = _configs.IndexOf(def);
            else ModelBox.ItemsSource = new[] { "（未配置模型，请前往设置页）" };

            await LoadHistoryAsync();
        }

        private async Task LoadHistoryAsync()
        {
            ChatPanel.Children.Clear();
            try
            {
                var history = await LogRepository.GetChatHistoryAsync(SessionService.CurrentUser!.Id, 60);
                foreach (var m in history) AddBubble(m.Role == "user", m.Content);
            }
            catch { }
            if (ChatPanel.Children.Count == 0)
                AddBubble(false, "你好！我是 AI 助手，可以自由对话、润色日报、生成总结素材。点击「润色今日日报」可一键生成润色版日报。");
        }

        private void OnModelChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModelBox.SelectedIndex >= 0 && ModelBox.SelectedIndex < _configs.Count)
                _current = _configs[ModelBox.SelectedIndex];
        }

        // ---------------- 气泡 ----------------
        private TextBlock AddBubble(bool isUser, string text)
        {
            var tb = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
            };
            var copyBtn = new Button
            {
                Content = "复制",
                FontSize = 11,
                Padding = new Thickness(8, 3, 8, 3),
                HorizontalAlignment = HorizontalAlignment.Right,
                Opacity = 0.75,
            };
            copyBtn.Click += (_, _) => CopyBubbleText(tb);

            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(tb);
            content.Children.Add(copyBtn);

            var border = new Border
            {
                Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(10),
                MaxWidth = 780,
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Background = (Brush)Application.Current.Resources[isUser
                    ? "AccentFillColorDefaultBrush"
                    : "CardBackgroundFillColorSecondaryBrush"],
            };
            border.Child = content;
            // 用户气泡用 accent 底色上的文字色
            tb.Foreground = (Brush)Application.Current.Resources[isUser
                ? "TextOnAccentFillColorPrimaryBrush"
                : "TextFillColorPrimaryBrush"];
            ChatPanel.Children.Add(border);
            ScrollToBottom();
            return tb;
        }

        private void CopyBubbleText(TextBlock tb)
        {
            try
            {
                var dataPackage = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
                dataPackage.SetText(tb.Text);
                Clipboard.SetContent(dataPackage);
                Toast.Show("已复制到剪贴板");
            }
            catch
            {
                Toast.Show("复制失败，请重试");
            }
        }

        private void ScrollToBottom()
        {
            ChatScroll.UpdateLayout();
            ChatScroll.ChangeView(null, ChatScroll.ScrollableHeight, null, disableAnimation: true);
        }

        // ---------------- 发送 ----------------
        private async void OnSend(object sender, RoutedEventArgs e) => SendAsync();

        private async void SendAsync()
        {
            var text = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(text) || _busy) return;
            if (_current == null)
            {
                Toast.Show("请先在「设置 → 大模型配置」添加并选择模型");
                return;
            }

            InputBox.Text = "";
            AddBubble(true, text);
            SetBusy(true);
            _ignoreStream = false;
            _streamBlock = AddBubble(false, "…");

            try
            {
                // 聊天消息由后端对话接口自动落库，无需客户端直写

                // 携带最近 20 条上下文，再追加当前用户消息（后端在 stream 开始时才落库 user 消息，此时历史中尚无本轮提问）
                var currentUser = SessionService.CurrentUser;
                if (currentUser == null)
                {
                    _streamBlock.Text = "登录已过期，请重新登录";
                    return;
                }
                var history = await LogRepository.GetChatHistoryAsync(currentUser.Id, 20);
                var msgs = new List<LlmService.ChatMsg> { new("system", "你是一名专业的工作日志助手，帮助测试工程师记录、总结与润色工作内容。回答使用简体中文。") };
                foreach (var h in history.TakeLast(20))
                    msgs.Add(new LlmService.ChatMsg(h.Role, h.Content));
                msgs.Add(new LlmService.ChatMsg("user", text));

                var sb = new StringBuilder();
                var (reply, error) = await LlmService.ChatStreamAsync(_current, msgs, delta =>
                {
                    if (_ignoreStream) return;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        sb.Append(delta);
                        if (_streamBlock != null)
                        {
                            _streamBlock.Text = sb.ToString();
                            ScrollToBottom();
                        }
                    });
                });

                if (_ignoreStream) return;
                if (!string.IsNullOrEmpty(error) && string.IsNullOrEmpty(reply))
                {
                    _streamBlock!.Text = $"调用失败：{error}";
                    Toast.Show("AI 调用失败");
                }
                else
                {
                    _streamBlock!.Text = reply.Length > 0 ? reply : "(空回复)";

                    ScrollToBottom();
                }
            }
            catch (Exception ex)
            {
                _streamBlock!.Text = "调用失败：" + ex.Message;
            }
            finally
            {
                _streamBlock = null;
                SetBusy(false);
            }
        }

        // ---------------- 快捷：润色今日日报 ----------------
        private async void OnPolishDaily(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            var log = SessionService.CurrentLog;
            if (log == null) { Toast.Show("暂无今日日志数据"); return; }

            var brief = BuildBrief(log);
            InputBox.Text = $"请帮我润色整理今日工作日报，输出结构清晰的 Markdown 版本：\n\n{brief}";
            SendAsync();
        }

        private static string BuildBrief(DailyLog log)
        {
            var s = DashboardPage.CalcStats(log);
            var projectName = SessionService.CurrentProject?.Name ?? "未指定项目";
            var md = new StringBuilder();
            md.AppendLine($"负责项目：{projectName}，日期：{DateTime.Today:yyyy-MM-dd}");
            md.AppendLine();
            md.AppendLine("## 今日工作成果");
            foreach (var t in log.Tasks)
                md.AppendLine($"- [{(t.IsDone ? "已完成" : "进行中")}] {t.Title}（{t.Type}，{t.CostHours}h）：{t.Result}");
            if (log.Tasks.Count == 0) md.AppendLine("- （无）");
            md.AppendLine();
            md.AppendLine("## 问题复盘");
            foreach (var r in log.Reflections)
            {
                md.AppendLine($"- {r.Title}：现象={r.Phenomenon}；方案={r.Solution}");
            }
            if (log.Reflections.Count == 0) md.AppendLine("- （无）");
            md.AppendLine();
            md.AppendLine("## 能力成长");
            foreach (var g in log.Growth) md.AppendLine($"- 【{GrowthPage.DimName(g.DimKey)}】{g.Content}");
            if (log.Growth.Count == 0) md.AppendLine("- （无）");
            md.AppendLine();
            md.AppendLine($"量化：执行 {s.exec} 条 / 通过 {s.pass} 条 / 缺陷 {s.bugs} 个 / 回归率 {s.rate}% / 工时 {s.hours}h");
            return md.ToString();
        }

        // ---------------- 清空 ----------------
        private async void OnClearChat(object sender, RoutedEventArgs e)
        {
            var dlg = new ContentDialog
            {
                Title = "清空对话",
                Content = "确定清空所有 AI 对话记录吗？该操作不可恢复。",
                PrimaryButtonText = "清空",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            _ignoreStream = true;
            try { await LogRepository.ClearChatAsync(SessionService.CurrentUser!.Id); }
            catch (Exception ex) { Toast.Show("清空失败：" + ex.Message); }
            ChatPanel.Children.Clear();
            Toast.Show("对话已清空");
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            SendBtn.IsEnabled = !busy;
            SendText.Text = busy ? "生成中…" : "发送";
            SendIcon.Glyph = busy ? "\uE9F5" : "\uE724";
        }
    }
}
