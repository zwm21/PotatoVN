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

    /// <summary>热力色板，下标 0 = 无记录，1~4 对应 <see cref="PlayHeatmapStats.GetIntensityLevel"/></summary>
    private static readonly string[] LightPalette = ["#EBEDF0", "#9BE9A8", "#40C463", "#30A14E", "#216E39"];

    private static readonly string[] DarkPalette = ["#2D333B", "#0E4429", "#006D32", "#26A641", "#39D353"];

    private readonly IGalgameCollectionService _gameService;
    private PlayHeatmapResult _result = null!;
    private Border? _selectedBorder;
    private DateTime _selectedDate;

    public AnnualReportSubPageHeatmapViewModel(IGalgameCollectionService gameService)
    {
        _gameService = gameService;
    }

    [ObservableProperty] private AnnualReportData _annualReportData = new();

    /// <summary>热力图宿主 Grid，由代码侧填充 7×N 格子与月份标签</summary>
    [ObservableProperty] private Grid? _heatmapHost;

    /// <summary>星期标签宿主 Grid，只标周一 / 周三 / 周五</summary>
    [ObservableProperty] private Grid? _weekdayHost;

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

    [ObservableProperty] private ObservableCollection<DailyGamePlayItem> _dayGames = [];

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
        value.ActualThemeChanged += (_, _) => RenderCells();
    }

    private void Rebuild()
    {
        _result = PlayHeatmapStats.Build(_gameService.Galgames, DateTime.Now, SelectedRange?.Year);

        RangeSummary = "AnnualReportPage_Heatmap_RangeSummary".GetLocalized(
            SelectedRange?.DisplayName ?? RangeLastYearName, _result.TotalMinutes / 60, _result.TotalMinutes % 60);
        TotalPlayTimeText = FormatHoursMinutes(_result.TotalMinutes);
        ActiveDays = _result.ActiveDays;
        LongestStreak = _result.LongestStreak;
        CurrentStreak = _result.CurrentStreak;

        RenderCells();
        SelectDate(_result.DefaultSelectedDate);
    }

    private static string FormatHoursMinutes(int totalMinutes) =>
        "AnnualReportPage_Heatmap_HoursMinutes".GetLocalized(totalMinutes / 60, totalMinutes % 60);

    private static string FormatToolTip(PlayHeatmapCell cell)
    {
        string date = cell.Date.ToString("yyyy-MM-dd");
        return cell.TotalMinutes > 0
            ? "AnnualReportPage_Heatmap_ToolTipWithGames".GetLocalized(date, cell.TotalMinutes / 60,
                cell.TotalMinutes % 60, cell.Games.Count)
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

        string[] palette = HeatmapHost.ActualTheme == ElementTheme.Dark ? DarkPalette : LightPalette;
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
                // 范围外补位格：不可见且不可命中
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
                ToolTipService.SetToolTip(border, FormatToolTip(cell));
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
        DayTotalText = HasDayRecord ? FormatHoursMinutes(cell.TotalMinutes) : string.Empty;
        DayGames = new ObservableCollection<DailyGamePlayItem>(cell.Games);
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
