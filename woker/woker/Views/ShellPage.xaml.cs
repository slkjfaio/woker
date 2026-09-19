using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using woker.Models;
using woker.Services;

namespace woker.Views
{
    public sealed partial class ShellPage : Page
    {
        private static readonly string[] WeekCn = { "日", "一", "二", "三", "四", "五", "六" };
        private DispatcherTimer? _toastTimer;
        private List<Project> _projects = new();

        public ShellPage()
        {
            InitializeComponent();
            Loaded += OnShellLoaded;
            Toast.Requested += OnToast;
        }

        private async void OnShellLoaded(object sender, RoutedEventArgs e)
        {
            await InitHeaderAsync();
            Nav.SelectedItem = Nav.MenuItems[0];
        }

        // ---------------- 顶部信息 ----------------
        private async Task InitHeaderAsync()
        {
            var user = SessionService.CurrentUser;
            if (user == null) return;

            var name = user.DisplayName;
            UserNameText.Text = name;
            AvatarText.Text = name.Length > 0 ? name[..1] : "测";
            UserInfoItem.Text = $"{name} · {user.Email ?? user.Username ?? "—"}";

            var today = DateTime.Today;
            ChipDate.Text = $"{today:yyyy-MM-dd}";
            ChipWeek.Text = $"星期{WeekCn[(int)today.DayOfWeek]} · 第{IsoWeek(today)}周";

            // 加载项目列表
            await LoadProjectsAsync();

            // 主题
            var saved = LocalStore.GetString("theme");
            SessionService.Theme = saved == "Dark" ? "Dark" : "Light";
            ApplyTheme(SessionService.Theme);
        }

        private async Task LoadProjectsAsync()
        {
            if (SessionService.CurrentUser == null) return;

            _projects = await LogRepository.GetProjectsAsync(SessionService.CurrentUser.Id);

            // 如果没有项目，创建默认项目
            if (_projects.Count == 0)
            {
                var defaultProject = await LogRepository.GetOrCreateDefaultProjectAsync(SessionService.CurrentUser.Id);
                if (defaultProject != null)
                {
                    _projects.Add(defaultProject);
                }
            }

            ProjectBox.ItemsSource = _projects.Select(p => p.DisplayName).ToList();

            // 恢复上次选择的项目或使用默认项目
            var lastProjectId = SessionService.GetLastProjectId();
            var selectedProject = _projects.FirstOrDefault(p => p.Id == lastProjectId)
                                ?? _projects.FirstOrDefault(p => p.IsDefault)
                                ?? _projects.FirstOrDefault();

            if (selectedProject != null)
            {
                SessionService.SetCurrentProject(selectedProject);
                var index = _projects.IndexOf(selectedProject);
                ProjectBox.SelectedIndex = index;

                // 加载当前项目的日志
                await LoadCurrentLogAsync();
            }
        }

        private async Task LoadCurrentLogAsync()
        {
            if (SessionService.CurrentUser == null || SessionService.CurrentProject == null) return;

            try
            {
                var log = await LogRepository.GetOrCreateLogAsync(
                    SessionService.CurrentUser.Id,
                    SessionService.CurrentProject.Id,
                    DateTime.Today);
                SessionService.CurrentLog = log;
            }
            catch (Exception ex)
            {
                Toast.Show("日志加载失败：" + ex.Message);
            }
        }

        private async void OnProjectChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProjectBox.SelectedIndex < 0 || ProjectBox.SelectedIndex >= _projects.Count) return;
            if (SessionService.CurrentUser == null) return;

            var selectedProject = _projects[ProjectBox.SelectedIndex];
            if (SessionService.CurrentProject?.Id == selectedProject.Id) return;

            SessionService.SetCurrentProject(selectedProject);
            await LoadCurrentLogAsync();
            Toast.Show($"已切换到项目：{selectedProject.Name}");

