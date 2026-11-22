using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GalgameManager.Contracts.Services;
using GalgameManager.Contracts.ViewModels;
using GalgameManager.Helpers;
using GalgameManager.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace GalgameManager.ViewModels;

/// <summary>
/// 「足迹」热力图分页：GitHub 贡献图风格的日历热力图 + 当日游玩明细。<br/>
/// 数据全部来自 <see cref="PlayHeatmapStats"/>，游戏列表直接取内存中的
/// <see cref="IGalgameCollectionService.Galgames"/>，不重复遍历磁盘。
/// </summary>
public partial class AnnualReportSubPageHeatmapViewModel : ObservableObject, INavigationAware
{
    /// <summary>格子边长</summary>
    private const double CellSize = 12;

    /// <summary>格子外边距，与 <see cref="CellSize"/> 共同决定横向步进</summary>
    private const double CellMargin = 2;

    private static readonly double CellStride = CellSize + CellMargin * 2;

    /// <summary>月份标签行高度，星期标签据此下移以与格子行顶部对齐</summary>
    private const double MonthRowHeight = 20;

    /// <summary>浮动提示与格子的垂直间距</summary>
    private const double TipGap = 4;

    /// <summary>热力色板，下标 0 = 无记录，1~4 对应 <see cref="PlayHeatmapStats.GetIntensityLevel"/></summary>
    private static readonly string[] LightPalette = ["#EBEDF0", "#9BE9A8", "#40C463", "#30A14E", "#216E39"];

    private static readonly string[] DarkPalette = ["#2D333B", "#0E4429", "#006D32", "#26A641", "#39D353"];

    private readonly IGalgameCollectionService _gameService;
    private PlayHeatmapResult _result = null!;
    private Border? _selectedBorder;
    private Border? _tipBorder;
    private TextBlock? _tipText;
    private DateTime _selectedDate;
    private ElementTheme _currentTheme = ElementTheme.Light;

    public AnnualReportSubPageHeatmapViewModel(IGalgameCollectionService gameService)
    {
        _gameService = gameService;
    }

    /// <summary>当前生效的热力色板，下标 0 = 无记录，1~4 对应热力等级</summary>
    private string[] Palette => _currentTheme == ElementTheme.Dark ? DarkPalette : LightPalette;

    [ObservableProperty] private AnnualReportData _annualReportData = new();

    /// <summary>图例 5 个色块的笔刷，与格子共用同一套色板，随主题切换</summary>
    [ObservableProperty] private Brush _level0Brush = new SolidColorBrush(Color.FromArgb(255, 0xEB, 0xED, 0xF0));

    [ObservableProperty] private Brush _level1Brush = new SolidColorBrush(Color.FromArgb(255, 0x9B, 0xE9, 0xA8));

    [ObservableProperty] private Brush _level2Brush = new SolidColorBrush(Color.FromArgb(255, 0x40, 0xC4, 0x63));

    [ObservableProperty] private Brush _level3Brush = new SolidColorBrush(Color.FromArgb(255, 0x30, 0xA1, 0x4E));

    [ObservableProperty] private Brush _level4Brush = new SolidColorBrush(Color.FromArgb(255, 0x21, 0x6E, 0x39));

    /// <summary>热力图宿主 Grid，由代码侧填充 7×N 格子与月份标签</summary>
    [ObservableProperty] private Grid? _heatmapHost;

    /// <summary>星期标签宿主 Grid，只标周一 / 周三 / 周五</summary>
    [ObservableProperty] private Grid? _weekdayHost;

    /// <summary>浮动提示的定位画布，覆盖整张热力图卡片且不拦截命中</summary>
    [ObservableProperty] private Canvas? _tipCanvas;

    [ObservableProperty] private ObservableCollection<HeatmapRangeOption> _rangeOptions = [];

    [ObservableProperty] private HeatmapRangeOption? _selectedRange;

    [ObservableProperty] private string _rangeSummary = string.Empty;

    [ObservableProperty] private string _totalPlayTimeText = string.Empty;

    [ObservableProperty] private int _activeDays;

    [ObservableProperty] private int _longestStreak;

    [ObservableProperty] private int _currentStreak;

    [ObservableProperty] private string _selectedDateText = string.Empty;

    [ObservableProperty] private string _dayTotalText = string.Empty;

    [ObservableProperty] private bool _hasDayRecord;

    [ObservableProperty] private ObservableCollection<HeatmapDayGameItem> _dayGames = [];

