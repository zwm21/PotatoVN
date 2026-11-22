using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using GalgameManager.Contracts.ViewModels;
using GalgameManager.Models;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;

namespace GalgameManager.ViewModels;

public partial class AnnualReportSubPage4ViewModel : ObservableObject, INavigationAware
{
    [ObservableProperty] private AnnualReportData _annualReportData = new();
    [ObservableProperty] private ISeries[]? _dayOfWeekSeries = Array.Empty<ISeries>();
    [ObservableProperty] private ICartesianAxis[]? _dayOfWeekXAxes = Array.Empty<ICartesianAxis>();
    [ObservableProperty] private ICartesianAxis[]? _dayOfWeekYAxes = Array.Empty<ICartesianAxis>();

    /// <summary>主题锚点，由视图注入；用于读取生效主题并在主题切换时重绘图表</summary>
    [ObservableProperty] private FrameworkElement? _themeAnchor;

    partial void OnThemeAnchorChanged(FrameworkElement? value)
    {
        if (value is null) return;
        value.ActualThemeChanged += (_, _) => BuildChart();
    }

    public void OnNavigatedTo(object parameter)
    {
        Debug.Assert(parameter is AnnualReportData);
        AnnualReportData = (AnnualReportData)parameter;
        BuildChart();
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

    /// <summary>
    /// 构建图表。坐标轴文字用主题感知的次要文字色，网格线压低透明度，避免深色下出现刺眼白线。
    /// </summary>
    private void BuildChart()
    {
        bool dark = ResolveEffectiveTheme() == ElementTheme.Dark;
        // 次要文字色：浅色 #595959 / 深色 #C9C9C9，均不用硬编码暗灰
        SKColor labelColor = dark ? new(0xC9, 0xC9, 0xC9) : new(0x59, 0x59, 0x59);
        // 网格线：同色系但透明度降到 ~33%，不抢戏
        SKColor separatorColor = new(labelColor.Red, labelColor.Green, labelColor.Blue, 0x55);

        DayOfWeekSeries =
        [
            new ColumnSeries<double>
            {
                Values = AnnualReportData.PlayTimePerDayOfWeek,
                Name = "游玩时长(小时)",
                Fill = new SolidColorPaint(dark ? new SKColor(0x26, 0xA6, 0x41) : new SKColor(0x40, 0xC4, 0x63))
            }
        ];

        DayOfWeekXAxes =
        [
            new Axis
            {
                Labels = ["周日", "周一", "周二", "周三", "周四", "周五", "周六"],
                LabelsRotation = 0,
                LabelsPaint = new SolidColorPaint(labelColor),
                SeparatorsPaint = new SolidColorPaint(separatorColor),
                SeparatorsAtCenter = false,
                TicksPaint = new SolidColorPaint(labelColor),
                TicksAtCenter = true
            }
        ];

        DayOfWeekYAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(labelColor),
                SeparatorsPaint = new SolidColorPaint(separatorColor),
                TicksPaint = new SolidColorPaint(labelColor)
            }
        ];
    }
}
