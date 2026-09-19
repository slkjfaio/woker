using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using woker.Models;
using woker.Services;

namespace woker.Views
{
    public sealed partial class SettingsPage : Page
    {
        private System.Collections.Generic.List<LlmConfig> _configs = new();

        public SettingsPage()
        {
            InitializeComponent();
            LoadServerConfig();
            LoadUpdateLog();
            var updates = DesktopUpdates.Preferences();
            UpdateRepositoryBox.Text = updates.Repository;
            CheckUpdatesOnStartup.IsChecked = updates.CheckOnStartup;
            AppVersionText.Text = "当前版本：" + DesktopUpdateService.CurrentVersion.ToString(3);
            Loaded += async (s, e) => await ReloadLlmAsync();
        }

        public static Microsoft.UI.Xaml.Visibility DefaultVisible(bool isDefault) =>
            isDefault ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        private void OnSaveUpdateSettings(object sender, RoutedEventArgs e)
        {
            try { SaveUpdateSettings(); Toast.Show("更新设置已保存"); }
            catch (Exception ex) { Toast.Show(ex.Message); }
        }

        private void SaveUpdateSettings() => DesktopUpdates.Save(new UpdatePreferences
        {
            Repository = UpdateRepositoryBox.Text.Trim(), CheckOnStartup = CheckUpdatesOnStartup.IsChecked == true
        });

        private async void OnCheckUpdate(object sender, RoutedEventArgs e)
        {
            CheckUpdateButton.IsEnabled = false;
            try { SaveUpdateSettings(); await DesktopUpdates.OfferAsync(XamlRoot); }
            catch (Exception ex) { Toast.Show(ex.Message); }
            finally { CheckUpdateButton.IsEnabled = true; }
        }

        // ---------------- 更新日志 ----------------
        private static readonly List<UpdateLogEntry> UpdateLogs = new()
        {
            new UpdateLogEntry
            {
                Version = "v2.0.0",
                Date = "2026-09-17",
                Title = "架构升级：接入后端 API",
                Changes = new List<string>
                {
                    "数据访问从直连 MySQL 改为后端 REST API，客户端不再直连数据库",
                    "新增 ApiService HTTP 客户端层，统一鉴权与错误处理",
                    "登录鉴权改为 JWT + refreshToken 自动续期",
                    "AI 对话改为后端代理转发，客户端不再直连 AI 服务",
                    "聊天消息由后端自动落库，移除客户端手动保存",
                    "修复 AI 对话上下文消息顺序（历史消息在前，当前提问在后）",
                    "设置页新增后端服务器地址配置，移除数据库连接配置",
                    "统一各业务页面错误提示文案（数据库读取失败 → 数据加载失败）",
                    "新增设置页更新日志展示，可折叠查看各版本变更",
                    "移除 MySqlConnector 依赖",
                },
            },
            new UpdateLogEntry
            {
                Version = "v1.1.0",
                Date = "2026-08-20",
                Title = "体验优化",
                Changes = new List<string>
                {
                    "AI 对话支持流式输出（SSE）",
                    "新增大模型预置配置（DeepSeek / 通义千问 / Kimi / 智谱 GLM）",
                    "日报导出 Markdown 格式优化",
                    "多项目切换与默认项目记忆",
                },
            },
            new UpdateLogEntry
            {
                Version = "v1.0.0",
                Date = "2026-07-15",
                Title = "初始版本",
                Changes = new List<string>
                {
                    "工作日志工作台桌面端发布",
                    "6 大业务模块：工作成果、问题复盘、能力成长、遗留待办、次日计划、总结素材",
                    "直连 MySQL 数据库，多用户数据隔离",
                    "AI 助手对话（OpenAI 兼容协议）",
                    "每日日报台账与多项目管理",
                },
            },
        };

        private void LoadUpdateLog()
        {
            UpdateLogList.ItemsSource = UpdateLogs;
        }

        // ---------------- 后端服务器 ----------------
        private void LoadServerConfig()
        {
            var cfg = ApiService.Config;
            SrvUrl.Text = cfg.BaseUrl;
        }

        private ServerConfig ReadServerConfig()
        {
            return new ServerConfig
            {
                BaseUrl = SrvUrl.Text.Trim(),
            };
        }

        private async void OnTestServer(object sender, RoutedEventArgs e)
        {
            ServerRing.IsActive = true;
            ServerStatus.Text = "正在测试连接…";
            var cfg = ReadServerConfig();
            try
            {
                var (ok, msg) = await ApiService.TestAsyncWith(cfg);
                ServerStatus.Text = ok ? "✓ " + msg : "✗ " + msg;
            }
            catch (Exception ex)
            {
                ServerStatus.Text = "✗ 测试失败：" + ex.Message;
            }
            finally
            {
                ServerRing.IsActive = false;
            }
        }

