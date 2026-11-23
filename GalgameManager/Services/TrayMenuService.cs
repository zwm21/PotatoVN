using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GalgameManager.Contracts.Services;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Models;
using GalgameManager.Models.Sources;
using GalgameManager.Services;
using GalgameManager.ViewModels;
using GalgameManager.WinApp.Base.Models.Msgs;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;

namespace GalgameManager.Services;

/// <summary>
/// 在系统托盘右键菜单的头部维护「最近游玩」二级子菜单。<br/>
/// 菜单在运行时直接挂到 <see cref="App.SystemTray"/> 的 <see cref="TaskbarIcon.ContextFlyout"/> 上——
/// 刻意不改 <c>Resource.xaml</c>：那是个共享的 <c>ResourceDictionary</c>，往里面加
/// <c>x:Name</c> 会让同一实例被多处引用时产生歧义。<br/>
/// 首版只收录「本地 exe 可一键启动」的游戏（见 <see cref="RecentGamesHelper.IsOneClickLaunchable"/>），
/// 这样点击后可以直接走到 <c>Process.Start()</c>，不触发任何 ContentDialog——
/// 主窗口在托盘隐藏态时那些对话框用户根本看不见。
/// </summary>
public class TrayMenuService : IDisposable
{
    private readonly GalgameCollectionService _gameService;
    private readonly IGameLaunchService _launchService;
    private readonly IInfoService _infoService;
    private readonly INavigationService _navigationService;
    private readonly IMessenger _messenger;

    private readonly MenuFlyoutSubItem _recentGamesItem = new();
    private readonly MenuFlyoutSeparator _separator = new();
    private readonly List<Galgame> _recentGames = [];
    private bool _disposed;

    public TrayMenuService(IGalgameCollectionService gameService, IGameLaunchService launchService,
        IInfoService infoService, INavigationService navigationService, IMessenger messenger)
    {
        // GalgameLoadedEvent 只在具体类上（接口未声明），与 GameLaunchService /
        // JumpListActivationHandler 内部转换为具体类的做法保持一致。
        _gameService = (GalgameCollectionService)gameService;
        _launchService = launchService;
        _infoService = infoService;
        _navigationService = navigationService;
        _messenger = messenger;

        _gameService.GalgameLoadedEvent += OnGalgameLoaded;
        _gameService.GalgameDeletedEvent += OnGalgameDeleted;
        // 任何入口（主页、详情页、JumpList、本托盘菜单）成功启动游戏后都会发这条消息，
        // 靠它把「最近游玩」刷新做成实时，不必等重启。注册写法与 ShellPage 对
        // GalgamePlayedMessage 的订阅保持一致；本类是 Singleton，无需担心弱引用回收。
        _messenger.Register<GalgamePlayedMessage>(this, (_, _) => Rebuild());
    }

    /// <summary>
    /// 把「最近游玩」子菜单挂到托盘右键菜单头部。必须在 <see cref="App.SystemTray"/> 创建后调用。
    /// </summary>
    public void Attach()
    {
        if (App.SystemTray?.ContextFlyout is not MenuFlyout flyout) return;
        // 幂等：重复挂载不产生两份
        if (flyout.Items.Contains(_recentGamesItem)) return;
        flyout.Items.Insert(0, _separator);
        flyout.Items.Insert(0, _recentGamesItem);
        Rebuild();
    }

    /// <summary>
    /// 按当前数据重建子菜单。结果为空时把子菜单与分隔线从托盘菜单里摘掉，不留下禁用占位项。<br/>
    /// 数据可能来自后台线程（<see cref="IGalgameCollectionService.GalgameLoadedEvent"/> 即如此），
    /// 菜单操作必须切回 UI 线程。
    /// </summary>
    public void Rebuild()
    {
        _ = UiThreadInvokeHelper.InvokeAsync(RebuildOnUiThread);
    }

    private void RebuildOnUiThread()
    {
        if (_disposed) return;
        if (App.SystemTray?.ContextFlyout is not MenuFlyout flyout) return;

        _recentGames.Clear();
        List<Galgame> selected = RecentGamesHelper.SelectRecentGames(_gameService.Galgames);
        for (var i = 0; i < selected.Count; i++) _recentGames.Add(selected[i]);

        // 严禁对 MenuFlyout.Items / MenuFlyoutSubItem.Items 用 LINQ 或 foreach：
        // WinRT 投影集合的 IEnumerable 接口在便携版裁剪下已被移除，会抛
        // InvalidCastException（见 AGENTS.md 的 Hard Constraints）。只用 Clear/Insert/Remove。
        _recentGamesItem.Items.Clear();
        for (var i = 0; i < _recentGames.Count; i++)
        {
            Galgame game = _recentGames[i];
            MenuFlyoutItem item = new()
            {
                Text = RecentGamesHelper.FormatMenuTitle(game.Name.Value ?? game.Uuid.ToString("D")),
                // 用 Command 而非 Click 事件：与既有托盘项（SetWindowNormalCommand /
                // CloseAppCommand）保持一致，且对 H.NotifyIcon 的各 ContextMenuMode 都成立。
                Command = new AsyncRelayCommand(() => LaunchFromTrayAsync(game)),
            };
            _recentGamesItem.Items.Add(item);
        }

        bool hasItems = _recentGamesItem.Items.Count > 0;
        bool attached = flyout.Items.Contains(_recentGamesItem);
        if (hasItems && !attached)
        {
            flyout.Items.Insert(0, _separator);
            flyout.Items.Insert(0, _recentGamesItem);
        }
        else if (!hasItems && attached)
        {
            flyout.Items.Remove(_recentGamesItem);
            flyout.Items.Remove(_separator);
        }
    }

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gameService.GalgameLoadedEvent -= OnGalgameLoaded;
        _gameService.GalgameDeletedEvent -= OnGalgameDeleted;
        _messenger.Unregister<GalgamePlayedMessage>(this);

        if (App.SystemTray?.ContextFlyout is MenuFlyout flyout)
        {
            flyout.Items.Remove(_recentGamesItem);
            flyout.Items.Remove(_separator);
        }
    }
}
