using CommunityToolkit.Mvvm.ComponentModel;

using GalgameManager.Contracts.Services;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.ViewModels;
using GalgameManager.Views;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace GalgameManager.Services;

public class PageService : IPageService
{
    public Action? OnInit { get; set; }

    public PageService()
    {
        Configure<HomeViewModel, HomePage>();
        Configure<GalgameViewModel, HomeDetailPage>();
        Configure<GalgameCharacterViewModel, GalgameCharacterPage>();
        Configure<SettingsViewModel, SettingsPage>();
        Configure<LibraryViewModel, LibraryPage>();
        Configure<GalgameSourceViewModel, GalgameSourcePage>();
        Configure<GalgameSettingViewModel, GalgameSettingPage>();
        Configure<PlayedTimeViewModel, PlayedTimePage>();
        Configure<CategoryViewModel, CategoryPage>();
        Configure<CategorySettingViewModel, CategorySettingPage>();
        Configure<AccountViewModel, AccountPage>();
        Configure<InfoViewModel, InfoPage>();
        Configure<MultiStreamViewModel, Views.MultiStreamPage>();
        Configure<AnnualReportViewModel, AnnualReportPage>();
        Configure<StaffViewModel, StaffPage>();
        Configure<ScanResultViewModel, ScanResultPage>();
        Configure<PluginStoreViewModel, PluginStorePage>();
        Configure<PluginViewModel, PluginPage>();
        Configure<PluginHostPageViewModel, PluginHostPage>();
    }
    
    private readonly Dictionary<string, Type> _pages = new();
    
    public Type GetPageType(string key)
    {
        Type? pageType;
        lock (_pages)
        {
            if (!_pages.TryGetValue(key, out pageType))
            {
                throw new ArgumentException($"Page not found: {key}. Did you forget to call PageService.Configure?");
            }
        }

        return pageType;
    }

    public async Task InitAsync()
    {
        App.MainWindow ??= new MainWindow();
        if (App.MainWindow.Content == null)
        {
            RestoreWindowPlacement();
            UIElement shell = App.GetService<ShellPage>();
            App.MainWindow.Content = shell;
            await App.GetService<IThemeSelectorService>().SetRequestedThemeAsync();
            await App.GetService<IThemeSelectorService>().SetBackgroundMaterialAsync();
            await App.GetService<IThemeSelectorService>().SetNavigationViewTransparencyAsync();
            OnInit?.Invoke();
        }
    }

    /// <summary>
    /// 恢复上次退出时记住的窗口位置/尺寸/最大化状态。<br/>
    /// 顺序不能反：<b>先 MoveAndResize 到普通窗口矩形，最后再 Maximize</b>——
    /// 先 Maximize 会被系统把尺寸覆盖回最大化前的值，普通矩形就丢了。<br/>
    /// 越界（副屏被拔掉）或存档损坏时回退到主屏工作区内缩 10% 的矩形。
    /// </summary>
    private static void RestoreWindowPlacement()
    {
        WindowPlacement? saved = App.GetService<ILocalSettingsService>()
            .ReadSettingAsync<WindowPlacement?>(KeyValues.WindowPlacement).Result;
        // 从未保存过：保持 WinUI 的默认摆放，不要动窗口
        if (saved is null) return;

        List<RectInt32> workAreas = DisplayArea.FindAll().Select(area => area.WorkArea).ToList();
        if (workAreas.Count == 0) workAreas.Add(new RectInt32(0, 0, 1600, 900));
        RectInt32 primary = DisplayArea.Primary?.WorkArea ?? workAreas[0];
        int marginX = primary.Width / 10;
        int marginY = primary.Height / 10;
        WindowPlacement fallback = WindowPlacementHelper.Clamp(new WindowPlacement(
            primary.X + marginX, primary.Y + marginY,
            primary.Width - marginX * 2, primary.Height - marginY * 2, false));

        WindowPlacement target = WindowPlacementHelper.Sanitize(saved, fallback, workAreas);
        AppWindow appWindow = App.MainWindow!.AppWindow;
        appWindow.MoveAndResize(new RectInt32(target.X, target.Y, target.Width, target.Height));
        if (target.IsMaximized)
            ((OverlappedPresenter)appWindow.Presenter).Maximize();
    }

    private void Configure<VM, V>()
        where VM : ObservableObject
        where V : Page
    {
        lock (_pages)
        {
            var key = typeof(VM).FullName!;
            if (_pages.ContainsKey(key))
            {
                throw new ArgumentException($"The key {key} is already configured in PageService");
            }

            var type = typeof(V);
            if (_pages.Any(p => p.Value == type))
            {
                throw new ArgumentException($"This type is already configured with key {_pages.First(p => p.Value == type).Key}");
            }

            _pages.Add(key, type);
        }
    }
}
