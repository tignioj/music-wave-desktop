namespace MusicWave;

public static class AnimationBindings
{
    public static AnimationObject? Find(ProjectSettings project, AnimationTarget target) =>
        target.AnimationId.Length == 0 ? null : project.Animations.FirstOrDefault(a => a.Id == target.AnimationId);
    public static AnimationSettings Resolve(ProjectSettings project, AnimationTarget target) => (AnimationSettings?)Find(project, target) ?? target;
    public static IEnumerable<AnimationTarget> Targets(ProjectSettings project) => project.Layers.Cast<AnimationTarget>().Concat(project.Groups);
    // An explicit layer binding takes precedence over its group's animation.
    public static LayerGroup? MotionGroup(ProjectSettings project, WallpaperLayer layer) => Find(project, layer) is null ? LayerGroups.Find(project, layer) : null;

    public static AnimationObject Create(ProjectSettings project, AnimationSettings? source = null)
    {
        var index = 1;
        while (project.Animations.Any(a => a.Name == $"动画 {index}")) index++;
        var animation = new AnimationObject { Name = $"动画 {index}" };
        if (source is not null) animation.CopyAnimationFrom(source);
        project.Animations.Add(animation);
        return animation;
    }
    public static void Assign(ProjectSettings project, AnimationObject animation, AnimationTarget target)
    {
        if (!project.Animations.Contains(animation) || !Targets(project).Contains(target))
            throw new InvalidOperationException("请选择项目中的动画和绑定目标。");
        target.AnimationId = animation.Id;
    }
    public static void Remove(ProjectSettings project, AnimationObject animation)
    {
        foreach (var target in Targets(project).Where(t => t.AnimationId == animation.Id)) target.AnimationId = "";
        project.Animations.Remove(animation);
        RefreshNames(project);
    }
    public static void RepairReferences(ProjectSettings project)
    {
        foreach (var target in Targets(project))
            if (target.AnimationId.Length > 0 && Find(project, target) is null) target.AnimationId = "";
        RefreshNames(project);
    }
    public static void RefreshNames(ProjectSettings project)
    {
        foreach (var target in Targets(project))
        {
            var animation = Find(project, target);
            target.AnimationName = animation is not null ? $"动画 · {animation.Name}"
                : target is WallpaperLayer layer && LayerGroups.Find(project, layer) is { } group
                    ? Find(project, group) is { } inherited ? $"继承动画 · {inherited.Name}" : "使用组参数"
                    : "独立参数";
        }
    }
}
