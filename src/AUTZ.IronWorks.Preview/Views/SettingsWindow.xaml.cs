using System.Windows;
using AUTZ.IronWorks.Preview.Models;
namespace AUTZ.IronWorks.Preview.Views;
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    public SettingsWindow(AppSettings settings) { InitializeComponent(); _settings=settings; WidthBox.Text=settings.PreviewWidth.ToString(); RefreshBox.Text=settings.RefreshMilliseconds.ToString(); VerticalBox.IsChecked=settings.VerticalLayout; LabelsBox.IsChecked=settings.ShowLabels; ColourBox.Text=settings.ActiveBorderHex; }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(WidthBox.Text,out var width) || width<180 || width>640) { MessageBox.Show("Preview width must be 180 to 640."); return; }
        if (!int.TryParse(RefreshBox.Text,out var refresh) || refresh<500 || refresh>10000) { MessageBox.Show("Refresh interval must be 500 to 10000 ms."); return; }
        try { _ = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ColourBox.Text); } catch { MessageBox.Show("Enter a valid hex colour such as #F59E0B."); return; }
        _settings.PreviewWidth=width; _settings.RefreshMilliseconds=refresh; _settings.VerticalLayout=VerticalBox.IsChecked==true; _settings.ShowLabels=LabelsBox.IsChecked==true; _settings.ActiveBorderHex=ColourBox.Text.Trim(); DialogResult=true; Close();
    }
}
