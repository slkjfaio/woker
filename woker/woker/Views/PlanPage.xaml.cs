using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using woker.Models;
using woker.Services;

namespace woker.Views
{
    public sealed partial class PlanPage : Page
    {
        public static readonly string[] PrioNames = { "紧急优先", "常规迭代", "能力提升" };

        private DailyLog Log => SessionService.CurrentLog ?? new DailyLog();

        public PlanPage()
        {
            InitializeComponent();
            Loaded += async (s, e) => await ReloadAsync();
        }

        public static string PrioLabel(int prio) =>
            prio >= 0 && prio < PrioNames.Length ? PrioNames[prio] : "常规迭代";

        public static Microsoft.UI.Xaml.Visibility NoteVisible(string note) =>
            string.IsNullOrWhiteSpace(note) || note == "—"
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

        private void Render()
        {
            var lists = new[] { List0, List1, List2 };
            var counts = new[] { Count0, Count1, Count2 };
            for (var p = 0; p < 3; p++)
            {
                var items = Log.Plans.Where(x => x.Prio == p).ToList();
                lists[p].ItemsSource = null;
                lists[p].ItemsSource = items;
                var hours = items.Sum(i => i.EstHours);
                counts[p].Text = $"{items.Count} 项 · {hours:0.#}h";
            }
        }

        private void OnAddPlanTop(object sender, RoutedEventArgs e) => ShowAddDialog(0);
        private void OnAddPlan0(object sender, RoutedEventArgs e) => ShowAddDialog(0);
        private void OnAddPlan1(object sender, RoutedEventArgs e) => ShowAddDialog(1);
        private void OnAddPlan2(object sender, RoutedEventArgs e) => ShowAddDialog(2);

        private async void ShowAddDialog(int prio)
        {
            var title = new TextBox { PlaceholderText = "次日计划做的事项" };
            var note = new TextBox { PlaceholderText = "补充说明 / 关联项" };
            var est = new NumberBox { Value = 1, SmallChange = 0.5, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };

            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(DashboardPage.Field("计划事项 *", title));
            panel.Children.Add(DashboardPage.Field("备注", note));
            panel.Children.Add(DashboardPage.Field("预估耗时（小时）", est));

            var dlg = new ContentDialog
            {
                Title = "新增计划 · " + PrioLabel(prio),
                Content = panel,
                PrimaryButtonText = "保存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            if (string.IsNullOrWhiteSpace(title.Text)) { Toast.Show("请填写计划事项"); return; }

            try
            {
                await LogRepository.AddPlanAsync(Log.Id, new PlanItem
                {
                    Prio = prio,
                    Title = title.Text.Trim(),
                    Note = string.IsNullOrWhiteSpace(note.Text) ? "—" : note.Text.Trim(),
                    EstHours = Math.Max(0.5, est.Value),
                });
                Toast.Show($"计划已排入「{PrioLabel(prio)}」");
                await ReloadAsync();
            }
            catch (Exception ex) { Toast.Show("保存失败：" + ex.Message); }
        }

        private async void OnDeletePlan(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not long id) return;
            try
            {
                await LogRepository.DeletePlanAsync(Log.Id, id);
                Toast.Show("计划已删除");
                await ReloadAsync();
            }
            catch (Exception ex) { Toast.Show("删除失败：" + ex.Message); }
        }
    }
}