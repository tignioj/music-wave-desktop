using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using AlphaMode = Vortice.DXGI.AlphaMode;
using PixelFormat = Vortice.DCommon.PixelFormat;
using Rect = System.Windows.Rect;
using FeatureLevel = Vortice.Direct3D.FeatureLevel;

namespace MusicWave.Rendering;

// Upload immutable layer pixels once. A frame changes only GPU transforms and
// opacity: no full-screen WPF rasterization, readback, GDI scaling or painting.
internal sealed class CompositionScene : IDisposable
{
    readonly int width, height;
    ID3D11Device? graphics;
    ID3D11DeviceContext? graphicsContext;
    ID2D1Device? drawing;
    IDCompositionDesktopDevice? device;
    IDCompositionTarget? target;
    IDCompositionVisual2? root;
    readonly List<LayerVisual> layers = [];
    public int UploadCount { get; private set; }
    public long CommitCount { get; private set; }

    sealed class LayerVisual(WallpaperLayer layer, IDCompositionVisual2 visual, IDCompositionEffectGroup opacity) : IDisposable
    {
        public readonly WallpaperLayer Layer = layer;
        public readonly IDCompositionVisual2 Visual = visual;
        public readonly IDCompositionEffectGroup Opacity = opacity;
        public IDCompositionSurface? Surface;
        public string Path = "";
        public double Width, Height;
        public bool Cover;
        public Rect Bounds;
        public int PixelWidth, PixelHeight;
        public Matrix3x2? Matrix;
        public float Alpha = -1;
        public void Dispose() { Visual.Dispose(); Opacity.Dispose(); Surface?.Dispose(); }
    }

