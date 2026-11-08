using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace MusicWave;

public partial class MainWindow
{
    readonly HashSet<AnimationObject> watchedAnimations = [];
    readonly List<AnimationBindingItem> animationBindingItems = [];
    sealed class AnimationBindingItem(AnimationTarget target) : Observable
    {
        string name = "", description = "";
        bool isBound;
        public AnimationTarget Target { get; } = target;
        public string Name { get => name; private set => Set(ref name, value); }
        public string Description { get => description; private set => Set(ref description, value); }
        public bool IsBound { get => isBound; private set => Set(ref isBound, value); }
        public void Refresh(ProjectSettings project, AnimationObject? animation)
        {
            Name = Target is LayerGroup group ? $"组 · {group.Name}" : $"图层 · {((WallpaperLayer)Target).Name}";
            IsBound = animation is not null && Target.AnimationId == animation.Id;
            Description = Target.AnimationName;
            if (Target is WallpaperLayer layer && LayerGroups.Find(project, layer) is { } parent) Description = $"{parent.Name} · {Description}";
        }
    }
    void RebindAnimations()
    {
        foreach (var animation in watchedAnimations.Where(a => !settings.Animations.Contains(a)).ToArray())
        { animation.PropertyChanged -= AnimationPropertyChanged; watchedAnimations.Remove(animation); }
        foreach (var animation in settings.Animations)
            if (watchedAnimations.Add(animation)) animation.PropertyChanged += AnimationPropertyChanged;
    }
    void AnimationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebindAnimations(); dirty = true; RefreshGroupUi(); RefreshAnimationUi();
    }
    void AnimationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        dirty = true;
        if (e.PropertyName == nameof(AnimationObject.Name)) { RefreshGroupUi(); RefreshAnimationUi(); }
    }
    void RefreshAnimationUi(bool rebuild = false)
    {
        if (settings is null || AnimationEditor is null || AnimationBindingPanel is null || AnimationTargets is null) return;
        AnimationBindings.RefreshNames(settings);
        var animation = AnimationPicker.SelectedItem as AnimationObject;
        AnimationEditor.DataContext = animation;
        AnimationEditor.IsEnabled = AnimationBindingPanel.IsEnabled = animation is not null;
        AnimationEmptyHint.Visibility = animation is null ? Visibility.Visible : Visibility.Collapsed;
        var targets = settings.Groups.Cast<AnimationTarget>().Concat(settings.Layers).ToArray();
        if (rebuild || !animationBindingItems.Select(i => i.Target).SequenceEqual(targets))
        {
            animationBindingItems.Clear();
            animationBindingItems.AddRange(targets.Select(t => new AnimationBindingItem(t)));
            AnimationTargets.ItemsSource = animationBindingItems.ToArray();
        }
        foreach (var item in animationBindingItems) item.Refresh(settings, animation);
        AnimationBindingCount.Text = $"{animationBindingItems.Count(i => i.IsBound)} 个目标";
    }
    void ShowAnimationProperties(AnimationObject? animation = null)
    {
        if (animation is not null) AnimationPicker.SelectedItem = animation;
        PropertyTabs.SelectedItem = AnimationPropertiesTab;
        AnimationPropertyScroll.ScrollToTop();
        RefreshAnimationUi();
    }
    void Animation_Changed(object sender, SelectionChangedEventArgs e) => RefreshAnimationUi();
    void ShowAnimationBindings_Click(object sender, RoutedEventArgs e)
    {
        AnimationPropertyScroll.UpdateLayout();
        AnimationBindingCount.BringIntoView();
    }
    void CreateAnimation_Click(object sender, RoutedEventArgs e)
    {
        var animation = AnimationBindings.Create(settings);
        ShowAnimationProperties(animation);
        Status($"已新建「{animation.Name}」，勾选绑定目标即可应用。");
    }
    void DuplicateAnimation_Click(object sender, RoutedEventArgs e)
    {
        if (AnimationPicker.SelectedItem is not AnimationObject source) return;
        var animation = AnimationBindings.Create(settings, source);
        animation.Name = source.Name + " · 副本";
        ShowAnimationProperties(animation);
        Status("已复制动画参数，可为副本选择新的绑定目标。");
    }
    void DeleteAnimation_Click(object sender, RoutedEventArgs e)
    {
        if (AnimationPicker.SelectedItem is not AnimationObject animation) return;
        var index = settings.Animations.IndexOf(animation);
        AnimationBindings.Remove(settings, animation);
        AnimationPicker.SelectedIndex = settings.Animations.Count == 0 ? -1 : Math.Min(index, settings.Animations.Count - 1);
        RefreshGroupUi(); RefreshAnimationUi();
        Status($"已删除「{animation.Name}」，绑定目标恢复原有动画参数。");
    }
    void AnimationTarget_Click(object sender, RoutedEventArgs e)
    {
        if (AnimationPicker.SelectedItem is not AnimationObject animation || sender is not CheckBox { Tag: AnimationTarget target } check) return;
        if (check.IsChecked == true) AnimationBindings.Assign(settings, animation, target);
        else if (target.AnimationId == animation.Id) target.AnimationId = "";
        RefreshGroupUi(); RefreshAnimationUi();
        Status(check.IsChecked == true ? $"已绑定到「{animation.Name}」。" : "已解除绑定，恢复原有动画参数。");
    }
    void BindSelectedLayers_Click(object sender, RoutedEventArgs e)
    {
        if (AnimationPicker.SelectedItem is not AnimationObject animation) return;
        var selected = SelectedLayers();
        foreach (var layer in selected) AnimationBindings.Assign(settings, animation, layer);
        RefreshGroupUi(); RefreshAnimationUi();
        Status(selected.Length == 0 ? "先在左侧选择一个或多个图层。" : $"已将 {selected.Length} 个图层绑定到「{animation.Name}」。");
    }
    void BindLayersAnimation_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLayers().Length == 0) { ShowAnimationProperties(); Status("先在左侧选择一个或多个图层，或新建动画后直接勾选目标。"); return; }
        if (settings.Animations.Count == 0)
        {
            var layer = SelectedLayers()[0];
            var source = AnimationBindings.Resolve(settings, (AnimationTarget?)AnimationBindings.MotionGroup(settings, layer) ?? layer);
            ShowAnimationProperties(AnimationBindings.Create(settings, source));
        }
        else ShowAnimationProperties(AnimationPicker.SelectedItem as AnimationObject ?? settings.Animations[0]);
        ShowAnimationBindings_Click(sender, e);
        Status("选择动画后点击「绑定左侧选中图层」，或直接勾选绑定目标。");
    }
    void BindGroupAnimation_Click(object sender, RoutedEventArgs e)
    {
        if (GroupPicker.SelectedItem is not LayerGroup group) return;
        if (settings.Animations.Count == 0) ShowAnimationProperties(AnimationBindings.Create(settings, group));
        else ShowAnimationProperties(AnimationBindings.Find(settings, group) ?? AnimationPicker.SelectedItem as AnimationObject ?? settings.Animations[0]);
        ShowAnimationBindings_Click(sender, e);
        Status($"在绑定目标中勾选「组 · {group.Name}」即可绑定。");
    }
    void EditLayerAnimation_Click(object sender, RoutedEventArgs e)
    {
        if (LayerEditor.DataContext is WallpaperLayer layer) ShowAnimationProperties(AnimationBindings.Find(settings, layer));
    }
    void EditGroupAnimation_Click(object sender, RoutedEventArgs e)
    {
        if (GroupPicker.SelectedItem is LayerGroup group) ShowAnimationProperties(AnimationBindings.Find(settings, group));
    }
}
