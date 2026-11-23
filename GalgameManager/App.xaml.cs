using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using CommunityToolkit.Mvvm.Messaging;
using GalgameManager.Activation;
using GalgameManager.Contracts.Services;
using GalgameManager.Core.Contracts.Services;
using GalgameManager.Core.Services;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Models;
using GalgameManager.Services;
using GalgameManager.ViewModels;
using GalgameManager.Views;
using H.NotifyIcon;
using H.NotifyIcon.EfficiencyMode;
using LaunchActivatedEventArgs = Microsoft.UI.Xaml.LaunchActivatedEventArgs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using UnhandledExceptionEventArgs = Microsoft.UI.Xaml.UnhandledExceptionEventArgs;
using WindowExtensions = H.NotifyIcon.WindowExtensions;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using SkiaSharp;
using AppInstance = Microsoft.Windows.AppLifecycle.AppInstance;

namespace GalgameManager;

// To learn more about WinUI 3, see https://docs.microsoft.com/windows/apps/winui/winui3/.
public partial class App : Application
{
    // The .NET Generic Host provides dependency injection, configuration, logging, and other services.
    // https://docs.microsoft.com/dotnet/core/extensions/generic-host
    // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
    // https://docs.microsoft.com/dotnet/core/extensions/configuration
    // https://docs.microsoft.com/dotnet/core/extensions/logging
    public IHost Host
    {
        get;
    }

    public static T GetService<T>() where T : class
    {
           if ((Current as App)!.Host.Services.GetService(typeof(T)) is not T service)
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");
        return service;
    }

    public static T GetResource<T>(string key)
    {
        if (Current.Resources[key] is not T resource)
            throw new ArgumentException($"{key} needs to be registered in Resource.xaml.");
        return resource;
    }

    private static Application _instance = null!;
    public static WindowEx? MainWindow { get; set; }
    public static TaskbarIcon? SystemTray { get; set; }

    /// <summary>
    /// 进入系统托盘前窗口是否处于最大化。<br/>
    /// WindowExtensions.Show 底层用的是 SW_NORMAL(1)，会把最大化打回普通尺寸，
    /// 所以改走 SW_SHOW(5) 并在这里记录/补回最大化状态。
    /// </summary>
    private static bool _wasMaximizedBeforeTray;

    public static UIElement? AppTitlebar { get; set; }
    public static WindowMode Status = WindowMode.Booting;
    public static event Action? OnAppClosing;
    public static DispatcherQueue DispatcherQueue { get; } = DispatcherQueue.GetForCurrentThread();
    /// <summary>安全模式（崩溃后自动重启）启动后在这段时间内再次崩溃，视为崩溃循环，不再自动重启</summary>
    private static readonly TimeSpan CrashLoopThreshold = TimeSpan.FromSeconds(60);
    private static bool _isHandlingUnhandledException;

