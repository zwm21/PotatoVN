using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GalgameManager.Contracts.Services;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Models;
using GalgameManager.Models.Sources;
using GalgameManager.ViewModels;
using GalgameManager.WinApp.Base.Models.Msgs;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GalgameManager.Services;

/// <summary>
/// 在系统托盘右键菜单的顶部平铺「最近游玩」游戏项。<br/>
/// 菜单在运行时直接挂到 <see cref="App.SystemTray"/> 的 <see cref="TaskbarIcon.ContextFlyout"/> 上——
/// 刻意不改 <c>Resource.xaml</c>：那是个共享的 <c>ResourceDictionary</c>，往里面加
/// <c>x:Name</c> 会让同一实例被多处引用时产生歧义。<br/>
/// 首版只收录「本地 exe 可一键启动」的游戏（见 <see cref="RecentGamesHelper.IsOneClickLaunchable"/>），
/// 这样点击后可以直接走到 <c>Process.Start()</c>，不触发任何 ContentDialog——
/// 主窗口在托盘隐藏态时那些对话框用户根本看不见。<br/>
/// <b>线程亲和性：必须在 UI 线程上首次解析。</b>构造函数之后的菜单操作都会 new XAML 控件
/// （MenuFlyoutItem / FontIcon / MenuFlyoutSeparator），在线程池线程（MTA）上构造会抛
/// COMException 0x8001010E(RPC_E_CHANGED_MODE)。现有解析点都在 UI 线程上；新增时务必保持。
/// </summary>
public class TrayMenuService : IDisposable
{
    /// <summary>播放图标。Segoe MDL2 Assets 通用码点，Win10 17763+ 可用；与 GalgamePage 的开始游戏按钮同一码点。</summary>
    private const string PlayGlyph = "\uE768";

    private readonly GalgameCollectionService _gameService;
    private readonly IGalgameSourceCollectionService _sourceService;
    private readonly IGameLaunchService _launchService;
    private readonly IInfoService _infoService;
    private readonly INavigationService _navigationService;
    private readonly IMessenger _messenger;

    /// <summary>
    /// 本服务动态插入托盘菜单的项与分隔线。用托管 List 记录，是为了移除时不必去遍历
    /// <see cref="MenuFlyout.Items"/>——那个集合禁止 LINQ/foreach（WinRT 裁剪红线）。
    /// </summary>
    private readonly List<MenuFlyoutItemBase> _dynamicItems = [];
    private readonly List<Galgame> _recentGames = [];

    private ElementTheme _appliedTheme = ElementTheme.Default;
    private Style? _presenterStyle;
    private bool _disposed;

    public TrayMenuService(IGalgameCollectionService gameService,
        IGalgameSourceCollectionService sourceCollectionService, IGameLaunchService launchService,
        IInfoService infoService, INavigationService navigationService, IMessenger messenger)
    {
        // GalgameLoadedEvent 只在具体类上（接口未声明），与 GameLaunchService /
        // JumpListActivationHandler 内部转换为具体类的做法保持一致。
        _gameService = (GalgameCollectionService)gameService;
        _sourceService = sourceCollectionService;
        _launchService = launchService;
        _infoService = infoService;
        _navigationService = navigationService;
        _messenger = messenger;

        _gameService.GalgameLoadedEvent += OnGalgameLoaded;
        _gameService.GalgameDeletedEvent += OnGalgameDeleted;
        // 增删游戏库会改变哪些游戏具备本地安装实例，进而改变「最近游玩」的筛选结果。
        // 注意初始加载不发这个事件（见 ActivationService 里的显式 Rebuild）。
        _sourceService.OnSourceChanged += OnSourceChanged;
        // 任何入口（主页、详情页、JumpList、本托盘菜单）成功启动游戏后都会发这条消息，
        // 靠它把「最近游玩」刷新做成实时，不必等重启。注册写法与 ShellPage 对
        // GalgamePlayedMessage 的订阅保持一致；本类是 Singleton，无需担心弱引用回收。
        _messenger.Register<GalgamePlayedMessage>(this, (_, _) => Rebuild());
    }

