using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using Span.ViewModels;

namespace Span
{
    public partial class App : Application
    {
        public IServiceProvider Services { get; }
        public new static App Current => (App)Application.Current;

        // Multi-window tracking
        private readonly List<Window> _windows = new();
        private readonly object _windowLock = new();

        public App()
        {
            this.InitializeComponent();
            Services = ConfigureServices();

            this.UnhandledException += OnUnhandledException;
        }

        public void RegisterWindow(Window w)
        {
            lock (_windowLock)
            {
                if (!_windows.Contains(w))
                    _windows.Add(w);
            }
        }

        public void UnregisterWindow(Window w)
        {
            lock (_windowLock)
            {
                _windows.Remove(w);
                if (_windows.Count == 0)
                    Exit();
            }
        }

        private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            // Log the exception
            System.Diagnostics.Debug.WriteLine($"Unhandled Exception: {e.Message}");
            e.Handled = true; // Prevent crash if possible, or at least suppress JIT dialog
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Services
            services.AddSingleton<Services.FileSystemService>();
            services.AddSingleton<Services.IconService>();
            services.AddSingleton<Services.FavoritesService>();
            services.AddSingleton<Services.PreviewService>();
            services.AddSingleton<Services.ShellService>();
            services.AddSingleton<Services.LocalizationService>();
            services.AddSingleton<Services.ContextMenuService>();
            services.AddSingleton<Services.ActionLogService>();
            services.AddSingleton<Services.SettingsService>();
            services.AddSingleton<Services.FolderContentCache>();

            // ViewModel 등록
            services.AddTransient<MainViewModel>();

            return services.BuildServiceProvider();
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                var iconService = Services.GetRequiredService<Services.IconService>();
                await iconService.LoadAsync();

                m_window = new MainWindow();
                RegisterWindow(m_window);
                m_window.Activate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unhandled exception in OnLaunched: {ex}");
                // In a real app, might show a dialog here
            }
        }

        private Window m_window;
    }
}
