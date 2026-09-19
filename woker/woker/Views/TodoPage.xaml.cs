using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using woker.Models;
using woker.Services;

namespace woker.Views
{
    public sealed partial class TodoPage : Page
    {
        public static readonly (string Key, string Name, string Icon)[] Cats =
        {
            ("pending", "未完成任务", "\uE73A"),
            ("retest", "待复测缺陷", "\uE7BA"),
            ("doc", "待输出文档", "\uE8A5"),
            ("connect", "待对接事项", "\uE8BD"),
        };

        private DailyLog Log => SessionService.CurrentLog ?? new DailyLog();
        private string _tab = "pending";

        public TodoPage()
        {
            InitializeComponent();
            foreach (var (key, name, icon) in Cats)
            {
                CatBar.Items.Add(new SelectorBarItem { Text = name, Tag = key });
            }
            CatBar.SelectedItem = CatBar.Items[0];
            Loaded += async (s, e) => await ReloadAsync();
        }

        public static string CatName(string? key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            return Cats.FirstOrDefault(c => c.Key == key).Name ?? key;
        }
        public static string PrioName(string key) => key switch
        {
            "high" => "紧急",
            "medium" => "中",
            _ => "低",
        };
        public static Microsoft.UI.Xaml.Visibility MetaVisible(string meta) =>
            string.IsNullOrWhiteSpace(meta) || meta == "—"
                ? Microsoft.UI.Xaml.Visibility.Collapsed
                : Microsoft.UI.Xaml.Visibility.Visible;

        private async Task ReloadAsync()
        {
            try
            {
                if (SessionService.CurrentUser == null || SessionService.CurrentProject == null) return;
                var log = await LogRepository.GetOrCreateLogAsync(
                    SessionService.CurrentUser.Id,
                    SessionService.CurrentProject.Id,
                    DateTime.Today);
                SessionService.CurrentLog = log;
            }
            catch (Exception ex) { Toast.Show("数据加载失败：" + ex.Message); }
            Render();
        }

        private void OnCatChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            if (sender.SelectedItem?.Tag is string key) _tab = key;
            Render();
        }

        private void Render()
        {
            var items = Log.Todos.Where(t => t.Category == _tab).ToList();
            TodoList.ItemsSource = null;
            TodoList.ItemsSource = items;

            // 分类徽标
            for (var i = 0; i < CatBar.Items.Count; i++)
            {
                var key = CatBar.Items[i].Tag as string ?? "";
                var count = Log.Todos.Count(t => t.Category == key && !t.IsDone);
                CatBar.Items[i].Text = $"{CatName(key)} ({count})";
            }

            FootTotal.Text = $"共 {items.Count} 项";
            FootUndone.Text = $"待处理 {items.Count(t => !t.IsDone)} 项";
        }

        private async void OnAddTodo(object sender, RoutedEventArgs e)
        {
            var title = new TextBox { PlaceholderText = "具体、可执行的待办描述" };
            var priority = new ComboBox
            {
                ItemsSource = new[] { "紧急", "中等", "低" },
                SelectedIndex = 1,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            var deadline = new TextBox { PlaceholderText = "例如：次日 / 2 天后 / 本周内" };
            var meta = new TextBox { PlaceholderText = "如 BUG-xxxx / 1.5h（可留空）" };

            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(DashboardPage.Field("待办事项 *", title));
            panel.Children.Add(DashboardPage.Field("优先级", priority));
            panel.Children.Add(DashboardPage.Field("截止", deadline));
            panel.Children.Add(DashboardPage.Field("补充信息", meta));

            var dlg = new ContentDialog
            {
                Title = "新增待办 · " + CatName(_tab),
                Content = panel,
                PrimaryButtonText = "保存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            if (string.IsNullOrWhiteSpace(title.Text)) { Toast.Show("请填写待办事项"); return; }

            try
            {
                await LogRepository.AddTodoAsync(Log.Id, new TodoItem
                {
                    Category = _tab,
                    Title = title.Text.Trim(),
                    Priority = new[] { "high", "medium", "low" }[priority.SelectedIndex],
                    Deadline = string.IsNullOrWhiteSpace(deadline.Text) ? "—" : deadline.Text.Trim(),
                    Meta = string.IsNullOrWhiteSpace(meta.Text) ? "—" : meta.Text.Trim(),
                });
                Toast.Show("待办已加入 · 闭环可控");
                await ReloadAsync();
            }
            catch (Exception ex) { Toast.Show("保存失败：" + ex.Message); }
        }

        private async void OnTodoToggle(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.DataContext is not TodoItem t) return;
            var done = cb.IsChecked == true;
            if (t.IsDone == done) return;
            t.IsDone = done;
            try
            {
                await LogRepository.ToggleTodoAsync(Log.Id, t.Id, done);
                Render();
            }
            catch (Exception ex) { Toast.Show("更新失败：" + ex.Message); }
        }

        private async void OnDeleteTodo(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not long id) return;
            try
            {
                await LogRepository.DeleteTodoAsync(Log.Id, id);
                Toast.Show("待办已删除");
                await ReloadAsync();
            }
            catch (Exception ex) { Toast.Show("删除失败：" + ex.Message); }
        }
    }
}
