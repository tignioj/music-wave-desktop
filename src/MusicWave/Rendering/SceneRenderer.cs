using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicWave.Desktop;

namespace MusicWave.Rendering;

public sealed partial class SceneRenderer : FrameworkElement
{
    readonly Dictionary<string, BitmapSource> images = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(string Path, double Width, double Height, bool Stretch), DrawingGroup> demos = [];
    static readonly SolidColorBrush background = Brush("#101D31");
    readonly HashSet<string> failed = [];
    ProjectSettings? project;
    MotionFrame frame;
    public bool ShowPreviewIcons { get; set; }
    public IconPreviewLayout? PreviewIcons { get; set; }
    public string? ImageError { get; private set; }
    public void Update(ProjectSettings settings, MotionFrame value)
    {
        project = settings; frame = value;
        if (gesture is not null && !CanEdit(gesture.Layer)) EndEdit();
        InvalidateVisual();
    }
    public void ClearCache() { images.Clear(); hitImages.Clear(); demos.Clear(); failed.Clear(); ImageError = null; }
    public static BitmapSource LoadImage(string path)
    {
        var uri = new Uri(Path.GetFullPath(path));
        var metadata = BitmapDecoder.Create(uri, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = uri;
        if (metadata.PixelWidth > 3840) bitmap.DecodePixelWidth = 3840;
        bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }
    public void Validate(ProjectSettings settings)
    {
        foreach (var layer in settings.Layers.Where(l => LayerGroups.IsVisible(settings, l) && !l.Path.StartsWith("demo:")))
            if (!images.ContainsKey(layer.Path)) images[layer.Path] = LoadImage(layer.Path);
    }
    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.PushClip(new RectangleGeometry(new(0, 0, w, h)));
        dc.DrawRectangle(background, null, new(0, 0, w, h));
        if (project is not null)
        {
            for (var i = 0; i < project.Layers.Count; i++)
            {
                var layer = project.Layers[i];
                if (!LayerGroups.IsVisible(project, layer)) continue;
                var motion = EditingMotion(layer);
                var cx = w * layer.X / 100 + motion.X * w / 1920;
                var cy = h * layer.Y / 100 + motion.Y * w / 1920;
                var lw = w * layer.Width / 100; var lh = h * layer.Height / 100;
                dc.PushOpacity(layer.Opacity);
                dc.PushTransform(new TranslateTransform(cx, cy));
                dc.PushTransform(new ScaleTransform(motion.ScaleX, motion.ScaleY));
                if (layer.Path.StartsWith("demo:"))
                {
                    dc.DrawDrawing(DemoDrawing(layer, lw, lh));
                }
                else if (!failed.Contains(layer.Path))
                {
                    try
                    {
                        if (!images.TryGetValue(layer.Path, out var bitmap)) images[layer.Path] = bitmap = LoadImage(layer.Path);
                        dc.DrawImage(bitmap, ImageBounds(layer, bitmap, lw, lh));
                    }
                    catch (Exception ex) { failed.Add(layer.Path); ImageError = $"无法加载 {layer.Name}：{ex.Message}"; }
                }
                dc.Pop(); dc.Pop(); dc.Pop();
            }
            if (ShowPreviewIcons) DrawIcons(dc, w, h);
            DrawSelection(dc);
        }
        dc.Pop();
    }
    DrawingGroup DemoDrawing(WallpaperLayer layer, double w, double h)
    {
        var key = (layer.Path, w, h, layer.StretchToBounds);
        if (!demos.TryGetValue(key, out var drawing))
        {
            drawing = new DrawingGroup();
            using (var content = drawing.Open()) DrawDemo(content, layer.Path, w, h, layer.StretchToBounds);
            drawing.Freeze();
            if (demos.Count >= 64) demos.Clear();
            demos[key] = drawing;
        }
        return drawing;
    }
    Rect ImageBounds(WallpaperLayer layer, BitmapSource bitmap, double w, double h)
    {
        if (layer.StretchToBounds) return new(-w / 2, -h, w, h);
        var cover = project?.Layers.IndexOf(layer) == 0 && layer.IsStatic;
        var scale = cover ? Math.Max(w / bitmap.PixelWidth, h / bitmap.PixelHeight) : Math.Min(w / bitmap.PixelWidth, h / bitmap.PixelHeight);
        var iw = bitmap.PixelWidth * scale; var ih = bitmap.PixelHeight * scale;
        return new(-iw / 2, -ih, iw, ih);
    }
    static SolidColorBrush Brush(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    internal static void DrawDemo(DrawingContext dc, string kind, double w, double h, bool stretchToBounds = false)
    {
        if (stretchToBounds && kind != "demo:sky")
        {
            var drawing = new DrawingGroup();
            using (var content = drawing.Open()) DrawDemo(content, kind, w, h);
            var bounds = drawing.Bounds;
            dc.PushTransform(new TranslateTransform(-w / 2, -h));
            dc.PushTransform(new ScaleTransform(w / bounds.Width, h / bounds.Height));
            dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
            dc.DrawDrawing(drawing);
            dc.Pop(); dc.Pop(); dc.Pop();
            return;
        }
        if (kind == "demo:sky")
        {
            dc.PushTransform(new TranslateTransform(-w / 2, -h));
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(12, 22, 42), Color.FromRgb(36, 61, 78), 90), null, new(0, 0, w, h));
            var glow = new RadialGradientBrush(Color.FromArgb(65, 101, 220, 215), Colors.Transparent) { RadiusX = .5, RadiusY = .5 };
            dc.DrawEllipse(glow, null, new(w * .53, h * .52), w * .6, h * .6);
            var random = new Random(17);
            for (var i = 0; i < 65; i++) dc.DrawEllipse(Brush(i % 3 == 0 ? "#95B7C8" : "#526F87"), null, new(random.NextDouble() * w, random.NextDouble() * h * .74), Math.Max(.5, w / 1100), Math.Max(.5, w / 1100));
            Polygon(dc, Brush("#172D41"), [new(0,h*.8),new(w*.16,h*.55),new(w*.33,h*.76),new(w*.59,h*.51),new(w*.8,h*.75),new(w,h*.56),new(w,h),new(0,h)]);
            Polygon(dc, Brush("#101F32"), [new(0,h*.88),new(w*.27,h*.72),new(w*.48,h*.92),new(w*.76,h*.73),new(w,h*.86),new(w,h),new(0,h)]);
            dc.DrawRectangle(Brush("#0B1828"), null, new(0, h * .92, w, h * .08));
            var pen = new Pen(Brush("#244350"), Math.Max(.5, w / 1800));
            for (var i = 0; i < 12; i++) dc.DrawLine(pen, new(w * i / 11, h * .92), new(w * (.5 + (i / 11.0 - .5) * 2), h));
            dc.Pop(); return;
        }
        var r = Math.Min(w / 2, h / 2);
        var center = new Point(0, -r);
        var mint = kind == "demo:mint";
        var violet = kind == "demo:violet";
        var c = (Color)ColorConverter.ConvertFromString(mint ? "#80EDD3" : violet ? "#AF93FA" : "#8DDDEB");
        dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(60, c.R, c.G, c.B), Colors.Transparent), null, new(0, -r * .12), r * 1.4, r * .16);
        var gradient = new RadialGradientBrush { GradientOrigin = new(.3, .2), Center = new(.5, .5), RadiusX = .7, RadiusY = .7 };
        gradient.GradientStops.Add(new(Colors.White, 0)); gradient.GradientStops.Add(new(c, .3)); gradient.GradientStops.Add(new(Color.FromRgb(31, 61, 85), 1));
        dc.DrawEllipse(gradient, new Pen(new SolidColorBrush(Color.FromArgb(180, c.R, c.G, c.B)), Math.Max(1, r * .009)), center, r * .88, r);
        dc.PushTransform(new RotateTransform(-20, 0, -r));
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(160, c.R, c.G, c.B)), r * .035), center, r * 1.2, r * .28);
        dc.Pop();
        if (!mint && !violet)
        {
            // A playful face makes the bottom-anchored squash immediately readable.
            foreach (var dx in new[] { -.28, .28 })
            {
                dc.DrawEllipse(Brush("#102C43"), null, new(r * dx, -r * 1.02), r * .045, r * .09);
                dc.DrawEllipse(Brush("#C8FAF0"), null, new(r * dx - r * .012, -r * 1.05), r * .016, r * .026);
            }
            dc.DrawEllipse(Brush("#245267"), null, new(0, -r * .78), r * .07, r * .05);
        }
    }
    static void Polygon(DrawingContext dc, Brush brush, Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open()) { ctx.BeginFigure(points[0], true, true); ctx.PolyLineTo(points.Skip(1).ToArray(), true, false); }
        geometry.Freeze(); dc.DrawGeometry(brush, null, geometry);
    }
    void DrawIcons(DrawingContext dc, double w, double h)
    {
        var size = w / 32;
        var layout = PreviewIcons;
        var screenWidth = layout?.Bounds.Width ?? MotionMath.DesignWidth;
        var screenHeight = layout?.Bounds.Height ?? MotionMath.DesignWidth * h / w;
        var positions = layout?.Positions ?? Enumerable.Range(0, 5)
            .Select(i => new NativePoint((int)(screenWidth * .04), (int)(screenHeight * (.08 + i * .16)))).ToArray();
        for (var i = 0; i < positions.Count; i++)
        {
            var p = positions[i];
            var x = p.X * w / screenWidth;
            var offset = project!.AnimateIcons ? MotionMath.IconOffset(p.X, p.Y, screenWidth, frame, project.IconAmplitude, project.IconSpread) : 0;
            var y = (p.Y + offset) * h / screenHeight;
            dc.DrawRoundedRectangle(Brush(i % 2 == 0 ? "#729FB2" : "#B59BD2"), null, new(x, y, size, size), size * .2, size * .2);
            dc.DrawLine(new Pen(Brush("#52677C"), 2), new(x - size * .15, y + size * 1.3), new(x + size * 1.15, y + size * 1.3));
        }
    }
}