        private async void OnSaveServer(object sender, RoutedEventArgs e)
        {
            ServerRing.IsActive = true;
            var cfg = ReadServerConfig();
            var (ok, msg) = await ApiService.TestAsyncWith(cfg);
            if (ok)
            {
                ApiService.UpdateConfig(cfg);
                ServerStatus.Text = "✓ 已保存，" + msg;
                Toast.Show("服务器配置已保存，请重新启动应用或重试登录");
            }
            else
            {
                ServerStatus.Text = "✗ 保存失败：" + msg;
                Toast.Show("连接失败，未保存");
            }
            ServerRing.IsActive = false;
        }

        // ---------------- 大模型配置 ----------------
        private async Task ReloadLlmAsync()
        {
            try
            {
                _configs = await LogRepository.GetLlmConfigsAsync(SessionService.CurrentUser!.Id);
            }
            catch (Exception ex) { Toast.Show("加载模型配置失败：" + ex.Message); }
            LlmList.ItemsSource = null;
            LlmList.ItemsSource = _configs;
            LlmEmpty.Visibility = _configs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void OnAddPreset(object sender, RoutedEventArgs e)
        {
            var presetList = LlmService.Presets;
            var pick = new ComboBox
            {
                ItemsSource = presetList.Select(p => p.Name).ToList(),
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            var apiKey = new PasswordBox { PlaceholderText = "sk-..." };
            var modelPick = CreateModelPicker(presetList[0].ModelName);
            var modelStatus = CreateModelStatus();
            var modelRing = new ProgressRing { Width = 18, Height = 18, IsActive = false };
            var fetchModels = new Button { Content = "获取模型列表" };
            fetchModels.Click += async (_, _) =>
            {
                var preset = presetList[pick.SelectedIndex];
                await FetchModelsAsync(preset.BaseUrl, apiKey.Password, modelPick, modelRing, modelStatus);
            };
            apiKey.LostFocus += async (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(apiKey.Password))
                {
                    var preset = presetList[pick.SelectedIndex];
                    await FetchModelsAsync(preset.BaseUrl, apiKey.Password, modelPick, modelRing, modelStatus);
                }
            };
            pick.SelectionChanged += async (_, _) =>
            {
                if (pick.SelectedIndex < 0 || pick.SelectedIndex >= presetList.Count) return;
                modelPick.ItemsSource = new[] { presetList[pick.SelectedIndex].ModelName };
                modelPick.SelectedIndex = 0;
                modelStatus.Text = "";
                if (!string.IsNullOrWhiteSpace(apiKey.Password))
                    await FetchModelsAsync(presetList[pick.SelectedIndex].BaseUrl, apiKey.Password,
                        modelPick, modelRing, modelStatus);
            };
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(DashboardPage.Field("预置模型", pick));
            panel.Children.Add(DashboardPage.Field("API Key *", apiKey));
            panel.Children.Add(DashboardPage.Field("选择模型", modelPick));
            panel.Children.Add(CreateModelActions(fetchModels, modelRing, modelStatus));

            var dlg = new ContentDialog
            {
                Title = "从预置添加大模型",
                Content = panel,
                PrimaryButtonText = "添加",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };
            await ShowPresetDialogAsync(dlg, pick, presetList, apiKey, modelPick);
        }

        private async Task ShowPresetDialogAsync(ContentDialog dlg, ComboBox pick,
            List<LlmConfig> presetList, PasswordBox apiKey, ComboBox modelPick)
        {
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            if (string.IsNullOrWhiteSpace(apiKey.Password)) { Toast.Show("请填写 API Key"); return; }

            var preset = presetList[pick.SelectedIndex];
            try
            {
                await LogRepository.SaveLlmConfigAsync(SessionService.CurrentUser!.Id, new LlmConfig
                {
                    Name = preset.Name,
                    BaseUrl = preset.BaseUrl,
                    ModelName = modelPick.SelectedItem as string ?? preset.ModelName,
                    ApiKey = apiKey.Password,
                    IsDefault = _configs.Count == 0,
                });
                Toast.Show("已接入 " + preset.Name);
                await ReloadLlmAsync();
            }
            catch (Exception ex) { Toast.Show("保存失败：" + ex.Message); }
        }

        private async void OnAddConfig(object sender, RoutedEventArgs e) => ShowConfigDialog(null);

        private async void OnEditConfig(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not long id) return;
            var cfg = _configs.FirstOrDefault(c => c.Id == id);
            if (cfg != null) ShowConfigDialog(cfg);
        }

