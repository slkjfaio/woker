using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using woker.Models;
using woker.Services;

namespace woker.Views
{
    public sealed partial class DashboardPage : Page
    {
        private DailyLog Log => SessionService.CurrentLog ?? new DailyLog();

        public DashboardPage()
        {
            InitializeComponent();
            Loaded += async (s, e) => await ReloadAsync();
        }

        public async Task ReloadAsync()
        {
            try
            {
                if (SessionService.CurrentUser == null || SessionService.CurrentProject == null)
                {
                    Toast.Show("未选择项目");
                    return;
                }
                var log = await LogRepository.GetOrCreateLogAsync(
                    SessionService.CurrentUser.Id,
                    SessionService.CurrentProject.Id,
                    DateTime.Today);
                SessionService.CurrentLog = log;
            }
            catch (Exception ex)
            {
                Toast.Show("数据加载失败：" + ex.Message);
            }
            Render();
        }

        private void Render()
        {
            var tasks = Log.Tasks;
            TaskList.ItemsSource = null;
            TaskList.ItemsSource = tasks;
            EmptyState.Visibility = tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var s = CalcStats(Log);
            SExec.Text = s.exec.ToString();
            SPass.Text = s.pass.ToString();
            SBug.Text = s.bugs.ToString();
            SRate.Text = s.rate + "%";
            SHours.Text = s.hours + "h";
        }

        public static (int exec, int pass, int bugs, int hours, int rate) CalcStats(DailyLog log)
        {
            var exec = log.Tasks.Sum(t => t.ExecCount);
            var pass = log.Tasks.Sum(t => t.PassCount);
            var bugs = log.Tasks.Sum(t => t.BugCount);
            var hours = (int)Math.Round(log.Tasks.Sum(t => t.CostHours));
            var done = log.Tasks.Count(t => t.IsDone);
            var effExec = exec > 0 ? exec : done;
            var effPass = pass > 0 ? pass : done;
            var rate = effExec > 0 ? (int)Math.Round(effPass * 100.0 / effExec) : 0;
            return (effExec, effPass, bugs, hours, rate);
        }

        // ---------------- 新增成果 ----------------
        private async void OnAddTask(object sender, RoutedEventArgs e)
        {
            var title = new TextBox { PlaceholderText = "例如：支付模块回归用例执行" };
            var result = new TextBox
            {
                PlaceholderText = "例如：执行 48 条 · 通过 46 · 新增缺陷 2 个",
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Height = 68,
            };
            var type = new ComboBox
            {
                ItemsSource = new[] { "用例执行", "用例设计", "缺陷验证", "接口联调", "性能脚本", "文档输出", "其他" },
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            var cost = new NumberBox { Value = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, SmallChange = 0.5 };
            var exec = new NumberBox { Value = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            var pass = new NumberBox { Value = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            var bugs = new NumberBox { Value = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };

            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(Field("任务 / 产出 *", title));
            panel.Children.Add(Field("量化结果 *", result));
            panel.Children.Add(Field("工作类型", type));
            panel.Children.Add(BuildNumRow(("耗时（小时）", cost), ("执行用例", exec), ("通过用例", pass), ("发现缺陷", bugs)));

            var dlg = new ContentDialog
            {
                Title = "新增今日成果",
                Content = new ScrollViewer { Content = panel, MaxHeight = 480 },
                PrimaryButtonText = "保存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            if (string.IsNullOrWhiteSpace(title.Text) || string.IsNullOrWhiteSpace(result.Text))
            {
                Toast.Show("请填写任务与量化结果");
                return;
            }

            var task = new WorkTask
            {
                Title = title.Text.Trim(),
                Result = result.Text.Trim(),
                Type = type.SelectedItem as string ?? "其他",
                CostHours = Math.Max(0, cost.Value),
                ExecCount = Math.Max(0, (int)exec.Value),
                PassCount = Math.Max(0, (int)pass.Value),
                BugCount = Math.Max(0, (int)bugs.Value),
            };
            try
            {
                await LogRepository.AddTaskAsync(Log.Id, task);
                Toast.Show("成果已记录");
                await ReloadAsync();
            }
            catch (Exception ex) { Toast.Show("保存失败：" + ex.Message); }
        }

        // ---------------- 勾选 / 删除 ----------------
        private async void OnTaskToggle(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.DataContext is not WorkTask t) return;
            var done = cb.IsChecked == true;
            if (t.IsDone == done) return;
            t.IsDone = done;
            try { await LogRepository.ToggleTaskAsync(Log.Id, t.Id, done); }
            catch (Exception ex) { Toast.Show("更新失败：" + ex.Message); }
            Render();
        }

        private async void OnDeleteTask(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not long id) return;
            try
            {
                await LogRepository.DeleteTaskAsync(Log.Id, id);
                Toast.Show("已删除该成果");
                await ReloadAsync();
            }
            catch (Exception ex) { Toast.Show("删除失败：" + ex.Message); }
        }

        // ---------------- 表单工具 ----------------
        internal static StackPanel Field(string label, FrameworkElement control) => new()
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = label, FontSize = 12, Opacity = 0.8 },
                control,
            },
        };

        internal static Grid BuildNumRow(params (string label, NumberBox box)[] items)
        {
            var grid = new Grid { ColumnSpacing = 10 };
            for (var i = 0; i < items.Length; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var panel = Field(items[i].label, items[i].box);
                Grid.SetColumn(panel, i);
                grid.Children.Add(panel);
            }
            return grid;
        }
    }
}