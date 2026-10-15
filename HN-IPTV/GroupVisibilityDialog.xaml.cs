using System.ComponentModel;
using System.Windows;
using HnIptv.Models;

namespace HnIptv;

public sealed class GroupVisibilityRow(ChannelGroup group, bool visible) : INotifyPropertyChanged
{
    public string Id { get; } = group.Id;
    public string Name { get; } = group.Name;
    private bool _visible = visible;
    public bool Visible { get => _visible; set { _visible = value; PropertyChanged?.Invoke(this, new(nameof(Visible))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class GroupVisibilityDialog : Window
{
    public List<GroupVisibilityRow> Rows { get; }
    public GroupVisibilityDialog(IEnumerable<ChannelGroup> groups, ISet<string> hidden)
    {
        InitializeComponent();
        Rows = groups.Where(g => g.Id is not ("*" or "fav"))
            .Select(g => new GroupVisibilityRow(g, !hidden.Contains(g.Id))).ToList();
        GroupList.ItemsSource = Rows;
    }
    private void HideAll_Click(object sender, RoutedEventArgs e) { foreach (var r in Rows) r.Visible = false; }
    private void ShowAll_Click(object sender, RoutedEventArgs e) { foreach (var r in Rows) r.Visible = true; }
    private void Done_Click(object sender, RoutedEventArgs e) { DialogResult = true; }
}
