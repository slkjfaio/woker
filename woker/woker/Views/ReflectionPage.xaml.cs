using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using woker.Models;
using woker.Services;

namespace woker.Views
{
    public sealed partial class ReflectionPage : Page
    {
        private DailyLog Log => SessionService.CurrentLog ?? new DailyLog();

        public ReflectionPage()
        {
            InitializeComponent();
            Loaded += async (s, e) => await ReloadAsync();
        }

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
            ReflectList.ItemsSource = null;
            ReflectList.ItemsSource = Log.Reflections;
            EmptyState.Visibility = Log.Reflections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void OnAddReflection(object sender, RoutedEventArgs e)
        {
            var fields = BuildForm(new Reflection());
            var dlg = new ContentDialog
            {
                Title = "新增问题复盘",
                Content = new ScrollViewer { Content = fields, MaxHeight = 520 },
                PrimaryButtonText = "保存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            var (item, error) = ReadForm(fields, null);
            if (item == null) { Toast.Show(error); return; }
            try
            {
                item.TimeStr = DateTime.Now.ToString("HH:mm");
                await LogRepository.AddReflectionAsync(Log.Id, item);
                Toast.Show("复盘已沉淀");
                await ReloadAsync();
            }
            catch (Exception ex) { Toast.Show("保存失败：" + ex.Message); }
        }

        private async void OnEditReflection(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not long id) return;
            var item = Log.Reflections.FirstOrDefault(r => r.Id == id);
            if (item == null) return;

            var fields = BuildForm(item);
            var dlg = new ContentDialog
            {
                Title = "编辑问题复盘",
                Content = new ScrollViewer { Content = fields, MaxHeight = 520 },
                PrimaryButtonText = "保存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            var (updated, error) = ReadForm(fields, item);
            if (updated == null) { Toast.Show(error); return; }
            try
            {
                await LogRepository.UpdateReflectionAsync(Log.Id, updated);
                Toast.Show("复盘已更新");
                await ReloadAsync();
            }
            catch (Exception ex) { Toast.Show("保存失败：" + ex.Message); }
        }

        private async void OnDeleteReflection(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not long id) return;
            try
            {
                await LogRepository.DeleteReflectionAsync(Log.Id, id);
                Toast.Show("复盘已删除");
                await ReloadAsync();
            }
            catch (Exception ex) { Toast.Show("删除失败：" + ex.Message); }
        }

        // ---------------- 表单 ----------------
        private static StackPanel BuildForm(Reflection r)
        {
            var title = new TextBox { PlaceholderText = "例如：支付回调偶现超时致订单状态未同步", Text = r.Title };
            var category = new ComboBox
            {
                ItemsSource = new[] { "接口稳定性", "测试框架", "环境问题", "用例设计", "其他" },
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            category.SelectedItem = category.Items.Contains(r.Category) ? r.Category : "其他";
            var s1 = MultiLine(r.Phenomenon, "描述现象、影响范围、出现频率");
            var s2 = MultiLine(r.Investigation, "复现手段、定位步骤、根因");
            var s3 = MultiLine(r.Solution, "已实施/计划采取的措施");
            var s4 = MultiLine(r.Prevention, "沉淀的规范、自动化、预防机制");

            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(DashboardPage.Field("问题标题 *", title));
            panel.Children.Add(DashboardPage.Field("分类", category));
            panel.Children.Add(DashboardPage.Field("问题现象 *", s1));
            panel.Children.Add(DashboardPage.Field("排查过程", s2));
            panel.Children.Add(DashboardPage.Field("落地方案", s3));
            panel.Children.Add(DashboardPage.Field("优化规避", s4));
            panel.Tag = (title, category, s1, s2, s3, s4);
            return panel;
        }

        private static (Reflection? item, string error) ReadForm(StackPanel panel, Reflection? existing)
        {
            var (title, category, s1, s2, s3, s4) =
                ((TextBox, ComboBox, TextBox, TextBox, TextBox, TextBox))panel.Tag!;
            if (string.IsNullOrWhiteSpace(title.Text) || string.IsNullOrWhiteSpace(s1.Text))
                return (null, "请填写问题标题与现象");

            var item = existing ?? new Reflection();
            item.Title = title.Text.Trim();
            item.Category = category.SelectedItem as string ?? "其他";
            item.Phenomenon = s1.Text.Trim();
            item.Investigation = string.IsNullOrWhiteSpace(s2.Text) ? "—" : s2.Text.Trim();
            item.Solution = string.IsNullOrWhiteSpace(s3.Text) ? "—" : s3.Text.Trim();
            item.Prevention = string.IsNullOrWhiteSpace(s4.Text) ? "—" : s4.Text.Trim();
            return (item, "");
        }

        private static TextBox MultiLine(string text, string placeholder) => new()
        {
            Text = text == "—" ? "" : text,
            PlaceholderText = placeholder,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Height = 64,
        };
    }
}