            // 刷新当前页面以显示新项目的数据
            if (ContentFrame.Content is Page currentPage)
            {
                ContentFrame.Navigate(currentPage.GetType());
            }
        }

        // ---------------- 导航 ----------------
        private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                PageTitle.Text = "设置";
                PageCrumb.Text = "设置";
                ContentFrame.Navigate(typeof(SettingsPage));
                return;
            }

            if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag) return;
            Navigate(tag);
        }

        private void Navigate(string tag)
        {
            Type page = tag switch
            {
                "dashboard" => typeof(DashboardPage),
                "reflection" => typeof(ReflectionPage),
                "growth" => typeof(GrowthPage),
                "todo" => typeof(TodoPage),
                "plan" => typeof(PlanPage),
                "summary" => typeof(SummaryPage),
                "projects" => typeof(ProjectsPage),
                "ai" => typeof(AiChatPage),
                "settings" => typeof(SettingsPage),
                _ => typeof(DashboardPage),
            };
            PageTitle.Text = ((NavigationViewItem)Nav.SelectedItem).Content?.ToString() ?? "";
            PageCrumb.Text = PageTitle.Text;
            ContentFrame.Navigate(page);
        }

        // ---------------- 主题 ----------------
        private void OnToggleTheme(object sender, RoutedEventArgs e)
        {
            SessionService.Theme = SessionService.Theme == "Dark" ? "Light" : "Dark";
            LocalStore.SetString("theme", SessionService.Theme);
            ApplyTheme(SessionService.Theme);
        }

        private void ApplyTheme(string theme)
        {
            (App.MainWindow as MainWindow)?.ApplyTheme();
            ThemeIcon.Glyph = theme == "Dark" ? "\uE706" : "\uE708"; // sun / moon
        }

        // ---------------- 用户菜单 ----------------
        private void OnUserMenu(object sender, RoutedEventArgs e) => UserFlyout.ShowAt(UserBtn, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight });

        private async void OnLogout(object sender, RoutedEventArgs e)
        {
            var dlg = new ContentDialog
            {
                Title = "退出登录",
                Content = "确定要退出当前账号吗？退出后需要重新登录才能访问工作台。",
                PrimaryButtonText = "退出登录",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            SessionService.Clear();
            Toast.Requested -= OnToast;
            Frame.Navigate(typeof(LoginPage));
        }

        // ---------------- 导出 Markdown ----------------
        private async void OnExportTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            var log = SessionService.CurrentLog;
            if (log == null) { Toast.Show("暂无可导出的日志数据"); return; }

            var md = BuildMarkdown(log);
            try
            {
                var picker = new Windows.Storage.Pickers.FileSavePicker
                {
                    SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                    SuggestedFileName = $"工作日志_{DateTime.Today:yyyy-MM-dd}",
                };
                picker.FileTypeChoices.Add("Markdown 文档", new System.Collections.Generic.List<string> { ".md" });

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSaveFileAsync();
                if (file == null) return;
                await Windows.Storage.FileIO.WriteTextAsync(file, md);
                Toast.Show("已导出 Markdown，可粘贴至工作群/日报系统");
            }
            catch (Exception ex)
            {
                Toast.Show("导出失败：" + ex.Message);
            }
        }

        private static string BuildMarkdown(Models.DailyLog log)
        {
            var s = DashboardPage.CalcStats(log);
            var today = DateTime.Today;
            var projectName = SessionService.CurrentProject?.Name ?? "未指定项目";
            var md = new StringBuilder();
            md.AppendLine($"# 工作日志 {today:yyyy-MM-dd}（星期{WeekCn[(int)today.DayOfWeek]}）");
            md.AppendLine();
            md.AppendLine($"> 负责项目：{projectName} ｜ 第 {IsoWeek(today)} 周");
            md.AppendLine();
            md.AppendLine("## 一、今日工作成果");
            foreach (var t in log.Tasks)
                md.AppendLine($"- [{(t.IsDone ? "x" : " ")}] {t.Title} —— {t.Result}（{t.Type}，{t.CostHours}h）");
            md.AppendLine();
            md.AppendLine($"达成：执行 {s.exec} 条 / 通过 {s.pass} 条 / 缺陷 {s.bugs} 个 / 回归率 {s.rate}% / 工时 {s.hours}h");
            md.AppendLine();
            md.AppendLine("## 二、问题复盘");
            foreach (var r in log.Reflections)
            {
                md.AppendLine($"### {r.Title}");
                md.AppendLine($"- **问题现象**：{r.Phenomenon}");
                md.AppendLine($"- **排查过程**：{r.Investigation}");
                md.AppendLine($"- **落地方案**：{r.Solution}");
                md.AppendLine($"- **优化规避**：{r.Prevention}");
                md.AppendLine();
            }
            md.AppendLine("## 三、当日能力成长");
            foreach (var g in log.Growth)
                md.AppendLine($"- 【{GrowthPage.DimName(g.DimKey)}】{g.Content}");
            md.AppendLine();
            md.AppendLine("## 四、遗留待办");
            foreach (var t in log.Todos.FindAll(t => !t.IsDone))
                md.AppendLine($"- [{TodoPage.CatName(t.Category)}][{TodoPage.PrioName(t.Priority)}] {t.Title}（{t.Deadline}）");
            md.AppendLine();
            md.AppendLine("## 五、次日优先级计划");
            for (var p = 0; p < 3; p++)
            {
                md.AppendLine($"### {PlanPage.PrioLabel(p)}");
                foreach (var x in log.Plans.FindAll(x => x.Prio == p))
                    md.AppendLine($"- {x.Title}（{x.EstHours}h）");
            }
            md.AppendLine();
            md.AppendLine("## 六、周/转正总结素材");
            foreach (var m in log.Materials.FindAll(m => m.IsPicked))
                md.AppendLine($"- [{m.Category}] {m.Content}");
            return md.ToString();
        }

        private static int IsoWeek(DateTime d)
        {
            var cal = System.Globalization.CultureInfo.InvariantCulture.Calendar;
            return cal.GetWeekOfYear(d, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        }

        // ---------------- Toast ----------------
        private void OnToast(string msg)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ToastBar.Message = msg;
                ToastBar.IsOpen = true;
                _toastTimer?.Stop();
                _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.4) };
                _toastTimer.Tick += (s, e) =>
                {
                    ToastBar.IsOpen = false;
                    if (s is DispatcherTimer timer) timer.Stop();
                };
                _toastTimer.Start();
            });
        }
    }
}
