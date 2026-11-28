using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AUTZ.IronWorks.Preview.Services;
namespace AUTZ.IronWorks.Preview.Views;
internal sealed class NewEdenMapWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly ComboBox _regions = new() { Width = 260, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _status = new() { Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center };
    private readonly Canvas _map = new() { Width = 1000, Height = 650, Background = new SolidColorBrush(Color.FromRgb(12, 17, 27)) };
    private readonly Dictionary<int, string> _names = new();
    public NewEdenMapWindow()
    {
        Title = "New Eden Map"; Width = 1120; Height = 790; MinWidth = 600; MinHeight = 450;
        Background = new SolidColorBrush(Color.FromRgb(13, 15, 22)); Foreground = Brushes.White;
        var root = new DockPanel { Margin = new Thickness(12) };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        bar.Children.Add(new TextBlock { Text = "REGION", Foreground = Brushes.Orange, Margin = new Thickness(0, 5, 12, 0) });
        bar.Children.Add(_regions);
        var load = new Button { Content = "Load Region", Margin = new Thickness(0, 0, 12, 0) };
        load.Click += async (_, _) => await LoadRegion(); bar.Children.Add(load); bar.Children.Add(_status);
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        var scroll = new ScrollViewer { Content = _map, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(scroll); Content = root;
        Loaded += async (_, _) => await LoadRegions();
    }
    private async Task LoadRegions()
    {
        try
        {
            _status.Text = "Loading regions...";
            int[] ids = await Http.GetFromJsonAsync<int[]>("https://esi.evetech.net/latest/universe/regions/?datasource=tranquility") ?? Array.Empty<int>();
            using var limit = new SemaphoreSlim(8);
            var regions = await Task.WhenAll(ids.Select(async id =>
            {
                await limit.WaitAsync();
                try
                {
                    using var doc = await Http.GetFromJsonAsync<JsonDocument>($"https://esi.evetech.net/latest/universe/regions/{id}/?datasource=tranquility");
                    return (Id: id, Name: doc?.RootElement.GetProperty("name").GetString() ?? id.ToString());
                }
                catch { return (Id: id, Name: id.ToString()); }
                finally { limit.Release(); }
            }));
            foreach (var region in regions.OrderBy(x => x.Name)) { _names[region.Id] = region.Name; _regions.Items.Add(region.Name); }
            _regions.SelectedIndex = 0; _status.Text = "Select a region";
        }
        catch (Exception ex) { _status.Text = "Unable to load regions"; Log.Debug("MAP", ex.GetType().Name); }
    }
    private async Task LoadRegion()
    {
        if (_regions.SelectedItem is not string selected) return;
        int id = _names.First(x => x.Value == selected).Key;
        try
        {
            _status.Text = "Loading systems..."; _map.Children.Clear();
            using var region = await Http.GetFromJsonAsync<JsonDocument>($"https://esi.evetech.net/latest/universe/regions/{id}/?datasource=tranquility");
            var ids = region!.RootElement.GetProperty("systems").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            using var limit = new SemaphoreSlim(12);
            var systems = await Task.WhenAll(ids.Select(async systemId =>
            {
                await limit.WaitAsync();
                try
                {
                    using var data = await Http.GetFromJsonAsync<JsonDocument>($"https://esi.evetech.net/latest/universe/systems/{systemId}/?datasource=tranquility");
                    var e = data!.RootElement; var pos = e.GetProperty("position");
                    return (Name: e.GetProperty("name").GetString() ?? "", X: pos.GetProperty("x").GetDouble(), Y: pos.GetProperty("z").GetDouble(), Security: e.GetProperty("security_status").GetDouble(), Valid: true);
                }
                catch { return (Name: "", X: 0d, Y: 0d, Security: 0d, Valid: false); }
                finally { limit.Release(); }
            }));
            var valid = systems.Where(x => x.Valid).ToArray();
            if (valid.Length == 0) { _status.Text = "No map data available"; return; }
            double minX = valid.Min(x => x.X), maxX = valid.Max(x => x.X), minY = valid.Min(x => x.Y), maxY = valid.Max(x => x.Y);
            foreach (var sys in valid)
            {
                double x = 28 + (sys.X - minX) / Math.Max(1, maxX - minX) * 940;
                double y = 28 + (sys.Y - minY) / Math.Max(1, maxY - minY) * 590;
                var dot = new Ellipse { Width = 7, Height = 7, Fill = sys.Security >= .5 ? Brushes.DodgerBlue : sys.Security > 0 ? Brushes.Orange : Brushes.IndianRed,
                    ToolTip = $"{sys.Name} ({sys.Security:F1})" };
                Canvas.SetLeft(dot, x); Canvas.SetTop(dot, y); _map.Children.Add(dot);
            }
            _status.Text = $"{valid.Length} systems  |  Hover for name";
            Log.Debug("MAP", $"region loaded; systems={valid.Length}");
        }
        catch (Exception ex) { _status.Text = "Map loading failed"; Log.Debug("MAP", ex.GetType().Name); }
    }
}
