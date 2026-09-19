using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace woker.Services;

public sealed class UpdatePreferences
{
    public string Repository { get; set; } = "";
    public bool CheckOnStartup { get; set; } = true;
}

public static class DesktopUpdates
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };
    private static readonly DesktopUpdateService Service = new(Http);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static UpdatePreferences Preferences()
    {
        var saved = LocalStore.Load<UpdatePreferences>("updates.json");
        if (saved != null) return saved;
        var path = Path.Combine(AppContext.BaseDirectory, "update-source.json");
        if (File.Exists(path))
        {
            try { return JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(path)) ?? new(); }
            catch (JsonException) { }
        }
        return new();
    }

    public static void Save(UpdatePreferences preferences)
    {
        if (!string.IsNullOrWhiteSpace(preferences.Repository))
            preferences.Repository = DesktopUpdateService.ValidateRepository(preferences.Repository);
        LocalStore.Save("updates.json", preferences);
    }

    public static async Task<DesktopUpdate?> CheckAsync()
    {
        var settings = Preferences();
        if (string.IsNullOrWhiteSpace(settings.Repository))
            throw new InvalidOperationException("尚未配置 GitHub 更新仓库，请在设置页填写 用户名/仓库名。");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await Service.CheckAsync(settings.Repository, DesktopUpdateService.CurrentVersion, DesktopUpdateService.Architecture, timeout.Token);
    }

    public static async Task OfferAsync(XamlRoot root, DesktopUpdate? knownUpdate = null)
    {
        if (!await Gate.WaitAsync(0)) return;
        try
        {
            var update = knownUpdate ?? await CheckAsync();
            if (update == null)
            {
                await MessageAsync(root, "检查更新", "当前已是最新正式版本。");
                return;
            }
            // MSIX has a different installation lifecycle; do not install into its protected directory.
            bool packaged;
            try { _ = Windows.ApplicationModel.Package.Current.Id; packaged = true; }
            catch (InvalidOperationException) { packaged = false; }
            if (packaged)
            {
                await MessageAsync(root, "安装方式不同", "当前运行的是 MSIX 版本，请从 GitHub Release 下载 EXE 安装包完成首次迁移。后续 EXE 版本可在此更新。");
                return;
            }
            var confirm = new ContentDialog
            {
                XamlRoot = root, Title = $"发现新版本 {update.Version.ToString(3)}",
                Content = "下载安装包并校验后，将关闭工作台并打开安装向导。请先保存正在编辑的内容。更新沿用当前安装目录。",
                PrimaryButtonText = "下载并安装", CloseButtonText = "稍后", DefaultButton = ContentDialogButton.Close
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            using var cancel = new CancellationTokenSource();
            var progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Width = 300 };
            var progressDialog = new ContentDialog
            {
                XamlRoot = root, Title = "正在下载更新", Content = progressBar, CloseButtonText = "取消"
            };
            progressDialog.CloseButtonClick += (_, _) => cancel.Cancel();
            var showing = progressDialog.ShowAsync();
            string installer;
            try
            {
                installer = await Service.DownloadAsync(update,
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkBench", "Updates"),
                    new Progress<int>(value => progressBar.Value = value), cancel.Token);
                cancel.Token.ThrowIfCancellationRequested();
            }
            finally
            {
                progressDialog.Hide();
                await showing;
            }
            var start = new ProcessStartInfo(installer) { UseShellExecute = true };
            start.ArgumentList.Add("/DIR=" + AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            if (Process.Start(start) == null) throw new InvalidOperationException("无法启动安装向导。");
            Application.Current.Exit();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await MessageAsync(root, "更新未完成", ex.Message);
        }
        finally { Gate.Release(); }
    }

    private static async Task MessageAsync(XamlRoot root, string title, string message) =>
        await new ContentDialog { XamlRoot = root, Title = title, Content = message, CloseButtonText = "关闭" }.ShowAsync();
}
