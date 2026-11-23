using System.Diagnostics;
using System.IO;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using GalgameManager.Activation;
using GalgameManager.Contracts.Services;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Views;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.AppLifecycle;

namespace GalgameManager.Services;

public class ActivationService : IActivationService
{
    public static object? ActivationArgs { get; private set; }
    private readonly IEnumerable<IActivationHandler> _activationHandlers;//
    private readonly IThemeSelectorService _themeSelectorService; //
    private readonly IGalgameSourceCollectionService _galgameFolderCollectionService;
    private readonly IGalgameCollectionService _galgameCollectionService;
    private readonly ICategoryService _categoryService;
    private readonly IStaffService _staffService;
    private readonly IAuthenticationService _authenticationService;
    private readonly IBgmOAuthService _bgmOAuthService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IFilterService _filterService;
    private readonly IPageService _pageService;
    private readonly IBgTaskService _bgTaskService;
    private readonly IPvnService _pvnService;
    private readonly IPluginService _pluginService;
    private readonly IInfoService _infoService;

    public ActivationService(
        IEnumerable<IActivationHandler> activationHandlers, IThemeSelectorService themeSelectorService,
        IGalgameSourceCollectionService galgameFolderCollectionService,
        IGalgameCollectionService galgameCollectionService,
        ICategoryService categoryService,IBgmOAuthService bgmOAuthService,
        IAuthenticationService authenticationService, ILocalSettingsService localSettingsService,
        IFilterService filterService, IPageService pageService, IBgTaskService bgTaskService, IPvnService pvnService,
        IInfoService infoService, IStaffService staffService, IPluginService pluginService)
    {
        _activationHandlers = activationHandlers;
        _themeSelectorService = themeSelectorService;
        _galgameFolderCollectionService = galgameFolderCollectionService;
        _galgameCollectionService = galgameCollectionService;
        _categoryService = categoryService;
        _bgmOAuthService = bgmOAuthService;
        _authenticationService = authenticationService;
        _localSettingsService = localSettingsService;
        _filterService = filterService;
        _pageService = pageService;
        _bgTaskService = bgTaskService;
        _pvnService = pvnService;
        _infoService = infoService;
        _staffService = staffService;
        _pluginService = pluginService;
    }

    public async Task LaunchedAsync(object activationArgs)
    {
        // Execute tasks before activation.
        await InitializeAsync();

        if (IsRestart() == false)
        {
            var result = await _authenticationService.StartAuthentication();
            if (!result)
            {
                Application.Current.Exit();
                return;
            }
        }

        try
        {
            await Task.Run(async () =>
            {
                _localSettingsService.InitDatabase();
                // 图片路径重定位：必须在任何 Service 把数据读进内存之前执行。
                // 便携包移动/重命名/跨版本升级、或从其他机器恢复备份后，DB 里持久化的
                // 旧绝对路径会全部失联；此处统一重定向到当前托管的 Images 目录。
                // 早于下方各 InitAsync()，也就早于 UI 绑定与 BgTask 里的 File.Delete。
                ImagePathRelocator.Relocate(_localSettingsService.Database,
                    Path.Combine(AppStoragePaths.LocalDataPath, "Images"));
                await _galgameCollectionService.InitAsync();
                await _galgameFolderCollectionService.InitAsync();
                await _categoryService.Init();
                await _staffService.InitAsync();
                await _filterService.InitAsync();
                await _localSettingsService.ImportPageSettingsAsync();
            });

            // GalgameLoadedEvent 在 Task.Run 内部就触发了，但那时 Galgame.SourceEntries 还是空的
            // （SourceEntries 标了 [JsonIgnore][BsonIgnore]，只由
            // GalgameSourceCollectionService.InitAsync 的 LoadData 填充），托盘菜单首轮必然
            // 全被过滤成空。而初始加载不会发 OnSourceChanged，之后也没有任何事件会补触发，
            // 所以必须在 SourceEntries 与 LocalConfig.ExePath 均就绪后显式重建一次。
            //
            // 必须放在 Task.Run 之后、try 之内：TrayMenuService 的字段初始化会 new
            // MenuFlyoutSubItem / MenuFlyoutSeparator，这两个 XAML 控件有线程亲和性，
            // 在 Task.Run 的线程池线程（MTA）上构造会抛
            // COMException 0x8001010E(RPC_E_CHANGED_MODE)，进而触发无限重启循环。
            // await Task.Run 之后已回到 UI 线程（OnLaunched 捕获的同步上下文）。
            App.GetService<TrayMenuService>().Rebuild();
        }
        catch (IOException) //数据库正在被占用
        {
            Application.Current.Exit();
            return;
        }
        catch (Exception e)
        {
            var backup = string.Empty;
            try
            {
                await _localSettingsService.BackupFailedDataAsync(removeAfterBackup: IsSafeMode());
            }
            catch (Exception exception)
            {
                _infoService.DeveloperEvent(e: exception);
            }
            _localSettingsService.Database.Rebuild();
            _localSettingsService.Database.Dispose();
            if (IsSafeMode())
                _infoService.Event(EventType.AppError, InfoBarSeverity.Error, title: "Oops",
                    msg: $"{"ActivationService_LoadDataError".GetLocalized(backup)} {e.Message}");
            AppInstance.Restart(IsSafeMode() ? string.Empty : "/safemode");
            return;
        }

        // Handle activation via ActivationHandlers.
        await HandleActivationAsync(activationArgs);

        if (IsRestart())
        {
            await _bgTaskService.ResolvedBgTasksAsync();
        }

        App.Status = WindowMode.SystemTray;

        // Execute tasks after activation.
        await StartupAsync(activationArgs);

    }