    /// <summary>
    /// 按当前数据重建托盘菜单顶部的「最近游玩」区。筛选结果为空时不插入任何项，连分隔线也不留。<br/>
    /// 数据可能来自后台线程（<see cref="IGalgameCollectionService.GalgameLoadedEvent"/> 即如此），
    /// 菜单操作必须切回 UI 线程。
    /// </summary>
    public void Rebuild()
    {
        // 用同步 lambda 而不是 async lambda：UiThreadInvokeHelper.InvokeAsync 有 Action 与
        // Func<Task> 两个重载，同步 lambda 会绑到 Action 那个，不会为了没有 await 的 async
        // 白背一个 CS1998 警告。异常必须自己接住——EnqueueAsync 返回的 Task 被 `_ =` 丢弃后，
        // 里面的异常会变成未观察异常静默消失，连 Serilog 都不会留下痕迹。
        _ = UiThreadInvokeHelper.InvokeAsync(() =>
        {
            try
            {
                RebuildOnUiThread();
            }
            catch (Exception e)
            {
                Serilog.Log.Error(e, "TrayMenuService: rebuild failed");
            }
        });
    }

    private void RebuildOnUiThread()
    {
        if (_disposed) return;
        if (App.SystemTray?.ContextFlyout is not MenuFlyout flyout) return;

        // 主题要跟着每次重建一起落实：SetRequestedThemeAsync 首次执行时托盘图标还没创建，
        // 那次 ApplyTheme 是空转，只能靠这里补上。
        ApplyTheme(ResolveEffectiveTheme());

        _recentGames.Clear();
        List<Galgame> selected = RecentGamesHelper.SelectRecentGames(_gameService.Galgames);
        for (var i = 0; i < selected.Count; i++) _recentGames.Add(selected[i]);

        // 先摘掉上一次动态插入的项。严禁对 MenuFlyout.Items 用 LINQ 或 foreach：
        // WinRT 投影集合的 IEnumerable 接口在便携版裁剪下已被移除，会抛
        // InvalidCastException（见 AGENTS.md 的 Hard Constraints）。
        for (var i = 0; i < _dynamicItems.Count; i++)
            flyout.Items.Remove(_dynamicItems[i]);
        _dynamicItems.Clear();

        // 结果为空时不插入任何项，连分隔线也不留
        for (var i = 0; i < _recentGames.Count; i++)
        {
            Galgame game = _recentGames[i];
            MenuFlyoutItem item = new()
            {
                Text = RecentGamesHelper.FormatMenuTitle(game.Name.Value ?? game.Uuid.ToString("D")),
                // 用 Command 而非 Click 事件：与既有托盘项（SetWindowNormalCommand /
                // CloseAppCommand）保持一致，且对 H.NotifyIcon 的各 ContextMenuMode 都成立。
                Command = new AsyncRelayCommand(() => LaunchFromTrayAsync(game)),
                Icon = new FontIcon
                {
                    Glyph = PlayGlyph,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                },
            };
            flyout.Items.Insert(i, item);
            _dynamicItems.Add(item);
        }

        if (_recentGames.Count == 0) return;
        MenuFlyoutSeparator separator = new();
        flyout.Items.Insert(_recentGames.Count, separator);
        _dynamicItems.Add(separator);
    }

