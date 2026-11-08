using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace MusicWave;

public partial class MainWindow
{
    readonly HashSet<LayerGroup> watchedGroups = [];
    WallpaperLayer[] SelectedLayers() => LayerList.SelectedItems.OfType<WallpaperLayer>().ToArray();
    void RebindGroups()
    {
        foreach (var group in watchedGroups.Where(g => !settings.Groups.Contains(g)).ToArray())
        { group.PropertyChanged -= GroupPropertyChanged; watchedGroups.Remove(group); }
        foreach (var group in settings.Groups)
            if (watchedGroups.Add(group)) group.PropertyChanged += GroupPropertyChanged;
    }
    void GroupsChanged(object? sender, NotifyCollectionChangedEventArgs e) { RebindGroups(); dirty = true; RefreshGroupUi(); }
    void GroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        dirty = true;
        if (e.PropertyName == nameof(LayerGroup.Name)) RefreshGroupUi();
    }
    void RefreshGroupUi()
    {
        if (settings is null || GroupEditor is null || LayerAnimationEditor is null) return;
        LayerGroups.RefreshNames(settings);
        var group = GroupPicker.SelectedItem as LayerGroup;
        GroupEditor.DataContext = group; GroupEditor.IsEnabled = group is not null;
        GroupEmptyHint.Visibility = group is null ? Visibility.Visible : Visibility.Collapsed;
        var members = group is null ? [] : LayerGroups.Members(settings, group);
        GroupMembersText.Text = group is null ? "" : $"{members.Length} 个成员 · 悬停查看名称";
        GroupMembersText.ToolTip = group is null ? null : string.Join(Environment.NewLine, members.Select(l => l.Name));
        var memberGroup = LayerEditor.DataContext is WallpaperLayer layer ? LayerGroups.Find(settings, layer) : null;
        LayerAnimationEditor.IsEnabled = memberGroup is null;
        LayerStaticOption.IsEnabled = memberGroup is null;
        MemberGroupHint.Visibility = memberGroup is null ? Visibility.Collapsed : Visibility.Visible;
        MemberGroupText.Text = memberGroup is null ? "" : $"由「{memberGroup.Name}」控制整体动画";
        Preview.SelectedGroup = GroupPropertiesPanel.Visibility == Visibility.Visible ? group : null;
    }
    void LayerProperties_Click(object sender, RoutedEventArgs e)
    {
        LayerPropertiesPanel.Visibility = Visibility.Visible; GroupPropertiesPanel.Visibility = Visibility.Collapsed;
        PropertyScroll.ScrollToTop();
        Preview.SelectedGroup = null;
    }
    void ShowGroupProperties()
    {
        LayerPropertiesPanel.Visibility = Visibility.Collapsed; GroupPropertiesPanel.Visibility = Visibility.Visible;
        PropertyScroll.ScrollToTop();
        RefreshGroupUi();
    }
    void GroupProperties_Click(object sender, RoutedEventArgs e) => ShowGroupProperties();
    void Group_Changed(object sender, SelectionChangedEventArgs e) => RefreshGroupUi();
    void CreateGroup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var group = LayerGroups.Create(settings, SelectedLayers());
            GroupPicker.SelectedItem = group; ShowGroupProperties();
            Status($"已创建「{group.Name}」，可在右侧统一调整组动画。");
        }
        catch (InvalidOperationException ex) { Status(ex.Message); }
    }
    void AddToGroup_Click(object sender, RoutedEventArgs e)
    {
        if (GroupPicker.SelectedItem is not LayerGroup group) { ShowGroupProperties(); Status("请先在右侧选择一个组，再将选中的图层加入当前组。"); return; }
        var count = LayerGroups.Assign(settings, group, SelectedLayers());
        RefreshGroupUi(); Status($"已将 {count} 个图层加入「{group.Name}」。");
    }
    void DetachGroup_Click(object sender, RoutedEventArgs e)
    {
        var count = LayerGroups.Detach(settings, SelectedLayers());
        RefreshGroupUi(); Status($"已将 {count} 个图层移出组，恢复各自原有的独立动画参数。");
    }
    void EditMemberGroup_Click(object sender, RoutedEventArgs e)
    {
        if (LayerEditor.DataContext is WallpaperLayer layer) GroupPicker.SelectedItem = LayerGroups.Find(settings, layer);
        ShowGroupProperties();
    }
    void SelectGroupMembers_Click(object sender, RoutedEventArgs e)
    {
        if (GroupPicker.SelectedItem is not LayerGroup group) return;
        var members = LayerGroups.Members(settings, group);
        LayerList.UnselectAll();
        foreach (var layer in members) LayerList.SelectedItems.Add(layer);
        if (members.Length > 0) LayerList.ScrollIntoView(members[0]);
    }
    void DissolveGroup_Click(object sender, RoutedEventArgs e)
    {
        if (GroupPicker.SelectedItem is not LayerGroup group) return;
        LayerGroups.Dissolve(settings, group); RefreshGroupUi();
        Status($"已解散「{group.Name}」，所有成员图层及其独立参数已保留。");
    }
    void ResetGroupAnchor_Click(object sender, RoutedEventArgs e)
    {
        if (GroupPicker.SelectedItem is not LayerGroup group) return;
        LayerGroups.ResetAnchor(settings, group); Status("组锚点已重置为成员布局的底部中心。");
    }
}
