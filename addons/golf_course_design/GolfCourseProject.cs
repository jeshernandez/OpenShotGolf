using Godot;

[Tool]
[GlobalClass]
public partial class GolfCourseProject : Resource
{
    [Export]
    public string CourseTitle { get; set; } = "New Course";

    [Export]
    public string OutputFolder { get; set; } = "res://Courses/UserCourses/NewCourse";

    [Export]
    public string TerrainFolderName { get; set; } = "Terrain";

    [Export]
    public TerrainImportProfile ImportProfile { get; set; } = new();

    // Tee colours generated for every hole on this course. Trim this list to disable tees a
    // course does not use (e.g. only "White","Red"). Consumed by hole import, marker export,
    // and course.json so all three stay in sync.
    [Export]
    public Godot.Collections.Array<string> TeeColors { get; set; } = new()
    {
        "Black",
        "Blue",
        "White",
        "Red"
    };

    [Export]
    public Godot.Collections.Array<GolfHoleDefinition> Holes { get; set; } = new();

    public static readonly System.Collections.Generic.IReadOnlyList<string> DefaultTeeColors =
        new[] { "Black", "Blue", "White", "Red" };

    // Normalised, de-duplicated tee colours, falling back to the standard four when unset.
    public System.Collections.Generic.IReadOnlyList<string> GetEffectiveTeeColors()
    {
        var result = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        if (TeeColors != null)
        {
            foreach (var color in TeeColors)
            {
                var trimmed = color?.Trim();
                if (!string.IsNullOrEmpty(trimmed) && seen.Add(trimmed))
                {
                    result.Add(trimmed);
                }
            }
        }

        return result.Count > 0 ? result : DefaultTeeColors;
    }

    public static GolfCourseProject CreateDefault()
    {
        var project = new GolfCourseProject();
        project.EnsureDefaults();
        return project;
    }

    public void EnsureDefaults()
    {
        ImportProfile ??= new TerrainImportProfile();

        if (Holes.Count == 0)
        {
            Holes.Add(new GolfHoleDefinition
            {
                HoleName = "Hole 1",
                Par = 4,
                HoleLocation = new Vector2(180.0f, 0.0f)
            });
        }

        var teeColors = GetEffectiveTeeColors();
        foreach (var hole in Holes)
        {
            hole.EnsureDefaultTeeBoxes(teeColors);
        }
    }

    public string GetTerrainOutputProjectPath()
    {
        var trimmedOutput = OutputFolder.TrimEnd('/');
        return $"{trimmedOutput}/{TerrainFolderName}";
    }
}