    /// <summary>
    /// 把明暗主题应用到托盘右键菜单。<br/>
    /// <see cref="TaskbarIcon.ContextMenuMode"/> 默认是 <c>SecondWindow</c>，菜单由一个独立的
    /// WinUI Window 承载（反编译 H.NotifyIcon 2.0.118 的
    /// <c>ShowContextMenuInSecondWindowMode</c> 可见它对 AppWindow 做 MoveAndResize），
    /// 因此主窗口的 <c>RequestedTheme</c> 不会继承过来，必须显式设置。<br/>
    /// 做法是把 <see cref="FrameworkElement.RequestedTheme"/> 写到
    /// <see cref="MenuFlyout.MenuFlyoutPresenterStyle"/> 里：RequestedTheme 是继承 DP，
    /// 设到 presenter 上就能一并覆盖其下所有菜单项，避免出现「背景亮、文字暗」的半套主题。
    /// </summary>
    public void ApplyTheme(ElementTheme theme)
    {
        if (App.SystemTray?.ContextFlyout is not MenuFlyout flyout) return;
        if (_presenterStyle is not null && _appliedTheme == theme) return;
        _presenterStyle = CreatePresenterStyle(theme);
        _appliedTheme = theme;
        flyout.MenuFlyoutPresenterStyle = _presenterStyle;
    }

    private static Style CreatePresenterStyle(ElementTheme theme)
    {
        Style style = new(typeof(MenuFlyoutPresenter));
        style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, theme));
        return style;
    }

    /// <summary>
    /// 取当前生效的主题。直接读 <see cref="FrameworkElement.ActualTheme"/>：主题已应用时它是
    /// Light/Dark，尚未应用（Default）时回退 Light。
    /// </summary>
    private static ElementTheme ResolveEffectiveTheme() =>
        App.MainWindow?.Content is FrameworkElement { ActualTheme: not ElementTheme.Default } root
            ? root.ActualTheme
            : ElementTheme.Light;

    private async Task LaunchFromTrayAsync(Galgame game)
    {
        GalgameAndPath? installation = game.PreferredLocalInstallation;
        // 复检：菜单是异步重建的，菜单项显示期间 exe 可能已被移动/删除
        if (installation is null || !RecentGamesHelper.IsOneClickLaunchable(game))
        {
            DegradeToMainWindow(game);
            return;
        }

        try
        {
            await _launchService.LaunchAsync(game, installation);
        }
        catch (Exception e)
        {
            Serilog.Log.Error(e, "TrayMenuService: launch from tray failed, game={GameUuid}", game.Uuid);
            DegradeToMainWindow(game);
            Rebuild();
            return;
        }

        // PlayingWindowMode 已在 GameLaunchService.LaunchCoreAsync 源头拦截：
        // 启动前若 App.Status 就是 SystemTray，且用户设置不是 Close，那里会直接跳过
        // SetWindowMode，隐藏窗口不会被拉出来闪一下，_wasMaximizedBeforeTray 也不被破坏。
        // 这里不再做任何补调——补调只会造成「先弹再藏」的可见闪烁。
        Rebuild();
    }

    /// <summary>
    /// 无法一键启动时降级：拉起主窗口并导航到该游戏详情页，让用户在完整 UI 里操作。<br/>
    /// 与 <see cref="JumpListActivationHandler"/> 对 /j 启动参数的处理保持一致，
    /// 但 <see cref="GalgamePageParameter.StartGame"/> 传 false——不由程序替用户点开始。
    /// </summary>
    private void DegradeToMainWindow(Galgame game)
    {
        _infoService.Info(InfoBarSeverity.Warning, "Shell_SystemTray_RecentGamesLaunchFailed".GetLocalized());
        App.SetWindowMode(WindowMode.Normal);
        NavigationHelper.NavigateToGalgamePage(_navigationService,
            new GalgamePageParameter { Galgame = game, StartGame = false });
    }

    private void OnGalgameLoaded() => Rebuild();

    private void OnGalgameDeleted(Galgame galgame) => Rebuild();

    private void OnSourceChanged() => Rebuild();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gameService.GalgameLoadedEvent -= OnGalgameLoaded;
        _gameService.GalgameDeletedEvent -= OnGalgameDeleted;
        _sourceService.OnSourceChanged -= OnSourceChanged;
        _messenger.Unregister<GalgamePlayedMessage>(this);

        if (App.SystemTray?.ContextFlyout is MenuFlyout flyout)
        {
            for (var i = 0; i < _dynamicItems.Count; i++)
                flyout.Items.Remove(_dynamicItems[i]);
            _dynamicItems.Clear();
        }
    }
}
