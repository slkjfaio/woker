using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using woker.Models;
using woker.Services;

namespace woker.Views
{
    public sealed partial class LoginPage : Page
    {
        private bool _isRegister;
        private string _captchaId = "";

        public LoginPage()
        {
            InitializeComponent();
            ServerUrlBox.Text = ApiService.Config.BaseUrl;
            PassBox.KeyDown += (s, e) => { if (e.Key == Windows.System.VirtualKey.Enter) SubmitAsync(); };
            UserBox.KeyDown += (s, e) => { if (e.Key == Windows.System.VirtualKey.Enter) SubmitAsync(); };
            RegConfirmPassBox.KeyDown += (s, e) => { if (e.Key == Windows.System.VirtualKey.Enter) SubmitAsync(); };
            CaptchaAnswerBox.KeyDown += (s, e) => { if (e.Key == Windows.System.VirtualKey.Enter) SubmitAsync(); };
            _ = RefreshCaptchaAsync();
        }

        private async void OnSwitchMode(object sender, RoutedEventArgs e)
        {
            if (!ConfigureServer()) return;
            var nextIsRegister = !_isRegister;
            try
            {
                if (nextIsRegister && !await AuthService.IsRegistrationEnabledAsync())
                {
                    ShowError("系统当前已关闭注册");
                    return;
                }
            }
            catch (Exception ex)
            {
                ShowError("无法读取注册状态：" + ex.Message);
                return;
            }
            _isRegister = nextIsRegister;
            RefreshModeUi();
            RegConfirmPassBox.Password = "";
            CaptchaAnswerBox.Text = "";
            ErrorBar.IsOpen = false;
            await RefreshCaptchaAsync();
        }

        private void RefreshModeUi()
        {
            RegExtraPanel.Visibility = _isRegister ? Visibility.Visible : Visibility.Collapsed;
            RegConfirmPassBox.Visibility = _isRegister ? Visibility.Visible : Visibility.Collapsed;
            HeaderTitle.Text = _isRegister ? "注册新账号" : "登录工作台";
            HeaderSub.Text = _isRegister
                ? "设置用户名与密码，注册后即可开始记录工作台账"
                : "使用账号密码登录，数据保存在后端服务器";
            SubmitText.Text = _isRegister ? "注册" : "登录";
            SubmitIcon.Glyph = _isRegister ? "\uE8FA" : "\uE8AF";
            ModeSwitch.Content = _isRegister ? "已有账号？去登录" : "没有账号？去注册";
        }

        private async void OnRefreshCaptcha(object sender, RoutedEventArgs e) => await RefreshCaptchaAsync();

        private async Task RefreshCaptchaAsync()
        {
            CaptchaImage.Source = null;
            CaptchaAnswerBox.Text = "";
            _captchaId = "";
            try
            {
                var captcha = await AuthService.GetCaptchaAsync();
                if (captcha == null) return;
                _captchaId = captcha.CaptchaId;
                await SetCaptchaImageAsync(captcha.Image);
            }
            catch { /* 验证码获取失败时留空，用户可点刷新重试 */ }
        }

        private async Task SetCaptchaImageAsync(string dataUrl)
        {
            var comma = dataUrl.IndexOf(',');
            if (comma < 0 || comma + 1 >= dataUrl.Length) return;
            var bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(ms.AsRandomAccessStream());
            CaptchaImage.Source = bmp;
        }

        private void OnSubmitClick(object sender, RoutedEventArgs e) => SubmitAsync();

        private bool ConfigureServer()
        {
            try
            {
                var config = new ServerConfig { BaseUrl = ServerUrlBox.Text.Trim() };
                _ = config.ApiBaseUrl;
                if (!string.Equals(config.BaseUrl, ApiService.Config.BaseUrl, StringComparison.Ordinal))
                {
                    SessionService.Clear();
                    ApiService.UpdateConfig(config);
                }
                return true;
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                return false;
            }
        }

        private async void SubmitAsync()
        {
            ErrorBar.IsOpen = false;
            if (!ConfigureServer()) return;

            if (_isRegister)
            {
                if (string.IsNullOrEmpty(RegConfirmPassBox.Password))
                {
                    ShowError("请再次输入密码");
                    return;
                }
                if (PassBox.Password != RegConfirmPassBox.Password)
                {
                    ShowError("两次输入的密码不一致");
                    return;
                }
            }

            if (string.IsNullOrEmpty(_captchaId))
            {
                ShowError("验证码未加载，请点击验证码图片刷新");
                await RefreshCaptchaAsync();
                return;
            }

            SetBusy(true);
            try
            {
                if (_isRegister)
                {
                    var (ok, error) = await AuthService.RegisterAsync(
                        UserBox.Text, PassBox.Password, RegConfirmPassBox.Password,
                        RegNameBox.Text, RegMailBox.Text, _captchaId, CaptchaAnswerBox.Text.Trim());

                    if (!ok)
                    {
                        ShowError(string.IsNullOrEmpty(error) ? "注册失败" : error);
                        await RefreshCaptchaAsync();
                        return;
                    }

                    // 后端注册接口不返回令牌，验证码已一次性消费，需重新登录
                    Toast.Show("注册成功，请使用新账号登录");
                    _isRegister = false;
                    RefreshModeUi();
                    RegConfirmPassBox.Password = "";
                    CaptchaAnswerBox.Text = "";
                    await RefreshCaptchaAsync();
                    return;
                }

                var (user, loginError) = await AuthService.LoginAsync(
                    UserBox.Text, PassBox.Password, _captchaId, CaptchaAnswerBox.Text.Trim());

                if (user == null)
                {
                    ShowError(string.IsNullOrEmpty(loginError) ? "登录失败" : loginError);
                    await RefreshCaptchaAsync();
                    return;
                }
                EnterApp(user);
            }
            catch (Exception ex)
            {
                ShowError("无法连接服务器：" + ex.Message);
                await RefreshCaptchaAsync();
            }
            finally { SetBusy(false); }
        }

        private async void EnterApp(User user)
        {
            SessionService.SetUser(user, RememberCheck.IsChecked == true);
            Toast.Show($"欢迎，{user.DisplayName}");

            // 加载或创建默认项目
            try
            {
                var defaultProject = await LogRepository.GetOrCreateDefaultProjectAsync(user.Id);
                if (defaultProject != null)
                {
                    SessionService.SetCurrentProject(defaultProject);
                    SessionService.CurrentLog = await LogRepository.GetOrCreateLogAsync(
                        user.Id,
                        defaultProject.Id,
                        DateTime.Today);
                }
            }
            catch { SessionService.CurrentLog = null; }

            Frame.Navigate(typeof(ShellPage));
        }

        private void ShowError(string msg)
        {
            ErrorBar.Message = msg;
            ErrorBar.IsOpen = true;
        }

        private void SetBusy(bool busy)
        {
            LoadingRing.IsActive = busy;
            SubmitBtn.IsEnabled = !busy;
            ServerUrlBox.IsEnabled = !busy;
            ModeSwitch.IsEnabled = !busy;
            CaptchaAnswerBox.IsEnabled = !busy;
        }
    }
}