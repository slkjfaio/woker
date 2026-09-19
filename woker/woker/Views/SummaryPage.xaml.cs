using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using woker.Models;
using woker.Services;

namespace woker.Views
{
    public sealed partial class SummaryPage : Page
    {
        private DailyLog Log => SessionService.CurrentLog ?? new DailyLog();
        private bool _busy;

        public SummaryPage()
        {
            InitializeComponent();
            Loaded += async (s, e) => await ReloadAsync();
        }

        public static string CatIcon(string cat) => cat switch
        {
            "核心成果" => "\uEC47",
            "问题改进" => "\uE7BA",
            _ => "\uE945",
        };

        public static string PickText(bool picked) => picked ? "已纳入" : "纳入素材";

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
            MatList.ItemsSource = null;
            MatList.ItemsSource = Log.Materials;
            MatCount.Text = $"已收录 {Log.Materials.Count(m => m.IsPicked)} 条";
        }

        private System.Collections.Generic.List<Material> Picked() =>
            Log.Materials.Where(m => m.IsPicked).ToList();

        // ---------------- 素材操作 ----------------
        private async void OnAddMaterial(object sender, RoutedEventArgs e)
        {
            var category = new ComboBox
            {
                ItemsSource = new[] { "核心成果", "问题改进", "工作亮点" },
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            var content = new TextBox
            {
                PlaceholderText = "例如：累计执行用例 60 条、发现缺陷 3 个，保障 2.4.1 版本按期提测。",
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Height = 80,
            };

            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(DashboardPage.Field("素材分类", category));
            panel.Children.Add(DashboardPage.Field("素材内容 *", content));

            var dlg = new ContentDialog
            {
                Title = "新增总结素材",
                Content = panel,
                PrimaryButtonText = "保存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            if (string.IsNullOrWhiteSpace(content.Text)) { Toast.Show("请填写素材内容"); return; }

            try
            {
                await LogRepository.AddMaterialAsync(Log.Id, new Material
                {
                    Category = category.SelectedItem as string ?? "核心成果",
                    Content = content.Text.Trim(),
                    DateLabel = "本日",
                });
                Toast.Show("素材已收录");
                await ReloadAsync();
            }
            catch (Exception ex) { Toast.Show("保存失败：" + ex.Message); }
        }

        private async void OnTogglePick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not long id) return;
            var m = Log.Materials.FirstOrDefault(x => x.Id == id);
            if (m == null) return;
            m.IsPicked = !m.IsPicked;
            try
            {
                await LogRepository.ToggleMaterialAsync(Log.Id, id, m.IsPicked);
                Toast.Show(m.IsPicked ? "素材已纳入" : "已移除素材");
                Render();
            }
            catch (Exception ex) { Toast.Show("更新失败：" + ex.Message); }
        }

