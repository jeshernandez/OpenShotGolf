using System.Collections.Generic;
using System;
using System.IO;
using System.Text;
using Godot;

public sealed record CourseExportResult(
    string OutputFolder,
    string ScenePath,
    string JsonPath,
    string TerrainPath);

public static class CourseExportService
{
    // The exported course inherits this shared, course-agnostic base scene (environment, sky,
    // lighting, camera, player, terrain) instead of cloning the range. See Courses/_shared/.
    private const string SharedBaseScenePath = "res://Courses/_shared/course_base.tscn";

    public static CourseExportResult ExportCourse(GolfCourseProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.EnsureDefaults();

        var outputFolderProjectPath = CourseFileUtilities.NormalizeProjectPath(project.OutputFolder);
        if (string.IsNullOrWhiteSpace(outputFolderProjectPath))
        {
            throw new InvalidOperationException("Set an output folder before exporting.");
        }

        var outputFolderAbsolute = ToAbsolutePath(outputFolderProjectPath);
        Directory.CreateDirectory(outputFolderAbsolute);

        var terrainProjectPath = CourseFileUtilities.NormalizeProjectPath(project.GetTerrainOutputProjectPath());
        var terrainAbsolutePath = ToAbsolutePath(terrainProjectPath);
        Directory.CreateDirectory(terrainAbsolutePath);

        var sceneProjectPath = $"{outputFolderProjectPath.TrimEnd('/')}/course.tscn";
        var sceneAbsolutePath = ToAbsolutePath(sceneProjectPath);
        var scriptProjectPath = $"{outputFolderProjectPath.TrimEnd('/')}/course.gd";
        var scriptAbsolutePath = ToAbsolutePath(scriptProjectPath);
        var jsonProjectPath = $"{outputFolderProjectPath.TrimEnd('/')}/course.json";
        var jsonAbsolutePath = ToAbsolutePath(jsonProjectPath);

        WriteCourseJson(project, jsonAbsolutePath);
        WriteCourseScript(project, scriptAbsolutePath);
        WriteCourseScene(project, sceneAbsolutePath, terrainProjectPath, scriptProjectPath);

        return new CourseExportResult(
            outputFolderProjectPath,
            sceneProjectPath,
            jsonProjectPath,
            terrainProjectPath);
    }

    private static void WriteCourseJson(GolfCourseProject project, string jsonAbsolutePath)
    {
        var root = new Godot.Collections.Dictionary
        {
            ["scene_path"] = "course.tscn",
            ["Title"] = project.CourseTitle.Trim(),
            ["Course Info"] = BuildCourseInfo(project),
            ["Hole Info"] = BuildHoleInfo(project)
        };

        var jsonText = Json.Stringify(root, "\t", true);
        File.WriteAllText(jsonAbsolutePath, jsonText + System.Environment.NewLine);
    }

    private static Godot.Collections.Dictionary BuildCourseInfo(GolfCourseProject project)
    {
        var teeColors = new Godot.Collections.Array<string>();
        foreach (var color in project.GetEffectiveTeeColors())
        {
            teeColors.Add(color);
        }

        return new Godot.Collections.Dictionary
        {
            ["Tee Colors"] = teeColors,
            ["Texture Indices"] = new Godot.Collections.Dictionary
            {
                ["Green"] = new Godot.Collections.Array<int> { 0 },
                ["Fairway"] = new Godot.Collections.Array<int> { 1 },
                ["Rough"] = new Godot.Collections.Array<int> { 2 },
                ["Sand"] = new Godot.Collections.Array<int> { 3 },
                ["Water"] = new Godot.Collections.Array<int> { 4 },
                ["Penalty"] = new Godot.Collections.Array<int> { 5 }
            }
        };
    }

    private static Godot.Collections.Dictionary BuildHoleInfo(GolfCourseProject project)
    {
        var holes = new Godot.Collections.Dictionary();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var teeColors = project.GetEffectiveTeeColors();

        for (var index = 0; index < project.Holes.Count; index++)
        {
            var hole = project.Holes[index];
            var holeName = BuildUniqueHoleName(hole.HoleName, index, usedNames);
            var teeBoxes = new Godot.Collections.Dictionary();
            var enabledTeeBoxes = GetEnabledTeeBoxes(hole, teeColors);

            foreach (var teeBox in enabledTeeBoxes)
            {
                teeBoxes[teeBox.TeeColor] = ToCoordinateArray(teeBox.Position);
            }

            holes[holeName] = new Godot.Collections.Dictionary
            {
                ["Par"] = hole.Par,
                ["Hole Location"] = ToCoordinateArray(hole.HoleLocation),
                ["Tee Boxes"] = teeBoxes
            };
        }

        return holes;
    }

