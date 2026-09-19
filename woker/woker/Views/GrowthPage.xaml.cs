using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using woker.Models;
using woker.Services;

namespace woker.Views
{
    public sealed partial class GrowthPage : Page
    {
        public static readonly (string Key, string Name)[] Dims =
        {
            ("business", "业务认知"),
            ("thinking", "测试思维"),
            ("debug", "问题排查"),
            ("tool", "工具技能"),
            ("comm", "沟通协作"),
        };

        private DailyLog Log => SessionService.CurrentLog ?? new DailyLog();
        private Dictionary<string, int> _levels = new();

        public GrowthPage()
        {
            InitializeComponent();
            Loaded += async (s, e) => await ReloadAsync();
        }

        public static string DimName(string key) =>
            Dims.FirstOrDefault(d => d.Key == key).Name ?? key;

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
                _levels = await LogRepository.GetDimLevelsAsync(SessionService.CurrentUser.Id);
            }
            catch (Exception ex) { Toast.Show("数据加载失败：" + ex.Message); }
            Render();
        }

        private void Render()
        {
            GrowthList.ItemsSource = null;
            GrowthList.ItemsSource = Log.Growth;
            GrowthCount.Text = Log.Growth.Count.ToString();
            GrowthWeek.Text = $"本周 +{Log.Growth.Count}";
            EmptyState.Visibility = Log.Growth.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // 五维进度条
            DimBars.Children.Clear();
            foreach (var (key, name) in Dims)
            {
                var level = _levels.TryGetValue(key, out var v) ? v : 50;
                var row = new StackPanel { Spacing = 5 };
                row.Children.Add(new Grid
                {
                    Children =
                    {
                        new TextBlock { Text = name, FontSize = 12 },
                        new TextBlock
                        {
                            Text = level.ToString(), FontSize = 12,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                        },
                    },
                });
                row.Children.Add(new ProgressBar { Maximum = 100, Value = level, Height = 6, CornerRadius = new CornerRadius(3) });
                DimBars.Children.Add(row);
            }
        }

        private async void OnAddGrowth(object sender, RoutedEventArgs e)
        {
            var dim = new ComboBox
            {
                ItemsSource = Dims.Select(d => d.Name).ToList(),
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            var text = new TextBox
            {
                PlaceholderText = "例如：首次独立使用 Grafana 看板盯队列水位，触发生效 1 个告警",
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Height = 80,
            };

            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(DashboardPage.Field("成长维度", dim));
            panel.Children.Add(DashboardPage.Field("增量成长描述 *", text));

            var dlg = new ContentDialog
            {
                Title = "记录能力成长",
                Content = panel,
                PrimaryButtonText = "保存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            if (string.IsNullOrWhiteSpace(text.Text)) { Toast.Show("请填写成长描述"); return; }

            var dimKey = Dims[dim.SelectedIndex].Key;
            try
            {
                await LogRepository.AddGrowthAsync(Log.Id, new GrowthEntry
                {
                    DimKey = dimKey,
                    TimeStr = DateTime.Now.ToString("HH:mm"),
                    Content = text.Text.Trim(),
                });
                await LogRepository.BumpDimLevelAsync(SessionService.CurrentUser!.Id, dimKey, 2);
                Toast.Show("成长已记录");
                await ReloadAsync();
            }
            catch (Exception ex) { Toast.Show("保存失败：" + ex.Message); }
        }

        private async void OnDeleteGrowth(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not long id) return;
            try
            {
                await LogRepository.DeleteGrowthAsync(Log.Id, id);
                Toast.Show("已删除该成长记录");
                await ReloadAsync();
            }
            catch (Exception ex) { Toast.Show("删除失败：" + ex.Message); }
        }
    }
}