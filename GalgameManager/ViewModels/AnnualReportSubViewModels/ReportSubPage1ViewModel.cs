using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalgameManager.Contracts.ViewModels;
using GalgameManager.Enums;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;

namespace GalgameManager.ViewModels;

public partial class ReportSubPage1ViewModel : ObservableObject, INavigationAware
{
    [ObservableProperty] private AnnualReportData _annualReportData = new();

    [ObservableProperty] private int _mostActiveMonth = 0;
    [ObservableProperty] private double _averagePlayHours = 0;
    [ObservableProperty] private bool _isPlayHoursDetailVisible;
    [ObservableProperty] private bool _isGamesPlayedDetailVisible;
    [ObservableProperty] private bool _isActiveMonthDetailVisible;
    [ObservableProperty] private bool _isAverageHoursDetailVisible;
    [ObservableProperty] private ObservableCollection<MonthlyData>? _monthlyPlayHours;

    [ObservableProperty]
    private ISeries[]? _playHoursSeries;
    [ObservableProperty]
    private ISeries[]? _gamesPlayedSeries;
    [ObservableProperty]
    private ISeries[]? _activeMonthSeries;
    [ObservableProperty]
    private ISeries[]? _averageHoursSeries;

    [ObservableProperty]
    private IEnumerable<ICartesianAxis>? _playHoursXAxes;
    [ObservableProperty]
    private IEnumerable<ICartesianAxis>? _monthXAxes;
    [ObservableProperty]
    private IEnumerable<ICartesianAxis>? _averageHoursXAxes;
    [ObservableProperty]
    private IEnumerable<ICartesianAxis>? _playHoursYAxes;
    [ObservableProperty]
    private IEnumerable<ICartesianAxis>? _monthYAxes;
    [ObservableProperty]
    private IEnumerable<ICartesianAxis>? _averageHoursYAxes;

    /// <summary>饼图图例文字笔刷，随深浅主题切换</summary>
    [ObservableProperty] private SolidColorPaint _legendTextPaint =
        new SolidColorPaint(new SKColor(0x59, 0x59, 0x59));

    /// <summary>主题锚点，由视图注入；用于读取生效主题并在主题切换时重绘图表</summary>
    [ObservableProperty] private FrameworkElement? _themeAnchor;

    partial void OnThemeAnchorChanged(FrameworkElement? value)
    {
        if (value is null) return;
        value.ActualThemeChanged += (_, _) => InitializeCharts();
    }

    public void OnNavigatedTo(object parameter)
    {
        Debug.Assert(parameter is AnnualReportData);
        AnnualReportData = (AnnualReportData)parameter;
        // 一部都没玩过时不能做除法，否则会得到 NaN 并直接显示在卡片上
        AveragePlayHours = AnnualReportData.TotalGamesPlayed > 0
            ? AnnualReportData.PlayedTime / AnnualReportData.TotalGamesPlayed
            : 0;
        // 初始化图表数据
        InitializeCharts();
    }

    public void OnNavigatedFrom()
    {
    }

    /// <summary>
    /// 解析当前生效的主题：直接读 <see cref="FrameworkElement.ActualTheme"/> 在元素尚未挂载进
    /// 可视化树时会得到 <see cref="ElementTheme.Default"/>，因此沿可视化树向上回退查找。
    /// </summary>
    private ElementTheme ResolveEffectiveTheme()
    {
        FrameworkElement? element = ThemeAnchor;
        while (element is not null)
        {
            if (element.ActualTheme != ElementTheme.Default) return element.ActualTheme;
            element = VisualTreeHelper.GetParent(element) as FrameworkElement;
        }
        return App.MainWindow?.Content is FrameworkElement root && root.ActualTheme != ElementTheme.Default
            ? root.ActualTheme
            : ElementTheme.Light;
    }

