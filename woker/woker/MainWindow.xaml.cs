using Microsoft.UI.Xaml;
using System;
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;
using woker.Services;
using woker.Views;
using Microsoft.UI.Windowing;
using Windows.UI;

namespace woker
{
    public sealed partial class MainWindow : Window
    {
        private AppWindow? _appWindow;
        private DesktopUpdate? _availableUpdate;
        private bool _checkedUpdates;

        public MainWindow()
        {
            InitializeComponent();
            Title = "工作日志工作台 · WorkBench";

            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                _appWindow = AppWindow.GetFromWindowId(id);
                _appWindow.Resize(new Windows.Graphics.SizeInt32(1280, 860));
            }
            catch { /* 忽略窗口尺寸设置失败 */ }

            ApplyTheme();
            RootGrid.Loaded += async (_, _) =>
            {
                if (_checkedUpdates) return;
                _checkedUpdates = true;
                var preferences = DesktopUpdates.Preferences();
                if (!preferences.CheckOnStartup || string.IsNullOrWhiteSpace(preferences.Repository)) return;
                try
                {
                    _availableUpdate = await DesktopUpdates.CheckAsync();
                    if (_availableUpdate != null)
                    {
                        UpdateNotice.Message = "WorkBench " + _availableUpdate.Version.ToString(3) + " 已发布";
                        UpdateNotice.IsOpen = true;
                    }
                }
                catch { /* 离线和更新源故障不阻塞登录；手动检查时显示原因。 */ }
            };
            _ = BootAsync();
        }

        private async Task BootAsync()
        {
            if (string.IsNullOrWhiteSpace(ApiService.Config.BaseUrl))
            {
                OnConfigureServer(this, new RoutedEventArgs());
                return;
            }
            BootView.Visibility = Visibility.Visible;
            BootError.Visibility = Visibility.Collapsed;
            RetryBtn.Visibility = Visibility.Collapsed;
            BootHint.Visibility = Visibility.Collapsed;
            BootRing.IsActive = true;
            BootText.Text = "正在连接服务器…";
            RootFrame.Content = null;

            // 1. 后端服务器可达性检查
            var (ok, msg) = await ApiService.TestAsync();
            if (!ok)
            {
                BootRing.IsActive = false;
                BootText.Text = "无法连接服务器";
                BootError.Text = msg + "\n\n请确认网络可用，或点击下方「重试连接」；也可在「设置 → 服务器地址」中修改后端服务器地址。";
                BootError.Visibility = Visibility.Visible;
                RetryBtn.Visibility = Visibility.Visible;
                BootHint.Visibility = Visibility.Visible;
                return;
            }

            // 2. 恢复保持登录的会话
            var restored = false;
            try { restored = await SessionService.TryRestoreAsync(); }
            catch { }

            if (restored && SessionService.CurrentUser != null)
            {
                try
                {
                    var defaultProject = await LogRepository.GetOrCreateDefaultProjectAsync(SessionService.CurrentUser.Id);
                    if (defaultProject != null)
                    {
                        SessionService.SetCurrentProject(defaultProject);
                        SessionService.CurrentLog = await LogRepository.GetOrCreateLogAsync(
                            SessionService.CurrentUser.Id,
                            defaultProject.Id,
                            DateTime.Today);
                    }
                }
                catch { SessionService.CurrentLog = null; }
                RootFrame.Navigate(typeof(ShellPage));
            }
            else
            {
                RootFrame.Navigate(typeof(LoginPage));
            }

            BootView.Visibility = Visibility.Collapsed;
        }

        private void OnRetry(object sender, RoutedEventArgs e) => _ = BootAsync();

        private void OnConfigureServer(object sender, RoutedEventArgs e)
        {
            BootView.Visibility = Visibility.Collapsed;
            BootRing.IsActive = false;
            RootFrame.Navigate(typeof(LoginPage));
        }

        private async void OnOpenUpdate(object sender, RoutedEventArgs e) =>
            await DesktopUpdates.OfferAsync(RootGrid.XamlRoot, _availableUpdate);

        internal void ApplyTheme()
        {
            var saved = LocalStore.GetString("theme");
            // Treat legacy "Default" values as light while keeping the persisted state explicit.
            SessionService.Theme = saved == "Dark" ? "Dark" : "Light";
            var isDark = SessionService.Theme == "Dark";
            RootGrid.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;

            if (_appWindow?.TitleBar is not AppWindowTitleBar titleBar) return;

            var background = isDark
                ? Color.FromArgb(255, 32, 32, 32)
                : Color.FromArgb(255, 250, 250, 250);
            var foreground = isDark
                ? Color.FromArgb(255, 255, 255, 255)
                : Color.FromArgb(255, 0, 0, 0);
            var inactiveBackground = isDark
                ? Color.FromArgb(255, 45, 45, 45)
                : Color.FromArgb(255, 240, 240, 240);
            var inactiveForeground = isDark
                ? Color.FromArgb(255, 190, 190, 190)
                : Color.FromArgb(255, 100, 100, 100);

            titleBar.BackgroundColor = background;
            titleBar.ForegroundColor = foreground;
            titleBar.InactiveBackgroundColor = inactiveBackground;
            titleBar.InactiveForegroundColor = inactiveForeground;
            titleBar.ButtonBackgroundColor = background;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonInactiveBackgroundColor = inactiveBackground;
            titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        }
    }
}