    private static string BuildUniqueHoleName(string? rawName, int index, ISet<string> usedNames)
    {
        var trimmed = rawName?.Trim();
        var fallback = $"Hole {index + 1}";
        var baseName = string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
        var candidate = baseName;
        var suffix = 2;

        while (!usedNames.Add(candidate))
        {
            candidate = $"{baseName} {suffix}";
            suffix++;
        }

        return candidate;
    }

    private static Godot.Collections.Array ToCoordinateArray(Vector2 point)
    {
        return new Godot.Collections.Array
        {
            point.X,
            point.Y
        };
    }

    private static void WriteCourseScript(GolfCourseProject project, string scriptAbsolutePath)
    {
        // Exported courses are a thin subclass of CoursePlay. All hole management, stroke
        // counting, the score overlay, the pin-distance indicator, per-shot camera framing,
        // and tee/flag resolution live in Courses/_shared/course_play.gd. Hole geometry is
        // read from the generated course.json (Hole Info) and the HoleMarkers nodes in
        // course.tscn. Keeping this a one-liner means every exported course inherits the
        // shared gameplay logic instead of cloning (and drifting from) it.
        var builder = new StringBuilder();
        builder.AppendLine("extends \"res://Courses/_shared/course_play.gd\"");
        File.WriteAllText(scriptAbsolutePath, builder.ToString());
    }

    private static void WriteCourseScene(
        GolfCourseProject project,
        string sceneAbsolutePath,
        string terrainProjectPath,
        string scriptProjectPath)
    {
        var markerBody = BuildHoleMarkerSection(project);

        var builder = new StringBuilder();
        // load_steps is a hint; Godot recomputes it on save. Use a safe upper bound.
        var loadSteps = 7;
        builder.AppendLine($"[gd_scene load_steps={loadSteps} format=3]");
        builder.AppendLine();
        var baseSceneUid = ResourceLoader.GetResourceUid(SharedBaseScenePath);
        var uidAttr = baseSceneUid >= 0 ? $"uid=\"{ResourceUid.IdToText(baseSceneUid)}\" " : string.Empty;
        builder.AppendLine(
            $"[ext_resource type=\"PackedScene\" {uidAttr}path=\"{SharedBaseScenePath}\" id=\"1_base\"]");
        builder.AppendLine($"[ext_resource type=\"Script\" path=\"{scriptProjectPath}\" id=\"2_course\"]");
        builder.AppendLine(
            "[ext_resource type=\"Script\" path=\"res://Courses/_shared/hole_markers_snap.gd\" id=\"3_markers\"]");
        builder.AppendLine();
        AppendMarkerSubResources(builder);

        // Inherited scene: root instances the shared base; overrides reference inherited
        // children by name + parent=".".
        builder.AppendLine("[node name=\"Course\" instance=ExtResource(\"1_base\")]");
        builder.AppendLine("script = ExtResource(\"2_course\")");
        builder.AppendLine();
        builder.AppendLine("[node name=\"Terrain3D\" parent=\".\"]");
        builder.AppendLine($"data_directory = \"{terrainProjectPath}\"");

        var cameraOverride = BuildCameraOverride(project);
        if (!string.IsNullOrEmpty(cameraOverride))
        {
            builder.AppendLine();
            builder.Append(cameraOverride);
        }

        builder.Append(markerBody);
        File.WriteAllText(sceneAbsolutePath, builder.ToString());
    }

    private static void AppendMarkerSubResources(StringBuilder builder)
    {
        builder.AppendLine("[sub_resource type=\"CylinderMesh\" id=\"PinPostMesh\"]");
        builder.AppendLine("top_radius = 0.12");
        builder.AppendLine("bottom_radius = 0.12");
        builder.AppendLine("height = 6.0");
        builder.AppendLine();
        builder.AppendLine("[sub_resource type=\"StandardMaterial3D\" id=\"PinPostMat\"]");
        builder.AppendLine("albedo_color = Color(0.85, 0.12, 0.12, 1)");
        builder.AppendLine("emission_enabled = true");
        builder.AppendLine("emission = Color(0.85, 0.12, 0.12, 1)");
        builder.AppendLine("emission_energy_multiplier = 0.35");
        builder.AppendLine();
    }