    public async Task HandleActivationAsync(object activationArgs)
    {
        ActivationArgs = activationArgs;
        IActivationHandler? activationHandler = _activationHandlers.FirstOrDefault(h => h.CanHandle(activationArgs));

        if (activationHandler != null)
        {
            await UiThreadInvokeHelper.InvokeAsync(async Task() =>
            {
                await activationHandler.HandleAsync(activationArgs);
            });
        }
    }

    private async Task InitializeAsync()
    {
        await _themeSelectorService.InitializeAsync().ConfigureAwait(false);
        UiDefaultValues.Init();

        // 初始化窗口
        if (IsRestart() == false)
        {
            await _pageService.InitAsync();
            //防止有人手快按到页面内容
            App.MainWindow!.Content.Visibility = Visibility.Collapsed;
        }

        if (AppStoragePaths.IsUpgradeUiTest) return;

        //系统托盘
        App.GetResource<XamlUICommand>("SetWindowNormalCommand").ExecuteRequested += (_, _) =>
        {
            App.SetWindowMode(WindowMode.Normal);
        };
        App.GetResource<XamlUICommand>("CloseAppCommand").ExecuteRequested += (_, _) =>
        {
            App.SetWindowMode(WindowMode.Close);
        };
        App.SystemTray = App.GetResource<TaskbarIcon>("TrayIcon");
        App.SystemTray.ForceCreate(false);
        // 「最近游玩」二级子菜单：挂在已创建好的托盘菜单头部，并订阅游戏列表加载/删除事件。
        // 此时游戏数据尚未就绪，首轮 Rebuild 会得到空结果、不挂载任何菜单项，
        // 等 GalgameLoadedEvent 触发时再建。
        App.GetService<TrayMenuService>().Attach();
    }

    private async Task StartupAsync(object activationArgs)
    {
        var isUpgradeUiTest = AppStoragePaths.IsUpgradeUiTest;
        await _galgameCollectionService.StartAsync();
        await _galgameFolderCollectionService.StartAsync();
        var activateWindow = !IsRestart();
        if (activationArgs is AppActivationArguments { Kind: ExtendedActivationKind.StartupTask })
            activateWindow = !await _localSettingsService.ReadSettingAsync<bool>(KeyValues.MinToTrayWhenAutoStart);
        if (activateWindow) App.SetWindowMode(WindowMode.Normal);
        await _pluginService.InitAsync();
        if (IsRestart() == false && !isUpgradeUiTest)
        {
            _pvnService.Startup();
        }
        if (!isUpgradeUiTest)
        {
            if (IsRestart() == false) await _bgmOAuthService.Init();
            await CheckFont();
        }
    }

