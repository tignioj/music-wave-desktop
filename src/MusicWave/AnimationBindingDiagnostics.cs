using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using MusicWave.Rendering;

namespace MusicWave;

internal static class AnimationBindingDiagnostics
{
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static bool Near(double a, double b) => Math.Abs(a - b) < .003;
    public static void Run(StringBuilder report, string directory)
    {
        var project = ProjectSettings.Demo();
        var a = project.Layers[1]; var b = project.Layers[2]; var c = project.Layers[3];
        var d = new WallpaperLayer { Path = "demo:mint", X = 70, Y = 60 };
        var e = new WallpaperLayer { Path = "demo:orb", X = 80, Y = 90 };
        project.Layers.Add(d); project.Layers.Add(e);
        var group = LayerGroups.Create(project, [a, b]);
        var other = LayerGroups.Create(project, [d, e]);
        var shared = AnimationBindings.Create(project);
        shared.Name = "共享律动"; shared.Bounce = 63; shared.Squash = .58; shared.Sway = 17; shared.Phase = 51; shared.Speed = 1.5;
        foreach (var target in new AnimationTarget[] { group, other, c }) AnimationBindings.Assign(project, shared, target);
        var original = a.Bounce;
        var poses = 0;
        foreach (var (w, h) in new[] { (960.0, 540.0), (3840.0, 2160.0), (540.0, 960.0) })
        foreach (var beat in new[] { 0.0, .25, .5, .85 })
        {
            var frame = new MotionFrame(beat, 1, 120, 0, 1);
            var parent = MotionMath.ForLayer(shared, frame);
            Check(MotionMath.ForLayer(project, c, frame, w, h) == parent, "Direct shared animation binding failed.");
            foreach (var (target, member) in new[] { (group, a), (other, d) })
            {
                var motion = MotionMath.ForLayer(project, member, frame, w, h);
                Check(Near(motion.X, parent.X + (member.X - target.AnchorX) / 100 * 1920 * (parent.ScaleX - 1))
                    && Near(motion.Y, parent.Y + (member.Y - target.AnchorY) / 100 * h / w * 1920 * (parent.ScaleY - 1)), "Shared animation lost a group's own anchor.");
            }
            poses++;
        }
        var direct = AnimationBindings.Create(project, shared); direct.Bounce = 91; direct.Phase = 0;
        AnimationBindings.Assign(project, direct, a);
        var pose = new MotionFrame(.5, 1, 120, 0, 1);
        Check(MotionMath.ForLayer(project, a, pose, 960, 540) == MotionMath.ForLayer(direct, pose), "Layer binding did not override group animation.");
        Check(a.Bounce == original && a.GroupId == group.Id, "Binding changed original parameters or membership.");
        a.AnimationId = "";
        Check(AnimationBindings.MotionGroup(project, a) == group, "Unbinding did not restore group inheritance.");
        shared.Name = "重命名"; AnimationBindings.RefreshNames(project);
        Check(a.AnimationName.Contains("重命名") && c.AnimationName.Contains("重命名"), "Animation rename did not update direct/inherited labels.");
        shared.IsStatic = true;
        Check(MotionMath.ForLayer(project, a, pose, 960, 540) == new LayerMotion(0, 0, 1, 1) && MotionMath.ForLayer(project, c, pose, 960, 540) == new LayerMotion(0, 0, 1, 1), "Paused shared animation moved its targets.");
        shared.IsStatic = false;
        Check(MotionMath.ForLayer(project, a, pose with { Strength = 0 }, 960, 540) == new LayerMotion(0, 0, 1, 1), "Bound group moved during silence.");

        var path = Path.Combine(directory, "animation-bindings.musicwave.json");
        ProjectStore.Save(project, path);
        var loaded = ProjectStore.Load(path);
        Check(loaded.Animations.Count == 2 && loaded.Groups.All(g => g.AnimationId == shared.Id) && loaded.Layers[3].AnimationId == shared.Id
            && loaded.Animations[0].Speed == 1.5 && loaded.Layers[1].AnimationName.Contains("重命名"), "Animation object/bindings round trip failed.");
        var node = JsonSerializer.SerializeToNode(project, ProjectStore.Json)!.AsObject(); node.Remove("Animations");
        File.WriteAllText(path, node.ToJsonString());
        var legacy = ProjectStore.Load(path);
        Check(legacy.Animations.Count == 0 && AnimationBindings.Targets(legacy).All(t => t.AnimationId == ""), "Legacy or missing animation references failed.");
        node["Animations"] = JsonSerializer.SerializeToNode(new[] { shared, shared }, ProjectStore.Json);
        File.WriteAllText(path, node.ToJsonString());
        try { ProjectStore.Load(path); throw new Exception("Duplicate animation IDs were accepted."); }
        catch (InvalidDataException) { }
        node["Animations"] = null; File.WriteAllText(path, node.ToJsonString());
        try { ProjectStore.Load(path); throw new Exception("Null animation collection was accepted."); }
        catch (InvalidDataException) { }

        var preview = new SceneRenderer { Width = 960, Height = 540, IsEditingEnabled = true };
        preview.Measure(new(960, 540)); preview.Arrange(new(0, 0, 960, 540));
        foreach (var member in project.Layers.Skip(1)) member.Path = Path.Combine(directory, "transparent-test.png");
        AnimationBindings.Assign(project, direct, b);
        var gestures = 0;
        foreach (var member in new[] { a, b })
        foreach (var handle in new[] { LayerHandle.Move, LayerHandle.Left, LayerHandle.Bottom | LayerHandle.Right })
        {
            preview.Update(project, pose);
            var start = preview.DisplayBounds(member);
            var parent = AnimationBindings.MotionGroup(project, member) is { } motionGroup ? MotionMath.ForLayer(AnimationBindings.Resolve(project, motionGroup), pose) : new LayerMotion(0, 0, 1, 1);
            var gesture = new LayerEditGesture(member, handle, new(100, 100), start, preview.RenderSize, MotionMath.ForLayer(project, member, pose, 960, 540), parent.ScaleX, parent.ScaleY);
            gesture.Apply(new(118, 111), false);
            var after = preview.DisplayBounds(member); var expected = MotionMath.ForLayer(project, member, pose, 960, 540);
            Check(Near(gesture.Motion.X, expected.X) && Near(gesture.Motion.Y, expected.Y), "Bound layer gesture jumped on release.");
            if (handle == LayerHandle.Move) Check(Near(after.Left - start.Left, 18) && Near(after.Top - start.Top, 11), "Bound layer did not follow pointer.");
            if (handle == LayerHandle.Left) Check(Near(after.Right, start.Right), "Bound resize moved opposite edge.");
            gesture.Restore(); gestures++;
        }
        using (var host = new HwndSource(new HwndSourceParameters("MusicWave animation binding GPU self-test") { Width = 960, Height = 540, WindowStyle = unchecked((int)0x80000000) }))
        using (var composition = new CompositionScene(host.Handle, 960, 540))
        {
            composition.Update(project, pose); var uploads = composition.UploadCount;
            shared.Bounce = 83; shared.Squash = .71;
            composition.Update(project, pose); preview.Update(project, pose);
            Check(composition.UploadCount == uploads, "Editing shared animation re-uploaded textures.");
            foreach (var member in project.Layers.Skip(1))
            {
                var matrix = composition.TransformFor(member)!.Value;
                var tl = Vector2.Transform(Vector2.Zero, matrix); var br = Vector2.Transform(new Vector2(2, 2), matrix);
                var bounds = preview.DisplayBounds(member);
                Check(Near(tl.X, bounds.Left) && Near(tl.Y, bounds.Top) && Near(br.X, bounds.Right) && Near(br.Y, bounds.Bottom), "GPU and preview differed for animation binding.");
            }
        }
        AnimationBindings.Remove(project, shared);
        Check(group.AnimationId == "" && other.AnimationId == "" && c.AnimationId == "" && b.AnimationId == direct.Id && project.Groups.Count == 2, "Deleting an animation damaged unrelated bindings or groups.");
        LayerGroups.Dissolve(project, group);
        Check(b.AnimationId == direct.Id && project.Animations.Contains(direct), "Dissolving a group removed a member's own animation.");
        report.AppendLine($"PASS: shared animation objects, copy/rename/pause/delete, multi-layer/multi-group bindings, direct override/inheritance/fallback, legacy/round-trip/invalid references; {poses} poses and {gestures} editing gestures; GPU/preview parity and texture reuse.");
    }
}
