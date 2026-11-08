using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicWave.Rendering;

namespace MusicWave;

internal static class GroupAnimationDiagnostics
{
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static bool Near(double a, double b) => Math.Abs(a - b) < .003;
    static byte[] Pixels(SceneRenderer preview)
    {
        preview.UpdateLayout();
        var bitmap = new RenderTargetBitmap(960, 540, 96, 96, PixelFormats.Pbgra32); bitmap.Render(preview);
        var pixels = new byte[960 * 540 * 4]; bitmap.CopyPixels(pixels, 960 * 4, 0); return pixels;
    }
    public static void Run(StringBuilder report, string directory)
    {
        var project = ProjectSettings.Demo();
        var original = project.Layers.ToArray();
        var a = original[1]; var b = original[2]; var c = original[3];
        var originalMotion = MotionMath.ForLayer(a, new(.5, 1, 120, 0, 1));
        try { LayerGroups.Create(project, [original[0], a]); throw new Exception("Background was grouped."); }
        catch (InvalidOperationException) { Check(project.Groups.Count == 0, "Invalid group creation changed the project."); }
        var group = LayerGroups.Create(project, [a, b, a]);
        Check(LayerGroups.Members(project, group).Length == 2 && project.Groups.Count == 1, "Group membership or duplicate selection failed.");
        Check(project.Layers.SequenceEqual(original), "Grouping changed the stacking order.");
        Check(Near(group.AnchorY, Math.Max(a.Y, b.Y)) && Near(group.Bounce, a.Bounce), "Initial group anchor or animation defaults failed.");
        group.Name = "角色组合"; LayerGroups.RefreshNames(project);
        Check(a.GroupName == group.Name && b.GroupName == group.Name, "Group rename did not update member labels.");
        group.Phase = 37; group.Speed = 1.75; group.Bounce = 49; group.Squash = .62; group.Sway = 31;
        var poseCases = 0;
        foreach (var (width, height) in new[] { (960.0, 540.0), (3840.0, 2160.0), (540.0, 960.0) })
        foreach (var beat in new[] { 0.0, .25, .5, .85 })
        {
            var frame = new MotionFrame(beat, 1, 120, 0, 1);
            var parent = MotionMath.ForLayer(group, frame);
            var ma = MotionMath.ForLayer(project, a, frame, width, height);
            var mb = MotionMath.ForLayer(project, b, frame, width, height);
            var ax = width * a.X / 100 + ma.X * width / 1920;
            var bx = width * b.X / 100 + mb.X * width / 1920;
            var ay = height * a.Y / 100 + ma.Y * width / 1920;
            var by = height * b.Y / 100 + mb.Y * width / 1920;
            Check(Near(ax - bx, width * (a.X - b.X) / 100 * parent.ScaleX) && Near(ay - by, height * (a.Y - b.Y) / 100 * parent.ScaleY), "Group did not scale relative member positions about one anchor.");
            Check(ma.ScaleX == mb.ScaleX && ma.ScaleY == mb.ScaleY, "Member local settings overrode group scale.");
            Check(MotionMath.ForLayer(project, c, frame, width, height) == MotionMath.ForLayer(c, frame), "Group changed an independent layer.");
            poseCases++;
        }
        group.IsStatic = true;
        Check(MotionMath.ForLayer(project, a, new(.5, 1, 120, 0, 1), 960, 540) == new LayerMotion(0, 0, 1, 1), "Static group still animated a member.");
        group.IsStatic = false;
        Check(MotionMath.ForLayer(project, a, new(.5, 0, 120, 0, 1), 960, 540) == new LayerMotion(0, 0, 1, 1), "Silent group did not return to its base layout.");

        var preview = new SceneRenderer { Width = 960, Height = 540, IsEditingEnabled = true };
        preview.Measure(new(960, 540)); preview.Arrange(new(0, 0, 960, 540));
        var imagePath = Path.Combine(directory, "transparent-test.png"); a.Path = b.Path = imagePath;
        var handles = new[] { LayerHandle.Move, LayerHandle.Left, LayerHandle.Right, LayerHandle.Top, LayerHandle.Bottom,
            LayerHandle.Top | LayerHandle.Left, LayerHandle.Top | LayerHandle.Right, LayerHandle.Bottom | LayerHandle.Left, LayerHandle.Bottom | LayerHandle.Right };
        var gestures = 0;
        foreach (var beat in new[] { .25, .5 })
        foreach (var handle in handles)
        foreach (var keepAspect in new[] { false, true })
        {
            var frame = new MotionFrame(beat, 1, 120, 0, 1);
            preview.Update(project, frame);
            var start = preview.DisplayBounds(a);
            var motion = MotionMath.ForLayer(project, a, frame, 960, 540);
            var parent = MotionMath.ForLayer(group, frame);
            var gesture = new LayerEditGesture(a, handle, new(100, 100), start, preview.RenderSize, motion, parent.ScaleX, parent.ScaleY);
            gesture.Apply(new(118, 111), keepAspect);
            var after = preview.DisplayBounds(a);
            var expectedMotion = MotionMath.ForLayer(project, a, frame, 960, 540);
            Check(Near(gesture.Motion.X, expectedMotion.X) && Near(gesture.Motion.Y, expectedMotion.Y), "Grouped gesture jumped when released.");
            if (handle == LayerHandle.Move) Check(Near(after.Left - start.Left, 18) && Near(after.Top - start.Top, 11), "Grouped layer did not follow pointer movement.");
            else
            {
                if (handle.HasFlag(LayerHandle.Left)) Check(Near(after.Right, start.Right), "Grouped left resize moved the opposite edge.");
                if (handle.HasFlag(LayerHandle.Right)) Check(Near(after.Left, start.Left), "Grouped right resize moved the opposite edge.");
                if (handle.HasFlag(LayerHandle.Top)) Check(Near(after.Bottom, start.Bottom), "Grouped top resize moved the opposite edge.");
                if (handle.HasFlag(LayerHandle.Bottom)) Check(Near(after.Top, start.Top), "Grouped bottom resize moved the opposite edge.");
                if (keepAspect) Check(Near(after.Width / after.Height, start.Width / start.Height), "Grouped Shift resize changed aspect ratio.");
            }
            gesture.Restore();
            Check(Near(preview.DisplayBounds(a).Left, start.Left) && Near(preview.DisplayBounds(a).Bottom, start.Bottom), "Grouped gesture cancellation failed.");
            gestures++;
        }
        group.Visible = false; b.Visible = false;
        var path = Path.Combine(directory, "groups-roundtrip.musicwave.json"); ProjectStore.Save(project, path);
        var loaded = ProjectStore.Load(path);
        Check(loaded.Groups.Count == 1 && loaded.Groups[0].Id == group.Id && loaded.Layers[1].GroupId == group.Id && loaded.Groups[0].Phase == 37 && loaded.Groups[0].Speed == 1.75 && loaded.Layers[1].GroupName == group.Name, "Group project round trip failed.");
        Check(!loaded.Groups[0].Visible && loaded.Layers[1].Visible && !loaded.Layers[2].Visible && !LayerGroups.IsVisible(loaded, loaded.Layers[1]), "Group visibility or individual visibility did not survive save/load.");
        var oldGroup = JsonSerializer.SerializeToNode(project, ProjectStore.Json)!;
        oldGroup["Groups"]![0]!.AsObject().Remove("Visible");
        File.WriteAllText(path, oldGroup.ToJsonString(ProjectStore.Json));
        Check(ProjectStore.Load(path).Groups[0].Visible, "Existing project without group visibility should default to visible.");
        group.Visible = true; b.Visible = true;
        var legacy = JsonSerializer.SerializeToNode(ProjectSettings.Demo(), ProjectStore.Json)!.AsObject(); legacy.Remove("Groups");
        File.WriteAllText(path, legacy.ToJsonString(ProjectStore.Json));
        Check(ProjectStore.Load(path).Groups.Count == 0, "Legacy project without groups failed to load.");
        loaded.Layers[1].GroupId = "missing-group"; ProjectStore.Save(loaded, path);
        Check(ProjectStore.Load(path).Layers[1].GroupId == "", "Missing group reference did not fall back to independent animation.");
        LayerGroups.Assign(project, group, [c]);
        Check(LayerGroups.Members(project, group).Length == 3, "Joining an existing group failed.");
        LayerGroups.Detach(project, [a]);
        Check(a.GroupId == "" && MotionMath.ForLayer(a, new(.5, 1, 120, 0, 1)) == originalMotion, "Detaching lost original member animation settings.");
        LayerGroups.Dissolve(project, group);
        Check(project.Groups.Count == 0 && project.Layers.All(l => l.GroupId == "") && project.Layers.SequenceEqual(original), "Dissolving removed layers, reordered them or left stale membership.");
        group = LayerGroups.Create(project, [a, b]);
        var other = LayerGroups.Create(project, [a, b, c]);
        Check(project.Groups.Count == 1 && project.Groups[0] == other && LayerGroups.Members(project, other).Length == 3, "Regrouping left an orphaned group.");
        group = other;
        using (var host = new HwndSource(new HwndSourceParameters("MusicWave group GPU self-test") { Width = 960, Height = 540, WindowStyle = unchecked((int)0x80000000) }))
        using (var composition = new CompositionScene(host.Handle, 960, 540))
        {
            var frame = new MotionFrame(.31, 1, 120, 0, 1);
            composition.Update(project, frame); var uploads = composition.UploadCount;
            group.Squash = .68; group.Bounce = 73; group.Phase = 81; group.Speed = 2;
            composition.Update(project, frame);
            Check(composition.UploadCount == uploads, "Group animation re-uploaded unchanged textures.");
            preview.Update(project, frame);
            foreach (var member in new[] { a, b })
            {
                var matrix = composition.TransformFor(member)!.Value;
                var topLeft = Vector2.Transform(Vector2.Zero, matrix);
                var bottomRight = Vector2.Transform(new Vector2(2, 2), matrix);
                var expected = preview.DisplayBounds(member);
                Check(Near(topLeft.X, expected.Left) && Near(topLeft.Y, expected.Top) && Near(bottomRight.X, expected.Right) && Near(bottomRight.Y, expected.Bottom), "GPU group transform differed from preview bounds.");
            }
            preview.SelectedGroup = group; preview.SelectedLayer = a; preview.UpdateLayout();
            var bitmap = new RenderTargetBitmap(960, 540, 96, 96, PixelFormats.Pbgra32); bitmap.Render(preview);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, "group-preview.png")); png.Save(stream);