    public void OnNavigatedTo(object parameter)
    {
        AnnualReportData = (AnnualReportData)parameter;

        List<HeatmapRangeOption> options = [new() { Year = null, DisplayName = RangeLastYearName }];
        options.AddRange(PlayHeatmapStats.GetAvailableYears(_gameService.Galgames, DateTime.Now)
            .Select(year => new HeatmapRangeOption
            {
                Year = year,
                DisplayName = "AnnualReportPage_Heatmap_RangeYear".GetLocalized(year)
            }));
        RangeOptions = new ObservableCollection<HeatmapRangeOption>(options);
        SelectedRange = RangeOptions[0];
    }

    public void OnNavigatedFrom()
    {
        HideTip();
    }

    private static string RangeLastYearName => "AnnualReportPage_Heatmap_RangeLastYear".GetLocalized();

    partial void OnSelectedRangeChanged(HeatmapRangeOption? value)
    {
        if (value is null) return;
        Rebuild();
    }

    partial void OnHeatmapHostChanged(Grid? value)
    {
        if (value is null) return;
        // 主题切换时重新上色，保证浅色 / 深色各自使用 GitHub 绿色板
        value.ActualThemeChanged += (_, _) => RefreshTheme();
        // 指针离开热力图区域时收起浮动提示
        value.PointerExited += (_, _) => HideTip();
    }

    /// <summary>
    /// 解析当前生效的主题。<br/>
    /// 直接读 <see cref="FrameworkElement.ActualTheme"/> 在元素尚未挂载进可视化树时会得到
    /// <see cref="ElementTheme.Default"/>（此时会误判成浅色），因此沿可视化树向上回退查找；
    /// 主题是由 ThemeSelectorService 写在 App.MainWindow.Content 上的，向上走一定能遇到。
    /// 全部落空时兜底为浅色。
    /// </summary>
    private ElementTheme ResolveEffectiveTheme()
    {
        FrameworkElement? element = HeatmapHost;
        while (element is not null)
        {
            if (element.ActualTheme != ElementTheme.Default) return element.ActualTheme;
            element = VisualTreeHelper.GetParent(element) as FrameworkElement;
        }
        return App.MainWindow?.Content is FrameworkElement root && root.ActualTheme != ElementTheme.Default
            ? root.ActualTheme
            : ElementTheme.Light;
    }

    /// <summary>主题变化：同步刷新格子配色与图例笔刷</summary>
    private void RefreshTheme()
    {
        if (HeatmapHost is null) return;
        _currentTheme = ResolveEffectiveTheme();
        string[] palette = Palette;
        Level0Brush = new SolidColorBrush(ParseColor(palette[0]));
        Level1Brush = new SolidColorBrush(ParseColor(palette[1]));
        Level2Brush = new SolidColorBrush(ParseColor(palette[2]));
        Level3Brush = new SolidColorBrush(ParseColor(palette[3]));
        Level4Brush = new SolidColorBrush(ParseColor(palette[4]));
        RenderCells();
    }

    /// <summary>
    /// 注入浮动提示的容器与文本，由视图在 InitializeComponent 后调用。
    /// </summary>
    internal void AttachTip(Border border, TextBlock text)
    {
        _tipBorder = border;
        _tipText = text;
    }

    /// <summary>
    /// 统一时长格式化：不足 1 小时只显示分钟；整小时只显示小时；其余显示「X 小时 Y 分钟」。
    /// </summary>
    public static string FormatDuration(int totalMinutes)
    {
        if (totalMinutes <= 0) return "AnnualReportPage_Heatmap_ZeroMinutes".GetLocalized();
        int hours = totalMinutes / 60;
        int minutes = totalMinutes % 60;
        if (hours == 0) return "AnnualReportPage_Heatmap_MinutesOnly".GetLocalized(minutes);
        return minutes == 0
            ? "AnnualReportPage_Heatmap_HoursOnly".GetLocalized(hours)
            : "AnnualReportPage_Heatmap_HoursMinutes".GetLocalized(hours, minutes);
    }

    private void Rebuild()
    {
        _result = PlayHeatmapStats.Build(_gameService.Galgames, DateTime.Now, SelectedRange?.Year);
        // 此时页面已挂载进可视化树，才能解析到真实主题
        RefreshTheme();

        RangeSummary = "AnnualReportPage_Heatmap_RangeSummary".GetLocalized(
            SelectedRange?.DisplayName ?? RangeLastYearName, FormatDuration(_result.TotalMinutes));
        TotalPlayTimeText = FormatDuration(_result.TotalMinutes);
        ActiveDays = _result.ActiveDays;
        LongestStreak = _result.LongestStreak;
        CurrentStreak = _result.CurrentStreak;

        SelectDate(_result.DefaultSelectedDate);
    }