    public App()
    {
        InitializeComponent();
        PluginXamlHost.Initialize(this);

        Host = Microsoft.Extensions.Hosting.Host.
        CreateDefaultBuilder().
        UseContentRoot(AppContext.BaseDirectory).
        ConfigureServices((context, services) =>
        {
            // 启动跳转处理
            // 从前往后依次处理，直到找到能处理的处理器
            // Launch Activation Handlers
            services.AddTransient<IActivationHandler, JumpListActivationHandler>();     // JumpList
            // Protocol Activation Handlers
            services.AddTransient<IActivationHandler, BgmOAuthActivationHandler>();     // BgmOAuth
            // Default Handler
            services.AddTransient<IActivationHandler, DefaultActivationHandler>();      // 启动页

            // Services
            services.AddTransient<INavigationViewService, NavigationViewService>();
            services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
            services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
            services.AddSingleton(TimeProvider.System);
            services.AddTransient<IJumpListService, JumpListService>();
            services.AddSingleton<IActivationService, ActivationService>();
            services.AddSingleton<IPageService, PageService>();
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<IGalgameCollectionService, GalgameCollectionService>();
            services.AddSingleton<IGalgameSourceCollectionService, GalgameSourceCollectionService>();
            services.AddSingleton<IGameLaunchService, GameLaunchService>();
            // Source Services
            services.AddSingleton<LocalFolderSourceService>();
            services.AddSingleton<VirtualSourceService>();
            services.AddSingleton<SteamSourceService>();
            services.AddSingleton<ZipSourceService>();
            //
            services.AddSingleton<IFilterService, FilterService>();
            services.AddSingleton<ICategoryService, CategoryService>();
            services.AddSingleton<IStaffService, StaffService>();
            services.AddSingleton<IAuthenticationService, AuthenticationService>();
            services.AddSingleton<IBgmOAuthService, BgmOAuthService>();
            services.AddSingleton<IInfoService, InfoService>();
            services.AddSingleton<ISidebarService, SidebarService>();
            services.AddSingleton<IBgTaskService, BgTaskService>();
            services.AddSingleton<IPvnService, PvnService>();
            services.AddSingleton<IVndbAuthService, VndbAuthService>();
            services.AddSingleton<ISteamService, SteamService>();
            services.AddSingleton<ISourceScanResultService, SourceScanResultService>();
            services.AddSingleton<IPluginService, PluginService>();
            // Main Bus
            services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

            // Core Services
            services.AddSingleton<IFileService, FileService>();

            // Views and ViewModels
            services.AddTransient<AnnualReportViewModel>();
            services.AddTransient<AnnualReportPage>();
            services.AddTransient<MultiStreamViewModel>();
            services.AddTransient<Views.MultiStreamPage>();
            services.AddTransient<InfoViewModel>();
            services.AddTransient<InfoPage>();
            services.AddTransient<AccountViewModel>();
            services.AddTransient<AccountPage>();
            services.AddTransient<CategoryViewModel>();
            services.AddTransient<CategoryPage>();
            services.AddTransient<CategorySettingViewModel>();
            services.AddTransient<CategorySettingPage>();
            services.AddTransient<GalgameSettingViewModel>();
            services.AddTransient<GalgameSettingPage>();
            services.AddTransient<PlayedTimePage>();
            services.AddTransient<PlayedTimeViewModel>();
            services.AddTransient<PluginStoreViewModel>();
            services.AddTransient<PluginStorePage>();
            services.AddTransient<LibraryViewModel>();
            services.AddTransient<LibraryPage>();
            services.AddTransient<GalgameSourceViewModel>();
            services.AddTransient<GalgameSourcePage>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<SettingsPage>();
            services.AddTransient<GalgameViewModel>();
            services.AddTransient<HomeDetailPage>();
            services.AddTransient<HomeViewModel>();
            services.AddTransient<HomePage>();
            services.AddTransient<ShellPage>();
            services.AddTransient<ShellViewModel>();
            services.AddTransient<GalgameCharacterPage>();
            services.AddTransient<GalgameCharacterViewModel>();
            services.AddTransient<StaffViewModel>();
            services.AddTransient<StaffPage>();
            services.AddTransient<ScanResultViewModel>();
            services.AddTransient<ScanResultPage>();
            services.AddTransient<PluginViewModel>();
            services.AddTransient<PluginPage>();
            services.AddTransient<PluginHostPage>();
            services.AddTransient<PluginHostPageViewModel>();

            // Configuration
            services.Configure<LocalSettingsOptions>(context.Configuration.GetSection(nameof(LocalSettingsOptions)));
        }).
        Build();

        UnhandledException += App_UnhandledException;
        AppInstance.GetCurrent().Activated += OnActivated;
    }

