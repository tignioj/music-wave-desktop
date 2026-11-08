using System.Windows;

namespace MusicWave.Rendering;

[Flags]
internal enum LayerHandle { None = 0, Move = 1, Left = 2, Right = 4, Top = 8, Bottom = 16 }

// A gesture uses one captured animation pose, so the pointer never chases the beat.
internal sealed class LayerEditGesture(WallpaperLayer layer, LayerHandle handle, Point pointer, Rect bounds, Size canvas, LayerMotion motion, double anchorScaleX = 1, double anchorScaleY = 1)
{
    public WallpaperLayer Layer { get; } = layer;
    public LayerMotion Motion => motion with
    {
        X = motion.X + (Layer.X - x) / 100 * MotionMath.DesignWidth * (anchorScaleX - 1),
        Y = motion.Y + (Layer.Y - y) / 100 * canvas.Height / canvas.Width * MotionMath.DesignWidth * (anchorScaleY - 1)
    };
    public LayerHandle Handle { get; } = handle;
    readonly double x = layer.X, y = layer.Y, width = layer.Width, height = layer.Height;
    readonly bool stretch = layer.StretchToBounds;

    public void Apply(Point position, bool keepAspect)
    {
        var delta = position - pointer;
        if (Handle == LayerHandle.Move)
        {
            Layer.X = x + delta.X * 100 / canvas.Width / anchorScaleX;
            Layer.Y = y + delta.Y * 100 / canvas.Height / anchorScaleY;
            return;
        }
        var left = Handle.HasFlag(LayerHandle.Left); var right = Handle.HasFlag(LayerHandle.Right);
        var top = Handle.HasFlag(LayerHandle.Top); var bottom = Handle.HasFlag(LayerHandle.Bottom);
        var sx = left ? (bounds.Width - delta.X) / bounds.Width : right ? (bounds.Width + delta.X) / bounds.Width : 1;
        var sy = top ? (bounds.Height - delta.Y) / bounds.Height : bottom ? (bounds.Height + delta.Y) / bounds.Height : 1;
        var baseWidth = bounds.Width / Motion.ScaleX;
        var baseHeight = bounds.Height / Motion.ScaleY;
        var minX = canvas.Width * .01 / baseWidth; var maxX = canvas.Width * 3 / baseWidth;
        var minY = canvas.Height * .01 / baseHeight; var maxY = canvas.Height * 3 / baseHeight;
        if (keepAspect)
        {
            var scale = !(left || right) ? sy : !(top || bottom) ? sx : Math.Abs(sx - 1) >= Math.Abs(sy - 1) ? sx : sy;
            var min = Math.Max(minX, minY); var max = Math.Min(maxX, maxY);
            // Extremely thin images cannot satisfy both model limits at one ratio.
            if (min <= max) sx = sy = Math.Clamp(scale, min, max);
            else { sx = Math.Clamp(scale, minX, maxX); sy = Math.Clamp(scale, minY, maxY); }
        }
        else { sx = Math.Clamp(sx, minX, maxX); sy = Math.Clamp(sy, minY, maxY); }
        var w = bounds.Width * sx; var h = bounds.Height * sy;
        var cx = left ? bounds.Right - w / 2 : right ? bounds.Left + w / 2 : bounds.Left + bounds.Width / 2;
        var by = top ? bounds.Bottom : bottom ? bounds.Top + h : bounds.Top + (bounds.Height + h) / 2;
        // Convert the displayed rectangle back to the model's center/bottom anchor.
        Layer.StretchToBounds = true;
        Layer.Width = w / Motion.ScaleX * 100 / canvas.Width;
        Layer.Height = h / Motion.ScaleY * 100 / canvas.Height;
        var startX = canvas.Width * x / 100 + motion.X * canvas.Width / MotionMath.DesignWidth;
        var startY = canvas.Height * y / 100 + motion.Y * canvas.Width / MotionMath.DesignWidth;
        Layer.X = x + (cx - startX) * 100 / canvas.Width / anchorScaleX;
        Layer.Y = y + (by - startY) * 100 / canvas.Height / anchorScaleY;
    }

    public void Restore()
    {
        Layer.X = x; Layer.Y = y; Layer.Width = width; Layer.Height = height; Layer.StretchToBounds = stretch;
    }
}
