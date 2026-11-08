using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using MusicWave.Rendering;

namespace MusicWave;

internal static class PreviewEditingDiagnostics
{
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static bool Near(double a, double b) => Math.Abs(a - b) < .001;

    public static void Run(StringBuilder report, string directory)
    {
        var preview = new SceneRenderer { Width = 960, Height = 540, IsEditingEnabled = true };
        preview.Measure(new(960, 540)); preview.Arrange(new(0, 0, 960, 540));
        var project = ProjectSettings.Demo();
        var handles = new[] { LayerHandle.Left, LayerHandle.Right, LayerHandle.Top, LayerHandle.Bottom,
            LayerHandle.Left | LayerHandle.Top, LayerHandle.Right | LayerHandle.Top,
            LayerHandle.Left | LayerHandle.Bottom, LayerHandle.Right | LayerHandle.Bottom };
        var cases = 0;
        foreach (var beat in new[] { 0.0, .25, .5 })
        foreach (var path in new[] { "demo:orb", Path.Combine(directory, "transparent-test.png") })
        {
            var layer = project.Layers[1]; layer.Path = path;
            var frame = new MotionFrame(beat, 1, 120, 0, 1);
            preview.Update(project, frame);
            var start = preview.DisplayBounds(layer);
            var move = new LayerEditGesture(layer, LayerHandle.Move, new(100, 100), start, preview.RenderSize, MotionMath.ForLayer(layer, frame));
            move.Apply(new(148, 127), false);
            var moved = preview.DisplayBounds(layer);
            Check(Near(moved.X - start.X, 48) && Near(moved.Y - start.Y, 27), "Animated layer did not follow pointer translation.");
            move.Restore();
            foreach (var handle in handles)
            {
                foreach (var keepAspect in new[] { false, true })
                {
                    var gesture = new LayerEditGesture(layer, handle, new(100, 100), start, preview.RenderSize, MotionMath.ForLayer(layer, frame));
                    gesture.Apply(new(118, 111), keepAspect);
                    var resized = preview.DisplayBounds(layer);
                    Check(layer.StretchToBounds && resized.Width > 0 && resized.Height > 0, "Resize did not produce a visible stretched layer.");
                    if (handle.HasFlag(LayerHandle.Left)) Check(Near(resized.Right, start.Right), "Left handle moved the opposite edge.");
                    if (handle.HasFlag(LayerHandle.Right)) Check(Near(resized.Left, start.Left), "Right handle moved the opposite edge.");
                    if (handle.HasFlag(LayerHandle.Top)) Check(Near(resized.Bottom, start.Bottom), "Top handle moved the bottom anchor.");
                    if (handle.HasFlag(LayerHandle.Bottom)) Check(Near(resized.Top, start.Top), "Bottom handle moved the opposite edge.");
                    if (keepAspect) Check(Near(resized.Width / resized.Height, start.Width / start.Height), "Shift resize changed the aspect ratio.");
                    if (!keepAspect && handle == LayerHandle.Right) Check(Near(resized.Width, start.Width + 18) && Near(resized.Height, start.Height), "Edge resize did not stretch independently.");
                    gesture.Restore();
                    Check(!layer.StretchToBounds && Near(preview.DisplayBounds(layer).X, start.X) && Near(preview.DisplayBounds(layer).Height, start.Height), "Cancel did not restore geometry and aspect mode.");
                    cases++;
                }
            }
            var clamp = new LayerEditGesture(layer, LayerHandle.Right | LayerHandle.Bottom, new(0, 0), start, preview.RenderSize, MotionMath.ForLayer(layer, frame));
            clamp.Apply(new(-100000, -100000), false);
            Check(Near(layer.Width, 1) && Near(layer.Height, 1), "Minimum resize limits failed.");
            clamp.Apply(new(100000, 100000), false);
            Check(Near(layer.Width, 300) && Near(layer.Height, 300), "Maximum resize limits failed.");
            clamp.Restore();
        }
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { Path.Combine(directory, "transparent-test.png"), Path.Combine(directory, "preview-rest.png"),
            Path.Combine(directory, "roundtrip.musicwave.json"), Path.Combine(directory, "missing.png"), directory });
        Check(MainWindow.DroppedImages(data).Length == 2, "File drop did not filter supported existing images.");
        Check(MainWindow.DroppedImages(new DataObject(DataFormats.Text, "text.png")).Length == 0, "Text was treated as a dropped image.");
        var corrupt = Path.Combine(directory, "invalid-image.png"); File.WriteAllText(corrupt, "not an image");
        var importTarget = ProjectSettings.Demo();
        var imported = MainWindow.ImportImages(importTarget, new[] { Path.Combine(directory, "transparent-test.png"), corrupt, Path.Combine(directory, "preview-rest.png") }, 31, 67);
        Check(imported.Count == 2 && imported.Errors.Count == 1 && importTarget.Layers.Count == 6 && ReferenceEquals(importTarget.Layers[^1], imported.LastLayer), "Multi-file import did not skip the corrupt file and retain valid layers.");
        Check(importTarget.Layers[4].X == 31 && importTarget.Layers[4].Y == 67 && importTarget.Layers[5].X == 33 && importTarget.Layers[5].Y == 69, "Imported images did not retain the drop point and staggered placement.");
        var behind = new WallpaperLayer { Path = "demo:sky", IsStatic = true, Width = 40, Height = 60 };
        var front = new WallpaperLayer { Path = Path.Combine(directory, "transparent-test.png"), Width = 40, Height = 60, StretchToBounds = true };
        var overlap = new ProjectSettings { Layers = [project.Layers[0], behind, front] };
        preview.Update(overlap, new(0, 1, 120, 0, 1));
        var hitBounds = preview.DisplayBounds(front);
        Check(ReferenceEquals(preview.LayerAt(new(hitBounds.Left + hitBounds.Width * .25, hitBounds.Top + hitBounds.Height * .25)), front), "Opaque PNG content was not selectable.");
        Check(ReferenceEquals(preview.LayerAt(new(hitBounds.Left + hitBounds.Width * .75, hitBounds.Top + hitBounds.Height * .25)), behind), "Transparent PNG margins blocked the layer behind.");
        front.Visible = false;
        Check(ReferenceEquals(preview.LayerAt(new(hitBounds.Left + hitBounds.Width * .25, hitBounds.Top + hitBounds.Height * .25)), behind), "Hidden layer remained selectable.");
        behind.Visible = false;
        Check(preview.LayerAt(new(480, 400)) is null, "Static background should not intercept canvas selection.");
        // Exercise the actual desktop compositor in an invisible, isolated native host.
        using (var host = new HwndSource(new HwndSourceParameters("MusicWave preview GPU self-test") { Width = 960, Height = 540, WindowStyle = unchecked((int)0x80000000) }))
        using (var composition = new CompositionScene(host.Handle, 960, 540))
        {
            front.Visible = true;
            var gpuProject = new ProjectSettings { Layers = [front] };
            composition.Update(gpuProject, new(0, 1, 120, 0, 1));
            var uploads = composition.UploadCount; var commits = composition.CommitCount;
            front.Width = 57; front.Height = 23; front.X = 65;
            composition.Update(gpuProject, new(.5, 1, 120, 0, 1));
            Check(composition.UploadCount == uploads && composition.CommitCount > commits, "GPU resize re-uploaded image pixels or failed to update the transform.");
            front.StretchToBounds = false;
            composition.Update(gpuProject, new(.5, 1, 120, 0, 1));
            Check(composition.UploadCount == uploads, "Aspect mode switch re-uploaded image pixels.");
        }
        project.Layers[1].StretchToBounds = true; project.Layers[1].Width = 53; project.Layers[1].Height = 28;
        var saved = Path.Combine(directory, "preview-editing.musicwave.json"); ProjectStore.Save(project, saved);
        var loaded = ProjectStore.Load(saved);
        Check(loaded.Layers[1].StretchToBounds && loaded.Layers[1].Width == 53 && loaded.Layers[1].Height == 28, "Stretched layout did not survive project round trip.");
        preview.SelectedLayer = project.Layers[1]; preview.Update(project, new(0, 1, 120, 0, 1)); preview.UpdateLayout();
        var bitmap = new RenderTargetBitmap(960, 540, 96, 96, PixelFormats.Pbgra32); bitmap.Render(preview);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(directory, "preview-editing.png"))) png.Save(stream);
        report.AppendLine($"PASS: {cases} resize cases across all 8 handles, PNG/demo content and 3 animation poses; pointer translation, opposite anchors, Shift ratio, cancel, min/max limits, multi-file import/corrupt-file isolation/drop placement, transparent/hidden/background hit testing, GPU resize texture reuse and stretched project round trip.");
    }
}