    private void App_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // https://docs.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.application.unhandledexception.
        e.Handled = true;
        if (_isHandlingUnhandledException) return; // 崩溃诊断探测过程中再次触发的异常，不重复处理
        _isHandlingUnhandledException = true;
        try
        {
            ILocalSettingsService? localSettingsService = null;
            try
            {
                localSettingsService = GetService<ILocalSettingsService>();
                var errMsg = localSettingsService.ReadSettingAsync<string>(KeyValues.LastError).Result ?? string.Empty;
                errMsg += (errMsg.Length > 0 ? "\n\n" : string.Empty) + e.Message + "\n" + e.Exception;
                localSettingsService.SaveSettingAsync(KeyValues.LastError, errMsg);
            }
            catch
            {
                // 设置服务本身可能就是异常来源（如启动时设置数据库被占用），此时只能放弃记录
            }
            WriteCrashReport(e);
            if (Status == WindowMode.Booting) return;
            if (ActivationService.IsSafeMode() && CrashDiagnostics.Uptime < CrashLoopThreshold)
            {
                // 安全模式重启后很快又崩溃，说明重启解决不了问题：停止自动重启，避免无限重启循环（issue #742）
                // 同时清掉崩溃记录，否则下次启动时弹出的崩溃通知可能再次触发同样的崩溃
                localSettingsService?.RemoveSettingAsync(KeyValues.LastError);
                ExitAfterCrashLoop(Path.Combine(AppStoragePaths.LocalDataPath, "Logs"));
                return;
            }
            AppInstance.Restart("/safemode");
        }
        catch
        {
            // 不能让异常逃出本函数：WinUI 会把它再次交给本函数，无限递归直至栈溢出
        }
        finally
        {
            _isHandlingUnhandledException = false;
        }
    }

    private static void WriteCrashReport(UnhandledExceptionEventArgs e)
    {
        try
        {
            CrashDiagnostics.WriteCrashReport(GetService<IInfoService>(), e.Exception, e.Message, MainWindow?.Content);
        }
        catch (Exception)
        {
            // 诊断信息尽力而为，不能影响后续的崩溃处理
        }
    }

    private static void ExitAfterCrashLoop(string logFolder)
    {
        CrashDiagnostics.ShowCrashLoopMessage(logFolder);
        try
        {
            SystemTray?.Dispose();
            Serilog.Log.CloseAndFlush();
        }
        catch (Exception)
        {
            // ignored
        }
        Process.GetCurrentProcess().Kill();
    }

    /// <summary>
    /// 应用启动入口
    /// </summary>
    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);
        if (await RedirectToMainInstanceAsync())
        {
            Exit();
            return;
        }

        // 完整备份/恢复：必须在解析任何服务之前执行。
        // 运行中的实例独占 pvn_data.db 与 pvn_settings.db，替换这两个文件夹只能在"没有实例在跑"时进行；
        // 而免安装版的 LocalSettingsService 一构造就会打开设置数据库（见 RedirectToMainInstanceAsync 注释）。
        // 进度窗口要一直留到启动流程结束再关闭：主窗口此时才被创建，提前关掉最后一个窗口会让应用直接退出。
        FullBackupWindow? fullBackupWindow = null;
        FullBackupResult? fullBackupResult = null;
        try
        {
            FullBackupService service = FullBackupService.CreateDefault();
            if (service.HasPendingRequest())
            {
                fullBackupWindow = new FullBackupWindow();
                fullBackupWindow.Activate();
                fullBackupResult = await service.RunPendingAsync((key, arg) =>
                    fullBackupWindow.DispatcherQueue.TryEnqueue(() => fullBackupWindow.SetStatus(key, arg)));
            }
        }
        catch (Exception e)
        {
            fullBackupResult = FullBackupResult.Fail("FullBackup_Fail", e.Message);
        }

        // 恢复用户保存的语言。WinRT 版 Windows.Globalization.ApplicationLanguages 的覆盖值
        // 在免安装/便携版下既写不进去也带不到下次启动，所以每次启动都从设置数据库读回，
        // 写入 WinAppSDK 版 Microsoft.Windows.Globalization.ApplicationLanguages（它面向
        // 免安装场景，写入本地应用数据）。必须放在完整备份之后、解析其它服务之前，
        // 保证后续所有 GetLocalized() 与 XAML 绑定都拿到正确语言。
        try
        {
            ILocalSettingsService settings = GetService<ILocalSettingsService>();
            LanguageEnum language = await settings.ReadSettingAsync<LanguageEnum>(KeyValues.Language);
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride =
                SettingsViewModel.GetLanguageTag(language);
        }
        catch (Exception e)
        {
            // 语言恢复失败不能阻断启动：保持默认语言继续跑
            try
            {
                GetService<IInfoService>().DeveloperEvent(e: e);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        // 设置LiveCharts字体
        LiveCharts.Configure(config => config.HasGlobalSKTypeface(SKFontManager.Default.MatchCharacter('汉')));
        _instance = this;
#pragma warning disable CS0618 // 计划的一部分
        WinApp.Base.Helpers.UiThreadInvokeHelper.Init(DispatcherQueue);
#pragma warning restore CS0618 //
        Status = WindowMode.Booting;
        try
        {
            await GetService<IActivationService>().LaunchedAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
        }
        finally
        {
            fullBackupWindow?.Close();
        }

        if (fullBackupResult is not null)
            GetService<IInfoService>().Info(fullBackupResult.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error,
                fullBackupResult.MessageKey.GetLocalized(), fullBackupResult.MessageArg);
    }

    /// <summary>
    /// 多实例启动时，把激活转交给第一实例（由其 <see cref="OnActivated"/> 响应）<br/>
    /// 必须在解析任何服务之前调用：免安装版的 LocalSettingsService 构造时就会打开设置数据库，
    /// 而该数据库正被第一实例独占
    /// </summary>
    /// <returns>是否已转交，若是则当前实例应直接退出</returns>
    private static async Task<bool> RedirectToMainInstanceAsync()
    {
        AppInstance current = AppInstance.GetCurrent();
        IList<AppInstance> instances = AppInstance.GetInstances();
        if (instances.Count <= 1 || instances[0].ProcessId == current.ProcessId) return false;
        await instances[0].RedirectActivationToAsync(current.GetActivatedEventArgs());
        return true;
    }

    private async void OnActivated(object?_, AppActivationArguments arguments)
    {
        if (Status == WindowMode.Booting) return; // 应用启动时不响应激活，避免启动软件时启动多次
        // 正在退出的实例不再响应激活：此时设置库已被 OnAppClosing 释放，再走一遍
        // SetWindowMode(Normal) 只会抛 ObjectDisposedException，升级成崩溃重启循环。
        if (Status == WindowMode.Close) return;
        await GetService<IActivationService>().HandleActivationAsync(arguments);
        await UiThreadInvokeHelper.InvokeAsync(() =>
        {
            SetWindowMode(WindowMode.Normal);
        });
    }

    /// <summary>
    /// 设置窗口模式<br/>
    /// <para>
    /// 系统托盘模式只隐藏窗口、不重启进程：托盘图标随进程生灭，重启会让图标先消失、
    /// 等新进程走完启动流程才重建。历史上曾改为重启以规避关闭窗口后内存无法释放的问题
    /// （https://github.com/microsoft/microsoft-ui-xaml/issues/9063 、
    /// https://github.com/microsoft/microsoft-ui-xaml/issues/7282），
    /// 现已回退为隐藏，并显式补齐 <see cref="Status"/> 置位以切断 Closing 的重入链。
    /// </para>
    /// </summary>
    /// <param name="mode"></param>
    public static void SetWindowMode(WindowMode mode)
    {
        CrashDiagnostics.AddBreadcrumb($"WindowMode -> {mode}");
        switch (mode)
        {
            case WindowMode.Normal:
                GetService<IPageService>().InitAsync();
                // 不用 WindowExtensions.Show：它的底层是 ShowWindow(hwnd, SW_NORMAL=1)，
                // 而 SW_NORMAL 会把「最小化/最大化/排列中」的窗口还原成原始尺寸——
                // 从托盘恢复时最大化会在一瞬间被打回普通窗口。改用 SW_SHOW(5)，
                // 实测它能完整保留最大化状态。
                ShowWindowSwShow(MainWindow!);
                // WindowExtensions.Show 原本负责的解除 EcoQoS，这里手动补上
                EfficiencyModeUtilities.SetEfficiencyMode(false);
                // Hide 之前记下的最大化状态在这里补回来：SW_SHOW 不改变窗口状态，
                // 但隐藏/显示一轮后 presenter 状态可能已被重置，按记录值兜底最稳。
                if (_wasMaximizedBeforeTray)
                {
                    _wasMaximizedBeforeTray = false;
                    if (MainWindow?.AppWindow.Presenter is OverlappedPresenter restorePresenter)
                        restorePresenter.Maximize();
                }
                MainWindow!.Activate();
                MainWindow!.BringToFront();
                MainWindow.Content.Visibility = Visibility.Visible;
                // 窗口位置恢复：必须放在这里——Activate/BringToFront/Visibility 之后，
                // 窗口已确定激活并显示，MoveAndResize 才有 HWND 可作用。
                // RestoreWindowPlacementOnce 内部有 _placementRestored 一次性守卫，
                // 所以从托盘反复恢复不会把用户拖好的窗口重置回存档值。
                PageService.RestoreWindowPlacementOnce();
                try
                {
                    if (GetService<ILocalSettingsService>().ReadSettingAsync<string>(KeyValues.LastError)
                            .Result is { } error)
                    {
                        GetService<IInfoService>().Event(EventType.AppError, InfoBarSeverity.Error,
                            "App_Error".GetLocalized(), msg: error);
                    }
                }
                catch (Exception)
                {
                    // 设置库可能已被 OnAppClosing 释放（异常路径下残留的实例）。
                    // 崩溃通知是尽力而为，不能让它把恢复流程带崩。
                }
                Status = WindowMode.Normal;
                break;
            case WindowMode.Minimize:
                Status = WindowMode.Minimize;
                MainWindow!.Minimize();
                break;
            case WindowMode.SystemTray:
                // 只隐藏窗口，不再重启进程。托盘图标是随进程生灭的：旧实现走
                // MainWindow.Close() + AppInstance.Restart("/r")，旧进程退出时图标随之消失，
                // 新进程要等整个启动流程跑完才 ForceCreate 出新图标，用户看到的就是一次闪烁。
                // 此处也不能调用 OnAppClosing——它的订阅者（LocalSettingsService 释放设置/数据
                // 数据库、LocalFolderSourceService 销毁所有 FileSystemWatcher）都是进程退出级的
                // 不可逆清理；隐藏后若从托盘恢复，设置写入与文件夹监听都会失效。
                GetService<IBgTaskService>().SaveBgTasksString();
                // 必须在 Hide 之前把窗口位置写盘：Hide 之后窗口进入隐藏态，
                // 之后再也没有合适的事件能拿到可靠的普通窗口矩形。
                _ = SaveWindowPlacementAsync();
                // 记下当前是否最大化。Normal 分支的 SW_SHOW 不改窗口状态，但隐藏/显示
                // 一轮后 presenter 状态可能被重置，按这里的记录值补 Maximize 最稳。
                _wasMaximizedBeforeTray = MainWindow?.AppWindow.Presenter is OverlappedPresenter trayPresenter
                                          && trayPresenter.State == OverlappedPresenterState.Maximized;
                // 必须置位 Status：ShellPage 的 Closing 处理器只靠 Status == Close 做重入守卫，
                // 少了这一行，Close() -> Closing -> SetWindowMode -> Close() 会无限递归。
                Status = WindowMode.SystemTray;
                WindowExtensions.Hide(MainWindow!);
                break;
            case WindowMode.Close:
                Status = WindowMode.Close;
                // 必须在 OnAppClosing 之前写盘。托盘右键退出走的是 CloseAppCommand ->
                // SetWindowMode(Close)，完全绕过 ShellPage.MainWindowOnClosed 里的保存，
                // 少了这一行，那条路径永远存不下窗口位置。OnAppClosing 会释放设置数据库，
                // 之后再写就落不进盘了。
                _ = SaveWindowPlacementAsync();
                OnAppClosing?.Invoke();
                SystemTray?.Dispose();
                // 先关窗再 Exit：托盘模式下窗口是被 SW_HIDE 藏起来的，仅靠 Exit()
                // 拆不掉这种窗口，进程会残留成僵尸，之后每次启动都会被单实例重定向
                // 到这个已释放设置库的僵尸上，表现为「退出后打不开」。
                // ShellPage.MainWindowOnClosed 有 Status == Close 的重入守卫，不会再次 Cancel。
                MainWindow?.Close();
                _instance.Exit();
                // 兜底计时：正常退出与文件落盘优先，所以这里只做延时观察而非同步
                // Environment.Exit——那会打断 LocalSettingsService.OnAppClosing 里的
                // WaitForWriteFinishAsync()。1.5 秒后进程仍在，说明 Exit() 没兜住，
                // 此时强制结束，保证绝不会变成永久僵尸。
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1500);
                    Environment.Exit(0);
                });
                break;
            case WindowMode.None:
                break;
        }
    }

    /// <summary>
    /// 把当前窗口的位置/尺寸/最大化状态写进设置。<br/>
    /// 必须在 <see cref="SetWindowMode"/> 之前调用：WindowMode.Close 分支会触发
    /// OnAppClosing 释放设置数据库，之后再写就落不进盘了。
    /// </summary>
    public static async Task SaveWindowPlacementAsync()
    {
        WindowPlacement placement;
        if (WindowPlacementHelper.LastRestored is { } tracked)
        {
            placement = tracked;
        }
        else
        {
            // LastRestored 为 null 说明 AppWindow.Changed 从未上报过普通窗口矩形
            // （例如用户全程没动过窗口）。此时直接读当前窗口几何兜底，
            // 不能静默 return——那会让窗口位置永远存不下来。
            if (App.MainWindow?.AppWindow.Presenter is not OverlappedPresenter presenter
                || presenter.State != OverlappedPresenterState.Restored) return;
            AppWindow appWindow = App.MainWindow.AppWindow;
            placement = new WindowPlacement(appWindow.Position.X, appWindow.Position.Y,
                appWindow.Size.Width, appWindow.Size.Height, false);
            WindowPlacementHelper.Track(placement);
        }

        OverlappedPresenterState state = App.MainWindow?.AppWindow.Presenter is OverlappedPresenter current
            ? current.State
            : OverlappedPresenterState.Restored;
        await GetService<ILocalSettingsService>().SaveSettingAsync(KeyValues.WindowPlacement,
            placement with { IsMaximized = state == OverlappedPresenterState.Maximized });
    }

    /// <summary>
    /// 以 SW_SHOW(5) 显示窗口。<br/>
    /// 不用 <see cref="WindowExtensions.Show(Microsoft.UI.Xaml.Window,bool)"/>：它底层是
    /// ShowWindow(hwnd, SW_NORMAL=1)，会把最大化窗口还原成普通尺寸（已反编译 + Win32 实测确认）。
    /// </summary>
    private static void ShowWindowSwShow(WindowEx window)
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _ = ShowWindow(hwnd, SwShow);
    }

    private const int SwShow = 5; // SW_SHOW：激活并按当前尺寸位置显示，不改窗口状态

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    /// <summary>
    /// 当前版本是否为微软应用商店版
    /// </summary>
    /// <returns></returns>
    public static bool IsStoreVersion()
    {
        const string storeVersionPackageName = "37126GoldenPotato137.PotatoVN";
        if (!RuntimeHelper.IsMSIX) return false;
        var packageName = Package.Current.Id.Name;
        // 商店版的包名是固定的，其他都是侧载版
        return packageName == storeVersionPackageName;
    }

}
