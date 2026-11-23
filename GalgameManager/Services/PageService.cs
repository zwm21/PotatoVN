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
            UIElement shell = App.GetService<ShellPage>();
            App.MainWindow.Content = shell;
            await App.GetService<IThemeSelectorService>().SetRequestedThemeAsync();
            await App.GetService<IThemeSelectorService>().SetBackgroundMaterialAsync();
            await App.GetService<IThemeSelectorService>().SetNavigationViewTransparencyAsync();
            OnInit?.Invoke();
        }
    }

    private static bool _placementRestored;

    /// <summary>
    /// 在窗口首次显示后恢复上次记住的位置/尺寸/最大化状态，只执行一次。<br/>
    /// 必须由 <see cref="App.SetWindowMode"/> 的 WindowMode.Normal 分支在
    /// Activate()/BringToFront()/Content.Visibility 之后显式调用——那时窗口必然已激活、
    /// 已显示，MoveAndResize 一定有 HWND 可作用。<br/>
    /// 不能改成订阅 Window.Activated：SW_SHOW 那一步就会激活窗口，等订阅方挂好时
    /// 激活态可能已不再变化，Activated 一次都不会来，恢复就永远不执行
    /// （表现为窗口停在 CW_USEDEFAULT、每次位置随机）。<br/>
    /// <see cref="_placementRestored"/> 守卫必须保留：SetWindowMode(Normal) 在每次
    /// 从托盘恢复时都会走一遍，没有守卫会把用户拖好的窗口强行重置回存档值。
    /// </summary>
    public static void RestoreWindowPlacementOnce()
    {
        if (_placementRestored) return;
        _placementRestored = true;
        CrashDiagnostics.AddBreadcrumb("RestoreWindowPlacementOnce: enter");
        RestoreWindowPlacement();
    }

    /// <summary>
    /// 主窗口默认尺寸。<br/>
    /// <see cref="MainWindow"/> 的 XAML 里也声明了同样的 Width/Height：那只是初始值，
    /// 在 InitializeComponent 时就落定，早于这里的恢复，二者不冲突；XAML 值同时保证
    /// 无存档时窗口也有个合理尺寸，不必完全依赖代码路径。
    /// </summary>
    private const int DefaultWindowWidth = 1280;
    private const int DefaultWindowHeight = 800;

    /// <summary>
    /// 恢复上次退出时记住的窗口位置/尺寸/最大化状态。<br/>
    /// 由 <see cref="RestoreWindowPlacementOnce"/> 在窗口首次显示后调用——必须晚于
    /// 窗口首次显示，否则 MoveAndResize/Maximize 会被框架确立窗口几何的过程整个丢掉。<br/>
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

            WindowPlacement target = saved is null
                ? fallback
                : WindowPlacementHelper.Sanitize(saved, fallback, workAreas);
            bool fromSave = saved is not null;

            AppWindow appWindow = App.MainWindow!.AppWindow;
            PointInt32 posBefore = appWindow.Position;
            SizeInt32 sizeBefore = appWindow.Size;
            string stateBefore = appWindow.Presenter?.GetType().Name ?? "<null>";
            if (appWindow.Presenter is OverlappedPresenter beforePresenter)
                stateBefore += $"/{beforePresenter.State}";

            appWindow.MoveAndResize(new RectInt32(target.X, target.Y, target.Width, target.Height));
            if (target.IsMaximized && appWindow.Presenter is OverlappedPresenter presenter)
                presenter.Maximize();

            PointInt32 posAfter = appWindow.Position;
            SizeInt32 sizeAfter = appWindow.Size;

            // 用恢复结果初始化跟踪基线：否则 LastRestored 一直是 null，
            // 用户若全程没动过窗口，保存侧就永远写不出东西。
            WindowPlacementHelper.Track(target with { IsMaximized = false });

            // 追踪日志：直接走 Serilog 打到 log.txt，不受开发者模式开关影响
            // （DeveloperEvent 在非开发者模式下会静默 return，之前正是因此抓不到问题）。
            // 同时打 MoveAndResize 前后的 Position/Size，用来区分「静默 no-op」
            // 和「生效后被别的东西覆盖」这两种完全不同的失败模式。
            Serilog.Log.Information(
                "WindowPlacement restore: source={Source} saved={Saved} target=({TX},{TY},{TW}x{TH}) max={Max} " +
                "before=({BX},{BY},{BW}x{BH}/{BState}) after=({AX},{AY},{AW}x{AH})",
                fromSave ? "saved" : "fallback",
                saved?.ToString() ?? "null",
                target.X, target.Y, target.Width, target.Height, target.IsMaximized,
                posBefore.X, posBefore.Y, sizeBefore.Width, sizeBefore.Height, stateBefore,
                posAfter.X, posAfter.Y, sizeAfter.Width, sizeAfter.Height);

            CrashDiagnostics.AddBreadcrumb(
                $"RestoreWindowPlacement: applied {(fromSave ? "saved" : "fallback")} " +
                $"pos=({target.X},{target.Y}) size=({target.Width}x{target.Height}) " +
                $"maximized={target.IsMaximized}");
        }
        catch (Exception e)
        {
            // 窗口位置是锦上添花：读设置失败、显示器信息异常、对尚未激活的窗口调用
            // MoveAndResize/Maximize 失败，都只应该回退到默认摆放，绝不允许把启动
            // 流程带崩——那会升级成崩溃重启循环，软件直接打不开。
            // 用 Serilog.Log.Error 直接落盘：DeveloperEvent 受 DevelopmentMode 开关限制，
            // 非开发者模式下会被静默丢弃，异常就彻底看不见了。
            Serilog.Log.Error(e, "WindowPlacement restore failed");
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