    /// <summary>
    /// 检查字体是否安装，如果没有安装，弹出提示框
    /// </summary>
    private async Task CheckFont()
    {
        if (IsRestart()) return;
        if(await _localSettingsService.ReadSettingAsync<bool>(KeyValues.FontInstalled) == false)
        {
            if (Utils.IsFontInstalled("Segoe Fluent Icons") == false)
            {
                ContentDialog dialog = new()
                {
                    XamlRoot = App.MainWindow!.Content.XamlRoot,
                    RequestedTheme = App.MainWindow?.Content is Microsoft.UI.Xaml.FrameworkElement element ? element.RequestedTheme : Microsoft.UI.Xaml.ElementTheme.Default,
                    Title = "ActivationService_FontPopup_Title".GetLocalized(),
                    PrimaryButtonText = "Yes".GetLocalized(),
                    CloseButtonText = "Cancel".GetLocalized(),
                    DefaultButton = ContentDialogButton.Primary
                };
                StackPanel stackPanel = new()
                {
                    Spacing = 20
                };
                TextBlock textBlock = new()
                {
                    Text = "ActivationService_FontPopup_Msg".GetLocalized()
                };
                CheckBox checkBox = new()
                {
                    Content = "ActivationService_FontPopup_NoLongerDisplay".GetLocalized()
                };
                stackPanel.Children.Add(textBlock);
                stackPanel.Children.Add(checkBox);
                dialog.Content = stackPanel;
                dialog.PrimaryButtonClick += async (_, _) =>
                {
                    StorageFile? file = await StorageFile.GetFileFromApplicationUriAsync
                        (new Uri("ms-appx:///Assets/Fonts/Segoe Fluent Icons.ttf"));
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = file.Path,
                        UseShellExecute = true,
                    });
                };
                dialog.CloseButtonClick += async (_, _) =>
                {
                    await _localSettingsService.SaveSettingAsync(KeyValues.FontInstalled, checkBox.IsChecked);
                };

                await dialog.ShowAsync();
            }

            if (Utils.IsFontInstalled("Segoe Fluent Icons"))
                await _localSettingsService.SaveSettingAsync(KeyValues.FontInstalled, true);
        }
    }

    /// <summary>
    /// 判断是否是重启（aka进入系统托盘模式）<br/>
    /// 关于为什么要以重启进入系统托盘模式，见：<see cref="App.SetWindowMode"/>
    /// </summary>
    public static bool IsRestart()
    {
        AppActivationArguments activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        ExtendedActivationKind kind = activatedArgs.Kind;

        if (kind == ExtendedActivationKind.Launch)
        {
            if (activatedArgs.Data is ILaunchActivatedEventArgs launchArgs)
            {
                var argStrings = launchArgs.Arguments.Split();
                if (argStrings.Length > 1)
                    argStrings = argStrings.Skip(1).ToArray();

                return Array.Exists(argStrings, str => str == "/r");
            }
        }
        return false;
    }

    /// <summary>
    /// 判断是否是崩溃后以安全模式自动重启的
    /// </summary>
    public static bool IsSafeMode()
    {
        AppActivationArguments activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        ExtendedActivationKind kind = activatedArgs.Kind;

        if (kind == ExtendedActivationKind.Launch)
        {
            if (activatedArgs.Data is ILaunchActivatedEventArgs launchArgs)
            {
                var argStrings = launchArgs.Arguments.Split();
                if (argStrings.Length > 1)
                    argStrings = argStrings.Skip(1).ToArray();

                return Array.Exists(argStrings, str => str.Contains("safemode"));
            }
        }
        return false;
    }
}
