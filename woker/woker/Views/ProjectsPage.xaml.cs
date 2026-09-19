using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using woker.Models;
using woker.Services;

namespace woker.Views
{
    public sealed partial class ProjectsPage : Page
    {
        public ObservableCollection<Project> Projects { get; } = new();

        public Visibility EmptyStateVisibility => Projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public ProjectsPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await LoadProjectsAsync();
        }

        private async System.Threading.Tasks.Task LoadProjectsAsync()
        {
            if (SessionService.CurrentUser == null) return;

            Projects.Clear();
            var list = await LogRepository.GetProjectsAsync(SessionService.CurrentUser.Id);
            foreach (var p in list)
            {
                Projects.Add(p);
            }
            Bindings.Update();
        }

        private async void OnAddProject(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "新建项目",
                PrimaryButtonText = "创建",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            var panel = new StackPanel { Spacing = 12 };

            var nameBox = new TextBox
            {
                Header = "项目名称",
                PlaceholderText = "输入项目名称",
                MaxLength = 128
            };
            panel.Children.Add(nameBox);

            var descBox = new TextBox
            {
                Header = "项目描述（可选）",
                PlaceholderText = "输入项目描述",
                MaxLength = 512,
                AcceptsReturn = true,
                Height = 80
            };
            panel.Children.Add(descBox);

            var colorPicker = new StackPanel { Spacing = 8 };
            colorPicker.Children.Add(new TextBlock { Text = "项目颜色", FontWeight = new Windows.UI.Text.FontWeight(600), FontSize = 14 });
            var colorGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                },
                ColumnSpacing = 8
            };

            var colors = new[] { "#0078D4", "#107C10", "#D83B01", "#8764B8", "#E3008C" };
            var selectedColor = colors[0];
            var colorButtons = new RadioButton[colors.Length];

            for (int i = 0; i < colors.Length; i++)
            {
                var color = colors[i];
                var btn = new RadioButton
                {
                    GroupName = "ProjectColor",
                    IsChecked = i == 0,
                    Style = Application.Current.Resources["ColorPickerRadioButtonStyle"] as Style
                };

                var border = new Border
                {
                    Width = 40,
                    Height = 40,
                    CornerRadius = new CornerRadius(20),
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Microsoft.UI.ColorHelper.FromArgb(
                            255,
                            System.Convert.ToByte(color.Substring(1, 2), 16),
                            System.Convert.ToByte(color.Substring(3, 2), 16),
                            System.Convert.ToByte(color.Substring(5, 2), 16)))
                };
                btn.Content = border;
                btn.Checked += (s, args) => { selectedColor = color; };

                Grid.SetColumn(btn, i);
                colorGrid.Children.Add(btn);
                colorButtons[i] = btn;
            }
            colorPicker.Children.Add(colorGrid);
            panel.Children.Add(colorPicker);

            dialog.Content = panel;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
            {
                var project = new Project
                {
                    Name = nameBox.Text.Trim(),
                    Description = descBox.Text?.Trim() ?? "",
                    Color = selectedColor
                };

                var newId = await LogRepository.CreateProjectAsync(SessionService.CurrentUser!.Id, project);
                project.Id = newId;

                await LoadProjectsAsync();
                Toast.Show($"项目 \"{project.Name}\" 创建成功");
            }
        }

        private async void OnEditProject(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not long id) return;
            var project = Projects.FirstOrDefault(p => p.Id == id);
            if (project == null) return;

            var dialog = new ContentDialog
            {
                Title = "编辑项目",
                PrimaryButtonText = "保存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            var panel = new StackPanel { Spacing = 12 };

            var nameBox = new TextBox
            {
                Header = "项目名称",
                Text = project.Name,
                MaxLength = 128
            };
            panel.Children.Add(nameBox);

            var descBox = new TextBox
            {
                Header = "项目描述",
                Text = project.Description,
                MaxLength = 512,
                AcceptsReturn = true,
                Height = 80
            };
            panel.Children.Add(descBox);

            dialog.Content = panel;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
            {
                project.Name = nameBox.Text.Trim();
                project.Description = descBox.Text?.Trim() ?? "";

                await LogRepository.UpdateProjectAsync(SessionService.CurrentUser!.Id, project);
                await LoadProjectsAsync();
                Toast.Show("项目信息已更新");
            }
        }

        private async void OnSetDefault(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item || item.Tag is not long id) return;

            await LogRepository.SetDefaultProjectAsync(SessionService.CurrentUser!.Id, id);
            await LoadProjectsAsync();
            Toast.Show("默认项目已设置");
        }

        private async void OnArchiveProject(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item || item.Tag is not long id) return;
            var project = Projects.FirstOrDefault(p => p.Id == id);
            if (project == null) return;

            var dialog = new ContentDialog
            {
                Title = "归档项目",
                Content = $"确定要归档项目 \"{project.Name}\" 吗？归档后该项目的日志数据将被保留，但不会显示在项目列表中。",
                PrimaryButtonText = "归档",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await LogRepository.ArchiveProjectAsync(SessionService.CurrentUser!.Id, id);
                await LoadProjectsAsync();
                Toast.Show($"项目 \"{project.Name}\" 已归档");
            }
        }

        private void OnMoreOptions(object sender, RoutedEventArgs e)
        {
            // Flyout 由 XAML 处理
        }
    }
}