    private static string BuildHoleMarkerSection(GolfCourseProject project)
    {
        var builder = new StringBuilder();
        var usedHoleNodeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var teeColors = project.GetEffectiveTeeColors();
        builder.AppendLine();
        builder.AppendLine("[node name=\"HoleMarkers\" type=\"Node3D\" parent=\".\"]");
        // Snaps every marker (pin Post and the HoleNumber Label3D) onto the terrain
        // surface once Terrain3D region data has loaded. See hole_markers_snap.gd.
        builder.AppendLine("script = ExtResource(\"3_markers\")");

        for (var index = 0; index < project.Holes.Count; index++)
        {
            var hole = project.Holes[index];
            var holeName = BuildUniqueNodeName(
                SanitizeNodeName(string.IsNullOrWhiteSpace(hole.HoleName) ? $"Hole {index + 1}" : hole.HoleName),
                $"Hole {index + 1}",
                usedHoleNodeNames);
            var holeNodePath = $"HoleMarkers/{holeName}";

            builder.AppendLine();
            builder.AppendLine($"[node name=\"{holeName}\" type=\"Node3D\" parent=\"HoleMarkers\"]");
            builder.AppendLine($"transform = {BuildTransform(hole.HoleLocation, 0.0f)}");

            // Visible pin post (cylinder base seated at y=0) so the hole reads from a distance.
            builder.AppendLine();
            builder.AppendLine($"[node name=\"Post\" type=\"MeshInstance3D\" parent=\"{holeNodePath}\"]");
            builder.AppendLine($"transform = {BuildTransform(Vector2.Zero, 3.0f)}");
            builder.AppendLine("mesh = SubResource(\"PinPostMesh\")");
            builder.AppendLine("surface_material_override/0 = SubResource(\"PinPostMat\")");

            var teeIndex = 0;
            foreach (var teeBox in GetEnabledTeeBoxes(hole, teeColors))
            {
                var teeNodeName = BuildUniqueNodeName(
                    SanitizeNodeName($"{teeBox.TeeColor} Tee"),
                    $"Tee {teeIndex + 1}",
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                var localOffset = teeBox.Position - hole.HoleLocation + new Vector2(teeIndex * 1.5f, 0.0f);

                // Empty tee anchor: positions the HoleNumber label at the tee box and lets
                // the snap script lift it to the tee-box ground height. No visible mesh —
                // tee spheres were removed because they obscured the ball/fairway in play.
                builder.AppendLine();
                builder.AppendLine($"[node name=\"{teeNodeName}\" type=\"Node3D\" parent=\"{holeNodePath}\"]");
                builder.AppendLine($"transform = {BuildTransform(localOffset, 0.0f)}");

                if (teeIndex == 0)
                {
                    builder.AppendLine();
                    builder.AppendLine($"[node name=\"HoleNumber\" type=\"Label3D\" parent=\"{holeNodePath}/{teeNodeName}\"]");
                    builder.AppendLine($"transform = {BuildTransform(Vector2.Zero, 3.25f)}");
                    builder.AppendLine("pixel_size = 0.08");
                    builder.AppendLine("billboard = 1");
                    builder.AppendLine("font_size = 80");
                    builder.AppendLine("outline_size = 18");
                    builder.AppendLine($"text = \"{EscapeSceneString(index + 1).Trim()}\"");
                }

                teeIndex++;
            }
        }

        return builder.ToString();
    }

    private static string BuildCameraOverride(GolfCourseProject project)
    {
        var (teePosition, pinPosition) = GetCourseStart(project);
        var direction = pinPosition - teePosition;
        if (direction.LengthSquared() <= 0.000001f)
        {
            direction = Vector2.Right;
        }

        direction = direction.Normalized();
        var cameraBackDistance = 6.0f;
        var cameraHeight = 3.0f;
        var targetForwardDistance = 20.0f;

        var cameraPoint = teePosition - direction * cameraBackDistance;
        var targetPoint = teePosition + direction * targetForwardDistance;
        var camPos = new Vector3(cameraPoint.X, cameraHeight, cameraPoint.Y);
        var target = new Vector3(targetPoint.X, 1.0f, targetPoint.Y);
        var transform = LookAtTransform(camPos, target);
        var followOffset = new Vector3(-direction.X * 2.5f, 1.5f, -direction.Y * 2.5f);

        var builder = new StringBuilder();
        builder.AppendLine("[node name=\"PhantomCamera3D\" parent=\".\"]");
        builder.AppendLine($"transform = {transform}");
        builder.AppendLine($"follow_offset = {BuildVector3(followOffset)}");
        builder.AppendLine();
        builder.AppendLine("[node name=\"Camera3D\" parent=\".\"]");
        builder.AppendLine($"transform = {transform}");
        builder.AppendLine("far = 8000.0");
        return builder.ToString();
    }

    private static (Vector2 TeePosition, Vector2 PinPosition) GetCourseStart(GolfCourseProject project)
    {
        if (project.Holes.Count == 0)
        {
            return (Vector2.Zero, Vector2.Right);
        }

        var firstHole = project.Holes[0];
        var firstTee = GetFirstEnabledTeeBox(firstHole, project.GetEffectiveTeeColors());
        return firstTee == null
            ? (firstHole.HoleLocation, firstHole.HoleLocation + Vector2.Right)
            : (firstTee.Position, firstHole.HoleLocation);
    }

    private static string LookAtTransform(Vector3 from, Vector3 to)
    {
        // Godot cameras look down their local -Z, so the basis Z column points back (from - to).
        var back = (from - to);
        back = back.LengthSquared() > 0.0f ? back.Normalized() : new Vector3(0, 0, 1);
        var up = new Vector3(0, 1, 0);
        var right = up.Cross(back);
        right = right.LengthSquared() > 0.0f ? right.Normalized() : new Vector3(1, 0, 0);
        var trueUp = back.Cross(right).Normalized();

        return "Transform3D("
            + $"{Inv(right.X)}, {Inv(right.Y)}, {Inv(right.Z)}, "
            + $"{Inv(trueUp.X)}, {Inv(trueUp.Y)}, {Inv(trueUp.Z)}, "
            + $"{Inv(back.X)}, {Inv(back.Y)}, {Inv(back.Z)}, "
            + $"{Inv(from.X)}, {Inv(from.Y)}, {Inv(from.Z)})";
    }

    private static string Inv(float value)
    {
        return value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string BuildUniqueNodeName(string value, string fallback, ISet<string> usedNames)
    {
        var baseName = string.IsNullOrWhiteSpace(value) ? fallback : value;
        var candidate = baseName;
        var suffix = 2;

        while (!usedNames.Add(candidate))
        {
            candidate = $"{baseName} {suffix}";
            suffix++;
        }

        return candidate;
    }

    private static string BuildTransform(Vector2 position, float y)
    {
        return $"Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, {position.X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}, {y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}, {position.Y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)})";
    }

    private static string BuildVector3(Vector3 value)
    {
        return $"Vector3({Inv(value.X)}, {Inv(value.Y)}, {Inv(value.Z)})";
    }

    private static List<GolfTeeBoxDefinition> GetEnabledTeeBoxes(
        GolfHoleDefinition hole,
        IReadOnlyList<string> enabledTeeColors)
    {
        var result = new List<GolfTeeBoxDefinition>();
        foreach (var color in enabledTeeColors)
        {
            foreach (var teeBox in hole.TeeBoxes)
            {
                if (string.Equals(teeBox.TeeColor?.Trim(), color, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(teeBox);
                    break;
                }
            }
        }

        if (result.Count == 0 && hole.TeeBoxes.Count > 0)
        {
            result.Add(hole.TeeBoxes[0]);
        }

        return result;
    }

    private static GolfTeeBoxDefinition? GetFirstEnabledTeeBox(
        GolfHoleDefinition hole,
        IReadOnlyList<string> enabledTeeColors)
    {
        var enabledTeeBoxes = GetEnabledTeeBoxes(hole, enabledTeeColors);
        return enabledTeeBoxes.Count > 0 ? enabledTeeBoxes[0] : null;
    }

    private static string SanitizeNodeName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) || character == ' ' || character == '_' ? character : '_');
        }

        var sanitized = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Node" : sanitized;
    }

    private static string EscapeSceneString(object value)
    {
        return value.ToString()!.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string ToAbsolutePath(string path)
    {
        return ProjectSettings.GlobalizePath(path.Replace('\\', '/'));
    }
}
