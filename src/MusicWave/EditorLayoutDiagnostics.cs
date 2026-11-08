using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MusicWave;

internal static class EditorLayoutDiagnostics
{
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    static void Click(DependencyObject root, string content) => Descendants(root).OfType<Button>().Single(b => Equals(b.Content, content)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static void Render(MainWindow window, string path)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var image = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth + 52), (int)Math.Ceiling(content.ActualHeight + 34), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(window.Background, null, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            var brush = new VisualBrush(content) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(content.Margin.Left, content.Margin.Top, content.ActualWidth, content.ActualHeight) };
            drawing.DrawRectangle(brush, null, new Rect(26, 20, content.ActualWidth, content.ActualHeight));
        }
        image.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); encoder.Save(file);
    }
    public static void Run(StringBuilder report, string directory)
    {
        var project = ProjectSettings.Demo();
        var group = LayerGroups.Create(project, [project.Layers[1], project.Layers[2]]);
        for (var i = 1; i <= 20; i++) project.Layers.Add(new WallpaperLayer { Name = $"额外图层 {i}", Path = "demo:mint", Visible = false });
        var window = new MainWindow(project, true) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0 };
        try
        {
            window.Show(); window.UpdateLayout();
            Check(window.LayerList.ActualHeight > 350 && window.Preview.ActualHeight > 0, "Sidebar or preview did not receive full editor height.");
            Check(window.GroupAnimationEditor.IsEnabled && !window.LayerAnimationEditor.IsEnabled, "Initial group inheritance UI failed.");
            window.PropertyTabs.SelectedItem = window.GroupPropertiesTab; window.UpdateLayout();
            project.Layers[2].Visible = false;
            Click(window, "隐藏组内图层"); window.UpdateLayout();
            Check(!group.Visible && Equals(window.GroupVisibilityButton.Content, "显示组内图层") && window.GroupMembersText.Text.StartsWith("已隐藏"), "Group hide button did not update the model, label or status.");
            Render(window, Path.Combine(directory, "editor-hidden-group.png"));
            Click(window, "显示组内图层"); window.UpdateLayout();
            Check(group.Visible && project.Layers[1].Visible && !project.Layers[2].Visible && Equals(window.GroupVisibilityButton.Content, "隐藏组内图层"), "Group show button changed individual member switches or failed to reset its label.");
            project.Layers[2].Visible = true;
            window.PropertyTabs.SelectedItem = window.AnimationPropertiesTab; window.UpdateLayout();
            Click(window, "＋新建"); window.UpdateLayout();
            Check(project.Animations.Count == 1 && window.AnimationEditor.IsEnabled && window.AnimationBindingPanel.IsEnabled, "Create animation UI failed.");
            var animation = project.Animations[0];
            var checks = Descendants(window.AnimationTargets).OfType<CheckBox>().ToArray();
            Check(checks.Length == project.Layers.Count + project.Groups.Count, "Binding target list omitted layers or groups.");
            foreach (var target in new AnimationTarget[] { group, project.Layers[3], project.Layers[4] })
            {
                var check = checks.Single(c => ReferenceEquals(c.Tag, target));
                check.IsChecked = true; check.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            }
            Check(group.AnimationId == animation.Id && project.Layers[3].AnimationId == animation.Id && project.Layers[4].AnimationId == animation.Id && window.AnimationBindingCount.Text == "3 个目标", "Checkbox binding UI failed.");
            Check(!window.GroupAnimationEditor.IsEnabled && window.GroupBindingHint.Visibility == Visibility.Visible, "Bound group still exposes ignored local parameters.");
            Check(window.GroupVisibilityButton.IsEnabled, "Animation binding disabled the group visibility button.");
            var nameBox = Descendants(window.AnimationEditor).OfType<TextBox>().First();
            nameBox.Text = "共享节拍测试";
            Check(animation.Name == "共享节拍测试" && project.Layers[1].AnimationName.Contains(animation.Name), "Rename did not update model or inherited layer label.");
            window.LayerList.UnselectAll(); window.LayerList.SelectedItems.Add(project.Layers[3]);
            Check(!window.LayerAnimationEditor.IsEnabled && window.LayerBindingHint.Visibility == Visibility.Visible, "Bound layer still exposes ignored local parameters.");
            Render(window, Path.Combine(directory, "editor-animation-objects.png"));
            var groupCheck = checks.Single(c => ReferenceEquals(c.Tag, group));
            groupCheck.IsChecked = false; groupCheck.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Check(group.AnimationId == "" && window.GroupAnimationEditor.IsEnabled && window.AnimationBindingCount.Text == "2 个目标", "Checkbox unbinding UI failed.");
            Click(window, "复制动画"); window.UpdateLayout();
            Check(project.Animations.Count == 2 && project.Animations[1].Name.Contains("副本") && window.AnimationBindingCount.Text == "0 个目标", "Duplicate copied bindings or failed to select copy.");
            Click(window, "删除动画"); window.UpdateLayout();
            Check(project.Animations.Count == 1 && ReferenceEquals(window.AnimationPicker.SelectedItem, animation), "Deleting animation did not select remaining object.");
            Click(window, "删除动画"); window.UpdateLayout();
            Check(project.Animations.Count == 0 && project.Layers.All(l => l.AnimationId == "") && !window.AnimationEditor.IsEnabled && window.LayerAnimationEditor.IsEnabled, "Deleting last animation left stale binding UI.");
            window.PropertyTabs.SelectedItem = window.LayerPropertiesTab;
            window.Width = 1180; window.Height = 740; window.UpdateLayout();
            Check(window.LayerList.ActualHeight > 200 && window.Preview.ActualWidth > 0, "Minimum window size hid the sidebar or preview.");
            Render(window, Path.Combine(directory, "editor-minimum-layout.png"));
            window.WorkspaceTabs.SelectedIndex = 1; window.UpdateLayout();
            Check(window.AudioDevices.IsVisible && !window.LayerList.IsVisible, "Desktop/tempo controls did not move to their own tab.");
            Render(window, Path.Combine(directory, "desktop-tempo-tab.png"));
            window.WorkspaceTabs.SelectedIndex = 0; window.PropertyTabs.SelectedItem = window.GroupPropertiesTab; window.UpdateLayout();
            Check(window.Preview.SelectedGroup == group, "Group property tab did not activate preview anchor overlay.");
            report.AppendLine("PASS: isolated editor create/rename/copy/delete, multi-target checkbox binding/unbinding, inherited/explicit property states, 24-layer full-height sidebar, minimum window layout, desktop/tempo tab and group overlay; UI screenshots saved without changing user configuration.");
        }
        finally { window.Close(); }
    }
}