        private async void ShowConfigDialog(LlmConfig? existing)
        {
            var name = new TextBox { PlaceholderText = "显示名称，如 Ollama 本地模型", Text = existing?.Name ?? "" };
            var baseUrl = new TextBox { PlaceholderText = "如 http://localhost:11434/v1", Text = existing?.BaseUrl ?? "" };
            var apiKey = new TextBox { PlaceholderText = "API Key（本地服务可留空）", Text = existing?.ApiKey ?? "" };
            var modelName = new TextBox { PlaceholderText = "如 llama3 / qwen-plus", Text = existing?.ModelName ?? "" };
            var modelPick = CreateModelPicker(existing?.ModelName);
            var modelStatus = CreateModelStatus();
            var modelRing = new ProgressRing { Width = 18, Height = 18, IsActive = false };
            var fetchModels = new Button { Content = "获取模型列表" };
            modelPick.SelectionChanged += (_, _) =>
            {
                if (modelPick.SelectedItem is string selected)
                    modelName.Text = selected;
            };
            fetchModels.Click += async (_, _) =>
            {
                await FetchModelsAsync(baseUrl.Text, apiKey.Text, modelPick, modelRing, modelStatus);
            };
            baseUrl.LostFocus += async (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(baseUrl.Text))
                    await FetchModelsAsync(baseUrl.Text, apiKey.Text, modelPick, modelRing, modelStatus);
            };
            apiKey.LostFocus += async (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(baseUrl.Text))
                    await FetchModelsAsync(baseUrl.Text, apiKey.Text, modelPick, modelRing, modelStatus);
            };

            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(DashboardPage.Field("名称 *", name));
            panel.Children.Add(DashboardPage.Field("BaseURL *（OpenAI 兼容）", baseUrl));
            panel.Children.Add(DashboardPage.Field("API Key", apiKey));
            panel.Children.Add(DashboardPage.Field("选择模型", modelPick));
            panel.Children.Add(CreateModelActions(fetchModels, modelRing, modelStatus));
            panel.Children.Add(DashboardPage.Field("模型名 *", modelName));

            var dlg = new ContentDialog
            {
                Title = existing == null ? "自定义接入大模型" : "编辑大模型配置",
                Content = panel,
                PrimaryButtonText = "保存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };
            dlg.Opened += async (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(existing?.BaseUrl))
                    await FetchModelsAsync(existing.BaseUrl, existing.ApiKey, modelPick, modelRing, modelStatus);
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(baseUrl.Text) || string.IsNullOrWhiteSpace(modelName.Text))
            {
                Toast.Show("请填写名称、BaseURL 与模型名");
                return;
            }

            try
            {
                var cfg = existing ?? new LlmConfig();
                cfg.Name = name.Text.Trim();
                cfg.BaseUrl = baseUrl.Text.Trim();
                cfg.ApiKey = apiKey.Text.Trim();
                cfg.ModelName = modelName.Text.Trim();
                if (existing == null) cfg.IsDefault = _configs.Count == 0;
                await LogRepository.SaveLlmConfigAsync(SessionService.CurrentUser!.Id, cfg);
                Toast.Show(existing == null ? "模型已接入" : "配置已更新");
                await ReloadLlmAsync();
            }
            catch (Exception ex) { Toast.Show("保存失败：" + ex.Message); }
        }

        private async void OnSetDefault(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not long id) return;
            try
            {
                await LogRepository.SetDefaultLlmAsync(SessionService.CurrentUser!.Id, id);
                Toast.Show("已设为默认模型");
                await ReloadLlmAsync();
            }
            catch (Exception ex) { Toast.Show("设置失败：" + ex.Message); }
        }

        private static ComboBox CreateModelPicker(string? selectedModel = null)
        {
            var picker = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                PlaceholderText = "点击“获取模型列表”后选择",
            };
            if (!string.IsNullOrWhiteSpace(selectedModel))
            {
                picker.ItemsSource = new[] { selectedModel };
                picker.SelectedIndex = 0;
            }
            return picker;
        }

        private static TextBlock CreateModelStatus() => new()
        {
            FontSize = 11,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
        };

        private static StackPanel CreateModelActions(
            Button fetchModels, ProgressRing modelRing, TextBlock modelStatus) => new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { fetchModels, modelRing, modelStatus },
        };

        private static async Task FetchModelsAsync(
            string baseUrl, string apiKey, ComboBox modelPick, ProgressRing modelRing, TextBlock modelStatus)
        {
            modelRing.IsActive = true;
            modelStatus.Text = "正在获取模型列表…";
            try
            {
                var (models, error) = await LlmService.GetModelsAsync(baseUrl.Trim(), apiKey.Trim());
                if (models.Count > 0)
                {
                    var current = modelPick.SelectedItem as string;
                    modelPick.ItemsSource = models;
                    modelPick.SelectedItem = models.FirstOrDefault(x =>
                        string.Equals(x, current, StringComparison.OrdinalIgnoreCase)) ?? models[0];
                    modelStatus.Text = $"已获取 {models.Count} 个模型";
                }
                else
                {
                    modelStatus.Text = "获取失败：" + error;
                }
            }
            catch (Exception ex)
            {
                modelStatus.Text = "获取失败：" + ex.Message;
            }
            finally { modelRing.IsActive = false; }
        }

        private async void OnDeleteConfig(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not long id) return;
            var cfg = _configs.FirstOrDefault(c => c.Id == id);
            var dlg = new ContentDialog
            {
                Title = "删除模型配置",
                Content = $"确定删除「{cfg?.Name}」吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            try
            {
                await LogRepository.DeleteLlmConfigAsync(SessionService.CurrentUser!.Id, id);
                Toast.Show("配置已删除");
                await ReloadLlmAsync();
            }
            catch (Exception ex) { Toast.Show("删除失败：" + ex.Message); }
        }
    }
}
