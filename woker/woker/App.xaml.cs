using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

namespace woker
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        public static Window? MainWindow { get; private set; }

        public App()
        {
            UnhandledException += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine("[全局异常] " + e.Message);
                if (MainWindow == null) RecordStartupFailure(new Exception(e.Message, e.Exception));
                e.Handled = MainWindow != null;
            };
            try { InitializeComponent(); }
            catch (Exception ex) { RecordStartupFailure(ex); throw; }
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                MainWindow = new MainWindow();
                MainWindow.Activate();
            }
            catch (Exception ex) { RecordStartupFailure(ex); throw; }
        }

        private static void RecordStartupFailure(Exception exception)
        {
            try
            {
                var directory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkBench");
                Directory.CreateDirectory(directory);
                File.WriteAllText(System.IO.Path.Combine(directory, "startup-error.log"), exception.ToString());
            }
            catch { }
        }
    }
}
