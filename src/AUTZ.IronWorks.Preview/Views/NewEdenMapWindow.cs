using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AUTZ.IronWorks.Preview.Services;
namespace AUTZ.IronWorks.Preview.Views;
internal sealed class NewEdenMapWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly SemaphoreSlim _limit = new(12);
    private readonly Dictionary<int, Region> _regions = new();
    private readonly Dictionary<int, SystemNode> _systems = new();
    private readonly Dictionary<int, int[]> _gates = new();
    private readonly Canvas _canvas = new() { Width = 1200, Height = 760, Background = new SolidColorBrush(Color.FromRgb(11, 17, 27)) };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ComboBox _regionBox = new() { Width = 190 };
    private readonly TextBox _search = new() { Width = 160, Margin = new Thickness(8, 0, 5, 0) };
    private readonly TextBlock _status = new() { Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private readonly CheckBox _activity = new() { Content = "Activity", IsChecked = false, Foreground = Brushes.White, Margin = new Thickness(12, 4, 0, 0) };
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Dictionary<int, (int Jumps, int Kills)> _activityData = new();
    private int? _region;
    private bool _loading;
    private record Region(int Id, string Name, int[] Systems, double X, double Z);
    private record SystemNode(int Id, string Name, double X, double Z, double Security, int[] Gates);
    private sealed class RegionChoice { public int Id { get; init; } public string Name { get; init; } = ""; public override string ToString() => Name; }
    public NewEdenMapWindow()
    {
        Title = "New Eden Map"; Width = 1200; Height = 820; MinWidth = 660; MinHeight = 450;
        Background = new SolidColorBrush(Color.FromRgb(13, 16, 23)); Foreground = Brushes.White;
        var root = new DockPanel { Margin = new Thickness(10) };
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        void Button(string text, Action action) { var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0) }; button.Click += (_, _) => action(); bar.Children.Add(button); }
        Button("Universe", () => _ = DrawUniverse());
        bar.Children.Add(_regionBox);
        Button("Region", () => { if (_regionBox.SelectedItem is RegionChoice r) _ = DrawRegion(r.Id); });
        bar.Children.Add(_search);
        Button("Find", () => _ = Search());
        Button("+", () => Zoom(1.25)); Button("-", () => Zoom(.8)); Button("Reset", () => { _scale.ScaleX = _scale.ScaleY = 1; });
        bar.Children.Add(_activity); _activity.Checked += async (_, _) => { await LoadActivity(); if (_region is int id) await DrawRegion(id); };
        _activity.Unchecked += async (_, _) => { if (_region is int id) await DrawRegion(id); };
        bar.Children.Add(_status);
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        _canvas.LayoutTransform = _scale; _scroll.Content = _canvas; root.Children.Add(_scroll); Content = root;
        _canvas.MouseWheel += (_, e) => { Zoom(e.Delta > 0 ? 1.15 : .87); e.Handled = true; };
        Loaded += async (_, _) => await LoadUniverse();
    }
    private void Zoom(double factor) { _scale.ScaleX = _scale.ScaleY = Math.Clamp(_scale.ScaleX * factor, .35, 5); }
    private async Task<T?> Get<T>(string route) where T : class
    {
        await _limit.WaitAsync();
        try { return await Http.GetFromJsonAsync<T>("https://esi.evetech.net/latest/" + route + (route.Contains('?') ? "&" : "?") + "datasource=tranquility"); }
        catch (Exception ex) { Log.Debug("MAP", "ESI request failed " + ex.GetType().Name); return null; }
        finally { _limit.Release(); }
    }
    private async Task<JsonDocument?> Json(string route) => await Get<JsonDocument>(route);
    private async Task LoadUniverse()
    {
        if (_loading) return; _loading = true;
        try
        {
            _status.Text = "Loading New Eden...";
            int[] ids = await Get<int[]>("universe/regions/") ?? Array.Empty<int>();
            var regions = await Task.WhenAll(ids.Select(async id =>
            {
                using var doc = await Json($"universe/regions/{id}/");
                if (doc is null) return null;
                var e = doc.RootElement;
                var systems = e.TryGetProperty("systems", out var arr) ? arr.EnumerateArray().Select(x => x.GetInt32()).ToArray() : Array.Empty<int>();
                return new Region(id, e.GetProperty("name").GetString() ?? id.ToString(), systems, 0, 0);
            }));
            foreach (var region in regions.OfType<Region>()) _regions[region.Id] = region;
            _regionBox.Items.Clear();
            foreach (var region in _regions.Values.OrderBy(x => x.Name)) _regionBox.Items.Add(new RegionChoice { Id = region.Id, Name = region.Name });
            // ESI exposes system positions but no region positions. Use one representative system for each region.
            var centers = await Task.WhenAll(_regions.Values.Select(async r =>
            {
                if (r.Systems.Length == 0) return (r.Id, X: 0d, Z: 0d, Ok: false);
                var node = await LoadSystem(r.Systems[0]);
                return (r.Id, X: node?.X ?? 0, Z: node?.Z ?? 0, Ok: node is not null);
            }));
            foreach (var center in centers.Where(x => x.Ok))
            {
                var r = _regions[center.Id]; _regions[center.Id] = r with { X = center.X, Z = center.Z };
            }
            await DrawUniverse();
        }
        finally { _loading = false; }
    }
    private async Task<SystemNode?> LoadSystem(int id)
    {
        if (_systems.TryGetValue(id, out var cached)) return cached;
        using var doc = await Json($"universe/systems/{id}/");
        if (doc is null) return null;
        var e = doc.RootElement; var pos = e.GetProperty("position");
        var gates = e.TryGetProperty("stargates", out var a) ? a.EnumerateArray().Select(x => x.GetInt32()).ToArray() : Array.Empty<int>();
        var node = new SystemNode(id, e.GetProperty("name").GetString() ?? id.ToString(), pos.GetProperty("x").GetDouble(), pos.GetProperty("z").GetDouble(), e.GetProperty("security_status").GetDouble(), gates);
        _systems[id] = node; return node;
    }
    private static (double X, double Y) Position(double x, double z, double minX, double maxX, double minZ, double maxZ) =>
        (35 + (x - minX) / Math.Max(1, maxX - minX) * 1120, 35 + (z - minZ) / Math.Max(1, maxZ - minZ) * 690);
    private async Task DrawUniverse()
    {
        _region = null; _canvas.Children.Clear();
        var all = _regions.Values.Where(x => x.Systems.Length > 0 && (x.X != 0 || x.Z != 0)).ToArray();
        if (all.Length == 0) { _status.Text = "Universe data unavailable"; return; }
        double minX = all.Min(x => x.X), maxX = all.Max(x => x.X), minZ = all.Min(x => x.Z), maxZ = all.Max(x => x.Z);
        foreach (var r in all)
        {
            var (x, y) = Position(r.X, r.Z, minX, maxX, minZ, maxZ);
            var label = new TextBlock { Text = "● " + r.Name, Foreground = Brushes.Orange, FontSize = 11, Cursor = Cursors.Hand, ToolTip = $"{r.Systems.Length} systems" };
            label.MouseLeftButtonUp += async (_, _) => await DrawRegion(r.Id);
            Canvas.SetLeft(label, x); Canvas.SetTop(label, y); _canvas.Children.Add(label);
        }
        _status.Text = $"Universe | {all.Length} regions | Click a region";
        await Task.CompletedTask;
    }
    private async Task DrawRegion(int regionId)
    {
        if (!_regions.TryGetValue(regionId, out var region)) return;
        _region = regionId; _canvas.Children.Clear(); _status.Text = "Loading " + region.Name + "...";
        var nodes = (await Task.WhenAll(region.Systems.Select(LoadSystem))).OfType<SystemNode>().ToArray();
        if (nodes.Length == 0) { _status.Text = "Region data unavailable"; return; }
        double minX = nodes.Min(x => x.X), maxX = nodes.Max(x => x.X), minZ = nodes.Min(x => x.Z), maxZ = nodes.Max(x => x.Z);
        var coords = nodes.ToDictionary(x => x.Id, x => Position(x.X, x.Z, minX, maxX, minZ, maxZ));
        // Resolve each gate's destination system, draw only intra-region connections.
        var gates = nodes.SelectMany(n => n.Gates.Select(g => (System: n.Id, Gate: g))).ToArray();
        var destinations = await Task.WhenAll(gates.Select(async g =>
        {
            if (_gates.TryGetValue(g.Gate, out var known)) return (g.System, Target: known[0]);
            using var doc = await Json($"universe/stargates/{g.Gate}/");
            if (doc is null) return (g.System, Target: -1);
            int target = doc.RootElement.GetProperty("destination").GetProperty("system_id").GetInt32();
            _gates[g.Gate] = new[] { target }; return (g.System, Target: target);
        }));
        foreach (var (from, to) in destinations.Where(g => coords.ContainsKey(g.Target) && g.System < g.Target).Distinct())
        {
            var a = coords[from]; var b = coords[to];
            var line = new Line { X1 = a.X + 3, Y1 = a.Y + 3, X2 = b.X + 3, Y2 = b.Y + 3, Stroke = new SolidColorBrush(Color.FromRgb(64, 82, 105)), StrokeThickness = 1 };
            _canvas.Children.Add(line);
        }
        foreach (var n in nodes)
        {
            var (x, y) = coords[n.Id];
            var dot = new Ellipse { Width = 8, Height = 8, Fill = n.Security >= .5 ? Brushes.DodgerBlue : n.Security > 0 ? Brushes.Orange : Brushes.IndianRed };
            string extra = _activityData.TryGetValue(n.Id, out var activity) && _activity.IsChecked == true ? $" | {activity.Jumps} jumps, {activity.Kills} kills" : "";
            dot.ToolTip = $"{n.Name} ({n.Security:F1}){extra}";
            Canvas.SetLeft(dot, x); Canvas.SetTop(dot, y); _canvas.Children.Add(dot);
            var label = new TextBlock { Text = n.Name, Foreground = Brushes.LightGray, FontSize = 9, ToolTip = dot.ToolTip };
            Canvas.SetLeft(label, x + 9); Canvas.SetTop(label, y - 4); _canvas.Children.Add(label);
        }
        _status.Text = $"{region.Name} | {nodes.Length} systems | Stargates shown";
    }
    private async Task LoadActivity()
    {
        try
        {
            using var jumps = await Json("universe/system_jumps/");
            using var kills = await Json("universe/system_kills/");
            if (jumps is not null) foreach (var row in jumps.RootElement.EnumerateArray())
            {
                int id = row.GetProperty("system_id").GetInt32(); int count = row.GetProperty("ship_jumps").GetInt32();
                _activityData[id] = (count, _activityData.TryGetValue(id, out var previousJumps) ? previousJumps.Kills : 0);
            }
            if (kills is not null) foreach (var row in kills.RootElement.EnumerateArray())
            {
                int id = row.GetProperty("system_id").GetInt32(); int count = row.GetProperty("ship_kills").GetInt32() + row.GetProperty("pod_kills").GetInt32();
                _activityData[id] = (_activityData.TryGetValue(id, out var previousKills) ? previousKills.Jumps : 0, count);
            }
        }
        catch (Exception ex) { Log.Debug("MAP", "activity unavailable: " + ex.GetType().Name); }
    }
    private async Task Search()
    {
        string term = _search.Text.Trim(); if (term.Length == 0) return;
        var region = _regions.Values.FirstOrDefault(x => x.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        if (region is not null) { await DrawRegion(region.Id); return; }
        var system = _systems.Values.FirstOrDefault(x => x.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        if (system is not null)
        {
            var containing = _regions.Values.FirstOrDefault(x => x.Systems.Contains(system.Id));
            if (containing is not null) { await DrawRegion(containing.Id); _status.Text = "Found " + system.Name; return; }
        }
        _status.Text = "Search the loaded regions or systems";
    }
}