    public CompositionScene(nint hwnd, int width, int height)
    {
        this.width = width; this.height = height;
        try
        {
            FeatureLevel[] levels = [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];
            var result = D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, levels,
                out graphics, out _, out graphicsContext);
            if (result.Failure)
                D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.BgraSupport, levels,
                    out graphics, out _, out graphicsContext).CheckError();
            using var dxgi = graphics!.QueryInterface<IDXGIDevice>();
            drawing = D2D1.D2D1CreateDevice(dxgi);
            using var composition = DComp.DCompositionCreateDevice2<IDCompositionDevice>(drawing);
            device = composition.QueryInterface<IDCompositionDesktopDevice>();
            device.CreateTargetForHwnd(hwnd, true, out target).CheckError();
            root = device.CreateVisual();
            root.SetClip(new Vortice.RawRectF(0, 0, width, height)).CheckError();
            target.SetRoot(root).CheckError();
            // Opaque base, including when a project has no background layer.
            using var surface = Upload(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Pbgra32, null, new byte[] { 49, 29, 16, 255 }, 4));
            // Keep the base in its own visual so it doesn't scale the children.
            using var backdrop = device.CreateVisual();
            backdrop.SetContent(surface).CheckError();
            backdrop.SetTransform(Matrix3x2.CreateScale(width, height)).CheckError();
            root.AddVisual(backdrop, false, null).CheckError();
        }
        catch { Dispose(); throw; }
    }

    public void InvalidateImages() { foreach (var item in layers) item.Path = ""; }

    public void Update(ProjectSettings project, MotionFrame frame)
    {
        bool changed = CommitCount == 0;
        // Reconcile by object identity; reorder without re-uploading textures.
        var visible = project.Layers.Where(l => l.Visible).ToArray();
        if (!layers.Select(l => l.Layer).SequenceEqual(visible))
        {
            var prior = layers.ToArray(); layers.Clear();
            foreach (var item in prior) root!.RemoveVisual(item.Visual).CheckError();
            foreach (var layer in visible)
            {
                var item = prior.FirstOrDefault(l => ReferenceEquals(l.Layer, layer));
                if (item is null)
                {
                    var visual = device!.CreateVisual();
                    var opacity = device.CreateEffectGroup();
                    visual.SetEffect(opacity).CheckError();
                    item = new(layer, visual, opacity);
                }
                layers.Add(item);
                // With no reference visual, FALSE inserts above all siblings.
                root!.AddVisual(item.Visual, false, null).CheckError();
            }
            foreach (var item in prior.Where(l => !layers.Contains(l))) item.Dispose();
            changed = true;
        }
        foreach (var item in layers)
        {
            var layer = item.Layer;
            var cover = project.Layers.IndexOf(layer) == 0 && layer.IsStatic;
            if (item.Surface is null || item.Path != layer.Path || item.Width != layer.Width || item.Height != layer.Height || item.Cover != cover)
            {
                var bitmap = LayerPixels(layer, cover, out var bounds);
                var surface = Upload(bitmap);
                try { item.Visual.SetContent(surface).CheckError(); }
                catch { surface.Dispose(); throw; }
                item.Surface?.Dispose(); item.Surface = surface;
                item.Path = layer.Path; item.Width = layer.Width; item.Height = layer.Height; item.Cover = cover;
                item.Bounds = bounds; item.PixelWidth = bitmap.PixelWidth; item.PixelHeight = bitmap.PixelHeight;
                item.Matrix = null; changed = true;
            }
            var motion = MotionMath.ForLayer(layer, frame);
            var sx = (float)(motion.ScaleX * item.Bounds.Width / item.PixelWidth);
            var sy = (float)(motion.ScaleY * item.Bounds.Height / item.PixelHeight);
            var matrix = new Matrix3x2(sx, 0, 0, sy,
                (float)(width * layer.X / 100 + motion.X * width / 1920 + item.Bounds.X * motion.ScaleX),
                (float)(height * layer.Y / 100 + motion.Y * width / 1920 + item.Bounds.Y * motion.ScaleY));
            if (item.Matrix != matrix) { item.Visual.SetTransform(matrix).CheckError(); item.Matrix = matrix; changed = true; }
            var alpha = (float)layer.Opacity;
            if (item.Alpha != alpha) { item.Opacity.SetOpacity(alpha).CheckError(); item.Alpha = alpha; changed = true; }
        }
        if (changed) { device!.Commit().CheckError(); CommitCount++; }
    }

    BitmapSource LayerPixels(WallpaperLayer layer, bool cover, out Rect bounds)
    {
        var w = width * layer.Width / 100; var h = height * layer.Height / 100;
        if (!layer.Path.StartsWith("demo:"))
        {
            var image = SceneRenderer.LoadImage(layer.Path);
            var scale = cover ? Math.Max(w / image.PixelWidth, h / image.PixelHeight) : Math.Min(w / image.PixelWidth, h / image.PixelHeight);
            bounds = new(-image.PixelWidth * scale / 2, -image.PixelHeight * scale, image.PixelWidth * scale, image.PixelHeight * scale);
            return image.Format == PixelFormats.Pbgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Pbgra32, null, 0);
        }
        var content = new DrawingVisual();
        using (var dc = content.RenderOpen()) SceneRenderer.DrawDemo(dc, layer.Path, w, h);
        bounds = content.ContentBounds;
        var ratio = Math.Min(1, 4096 / Math.Max(bounds.Width, bounds.Height));
        var pixels = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(bounds.Width * ratio)), Math.Max(1, (int)Math.Ceiling(bounds.Height * ratio)), 96, 96, PixelFormats.Pbgra32);
        var raster = new DrawingVisual();
        using (var dc = raster.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(ratio, ratio));
            dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
            dc.DrawDrawing(content.Drawing);
        }
        pixels.Render(raster); pixels.Freeze();
        // Include the subpixel padding introduced by rounding the raster size.
        bounds = new(bounds.X, bounds.Y, pixels.PixelWidth / ratio, pixels.PixelHeight / ratio);
        return pixels;
    }

    IDCompositionSurface Upload(BitmapSource bitmap)
    {
        var surface = device!.CreateSurface((uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied);
        try
        {
            using var context = surface.BeginDraw<ID2D1DeviceContext>(null, out var offset);
            try
            {
                context.SetDpi(96, 96);
                context.Transform = Matrix3x2.Identity;
                var update = new Vortice.RawRectF(offset.X, offset.Y, offset.X + bitmap.PixelWidth, offset.Y + bitmap.PixelHeight);
                context.PushAxisAlignedClip(update, AntialiasMode.Aliased);
                var stride = bitmap.PixelWidth * 4;
                var pixels = new byte[stride * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, stride, 0);
                var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try
                {
                    using var source = context.CreateBitmap(new SizeI(bitmap.PixelWidth, bitmap.PixelHeight), pin.AddrOfPinnedObject(), (uint)stride,
                        new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96));
                    context.Clear(new Color4(0, 0, 0, 0));
                    context.DrawBitmap(source, update, 1, Vortice.Direct2D1.InterpolationMode.Linear, null, null);
                }
                finally { pin.Free(); context.PopAxisAlignedClip(); }
            }
            finally { surface.EndDraw().CheckError(); }
            UploadCount++;
            return surface;
        }
        catch { surface.Dispose(); throw; }
    }

    public void Dispose()
    {
        foreach (var item in layers) item.Dispose(); layers.Clear();
        root?.Dispose(); root = null; target?.Dispose(); target = null; device?.Dispose(); device = null;
        drawing?.Dispose(); drawing = null; graphicsContext?.Dispose(); graphicsContext = null; graphics?.Dispose(); graphics = null;
    }
}
