using Godot;
using System.Collections.Generic;

[Tool]
[GlobalClass]
public partial class GolfHoleDefinition : Resource
{
    private static readonly IReadOnlyDictionary<string, Vector2> DefaultTeePositions =
        new Dictionary<string, Vector2>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["Black"] = new Vector2(0.0f, 0.0f),
            ["Blue"] = new Vector2(10.0f, 0.0f),
            ["White"] = new Vector2(25.0f, 3.0f),
            ["Red"] = new Vector2(50.0f, -5.5f)
        };

    [Export] public string HoleName { get; set; } = "Hole 1";
    [Export] public int Par { get; set; } = 4;
    [Export] public Vector2 HoleLocation { get; set; } = new Vector2(180.0f, 0.0f);
    [Export] public Godot.Collections.Array<GolfTeeBoxDefinition> TeeBoxes { get; set; } = new();

    public GolfHoleDefinition()
    {
        EnsureDefaultTeeBoxes();
    }

    public void EnsureDefaultTeeBoxes()
    {
        EnsureDefaultTeeBoxes(GolfCourseProject.DefaultTeeColors);
    }

    public void EnsureDefaultTeeBoxes(IReadOnlyList<string> teeColors)
    {
        if (TeeBoxes.Count > 0)
        {
            return;
        }

        var colors = teeColors is { Count: > 0 } ? teeColors : GolfCourseProject.DefaultTeeColors;
        TeeBoxes.Clear();
        for (var index = 0; index < colors.Count; index++)
        {
            var teeColor = colors[index];
            var position = DefaultTeePositions.TryGetValue(teeColor, out var known)
                ? known
                : new Vector2(index * 10.0f, 0.0f);
            TeeBoxes.Add(new GolfTeeBoxDefinition
            {
                TeeColor = teeColor,
                Position = position
            });
        }
    }

    public GolfHoleDefinition DuplicateHole()
    {
        var copy = new GolfHoleDefinition
        {
            HoleName = HoleName,
            Par = Par,
            HoleLocation = HoleLocation
        };

        copy.TeeBoxes.Clear();
        foreach (var teeBox in TeeBoxes)
        {
            copy.TeeBoxes.Add(teeBox.DuplicateBox());
        }

        return copy;
    }
}
