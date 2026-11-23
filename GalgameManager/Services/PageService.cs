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
    /// 主窗口默认尺寸。<br/>
    /// <see cref="MainWindow"/> 的 XAML 里不再声明 Width/Height：WinUIEx 的 WindowManager
    /// 会在窗口内容加载时用 XAML 值重新应用窗口矩形，那会冲掉下面恢复出来的存档，
    /// 表现为「记忆窗口位置不生效」。窗口初始尺寸改由此处独占。
    /// </summary>
    private const int DefaultWindowWidth = 1280;
    private const int DefaultWindowHeight = 800;

    /// <summary>
    /// 恢复上次退出时记住的窗口位置/尺寸/最大化状态。<br/>
    /// 顺序不能反：<b>先 MoveAndResize 到普通窗口矩形，最后再 Maximize</b>——
    /// 先 Maximize 会被系统把尺寸覆盖回最大化前的值，普通矩形就丢了。<br/>
    /// 越界（副屏被拔掉）或存档损坏时回退到默认尺寸并放在主屏工作区中央。
    /// </summary>
    private static void RestoreWindowPlacement()
    {
        try
        {
            WindowPlacement? saved = App.GetService<ILocalSettingsService>()
                .ReadSettingAsync<WindowPlacement?>(KeyValues.WindowPlacement).Result;
            List<RectInt32> workAreas = DisplayArea.FindAll().Select(area => area.WorkArea).ToList();
            if (workAreas.Count == 0) workAreas.Add(new RectInt32(0, 0, 1600, 900));
            RectInt32 primary = DisplayArea.Primary?.WorkArea ?? workAreas[0];
            WindowPlacement fallback = DefaultPlacement(primary);

            // 无存档时也要用默认尺寸显式摆一次：XAML 已不声明 Width/Height，
            // 不设置的话窗口会落到系统的 CW_USEDEFAULT 尺寸上。
            WindowPlacement target = saved is null
                ? fallback
                : WindowPlacementHelper.Sanitize(saved, fallback, workAreas);

            AppWindow appWindow = App.MainWindow!.AppWindow;
            appWindow.MoveAndResize(new RectInt32(target.X, target.Y, target.Width, target.Height));
            if (target.IsMaximized && appWindow.Presenter is OverlappedPresenter presenter)
                presenter.Maximize();
        }
        catch (Exception e)
        {
            // 窗口位置是锦上添花：读设置失败、显示器信息异常、对尚未激活的窗口调用
            // MoveAndResize/Maximize 失败，都只应该回退到默认摆放，绝不允许把启动
            // 流程带崩——那会升级成崩溃重启循环，软件直接打不开。
            try
            {
                App.GetService<IInfoService>().DeveloperEvent(e: e);
            }
            catch (Exception)
            {
                // 启动期 InfoService 自身可能还不可用，记日志不能再引发二次异常
            }
        }
    }

    /// <summary>默认窗口矩形：1280x800，放在主屏工作区中央</summary>
    private static WindowPlacement DefaultPlacement(RectInt32 primary) => new(
        primary.X + Math.Max((primary.Width - DefaultWindowWidth) / 2, 0),
        primary.Y + Math.Max((primary.Height - DefaultWindowHeight) / 2, 0),
        DefaultWindowWidth, DefaultWindowHeight, false);

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
