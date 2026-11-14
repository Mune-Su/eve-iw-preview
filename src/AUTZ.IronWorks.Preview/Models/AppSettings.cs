namespace AUTZ.IronWorks.Preview.Models;
public sealed class AppSettings
{
    public double Left { get; set; } = 24;
    public double Top { get; set; } = 24;
    public int PreviewWidth { get; set; } = 280;
    public int RefreshMilliseconds { get; set; } = 1500;
    public bool VerticalLayout { get; set; } = true;
    public bool ShowLabels { get; set; } = true;
    public string ActiveBorderHex { get; set; } = "#F59E0B";
}
