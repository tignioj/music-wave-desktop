namespace MusicWave;

public static class LayerGroups
{
    public static LayerGroup? Find(ProjectSettings project, WallpaperLayer layer) =>
        layer.GroupId.Length == 0 ? null : project.Groups.FirstOrDefault(g => g.Id == layer.GroupId);
    public static WallpaperLayer[] Members(ProjectSettings project, LayerGroup group) => project.Layers.Where(l => l.GroupId == group.Id).ToArray();
    static WallpaperLayer[] Eligible(ProjectSettings project, IEnumerable<WallpaperLayer> selected) => selected.Distinct()
        .Where(l => project.Layers.Contains(l) && !(project.Layers.IndexOf(l) == 0 && l.IsStatic)).ToArray();

    public static LayerGroup Create(ProjectSettings project, IEnumerable<WallpaperLayer> selected)
    {
        var members = Eligible(project, selected);
        if (members.Length < 2) throw new InvalidOperationException("按住 Ctrl 或 Shift 选择至少两个前景图层，再新建组。");
        var index = 1;
        while (project.Groups.Any(g => g.Name == $"图层组 {index}")) index++;
        var group = new LayerGroup { Name = $"图层组 {index}" };
        group.CopyAnimationFrom((AnimationSettings?)Find(project, members[0]) ?? members[0]);
        group.IsStatic = false;
        project.Groups.Add(group);
        Assign(project, group, members);
        return group;
    }
    public static int Assign(ProjectSettings project, LayerGroup group, IEnumerable<WallpaperLayer> selected)
    {
        if (!project.Groups.Contains(group)) throw new InvalidOperationException("请选择一个现有分组。");
        var members = Eligible(project, selected);
        if (members.Length == 0) return 0;
        foreach (var layer in members) layer.GroupId = group.Id;
        ResetAnchor(project, group);
        PruneEmpty(project); RefreshNames(project);
        return members.Length;
    }
    public static int Detach(ProjectSettings project, IEnumerable<WallpaperLayer> selected)
    {
        var members = selected.Distinct().Where(l => project.Layers.Contains(l) && l.GroupId.Length > 0).ToArray();
        foreach (var layer in members) layer.GroupId = "";
        PruneEmpty(project); RefreshNames(project);
        return members.Length;
    }
    public static void Dissolve(ProjectSettings project, LayerGroup group)
    {
        foreach (var layer in Members(project, group)) layer.GroupId = "";
        project.Groups.Remove(group); RefreshNames(project);
    }
    public static void ResetAnchor(ProjectSettings project, LayerGroup group)
    {
        var members = Members(project, group);
        if (members.Length == 0) return;
        group.AnchorX = (members.Min(l => l.X - l.Width / 2) + members.Max(l => l.X + l.Width / 2)) / 2;
        group.AnchorY = members.Max(l => l.Y);
    }
    public static void PruneEmpty(ProjectSettings project)
    {
        foreach (var group in project.Groups.Where(g => !project.Layers.Any(l => l.GroupId == g.Id)).ToArray()) project.Groups.Remove(group);
    }
    public static void RefreshNames(ProjectSettings project)
    {
        foreach (var layer in project.Layers) layer.GroupName = Find(project, layer)?.Name ?? "独立图层";
    }
}