        private async void OnDeleteMaterial(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not long id) return;
            try
            {
                await LogRepository.DeleteMaterialAsync(Log.Id, id);
                Toast.Show("素材已删除");
                await ReloadAsync();
            }
            catch (Exception ex) { Toast.Show("删除失败：" + ex.Message); }
        }

        // ---------------- 生成总结 ----------------
        private async void OnGenWeek(object sender, RoutedEventArgs e) => GenerateAsync(isWeek: true);

        private async void OnGenTransfer(object sender, RoutedEventArgs e) => GenerateAsync(isWeek: false);

        private async void GenerateAsync(bool isWeek)
        {
            if (!Picked().Any()) { Toast.Show("请先在素材库勾选素材"); return; }
            if (_busy) return;

            var localMd = isWeek ? BuildWeekLocal() : BuildTransferLocal();
            PreviewTitle.Text = isWeek ? "本周工作周报（草稿）" : "转正述职（草稿）";
            PreviewBox.Text = localMd;

            if (AiPolishCheck.IsChecked != true)
            {
                Toast.Show(isWeek ? "周报草稿已生成（本地模板）" : "转正述职草稿已生成（本地模板）");
                return;
            }

            // AI 润色
            var cfg = await GetDefaultConfigAsync();
            if (cfg == null)
            {
                Toast.Show("未配置大模型，已使用本地模板。请到「设置 → 大模型配置」添加。");
                return;
            }

            _busy = true;
            PreviewBox.Text = localMd + "\n\n[AI 润色中，请稍候…]";
            try
            {
                var sys = "你是一名资深 QA 测试工程师的工作汇报助手。请把用户提供的素材整理成结构清晰、突出量化结果的正式中文工作文档。" +
                          "使用 Markdown 格式，保留用户提供的所有数字与事实，不要编造数据。输出仅包含正文。";
                var msgs = new System.Collections.Generic.List<LlmService.ChatMsg>
                {
                    new("system", sys),
                    new("user", $"请基于以下素材生成一份「{(isWeek ? "本周工作周报" : "试用期转正述职报告")}」：\n\n{localMd}"),
                };
                var (reply, error) = await LlmService.ChatAsync(cfg, msgs);
                if (!string.IsNullOrEmpty(error))
                {
                    Toast.Show("AI 生成失败，已保留本地模板：" + error);
                    PreviewBox.Text = localMd;
                }
                else
                {
                    PreviewBox.Text = reply;
                    Toast.Show(isWeek ? "周报已由 AI 生成" : "转正述职已由 AI 生成");
                }
            }
            catch (Exception ex)
            {
                Toast.Show("AI 调用失败：" + ex.Message);
                PreviewBox.Text = localMd;
            }
            finally { _busy = false; }
        }

        private async Task<LlmConfig?> GetDefaultConfigAsync()
        {
            try
            {
                var configs = await LogRepository.GetLlmConfigsAsync(SessionService.CurrentUser!.Id);
                return configs.FirstOrDefault(c => c.IsDefault) ?? configs.FirstOrDefault();
            }
            catch { return null; }
        }

        private string BuildWeekLocal()
        {
            var s = DashboardPage.CalcStats(Log);
            var today = DateTime.Today;
            var projectName = SessionService.CurrentProject?.Name ?? "未指定项目";
            var md = new StringBuilder();
            md.AppendLine($"# 工作周报");
            md.AppendLine($"{today:yyyy-MM-dd} · 第 {System.Globalization.CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(today, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday)} 周 · 负责项目：{projectName}");
            md.AppendLine();
            AppendCat(md, "一、本周核心成果", "核心成果");
            AppendCat(md, "二、问题改进与沉淀", "问题改进");
            md.AppendLine($"## 三、量化达成情况");
            md.AppendLine($"累计执行用例 {s.exec} 条、通过 {s.pass} 条，发现缺陷 {s.bugs} 个；回归通过率 {s.rate}%，有效工时约 {s.hours}h。");
            md.AppendLine();
            AppendCat(md, "四、工作亮点", "工作亮点");
            md.AppendLine("## 五、下周计划要点");
            md.AppendLine($"紧急优先 {Log.Plans.Count(x => x.Prio == 0)} 项、常规迭代 {Log.Plans.Count(x => x.Prio == 1)} 项、能力提升 {Log.Plans.Count(x => x.Prio == 2)} 项。");
            return md.ToString();
        }

        private string BuildTransferLocal()
        {
            var s = DashboardPage.CalcStats(Log);
            var md = new StringBuilder();
            md.AppendLine("# 转正述职报告");
            md.AppendLine($"候选人：{SessionService.CurrentUser?.DisplayName} · 部门：测试 · 周期：试用期 · 生成于 {DateTime.Today:yyyy-MM-dd}");
            md.AppendLine();
            md.AppendLine("## 一、工作职责与定位");
            md.AppendLine("作为测试工程师，负责业务线的功能、接口与性能质量保障，围绕「回归、复盘、沉淀」三个闭环推进工作。");
            md.AppendLine();
            md.AppendLine("## 二、核心产出（量化）");
            foreach (var m in Picked().Where(m => m.Category == "核心成果")) md.AppendLine($"- {m.Content}");
            md.AppendLine($"累计执行用例 {s.exec} 条、发现缺陷 {s.bugs} 个，回归通过率 {s.rate}%。");
            md.AppendLine();
            md.AppendLine("## 三、质量改进与风险把控");
            foreach (var m in Picked().Where(m => m.Category == "问题改进")) md.AppendLine($"- {m.Content}");
            md.AppendLine();
            md.AppendLine("## 四、能力成长");
            foreach (var g in Log.Growth) md.AppendLine($"- 【{GrowthPage.DimName(g.DimKey)}】{g.Content}");
            md.AppendLine();
            md.AppendLine("## 五、工作亮点");
            foreach (var m in Picked().Where(m => m.Category == "工作亮点")) md.AppendLine($"- {m.Content}");
            md.AppendLine();
            md.AppendLine("## 六、改进方向");
            md.AppendLine("深化性能与异常场景质量建设，将可复用经验固化为团队规范与自动化用例。");
            return md.ToString();
        }

        private void AppendCat(StringBuilder md, string title, string cat)
        {
            md.AppendLine($"## {title}");
            var items = Picked().Where(m => m.Category == cat).ToList();
            if (items.Count == 0) md.AppendLine("- 暂无收录素材，可前往素材库勾选。");
            else foreach (var m in items) md.AppendLine($"- {m.Content}");
            md.AppendLine();
        }

        private async void OnCopy(object sender, RoutedEventArgs e)
        {
            var text = PreviewBox.Text;
            if (string.IsNullOrWhiteSpace(text)) { Toast.Show("暂无总结内容"); return; }
            try
            {
                var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dp.SetText(text);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                Toast.Show("已复制到剪贴板");
            }
            catch (Exception ex) { Toast.Show("复制失败：" + ex.Message); }
        }
    }
}