            // A group hides every member, including independently bound members, without changing their own switches.
            var animation = new AnimationObject { IsStatic = true }; project.Animations.Add(animation); a.AnimationId = animation.Id;
            b.Visible = false; preview.SelectedGroup = null; preview.SelectedLayer = null;
            composition.Update(project, frame); preview.Update(project, frame);
            var shown = Pixels(preview);
            var bounds = preview.DisplayBounds(a);
            var point = new Point(bounds.Left + bounds.Width * .25, bounds.Top + bounds.Height * .25);
            Check(preview.LayerAt(point) == a, "Visible independently bound member was not selectable.");
            group.Visible = false; preview.SelectedGroup = group; preview.SelectedLayer = a;
            composition.Update(project, frame); preview.Update(project, frame);
            Check(new[] { a, b, c }.All(l => composition.TransformFor(l) is null) && composition.TransformFor(original[0]) is not null, "GPU did not hide all group members or hid the independent background.");
            Check(preview.LayerAt(point) is null && a.Visible && !b.Visible, "Hidden group remained selectable or changed member visibility.");
            var hidden = Pixels(preview);
            var backgroundOnly = new ProjectSettings { Layers = [original[0]] };
            preview.Update(backgroundOnly, frame);
            Check(hidden.SequenceEqual(Pixels(preview)) && !hidden.SequenceEqual(shown), "Hidden group content or selection overlays remained in the preview.");
            group.Visible = true; preview.SelectedGroup = null; preview.SelectedLayer = null;
            composition.Update(project, frame); preview.Update(project, frame);
            Check(composition.TransformFor(a) is not null && composition.TransformFor(b) is null && composition.TransformFor(c) is not null && shown.SequenceEqual(Pixels(preview)), "Showing group failed to restore the original per-layer visibility or rendered content.");

            group.Visible = false;
            var added = new WallpaperLayer { Path = "demo:mint" }; project.Layers.Add(added);
            LayerGroups.Assign(project, group, [added]); composition.Update(project, frame);
            Check(added.Visible && composition.TransformFor(added) is null, "New member of hidden group remained visible.");
            LayerGroups.Detach(project, [added]); composition.Update(project, frame);
            Check(composition.TransformFor(added) is not null, "Detached layer inherited stale group visibility.");
            LayerGroups.Dissolve(project, group); composition.Update(project, frame);
            Check(composition.TransformFor(a) is not null && composition.TransformFor(b) is null, "Dissolving hidden group lost individual visibility settings.");
        }
        report.AppendLine("PASS: group hide/show preserves member switches, hides independently bound members, removes preview content/selection/hit targets and GPU visuals, restores exact pixels, handles join/detach/dissolve, persists visibility and defaults legacy groups to visible.");
        report.AppendLine($"PASS: grouping, membership, rename, detach/dissolve/regroup, legacy/round-trip/dangling references; {poseCases} shared-anchor poses across landscape/4K/portrait; {gestures} grouped drag/resize/cancel cases; GPU/preview transform parity and texture reuse.");
    }
}
