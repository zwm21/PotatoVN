using GalgameManager.Contracts.Services;
using GalgameManager.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GalgameManager.Views;

/// <summary>
/// 「足迹」分页：GitHub 贡献图风格的日历热力图 + 当日游玩明细。
/// </summary>
public sealed partial class AnnualReportSubPageHeatmap : Page
{
    public AnnualReportSubPageHeatmapViewModel ViewModel
    {
        get;
    }

    public AnnualReportSubPageHeatmap()
    {
        ViewModel = new AnnualReportSubPageHeatmapViewModel(App.GetService<IGalgameCollectionService>());
        DataContext = ViewModel;
        InitializeComponent();
        ViewModel.HeatmapHost = HeatmapHost;
        ViewModel.WeekdayHost = WeekdayHost;
        ViewModel.TipCanvas = TipCanvas;
        // 浮动提示层取代原生 ToolTip，这里把容器与文本交给 ViewModel 定位
        ViewModel.AttachTip(TipBorder, TipText);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.OnNavigatedTo(e.Parameter);
    }
}
