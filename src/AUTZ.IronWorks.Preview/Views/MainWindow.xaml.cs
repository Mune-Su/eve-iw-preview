using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Models;
using AUTZ.IronWorks.Preview.Services;

namespace AUTZ.IronWorks.Preview.Views;
public partial class MainWindow : Window
{
    private readonly AppSettings _settings; private readonly EveWindowService _windows = new(); private DispatcherTimer _timer = new(); private bool _closing; private string _signature="";
    public MainWindow(AppSettings settings)
    {
        InitializeComponent(); _settings=settings; Left=settings.Left; Top=settings.Top; ConfigureTimer(); Loaded += (_,__) => RefreshClients(); LocationChanged += (_,__) => SavePosition(); Closing += (_,e) => { if(!_closing){ e.Cancel=true; Hide(); } };
    }
    private void ConfigureTimer() { _timer.Stop(); _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_settings.RefreshMilliseconds) }; _timer.Tick += (_,__) => RefreshClients(); _timer.Start(); }
    public void RefreshClients()
    {
        var clients=_windows.FindClients(); string sig=string.Join('|',clients.Select(c=>$"{c.Handle}:{c.Title}")); if(sig==_signature){UpdateActiveBorders(); return;} _signature=sig; Rebuild(clients);
    }
    private void Rebuild(IReadOnlyList<EveClientWindow> clients)
    {
        ClientPanel.Children.Clear(); ClientPanel.Orientation=_settings.VerticalLayout?Orientation.Vertical:Orientation.Horizontal; EmptyMessage.Visibility=clients.Count==0?Visibility.Visible:Visibility.Collapsed;
        foreach(var c in clients)
        {
            var card=new PreviewCard(c){Width=_settings.PreviewWidth,Height=Math.Round(_settings.PreviewWidth*9d/16d)}; card.ClientClicked += h=>_windows.Activate(h);
            var border=new Border{Tag=c.Handle,BorderThickness=new Thickness(3),BorderBrush=Brushes.Transparent,Margin=new Thickness(2),CornerRadius=new CornerRadius(4),Child=card};
            if(_settings.ShowLabels){ var grid=new Grid(); grid.Children.Add(border); var label=new Border{Background=new SolidColorBrush(Color.FromArgb(210,10,15,22)),VerticalAlignment=VerticalAlignment.Bottom,HorizontalAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(7,4,7,4),IsHitTestVisible=false}; label.Child=new TextBlock{Text=c.CharacterName,Foreground=Brushes.White,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis}; grid.Children.Add(label); var wrap=new Border{Tag=c.Handle,BorderThickness=new Thickness(3),BorderBrush=Brushes.Transparent,Margin=new Thickness(2),CornerRadius=new CornerRadius(4),Child=grid}; border.BorderThickness=new Thickness(0); border.Margin=new Thickness(0); ClientPanel.Children.Add(wrap); } else ClientPanel.Children.Add(border);
        } UpdateActiveBorders();
    }
    private void UpdateActiveBorders() { nint active=NativeMethods.GetForegroundWindow(); Brush activeBrush; try{activeBrush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(_settings.ActiveBorderHex));}catch{activeBrush=Brushes.Orange;} foreach(var b in ClientPanel.Children.OfType<Border>()) b.BorderBrush=(nint)b.Tag==active?activeBrush:Brushes.Transparent; }
    private void Header_Drag(object sender, MouseButtonEventArgs e) { if(e.LeftButton==MouseButtonState.Pressed) DragMove(); }
    private void Settings_Click(object sender, RoutedEventArgs e)=>OpenSettings();
    public void OpenSettings() { var dialog=new SettingsWindow(_settings){Owner=this}; if(dialog.ShowDialog()==true){SettingsService.Save(_settings); ConfigureTimer(); _signature=""; RefreshClients();} }
    public void ToggleVisibility(){if(IsVisible)Hide();else{Show();Activate();}}
    private void SavePosition(){if(!IsLoaded)return;_settings.Left=Left;_settings.Top=Top;SettingsService.Save(_settings);}
    public void CloseForExit(){_closing=true;_timer.Stop();SettingsService.Save(_settings);Close();}
}
