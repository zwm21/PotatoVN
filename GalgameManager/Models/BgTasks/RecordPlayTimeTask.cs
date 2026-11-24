using System.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using GalgameManager.Contracts.Services;
using GalgameManager.Core.Helpers;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using GalgameManager.Helpers.Converter;
using GalgameManager.Models.Sources;
using GalgameManager.Services;
using GalgameManager.ViewModels;
using GalgameManager.WinApp.Base.Models.Msgs;
using H.NotifyIcon.Core;
using Serilog;

namespace GalgameManager.Models.BgTasks;

public class RecordPlayTimeTask : BgTaskBase
{
    private const int ManuallySelectProcessSec = 15; //认定为需要手动选择游戏进程的时间阈值

    public string ProcessName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }= DateTime.Now;
    public int CurrentPlayTime { get; set; } //本次游玩时间
    public Guid? InstallationId { get; set; } // 本次游玩使用的安装实例Id
    public override bool ProgressOnTrayIcon => true;

    public Galgame? Galgame;
    private Process? _process;
    private string? _directoryPrefix; // 安装目录前缀，用于退出后探测新出现的游戏进程
    private HashSet<int>? _knownProcessIds; // 开始跟踪时已存在于安装目录的进程，复查时只附着新出现的进程
    private volatile bool _stopped;

    private readonly ILocalSettingsService _localSettingsService;
    private readonly IGalgameCollectionService _gameService;
    private int _minPlayTimeRecordThreshold;
    /// <summary>
    /// 上次弹出休息提醒时的本次游玩时长（分钟）。<br/>
    /// 只在计时循环的 UI 线程 Invoke 块内读写（与 <see cref="CurrentPlayTime"/> 同步），
    /// 因此不需要 volatile——不存在跨线程竞争。
    /// </summary>
    private int _lastBreakReminderMinute;

    public RecordPlayTimeTask()
        : this(App.GetService<ILocalSettingsService>(), App.GetService<IGalgameCollectionService>())
    {
    }

    /// <summary>
    /// 使用明确的服务依赖创建计时任务。
    /// </summary>
    public RecordPlayTimeTask(ILocalSettingsService localSettingsService, IGalgameCollectionService gameService)
    {
        _localSettingsService = localSettingsService;
        _gameService = gameService;
    }

    /// <summary>
    /// 使用游戏的首选安装实例创建游玩时间记录任务。
    /// </summary>
    /// <param name="game">目标逻辑游戏</param>
    /// <param name="process">要跟踪的游戏进程</param>
    public RecordPlayTimeTask(Galgame game, Process process)
        : this(game, process, game.PreferredInstallationId)
    {
    }

    /// <summary>
    /// 使用明确安装实例创建游玩时间记录任务。
    /// </summary>
    /// <param name="game">目标逻辑游戏</param>
    /// <param name="process">要跟踪的游戏进程</param>
    /// <param name="installationId">本次游玩使用的安装实例Id</param>
    public RecordPlayTimeTask(Galgame game, Process process, Guid? installationId)
        : this()
    {
        Debug.Assert(game.IsLocalGame);
        try
        {
            if (process.HasExited) return;
            ProcessName = process.ProcessName;
        }
        catch
        {
            // 进程对象可能无效（例如通过ShellExecute启动的快捷方式），仍创建任务，由退出后的目录检查兜底
        }
        Galgame = game;
        _process = process;
        InstallationId = installationId;
        InitDirectoryWatch();
    }

    protected override Task RecoverFromJsonInternal()
    {
        _process = Process.GetProcessesByName(ProcessName).FirstOrDefault();
        InitDirectoryWatch();
        return Task.CompletedTask;
    }

    private void InitDirectoryWatch()
    {
        string? path = Galgame?.SourceEntries.FirstOrDefault(e => e.EntryId == InstallationId)?.Path;
        if (string.IsNullOrEmpty(path)) return;
        _directoryPrefix = GameProcessDetector.GetDirectoryPrefix(path);
        _knownProcessIds = GameProcessDetector.GetProcessIdsInDirectory(_directoryPrefix);
    }

    protected async override Task RunInternal()
    {
        if(_process is null || Galgame is null) return ;
        ChangeProgress(0, 1, "RecordPlayTimeTask_ProgressMsg".GetLocalized(Galgame.Name.Value!));
        Task t = Task.Run(async () =>
        {
            // 被跟踪的进程退出后，游戏目录内可能出现真正的游戏进程（启动器场景），尝试重新附着
            while (true)
            {
                try
                {
                    await _process.WaitForExitAsync();
                }
                catch
                {
                    // 进程对象无效（例如通过ShellExecute启动的快捷方式），直接进行目录检查
                }
                Process? next = await WaitForReplacementProcessAsync(GameProcessDetector.SafeGetId(_process));
                if (next is null) break;
                _process = next;
                ProcessName = next.ProcessName;
                _knownProcessIds?.Add(next.Id);
                ChangeProgress(0, 1, "RecordPlayTimeTask_ProgressMsg".GetLocalized(Galgame.Name.Value!));
            }
            _stopped = true;
            var windowMode = await _localSettingsService.ReadSettingAsync<WindowMode>(KeyValues.PlayingWindowMode);
            // 「本次游玩小结」气泡的两个前置设置在这里一次性读掉：结算块内部是同步 lambda，
            // 不方便 await；且模板/单位词必须先本地化好才能交给 ShowNotification。
            // 这里不做 notifyPlayTimeSummary 的短路——阈值判定（ShouldNotify）与窗口状态无关，
            // 统一留给 ShowPlayTimeSummaryNotification 决定，避免同一处出现两套判据。
            var notifyPlayTimeSummary =
                await _localSettingsService.ReadSettingAsync<bool>(KeyValues.NotifyPlayTimeSummary);
            var timeAsHour = await _localSettingsService.ReadSettingAsync<bool>(KeyValues.TimeAsHour);
            await UiThreadInvokeHelper.InvokeAsync(() =>
            {
                _gameService.SaveGalgameAsync(Galgame);
                GalgamePageParameter parma = new()
                {
                    Galgame = Galgame,
                    SelectProgress = DateTime.Now - StartTime < TimeSpan.FromSeconds(ManuallySelectProcessSec)
                                     && Galgame.SourceEntries.FirstOrDefault(e => e.EntryId == InstallationId)
                                         ?.LocalConfig?.ProcessName is null
                };
                if (windowMode == WindowMode.SystemTray)
                    App.GetService<INavigationService>().NavigateTo(typeof(GalgameViewModel).FullName!, parma);
                App.SetWindowMode(WindowMode.Normal);
                ChangeProgress(1, 1,
                    "RecordPlayTimeTask_Done".GetLocalized(Galgame.Name.Value ?? string.Empty,
                        TimeToDisplayTimeConverter.Convert(CurrentPlayTime)));
                // 手动通知LastPlayTime属性已更改
                Galgame.RaisePropertyChanged(nameof(Galgame.LastPlayTime));
                if (CurrentPlayTime >= _minPlayTimeRecordThreshold) Galgame!.PlayCount++;
                App.GetService<IMessenger>().Send(new GalgameStoppedMessage(Galgame));
                ShowPlayTimeSummaryNotification(notifyPlayTimeSummary, timeAsHour);
            });
            await App.GetService<IGalgameCollectionService>().SaveGalgameAsync(Galgame);
            if(await App.GetService<ILocalSettingsService>().ReadSettingAsync<bool>(KeyValues.SyncGames))
                App.GetService<IPvnService>().Upload(Galgame, PvnUploadProperties.PlayTime);
        });

        _ = RecordPlayTimeAsync();

        await t;
    }

    /// <summary>
    /// 弹出「本次游玩小结」托盘气泡：本次游玩时长 + 累计时长。<br/>
    /// <b>只受设置开关控制，不区分当前窗口状态</b>——结算时 <c>SetWindowMode(Normal)</c> 会把主窗口
    /// 拉回前台，但用户此时可能已经切到别的应用，气泡比抢占焦点更轻；一直躲在托盘里时它也是
    /// 唯一的反馈渠道（InfoBar 在窗口隐藏态下用户根本看不见）。<br/>
    /// 层级刻意做成「标题=游戏名、正文=时长」两行：游戏名放标题能让用户一眼认出刚退出的是
    /// 哪一款，正文不放游戏名则保证时长不会被换行挤到气泡可见区域之外。<br/>
    /// 图标用 <see cref="NotificationIcon.None"/>：Info 会在卡片左侧画一个巨大的蓝色圆 i，
    /// 既挤压文字宽度又和 PotatoVN 的视觉风格不符；None 下文字铺满整张卡片。<br/>
    /// 时长阈值固定在 1 分钟（见 <see cref="PlayTimeSummaryHelper.MinNotifyMinutes"/>），刻意不复用
    /// <c>MinPlayTimeRecordThreshold</c>：那个字段由 <see cref="RecordPlayTimeAsync"/> 异步赋值，
    /// 秒退场景下可能还是默认值 0，拿它当阈值会把「0 &gt;= 0」判成真。<br/>
    /// 必须是纯尽力而为：任何异常都只进 Serilog，绝不能把正常结算流程带崩。
    /// </summary>
    private void ShowPlayTimeSummaryNotification(bool enabled, bool timeAsHour)
    {
        try
        {
            if (!enabled || !PlayTimeSummaryHelper.ShouldNotify(CurrentPlayTime)) return;
            if (App.SystemTray is null) return;
            // 游戏名为空时才用到这个本地化兜底标题；有名字时标题位直接放游戏名
            string title = PlayTimeSummaryHelper.FormatTitle(Galgame?.Name.Value,
                "RecordPlayTimeTask_PlayTimeSummary_Title".GetLocalized());
            string message = PlayTimeSummaryHelper.FormatMessage(CurrentPlayTime,
                Galgame?.TotalPlayTime ?? 0, timeAsHour,
                "RecordPlayTimeTask_PlayTimeSummary_Msg".GetLocalized(), "Minute".GetLocalized());
            App.SystemTray.ShowNotification(title, message, NotificationIcon.None);
        }
        catch (Exception e)
        {
            Serilog.Log.Error(e, "RecordPlayTimeTask: show playtime summary notification failed");
        }
    }

    /// <summary>
    /// 启动器类游戏中，被跟踪进程退出后真正的游戏进程才刚被拉起。
    /// 退出后做5秒检查（每秒一次），若安装目录内出现了新进程则返回该进程。
    /// </summary>
    private async Task<Process?> WaitForReplacementProcessAsync(int exitedProcessId)
    {
        if (_directoryPrefix is null || _knownProcessIds is null) return null;
        _knownProcessIds.Add(exitedProcessId);
        for (int i = 0; i < 5; i++)
        {
            ChangeProgress(0, 1,
                "RecordPlayTimeTask_WaitingForProcess".GetLocalized(Galgame!.Name.Value ?? string.Empty,
                    (5 - i).ToString()));
            await Task.Delay(1000);
            Process? candidate =
                GameProcessDetector.FindBestProcessInDirectory(_directoryPrefix, _knownProcessIds);
            if (candidate is not null) return candidate;
        }
        return null;
    }

    /// <summary>
    /// 弹出「定时休息提醒」托盘气泡：单次连续游玩每满设定分钟数一次。<br/>
    /// 视觉风格与 <see cref="ShowPlayTimeSummaryNotification"/> 完全一致（上一轮实机终验通过）：
    /// <see cref="NotificationIcon.None"/> 去掉左侧大蓝圆图标让文字铺满卡片、标题放游戏名、
    /// 正文单行只放时长。<br/>
    /// 声音与勿扰参数显式传 <c>sound: false</c> / <c>respectQuietTime: true</c>——休息提醒不该有
    /// 提示音（那会比 InfoBar 更打扰），且应尊重系统勿扰。之所以显式传而不依赖默认值：
    /// H.NotifyIcon 2.0.118 的 ShowNotification 有 7 个可选参数，本机反编译只能确认它们都带
    /// <c>pdHasDefault</c> 标志，取不到 Constant 表里的具体值，不猜。<br/>
    /// 必须是纯尽力而为：任何异常都只进 Serilog，绝不能把计时循环或游玩时长累计带崩。
    /// </summary>
    private void ShowBreakReminderNotification(bool timeAsHour)
    {
        try
        {
            if (App.SystemTray is null) return;
            string title = PlayTimeSummaryHelper.FormatTitle(Galgame?.Name.Value,
                "RecordPlayTimeTask_BreakReminder_Title".GetLocalized());
            string message = BreakReminderHelper.FormatMessage(CurrentPlayTime, timeAsHour,
                "RecordPlayTimeTask_BreakReminder_Msg".GetLocalized(), "Minute".GetLocalized());
            App.SystemTray.ShowNotification(title, message, NotificationIcon.None,
                sound: false, respectQuietTime: true);
        }
        catch (Exception e)
        {
            Serilog.Log.Error(e, "RecordPlayTimeTask: show break reminder notification failed");
        }
    }

    private Task RecordPlayTimeAsync()
    {
        return Task.Run(async () =>
        {
            var recordOnlyWhenForeground =
                await _localSettingsService.ReadSettingAsync<bool>(KeyValues.RecordOnlyWhenForeground);
            _minPlayTimeRecordThreshold = await _localSettingsService.ReadSettingAsync<int>(KeyValues.MinPlayTimeRecordThreshold);
            // 休息提醒的间隔与时长展示习惯也在这里一次性读掉，之后完全靠 OnSettingChanged
            // 同步，不再每分钟读一次设置——非 MSIX 小值路径的 ReadSettingAsync 虽然没有 await
            // （不会死锁），但每分次要付一次 LiteDB FindById + JsonConvert 反序列化，没必要。
            var breakReminderInterval =
                await _localSettingsService.ReadSettingAsync<int>(KeyValues.BreakReminderInterval);
            var timeAsHour = await _localSettingsService.ReadSettingAsync<bool>(KeyValues.TimeAsHour);
            try
            {
                _localSettingsService.OnSettingChanged += OnSettingChanged;

                while (!_stopped)
                {
                    Thread.Sleep(1000 * 60);
                    Process? current = _process;
                    if (_stopped || current is null || !GameProcessDetector.IsAlive(current) ||
                        (recordOnlyWhenForeground && (current.IsMainWindowMinimized() || !current.IsMainWindowActive())))
                        continue;
                    UiThreadInvokeHelper.Invoke(() =>
                    {
                        Galgame!.TotalPlayTime++;
                        CurrentPlayTime++;
                        _gameService.SaveGalgameAsync(Galgame);
                        // 休息提醒刻意放在这个块内、CurrentPlayTime++ 之后：CurrentPlayTime、
                        // _lastBreakReminderMinute 与 ShowNotification 三者收敛在同一 UI 线程
                        // 上下文，不存在跨线程竞争；提醒失败也不影响上面的时长累计。
                        if (BreakReminderHelper.ShouldRemind(CurrentPlayTime, breakReminderInterval,
                                _lastBreakReminderMinute))
                        {
                            _lastBreakReminderMinute = CurrentPlayTime;
                            ShowBreakReminderNotification(timeAsHour);
                        }
                    });
                    var now = DateTime.Now.ToStringDefault();
                    if (!Galgame!.PlayedTime.TryAdd(now, 1))
                        Galgame.PlayedTime[now]++;
                }
            }
            finally
            {
                _localSettingsService.OnSettingChanged -= OnSettingChanged;
            }

            return;

            void OnSettingChanged(string key, object? value)
            {
                if(key == KeyValues.RecordOnlyWhenForeground && value is bool b)
                    recordOnlyWhenForeground = b;
                if(key == KeyValues.MinPlayTimeRecordThreshold && value is int i)
                    _minPlayTimeRecordThreshold = i;
                if(key == KeyValues.BreakReminderInterval && value is int iv)
                    breakReminderInterval = iv;
                if(key == KeyValues.TimeAsHour && value is bool th)
                    timeAsHour = th;
            }
        });
    }

    public override bool OnSearch(string key) =>
        Galgame is not null && string.Equals(Galgame.Uuid.ToString("D"), key, StringComparison.OrdinalIgnoreCase);

    public override string Title { get; } = "RecordPlayTimeTask_Title".GetLocalized();
}