    private static string FormatToolTip(PlayHeatmapCell cell)
    {
        string date = cell.Date.ToString("yyyy-MM-dd");
        return cell.TotalMinutes > 0
            ? "AnnualReportPage_Heatmap_ToolTipWithGames".GetLocalized(date,
                FormatDuration(cell.TotalMinutes), cell.Games.Count)
            : "AnnualReportPage_Heatmap_ToolTipNoRecord".GetLocalized(date);
    }

    /// <summary>按当前主题重建全部格子。主题切换与范围切换都走这里。</summary>
    private void RenderCells()
    {
        if (HeatmapHost is null || _result is null) return;

        _selectedBorder = null;
        HeatmapHost.Children.Clear();
        HeatmapHost.RowDefinitions.Clear();
        HeatmapHost.ColumnDefinitions.Clear();

        string[] palette = Palette;
        int columns = _result.ColumnCount;

        // 行 0 放月份标签，行 1~7 依次是周日..周六
        HeatmapHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(MonthRowHeight) });
        for (int i = 0; i < PlayHeatmapStats.RowCount; i++)
            HeatmapHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CellStride) });
        for (int i = 0; i < columns; i++)
            HeatmapHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CellStride) });

        // 月份标签：用 Canvas 绝对定位，避免被 16px 宽的列裁掉
        Canvas monthCanvas = new() { Width = columns * CellStride, Height = MonthRowHeight };
        Grid.SetRow(monthCanvas, 0);
        HeatmapHost.Children.Add(monthCanvas);
        foreach (PlayHeatmapMonthLabel label in _result.MonthLabels)
        {
            TextBlock text = new()
            {
                Text = "AnnualReportPage_Heatmap_MonthLabel".GetLocalized(label.MonthIndex),
                FontSize = 11,
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center
            };
            Canvas.SetLeft(text, label.ColumnIndex * CellStride);
            monthCanvas.Children.Add(text);
        }

        // 格子：Cells 是行主序，下标 = row * ColumnCount + col
        for (int row = 0; row < PlayHeatmapStats.RowCount; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                PlayHeatmapCell cell = _result.Cells[row * columns + col];
                // 范围外补位格：不生成 Border，天然不可见且不可命中
                if (cell.IsOutOfRange) continue;

                Border border = new()
                {
                    Width = CellSize,
                    Height = CellSize,
                    Margin = new Thickness(CellMargin),
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(ParseColor(palette[cell.Level])),
                    BorderThickness = new Thickness(0)
                };
                // 不用原生 ToolTip：它的弹出延迟无法关闭；改用卡片内的浮动提示层，0ms 跟随
                border.PointerEntered += (_, _) => ShowTip(border, cell);
                border.PointerPressed += (_, _) => SelectDate(cell.Date);
                Grid.SetRow(border, row + 1);
                Grid.SetColumn(border, col);
                HeatmapHost.Children.Add(border);
            }
        }

        ApplySelection();
    }

    private void RenderWeekdayLabels()
    {
        if (WeekdayHost is null) return;
        WeekdayHost.Children.Clear();
        WeekdayHost.RowDefinitions.Clear();
        for (int i = 0; i < PlayHeatmapStats.RowCount; i++)
            WeekdayHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CellStride) });

        // 只标周一 / 周三 / 周五，对应 row 1 / 3 / 5
        (int Row, string Key)[] labels = [(1, "AnnualReportPage_Heatmap_WeekdayMon"),
            (3, "AnnualReportPage_Heatmap_WeekdayWed"), (5, "AnnualReportPage_Heatmap_WeekdayFri")];
        foreach ((int row, string key) in labels)
        {
            TextBlock text = new()
            {
                Text = key.GetLocalized(),
                FontSize = 11,
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetRow(text, row);
            WeekdayHost.Children.Add(text);
        }
    }

    partial void OnWeekdayHostChanged(Grid? value)
    {
        if (value is null) return;
        RenderWeekdayLabels();
    }

    /// <summary>
    /// 把浮动提示定位到指定格子正上方，并做左右边界防裁切。<br/>
    /// 提示画布是卡片的直接子元素，用 <see cref="UIElement.TransformToVisual"/> 换算格子
    /// 相对画布的偏移，横向滚动时偏移会随之更新。
    /// </summary>
    private void ShowTip(Border cell, PlayHeatmapCell data)
    {
        if (TipCanvas is null || _tipBorder is null || _tipText is null) return;

        _tipText.Text = FormatToolTip(data);
        _tipBorder.Visibility = Visibility.Visible;
        // 文本刚赋值，强制一次同步测量拿到真实尺寸，否则定位会按上一帧的宽高计算
        _tipBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size tipSize = _tipBorder.DesiredSize;

        GeneralTransform transform = cell.TransformToVisual(TipCanvas);
        Point cellOrigin = transform.TransformPoint(default);

        double maxLeft = Math.Max(0, TipCanvas.ActualWidth - tipSize.Width);
        double left = Math.Clamp(cellOrigin.X + cell.Width / 2 - tipSize.Width / 2, 0, maxLeft);
        // 优先放格子上方；顶部空间不足时贴住画布顶边，避免被裁掉
        double top = Math.Max(0, cellOrigin.Y - tipSize.Height - TipGap);

        Canvas.SetLeft(_tipBorder, left);
        Canvas.SetTop(_tipBorder, top);
    }

    private void HideTip()
    {
        if (_tipBorder is not null) _tipBorder.Visibility = Visibility.Collapsed;
    }

    private void SelectDate(DateTime date)
    {
        _selectedDate = date.Date;
        SelectedDateText = _selectedDate.ToString("yyyy-MM-dd");

        PlayHeatmapCell? cell = _result.Cells.FirstOrDefault(c => !c.IsOutOfRange && c.Date == _selectedDate);
        if (cell is null)
        {
            HasDayRecord = false;
            DayTotalText = string.Empty;
            DayGames = [];
            return;
        }

        HasDayRecord = cell.TotalMinutes > 0;
        DayTotalText = HasDayRecord ? FormatDuration(cell.TotalMinutes) : string.Empty;
        DayGames = new ObservableCollection<HeatmapDayGameItem>(cell.Games.Select(g => new HeatmapDayGameItem
        {
            GameName = g.GameName,
            Minutes = g.Minutes,
            HasCover = g.HasCover,
            CoverPath = g.CoverPath,
            DurationText = FormatDuration(g.Minutes)
        }));
        ApplySelection();
    }

    /// <summary>把选中边框画到当前日期对应的格子上</summary>
    private void ApplySelection()
    {
        if (_selectedBorder is not null)
        {
            _selectedBorder.BorderThickness = new Thickness(0);
            _selectedBorder.BorderBrush = null;
            _selectedBorder = null;
        }
        if (HeatmapHost is null || _result is null) return;

        int columns = _result.ColumnCount;
        for (int row = 0; row < PlayHeatmapStats.RowCount; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                if (_result.Cells[row * columns + col].Date != _selectedDate) continue;
                // 该格子可能因补位而被跳过，此时没有对应 Border
                Border? border = HeatmapHost.Children.OfType<Border>()
                    .FirstOrDefault(b => Grid.GetRow(b) == row + 1 && Grid.GetColumn(b) == col);
                if (border is null) return;
                border.BorderThickness = new Thickness(1);
                border.BorderBrush = (Brush)Application.Current.Resources["SystemControlForegroundAccentBrush"];
                _selectedBorder = border;
                return;
            }
        }
    }

    private static Color ParseColor(string hex)
    {
        byte r = Convert.ToByte(hex.Substring(1, 2), 16);
        byte g = Convert.ToByte(hex.Substring(3, 2), 16);
        byte b = Convert.ToByte(hex.Substring(5, 2), 16);
        return Color.FromArgb(255, r, g, b);
    }
}

/// <summary>
/// 热力图顶部范围切换选项。<see cref="Year"/> 为 null 表示滚动一年。
/// </summary>
public partial class HeatmapRangeOption
{
    public int? Year { get; init; }

    public required string DisplayName { get; init; }
}

/// <summary>
/// 当日游玩明细中的一款游戏（视图层包装，附带本地化后的时长文本）
/// </summary>
public class HeatmapDayGameItem
{
    public required string GameName { get; init; }

    public int Minutes { get; init; }

    public bool HasCover { get; init; }

    public string? CoverPath { get; init; }

    /// <summary>本地化后的时长文本，如「17 分钟」「1 小时 20 分钟」</summary>
    public required string DurationText { get; init; }
}
