using System;
using System.IO;
using Godot;

[Tool]
public partial class TurfPreviewTerrainRunner : Node
{
    [Export]
    public string SourceProjectPath { get; set; } = "res://Courses/UserCourses/Airways/course_design.tres";

    [Export]
    public string PreviewOutputFolder { get; set; } = "res://Courses/_preview/AirwaysSoftEdges";

    [Export]
    public string PreviewTerrainFolderName { get; set; } = "Terrain";

    [Export]
    public string PreviewScenePath { get; set; } = "res://Courses/_preview/AirwaysSoftEdges/course_soft_edges_preview.tscn";

    public override void _Ready()
    {
        CallDeferred(MethodName.BuildPreview);
    }

    private void BuildPreview()
    {
        try
        {
            var sourceProject = ResourceLoader.Load<GolfCourseProject>(
                SourceProjectPath,
                string.Empty,
                ResourceLoader.CacheMode.Ignore);

            if (sourceProject is null)
            {
                throw new InvalidOperationException($"Could not load project: {SourceProjectPath}");
            }

            var previewProject = (GolfCourseProject)sourceProject.Duplicate(true);
            previewProject.OutputFolder = PreviewOutputFolder;
            previewProject.TerrainFolderName = PreviewTerrainFolderName;
            previewProject.ImportProfile ??= new TerrainImportProfile();
            previewProject.ImportProfile.TextureBlendWidthMeters = Math.Max(
                previewProject.ImportProfile.TextureBlendWidthMeters,
                3.0f);
            previewProject.ImportProfile.BunkerSmoothMeters = Math.Max(
                previewProject.ImportProfile.BunkerSmoothMeters,
                2.0f);

            Directory.CreateDirectory(CourseFileUtilities.ToAbsolutePath(PreviewOutputFolder));
            ResourceSaver.Save(previewProject, $"{PreviewOutputFolder}/course_design_preview.tres");

            var staging = TerrainImportService.RunBackgroundPipeline(previewProject);
            var result = TerrainImportService.FinalizeOnMainThread(staging, this);
            WritePreviewScene(result.TerrainPath);

            GD.Print($"Turf preview built: {PreviewScenePath}");
            GD.Print(result.Message);
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"Turf preview build failed: {ex}");
            GetTree().Quit(1);
        }
    }

    private void WritePreviewScene(string terrainPath)
    {
        var sceneAbsolutePath = CourseFileUtilities.ToAbsolutePath(PreviewScenePath);
        Directory.CreateDirectory(Path.GetDirectoryName(sceneAbsolutePath) ?? ".");

        const string courseScenePath = "res://Courses/UserCourses/Airways/course.tscn";
        const string previewAssetsPath = "res://Courses/_preview/cc0_turf_preview_assets.tres";

        var sceneText = $"""
            [gd_scene load_steps=3 format=3]

            [ext_resource type="PackedScene" path="{courseScenePath}" id="1_airways"]
            [ext_resource type="Terrain3DAssets" path="{previewAssetsPath}" id="2_assets"]

            [node name="CourseSoftEdgesPreview" instance=ExtResource("1_airways")]

            [node name="Terrain3D" parent="." index="3"]
            data_directory = "{terrainPath}"
            assets = ExtResource("2_assets")
            """;

        File.WriteAllText(sceneAbsolutePath, sceneText);
    }
}
