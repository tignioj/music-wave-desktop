using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MusicWave.Rendering;

public sealed partial class SceneRenderer
{
    static readonly SolidColorBrush selectionBrush = Brush("#68E1CA");
    readonly Dictionary<string, BitmapSource> hitImages = new(StringComparer.OrdinalIgnoreCase);
    WallpaperLayer? selectedLayer;
    LayerEditGesture? gesture;
    MotionFrame editFrame;
    public bool IsEditingEnabled { get; set; }
    LayerGroup? selectedGroup;
    public LayerGroup? SelectedGroup { get => selectedGroup; set { selectedGroup = value; InvalidateVisual(); } }
    public event EventHandler? SelectedLayerChanged;
    public WallpaperLayer? SelectedLayer
    {
        get => selectedLayer;
        set
        {
            if (ReferenceEquals(selectedLayer, value)) return;
            EndEdit(); selectedLayer = value; InvalidateVisual(); SelectedLayerChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    bool CanEdit(WallpaperLayer layer) => IsEditingEnabled && project is not null && project.Layers.Contains(layer) && LayerGroups.IsVisible(project, layer)
        && !(project.Layers.IndexOf(layer) == 0 && layer.IsStatic);
    LayerMotion EditingMotion(WallpaperLayer layer)
    {
        if (gesture is not null && ReferenceEquals(gesture.Layer, layer)) return gesture.Motion;
        var group = project is null ? null : AnimationBindings.MotionGroup(project, layer);
        var pose = gesture is not null && group is not null && AnimationBindings.MotionGroup(project!, gesture.Layer) == group ? editFrame : frame;
        return project is null ? MotionMath.ForLayer(layer, pose) : MotionMath.ForLayer(project, layer, pose, ActualWidth, ActualHeight);
    }

    internal Rect DisplayBounds(WallpaperLayer layer)
    {
        var w = ActualWidth; var h = ActualHeight;
        if (w <= 0 || h <= 0) return Rect.Empty;
        Rect bounds;
        if (layer.Path.StartsWith("demo:")) bounds = DemoDrawing(layer, w * layer.Width / 100, h * layer.Height / 100).Bounds;
        else
        {
            if (failed.Contains(layer.Path)) return Rect.Empty;
            try
            {
                if (!images.TryGetValue(layer.Path, out var bitmap)) images[layer.Path] = bitmap = LoadImage(layer.Path);
                bounds = ImageBounds(layer, bitmap, w * layer.Width / 100, h * layer.Height / 100);
            }
            catch { return Rect.Empty; }
        }
        if (bounds.IsEmpty) return bounds;
        var motion = EditingMotion(layer);
        bounds.Scale(motion.ScaleX, motion.ScaleY);
        bounds.Offset(w * layer.X / 100 + motion.X * w / MotionMath.DesignWidth, h * layer.Y / 100 + motion.Y * w / MotionMath.DesignWidth);
        return bounds;
    }
    double OverlayUnit
    {
        get
        {
            var root = Window.GetWindow(this);
            if (root is null) return 1;
            var transform = TransformToAncestor(root);
            var origin = transform.Transform(new Point());
            var unit = transform.Transform(new Point(1, 0));
            return 1 / Math.Max(.01, (unit - origin).Length);
        }
    }
    static IEnumerable<(Point Point, LayerHandle Handle)> Handles(Rect b)
    {
        var cx = b.Left + b.Width / 2; var cy = b.Top + b.Height / 2;
        yield return (b.TopLeft, LayerHandle.Top | LayerHandle.Left);
        yield return (new(cx, b.Top), LayerHandle.Top);
        yield return (b.TopRight, LayerHandle.Top | LayerHandle.Right);
        yield return (new(b.Right, cy), LayerHandle.Right);
        yield return (b.BottomRight, LayerHandle.Bottom | LayerHandle.Right);
        yield return (new(cx, b.Bottom), LayerHandle.Bottom);
        yield return (b.BottomLeft, LayerHandle.Bottom | LayerHandle.Left);
        yield return (new(b.Left, cy), LayerHandle.Left);
    }
    void DrawSelection(DrawingContext dc)
    {
        if (IsEditingEnabled && SelectedGroup is { Visible: true } group && project is not null && project.Groups.Contains(group))
        {
            var union = Rect.Empty;
            foreach (var member in LayerGroups.Members(project, group).Where(l => l.Visible)) union.Union(DisplayBounds(member));
            var groupUnit = OverlayUnit;
            var groupPen = new Pen(Brush("#BCA0FA"), 1.5 * groupUnit) { DashStyle = DashStyles.Dash };
            if (!union.IsEmpty) dc.DrawRectangle(null, groupPen, union);
            var motion = MotionMath.ForLayer(AnimationBindings.Resolve(project, group), gesture is not null && AnimationBindings.MotionGroup(project, gesture.Layer) == group ? editFrame : frame);
            var anchor = new Point(ActualWidth * group.AnchorX / 100 + motion.X * ActualWidth / MotionMath.DesignWidth,
                ActualHeight * group.AnchorY / 100 + motion.Y * ActualWidth / MotionMath.DesignWidth);
            dc.DrawEllipse(background, groupPen, anchor, 5 * groupUnit, 5 * groupUnit);
            dc.DrawLine(groupPen, new(anchor.X - 9 * groupUnit, anchor.Y), new(anchor.X + 9 * groupUnit, anchor.Y));
            dc.DrawLine(groupPen, new(anchor.X, anchor.Y - 9 * groupUnit), new(anchor.X, anchor.Y + 9 * groupUnit));
        }
        if (SelectedLayer is not { } layer || !CanEdit(layer)) return;
        var bounds = DisplayBounds(layer);
        if (bounds.IsEmpty) return;
        var unit = OverlayUnit; var size = 8 * unit;
        dc.DrawRectangle(null, new Pen(selectionBrush, 1.5 * unit), bounds);
        foreach (var (p, _) in Handles(bounds))
            dc.DrawRectangle(background, new Pen(selectionBrush, unit), new(p.X - size / 2, p.Y - size / 2, size, size));
    }
    LayerHandle HandleAt(Point position)
    {
        if (SelectedLayer is not { } layer || !CanEdit(layer)) return LayerHandle.None;
        var bounds = DisplayBounds(layer);
        if (bounds.IsEmpty) return LayerHandle.None;
        var radius = 7 * OverlayUnit;
        foreach (var (p, handle) in Handles(bounds))
            if (Math.Abs(p.X - position.X) <= radius && Math.Abs(p.Y - position.Y) <= radius) return handle;
        return LayerHandle.None;
    }
    internal WallpaperLayer? LayerAt(Point position) => project?.Layers.Reverse().FirstOrDefault(l => CanEdit(l) && l.Opacity > 0 && ContainsContent(l, position));
    bool ContainsContent(WallpaperLayer layer, Point position)
    {
        var bounds = DisplayBounds(layer);
        if (bounds.IsEmpty || !bounds.Contains(position)) return false;
        if (layer.Path.StartsWith("demo:")) return true;
        // Transparent PNG margins must not block a character behind them.
        if (!hitImages.TryGetValue(layer.Path, out var bitmap))
        {
            var source = images[layer.Path];
            hitImages[layer.Path] = bitmap = source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32
                ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        }
        var x = Math.Clamp((int)((position.X - bounds.Left) / bounds.Width * bitmap.PixelWidth), 0, bitmap.PixelWidth - 1);
        var y = Math.Clamp((int)((position.Y - bounds.Top) / bounds.Height * bitmap.PixelHeight), 0, bitmap.PixelHeight - 1);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel[3] > 8;
    }
    static Cursor HandleCursor(LayerHandle handle) => handle switch
    {
        LayerHandle.Left or LayerHandle.Right => Cursors.SizeWE,
        LayerHandle.Top or LayerHandle.Bottom => Cursors.SizeNS,
        LayerHandle.Top | LayerHandle.Left or LayerHandle.Bottom | LayerHandle.Right => Cursors.SizeNWSE,
        LayerHandle.Top | LayerHandle.Right or LayerHandle.Bottom | LayerHandle.Left => Cursors.SizeNESW,
        LayerHandle.Move => Cursors.SizeAll,
        _ => Cursors.Arrow
    };
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsEditingEnabled) return;
        var position = e.GetPosition(this);
        var handle = HandleAt(position);
        if (handle == LayerHandle.None)
        {
            SelectedLayer = LayerAt(position);
            if (SelectedLayer is null) { e.Handled = true; return; }
            handle = LayerHandle.Move;
        }
        Focus();
        var layer = SelectedLayer!;
        var group = AnimationBindings.MotionGroup(project!, layer);
        var parentMotion = group is null ? new LayerMotion(0, 0, 1, 1) : MotionMath.ForLayer(AnimationBindings.Resolve(project!, group), frame);
        var next = new LayerEditGesture(layer, handle, position, DisplayBounds(layer), RenderSize, EditingMotion(layer), parentMotion.ScaleX, parentMotion.ScaleY);
        if (CaptureMouse()) { editFrame = frame; gesture = next; Cursor = HandleCursor(handle); }
        e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsEditingEnabled) return;
        var position = e.GetPosition(this);
        if (gesture is not null)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { EndEdit(); return; }
            gesture.Apply(position, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            InvalidateVisual(); e.Handled = true;
        }
        else
        {
            var handle = HandleAt(position);
            Cursor = HandleCursor(handle != LayerHandle.None ? handle : LayerAt(position) is not null ? LayerHandle.Move : LayerHandle.None);
        }
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (gesture is null) return;
        EndEdit(); e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); EndEdit(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape || gesture is null) return;
        gesture.Restore(); EndEdit(); e.Handled = true;
    }
    void EndEdit()
    {
        if (gesture is null) return;
        gesture = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Cursor = Cursors.Arrow; InvalidateVisual();
    }
}