    private void InitializeCharts()
    {
        bool dark = ResolveEffectiveTheme() == ElementTheme.Dark;
        var months = new string[] { "1月", "2月", "3月", "4月", "5月", "6月",
                                    "7月", "8月", "9月", "10月", "11月", "12月" };

        // 坐标轴文字用主题感知的次要色：浅色 #595959 / 深色 #C9C9C9，不用硬编码暗灰
        SKColor labelColor = dark ? new SKColor(0xC9, 0xC9, 0xC9) : new SKColor(0x59, 0x59, 0x59);
        // 网格线：同色系但透明度降到 ~33%，避免深色下刺眼白线抢戏
        SKColor separatorColor = new SKColor(labelColor.Red, labelColor.Green, labelColor.Blue, 0x55);
        LegendTextPaint = new SolidColorPaint(labelColor);

        // 游玩时长柱状图
        PlayHoursSeries = new ISeries[]
        {
            new ColumnSeries<double>
            {
                Values = AnnualReportData.PlayedTimePerMonth,
                Name = "月度游玩时长",
                Fill = new SolidColorPaint(dark ? new SKColor(0x26, 0xA6, 0x41) : new SKColor(0x26, 0x64, 0x89)),
                YToolTipLabelFormatter = point => $"{point.Model:F1} 小时",
            }
        };
        PlayHoursXAxes =
        [
            new Axis
            {
                Labels = months,
                LabelsRotation = 0,
                LabelsPaint = new SolidColorPaint(labelColor),
                SeparatorsPaint = new SolidColorPaint(separatorColor)
            }
        ];
        PlayHoursYAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(labelColor),
                SeparatorsPaint = new SolidColorPaint(separatorColor)
            }
        ];

        // 游戏完成度饼图
        List<ISeries> gamesPlayedSeries = new();
        foreach (KeyValuePair<PlayType, int> series in AnnualReportData.PlayTypeCnt)
        {
            if (series.Value == 0) continue;
            gamesPlayedSeries.Add(new PieSeries<double>
            {
                Values = [series.Value],
                Name = series.Key.GetLocalized(),
            });
        }
        GamesPlayedSeries = gamesPlayedSeries.ToArray();

        // 月度活跃度折线图
        var activity = new double[12];
        for (var i = 0; i < 12; i++)
        {
            activity[i] = AnnualReportData.PlayedTimePerMonth[i] + AnnualReportData.PlayedGamesPerMonth[i] * 10;
            MostActiveMonth = activity[i] > activity[MostActiveMonth] ? i : MostActiveMonth;
        }
        MostActiveMonth++; // 月份从1开始
        ActiveMonthSeries = new ISeries[]
        {
            new LineSeries<double>
            {
                Values = activity,
                Name = "月度活跃度",
                Fill = null,
                GeometrySize = 10,
                Stroke = new SolidColorPaint(dark ? new SKColor(0x68, 0xB9, 0xC0) : new SKColor(0x68, 0xB9, 0xC0), 3),
                GeometryStroke = new SolidColorPaint(new SKColor(0x68, 0xB9, 0xC0), 3),
                // 活跃度是合成值，原始 double 会是 63.7666667 这种长尾；限制为 1 位小数
                YToolTipLabelFormatter = point => $"{point.Model:F1}",
            }
        };

        MonthXAxes =
        [
            new Axis
            {
                Labels = months,
                LabelsRotation = 0,
                LabelsPaint = new SolidColorPaint(labelColor),
                SeparatorsPaint = new SolidColorPaint(separatorColor)
            }
        ];
        MonthYAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(labelColor),
                SeparatorsPaint = new SolidColorPaint(separatorColor)
            }
        ];

        // 游戏时长分布条形图
        List<double> playedTimeRange = [];
        List<string> playedTimeRangeLabels = [];
        for (var i = 0; i < AnnualReportData.PlayedTimeRange.Length; i++)
            if (AnnualReportData.PlayedTimeRangeCnt[i] > 0)
            {
                playedTimeRange.Add(AnnualReportData.PlayedTimeRangeCnt[i]);
                var label = i == AnnualReportData.PlayedTimeRange.Length - 1
                    ? $"{AnnualReportData.PlayedTimeRange[i]}h+"
                    : $"{AnnualReportData.PlayedTimeRange[i]}-{AnnualReportData.PlayedTimeRange[i + 1]}h";
                playedTimeRangeLabels.Add(label);
            }
        AverageHoursSeries = new ISeries[]
        {
            new ColumnSeries<double>
            {
                Values = playedTimeRange.ToArray(),
                Name = "游戏数量",
                Fill = new SolidColorPaint(dark ? new SKColor(0x26, 0xA6, 0x41) : new SKColor(0x26, 0x64, 0x89)),
                YToolTipLabelFormatter = point => $"{point.Model:F0} 部",
            }
        };

        AverageHoursXAxes =
        [
            new Axis
            {
                Labels = playedTimeRangeLabels.ToArray(),
                LabelsRotation = 0,
                LabelsPaint = new SolidColorPaint(labelColor),
                SeparatorsPaint = new SolidColorPaint(separatorColor),
                // 区间标签较多，强制每根柱子都画标签，否则中间的会被自动跳过
                ForceStepToMin = true,
                MinStep = 1
            }
        ];
        AverageHoursYAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(labelColor),
                SeparatorsPaint = new SolidColorPaint(separatorColor)
            }
        ];
    }

    [RelayCommand]
    private void TogglePlayHoursDetail()
    {
        IsPlayHoursDetailVisible = !IsPlayHoursDetailVisible;

        IsGamesPlayedDetailVisible = false;
        IsActiveMonthDetailVisible = false;
        IsAverageHoursDetailVisible = false;
    }

    [RelayCommand]
    private void ToggleGamesPlayedDetail()
    {
        IsGamesPlayedDetailVisible = !IsGamesPlayedDetailVisible;

        IsPlayHoursDetailVisible = false;
        IsActiveMonthDetailVisible = false;
        IsAverageHoursDetailVisible = false;
    }

    [RelayCommand]
    private void ToggleActiveMonthDetail()
    {
        IsActiveMonthDetailVisible = !IsActiveMonthDetailVisible;

        IsPlayHoursDetailVisible = false;
        IsGamesPlayedDetailVisible = false;
        IsAverageHoursDetailVisible = false;
    }

    [RelayCommand]
    private void ToggleAverageHoursDetail()
    {
        IsAverageHoursDetailVisible = !IsAverageHoursDetailVisible;

        IsPlayHoursDetailVisible = false;
        IsGamesPlayedDetailVisible = false;
        IsActiveMonthDetailVisible = false;
    }
}

public class MonthlyData
{
    public string? Month { get; set; }
    public double Hours { get; set; }
}
