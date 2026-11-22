
using GalgameManager.ViewModels;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;


// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace GalgameManager.Views;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class AnnualReportSubPage1 : Page
{
    public ReportSubPage1ViewModel ViewModel
    {
        get;
    }

    public AnnualReportSubPage1()
    {
        ViewModel = new ReportSubPage1ViewModel();
        this.InitializeComponent();
        // 主题锚点：图表坐标轴与图例颜色要随深浅主题切换
        ViewModel.ThemeAnchor = this;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.OnNavigatedTo(e.Parameter);
    }
}
