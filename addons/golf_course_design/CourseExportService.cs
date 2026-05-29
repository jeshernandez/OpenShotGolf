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
        var (teePosition, pinPosition) = GetCourseStart(project);
        var direction = pinPosition - teePosition;
        if (direction.LengthSquared() <= 0.000001f)
        {
            direction = Vector2.Right;
        }

        direction = direction.Normalized();
        var builder = new StringBuilder();
        builder.AppendLine("extends \"res://Courses/Range/range.gd\"");
        builder.AppendLine();
        builder.AppendLine($"const COURSE_TEE := Vector2({Inv(teePosition.X)}, {Inv(teePosition.Y)})");
        builder.AppendLine($"const COURSE_DIRECTION := Vector3({Inv(direction.X)}, 0.0, {Inv(direction.Y)})");
        builder.AppendLine("const COURSE_CAMERA_BACK_DISTANCE := 6.0");
        builder.AppendLine("const COURSE_CAMERA_HEIGHT := 3.0");
        builder.AppendLine("const COURSE_CAMERA_LOOKAHEAD := 20.0");
        builder.AppendLine("const COURSE_FOLLOW_DISTANCE := 2.5");
        builder.AppendLine("const COURSE_FOLLOW_HEIGHT := 1.5");
        builder.AppendLine();
        builder.AppendLine("var _course_start_ready := false");
        builder.AppendLine("var _course_start_position := Vector3.ZERO");
        builder.AppendLine("var _course_start_direction := COURSE_DIRECTION");
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("func _ready() -> void:");
        builder.AppendLine("\tsuper._ready()");
        builder.AppendLine("\tcall_deferred(\"_apply_course_start_deferred\")");
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("func _apply_course_start_deferred() -> void:");
        builder.AppendLine("\tawait get_tree().process_frame");
        builder.AppendLine("\t_apply_course_start()");
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("func _apply_course_start() -> void:");
        builder.AppendLine("\tvar tee_height := _get_terrain_height_at(COURSE_TEE, 0.0)");
        builder.AppendLine("\t_course_start_position = Vector3(COURSE_TEE.x, tee_height, COURSE_TEE.y)");
        builder.AppendLine("\t_course_start_direction = COURSE_DIRECTION.normalized()");
        builder.AppendLine("\t_course_start_ready = true");
        builder.AppendLine("\t_position_player_at_course_start(true)");
        builder.AppendLine("\t_apply_ball_aim()");
        builder.AppendLine("\t_sync_camera_to_course_start()");
        builder.AppendLine("\tif GlobalSettings.range_settings.camera_follow_mode.value:");
        builder.AppendLine("\t\tset_camera_follow_mode(true)");
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("func _on_tcp_client_hit_ball(data: Dictionary) -> void:");
        builder.AppendLine("\t_prepare_course_shot_start()");
        builder.AppendLine("\tsuper._on_tcp_client_hit_ball(data)");
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("func _on_range_ui_hit_shot(data: Dictionary) -> void:");
        builder.AppendLine("\t_prepare_course_shot_start()");
        builder.AppendLine("\tsuper._on_range_ui_hit_shot(data)");
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("func _on_player_manual_hit() -> void:");
        builder.AppendLine("\t_prepare_course_shot_start()");
        builder.AppendLine("\tsuper._on_player_manual_hit()");
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("func set_camera_follow_mode(value) -> void:");
        builder.AppendLine("\tsuper.set_camera_follow_mode(value)");
        builder.AppendLine("\tif value and _course_start_ready:");
        builder.AppendLine("\t\t$PhantomCamera3D.follow_offset = _get_camera_follow_offset()");
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("func reset_camera_to_start() -> void:");
        builder.AppendLine("\tif not _course_start_ready:");
        builder.AppendLine("\t\tsuper.reset_camera_to_start()");
        builder.AppendLine("\t\treturn");
        builder.AppendLine();
        builder.AppendLine("\tvar camera = $PhantomCamera3D");
        builder.AppendLine("\tcamera.follow_mode = PhantomCamera3D.FollowMode.NONE");
        builder.AppendLine("\tvar tween := create_tween()");
        builder.AppendLine("\ttween.set_trans(Tween.TRANS_CUBIC)");
        builder.AppendLine("\ttween.set_ease(Tween.EASE_IN_OUT)");
        builder.AppendLine("\ttween.tween_property(camera, \"global_position\", _get_camera_start_position(), 1.5)");
        builder.AppendLine("\tawait tween.finished");
        builder.AppendLine("\t_position_player_at_course_start(false)");
        builder.AppendLine("\tif $Player.ball != null:");
        builder.AppendLine("\t\t$Player.ball.reset()");
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("func _prepare_course_shot_start() -> void:");
        builder.AppendLine("\tif not _course_start_ready:");
        builder.AppendLine("\t\t_apply_course_start()");
        builder.AppendLine("\t_position_player_at_course_start(false)");
        builder.AppendLine("\t_apply_ball_aim()");
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("func _position_player_at_course_start(reset_ball: bool) -> void:");
        builder.AppendLine("\tif not _course_start_ready:");
        builder.AppendLine("\t\treturn");
        builder.AppendLine("\t$Player.global_position = _course_start_position");
        builder.AppendLine("\tif reset_ball and $Player.ball != null:");
        builder.AppendLine("\t\t$Player.ball.reset()");
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("func _apply_ball_aim() -> void:");
        builder.AppendLine("\tif not _course_start_ready or $Player.ball == null:");
        builder.AppendLine("\t\treturn");
        builder.AppendLine("\t$Player.ball.aim_yaw_offset_deg = rad_to_deg(atan2(-_course_start_direction.z, _course_start_direction.x))");
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("func _sync_camera_to_course_start() -> void:");
        builder.AppendLine("\tvar camera_start := _get_camera_start_position()");
        builder.AppendLine("\tvar camera_target := _get_camera_target_position()");
        builder.AppendLine("\t$PhantomCamera3D.global_position = camera_start");
        builder.AppendLine("\t$PhantomCamera3D.look_at(camera_target, Vector3.UP)");
        builder.AppendLine("\t$PhantomCamera3D.follow_offset = _get_camera_follow_offset()");
        builder.AppendLine("\t$Camera3D.global_position = camera_start");
        builder.AppendLine("\t$Camera3D.look_at(camera_target, Vector3.UP)");
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("func _get_camera_start_position() -> Vector3:");
        builder.AppendLine("\tvar camera_point := _course_start_position - _course_start_direction * COURSE_CAMERA_BACK_DISTANCE");
        builder.AppendLine("\tvar camera_xz := Vector2(camera_point.x, camera_point.z)");
        builder.AppendLine("\tvar camera_height := _get_terrain_height_at(camera_xz, _course_start_position.y) + COURSE_CAMERA_HEIGHT");
        builder.AppendLine("\treturn Vector3(camera_point.x, camera_height, camera_point.z)");
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("func _get_camera_target_position() -> Vector3:");
        builder.AppendLine("\tvar target_point := _course_start_position + _course_start_direction * COURSE_CAMERA_LOOKAHEAD");
        builder.AppendLine("\tvar target_xz := Vector2(target_point.x, target_point.z)");
        builder.AppendLine("\tvar target_height := _get_terrain_height_at(target_xz, _course_start_position.y) + 1.0");
        builder.AppendLine("\treturn Vector3(target_point.x, target_height, target_point.z)");
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("func _get_camera_follow_offset() -> Vector3:");
        builder.AppendLine("\treturn -_course_start_direction * COURSE_FOLLOW_DISTANCE + Vector3.UP * COURSE_FOLLOW_HEIGHT");
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("func _get_terrain_height_at(point: Vector2, fallback: float) -> float:");
        builder.AppendLine("\tvar terrain := get_node_or_null(\"Terrain3D\")");
        builder.AppendLine("\tif terrain == null:");
        builder.AppendLine("\t\treturn fallback");
        builder.AppendLine("\tvar data = terrain.get(\"data\")");
        builder.AppendLine("\tif data == null or not data.has_method(\"get_height\"):");
        builder.AppendLine("\t\treturn fallback");
        builder.AppendLine("\tvar height = data.call(\"get_height\", Vector3(point.x, 0.0, point.y))");
        builder.AppendLine("\tif typeof(height) == TYPE_FLOAT or typeof(height) == TYPE_INT:");
        builder.AppendLine("\t\tvar value := float(height)");
        builder.AppendLine("\t\tif is_finite(value):");
        builder.AppendLine("\t\t\treturn value");
        builder.AppendLine("\treturn fallback");

        File.WriteAllText(scriptAbsolutePath, builder.ToString());
    }

    private static void WriteCourseScene(
        GolfCourseProject project,
        string sceneAbsolutePath,
        string terrainProjectPath,
        string scriptProjectPath)
    {
        // Collect the distinct tee colours actually used so we emit one material per colour.
        var teeMaterialIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var teeColors = project.GetEffectiveTeeColors();
        foreach (var hole in project.Holes)
        {
            foreach (var teeBox in GetEnabledTeeBoxes(hole, teeColors))
            {
                var key = teeBox.TeeColor?.Trim() ?? string.Empty;
                if (key.Length > 0 && !teeMaterialIds.ContainsKey(key))
                {
                    teeMaterialIds[key] = $"TeeMat_{SanitizeNodeName(key).Replace(' ', '_')}";
                }
            }
        }

        var markerBody = BuildHoleMarkerSection(project, teeMaterialIds);

        var builder = new StringBuilder();
        // load_steps is a hint; Godot recomputes it on save. Use a safe upper bound.
        var loadSteps = 7 + teeMaterialIds.Count;
        builder.AppendLine($"[gd_scene load_steps={loadSteps} format=3]");
        builder.AppendLine();
        var baseSceneUid = ResourceLoader.GetResourceUid(SharedBaseScenePath);
        var uidAttr = baseSceneUid >= 0 ? $"uid=\"{ResourceUid.IdToText(baseSceneUid)}\" " : string.Empty;
        builder.AppendLine(
            $"[ext_resource type=\"PackedScene\" {uidAttr}path=\"{SharedBaseScenePath}\" id=\"1_base\"]");
        builder.AppendLine($"[ext_resource type=\"Script\" path=\"{scriptProjectPath}\" id=\"2_course\"]");
        builder.AppendLine();
        AppendMarkerSubResources(builder, teeMaterialIds);

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

    private static void AppendMarkerSubResources(StringBuilder builder, Dictionary<string, string> teeMaterialIds)
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
        builder.AppendLine("[sub_resource type=\"SphereMesh\" id=\"TeeMesh\"]");
        builder.AppendLine("radius = 0.5");
        builder.AppendLine("height = 1.0");
        builder.AppendLine();

        // Shared dark-green tee body (#0C6100), matching the overlay tee disc. All tee spheres use this;
        // the real tee colour is shown by the small indicator topper instead.
        builder.AppendLine("[sub_resource type=\"StandardMaterial3D\" id=\"TeeBodyMat\"]");
        builder.AppendLine("albedo_color = Color(0.047059, 0.380392, 0, 1)");
        builder.AppendLine();

        // Small topper that carries the per-tee colour (blue/white/red/gold/etc.).
        builder.AppendLine("[sub_resource type=\"SphereMesh\" id=\"TeeIndicatorMesh\"]");
        builder.AppendLine("radius = 0.28");
        builder.AppendLine("height = 0.56");
        builder.AppendLine();

        foreach (var entry in teeMaterialIds)
        {
            var (r, g, b) = TeeColorToRgb(entry.Key);
            builder.AppendLine($"[sub_resource type=\"StandardMaterial3D\" id=\"{entry.Value}\"]");
            builder.AppendLine($"albedo_color = Color({Inv(r)}, {Inv(g)}, {Inv(b)}, 1)");
            builder.AppendLine("emission_enabled = true");
            builder.AppendLine($"emission = Color({Inv(r)}, {Inv(g)}, {Inv(b)}, 1)");
            builder.AppendLine("emission_energy_multiplier = 0.3");
            builder.AppendLine();
        }
    }

    private static string BuildHoleMarkerSection(GolfCourseProject project, Dictionary<string, string> teeMaterialIds)
    {
        var builder = new StringBuilder();
        var usedHoleNodeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var teeColors = project.GetEffectiveTeeColors();
        builder.AppendLine();
        builder.AppendLine("[node name=\"HoleMarkers\" type=\"Node3D\" parent=\".\"]");

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
                // Tee Position is a world coordinate; convert to an offset under the hole node,
                // nudged sideways per tee so identical-position tees don't fully overlap.
                var localOffset = teeBox.Position - hole.HoleLocation + new Vector2(teeIndex * 1.5f, 0.0f);
                var materialId = teeMaterialIds.TryGetValue(teeBox.TeeColor?.Trim() ?? string.Empty, out var id)
                    ? id
                    : null;

                builder.AppendLine();
                builder.AppendLine($"[node name=\"{teeNodeName}\" type=\"Node3D\" parent=\"{holeNodePath}\"]");
                builder.AppendLine($"transform = {BuildTransform(localOffset, 0.0f)}");

                builder.AppendLine();
                builder.AppendLine($"[node name=\"Marker\" type=\"MeshInstance3D\" parent=\"{holeNodePath}/{teeNodeName}\"]");
                builder.AppendLine($"transform = {BuildTransform(Vector2.Zero, 0.5f)}");
                builder.AppendLine("mesh = SubResource(\"TeeMesh\")");
                builder.AppendLine("surface_material_override/0 = SubResource(\"TeeBodyMat\")");

                // Colour indicator sitting on top of the dark-green body, carrying the real tee colour.
                if (materialId != null)
                {
                    builder.AppendLine();
                    builder.AppendLine($"[node name=\"ColorIndicator\" type=\"MeshInstance3D\" parent=\"{holeNodePath}/{teeNodeName}/Marker\"]");
                    builder.AppendLine($"transform = {BuildTransform(Vector2.Zero, 0.65f)}");
                    builder.AppendLine("mesh = SubResource(\"TeeIndicatorMesh\")");
                    builder.AppendLine($"surface_material_override/0 = SubResource(\"{materialId}\")");
                }

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

    private static (float R, float G, float B) TeeColorToRgb(string teeColor)
    {
        return teeColor.Trim().ToLowerInvariant() switch
        {
            "black" => (0.05f, 0.05f, 0.05f),
            "blue" => (0.15f, 0.35f, 0.85f),
            "white" => (0.95f, 0.95f, 0.95f),
            "red" => (0.85f, 0.15f, 0.15f),
            "gold" or "yellow" => (0.9f, 0.8f, 0.2f),
            "green" => (0.2f, 0.7f, 0.3f),
            _ => (0.6f, 0.6f, 0.6f)
        };
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
