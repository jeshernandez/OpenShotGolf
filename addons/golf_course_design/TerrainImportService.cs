using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Godot;

public sealed record TerrainBuildResult(string TerrainPath, string? BackupPath, string Message);

// Returned by RunBackgroundPipeline. Either the build completed entirely in the background
// (TerrainBuildComplete) or it needs a Terrain3D import step on the main thread (TerrainBuildPending).
public abstract record TerrainBuildStaging;
public sealed record TerrainBuildComplete(TerrainBuildResult Result) : TerrainBuildStaging;
public sealed record TerrainBuildPending(
    string StagingTerrainPath,
    string TerrainAbsolutePath,
    string TerrainProjectPath,
    string HeightImagePath,
    string? ColorImagePath,
    float ImportScale,
    float HeightOffset,
    string? ClassImagePath,
    double RasterMinX,
    double RasterMinY,
    double RasterMaxX,
    double RasterMaxY) : TerrainBuildStaging;

public static class TerrainImportService
{
    private const string RangeTerrainPath = "res://Courses/Range/Terrain";
    private const string ImportRunnerPath = "res://addons/golf_course_design/TerrainImportRunner.gd";

    // Convenience wrapper: runs the full pipeline synchronously (background pipeline + main-thread finalize).
    public static TerrainBuildResult BuildTerrain(GolfCourseProject project, Node helperHost)
    {
        ArgumentNullException.ThrowIfNull(helperHost);
        var staging = RunBackgroundPipeline(project);
        return FinalizeOnMainThread(staging, helperHost);
    }

    // Runs all file I/O and external-tool work that is safe on a background thread.
    // For copy-only modes the build is complete and returns TerrainBuildComplete.
    // For Heightmap/PointCloud modes it returns TerrainBuildPending for FinalizeOnMainThread.
    public static TerrainBuildStaging RunBackgroundPipeline(GolfCourseProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.EnsureDefaults();

        var terrainProjectPath = CourseFileUtilities.NormalizeProjectPath(project.GetTerrainOutputProjectPath());
        if (string.IsNullOrWhiteSpace(terrainProjectPath))
            throw new InvalidOperationException("Set a terrain folder before building terrain.");

        var terrainAbsolutePath = CourseFileUtilities.ToAbsolutePath(terrainProjectPath);
        var profile = project.ImportProfile ?? new TerrainImportProfile();
        ValidateModeMatchesSources(profile);

        if (profile.Mode is TerrainImportProfile.TerrainImportMode.Manual && !profile.CopySourceTerrainData)
        {
            Directory.CreateDirectory(terrainAbsolutePath);
            return new TerrainBuildComplete(new TerrainBuildResult(terrainProjectPath, null,
                "Manual terrain mode left the terrain folder untouched."));
        }

        var workspaceAbsolutePath = CreateWorkspace(project);
        var terrainFolderName = Path.GetFileName(
            terrainAbsolutePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var stagingTerrainPath = Path.Combine(workspaceAbsolutePath, $"{terrainFolderName}.build");
        CourseFileUtilities.EnsureCleanDirectory(stagingTerrainPath);

        switch (profile.Mode)
        {
            case TerrainImportProfile.TerrainImportMode.Manual:
                CopyBaseTerrain(profile, stagingTerrainPath);
                return new TerrainBuildComplete(FinalizeCopyMode(stagingTerrainPath, terrainAbsolutePath, terrainProjectPath));

            case TerrainImportProfile.TerrainImportMode.ExternalTerrainData:
                if (!profile.CopySourceTerrainData)
                {
                    Directory.CreateDirectory(terrainAbsolutePath);
                    return new TerrainBuildComplete(new TerrainBuildResult(terrainProjectPath, null,
                        "External terrain mode left the terrain folder untouched."));
                }
                CopyBaseTerrain(profile, stagingTerrainPath);
                return new TerrainBuildComplete(FinalizeCopyMode(stagingTerrainPath, terrainAbsolutePath, terrainProjectPath));

            case TerrainImportProfile.TerrainImportMode.Heightmap:
                ValidateGdalTools(profile);
                var (hPath, cPath, scale, offset, hClassPath, hGrid) = PrepareHeightmapPipeline(project, workspaceAbsolutePath);
                return new TerrainBuildPending(stagingTerrainPath, terrainAbsolutePath, terrainProjectPath,
                    hPath, cPath, scale, offset, hClassPath,
                    hGrid?.MinX ?? 0, hGrid?.MinY ?? 0, hGrid?.MaxX ?? 0, hGrid?.MaxY ?? 0);

            case TerrainImportProfile.TerrainImportMode.PointCloud:
                ValidateGdalTools(profile);
                ExternalToolRunner.EnsureCommandAvailable(profile.PdalCommand, "PDAL");
                var (pcPath, pcColor, pcScale, pcOffset, pcClassPath, pcGrid) = PreparePointCloudPipeline(project, workspaceAbsolutePath);
                return new TerrainBuildPending(stagingTerrainPath, terrainAbsolutePath, terrainProjectPath,
                    pcPath, pcColor, pcScale, pcOffset, pcClassPath,
                    pcGrid?.MinX ?? 0, pcGrid?.MinY ?? 0, pcGrid?.MaxX ?? 0, pcGrid?.MaxY ?? 0);

            default:
                throw new InvalidOperationException($"Unsupported import mode: {profile.Mode}");
        }
    }

    // Completes a TerrainBuildPending by running the Terrain3D importer and swapping the terrain
    // directory. Must be called on the Godot main thread (AddChild/Call require it).
    // For TerrainBuildComplete the result is returned immediately.
    public static TerrainBuildResult FinalizeOnMainThread(TerrainBuildStaging staging, Node helperHost)
    {
        ArgumentNullException.ThrowIfNull(helperHost);

        if (staging is TerrainBuildComplete complete)
            return complete.Result;

        var pending = (TerrainBuildPending)staging;
        var runner = GetOrCreateRunner(helperHost);
        // Deliberately do NOT bake the hole-zone overlay into the Terrain3D color map. The color map
        // multiplies the albedo (ALBEDO = albedo * color_map.rgb), so a saturated overlay would tint
        // the textures (e.g. pink fairways) and also trips the importer's auto "show_colormap" debug
        // view when the asset list is empty. Per-zone texturing comes from the control map painted by
        // paint_control_map() below; overlay_color.png remains a design-time artifact only.
        var colorPath = string.Empty;

        var result = runner.Call(
            "import_to_terrain",
            pending.HeightImagePath,
            colorPath,
            pending.StagingTerrainPath,
            pending.ImportScale,
            pending.HeightOffset);

        if (result.VariantType == Variant.Type.Bool && !result.As<bool>())
            throw new InvalidOperationException("Terrain3D import failed.");

        if (!string.IsNullOrEmpty(pending.ClassImagePath) && File.Exists(pending.ClassImagePath))
        {
            runner.Call("paint_control_map",
                pending.ClassImagePath,
                pending.StagingTerrainPath,
                (float)pending.RasterMinX,
                (float)pending.RasterMinY,
                (float)pending.RasterMaxX,
                (float)pending.RasterMaxY,
                pending.ImportScale);
        }

        var backupPath = ReplaceTerrainDirectory(pending.StagingTerrainPath, pending.TerrainAbsolutePath);
        var backupMessage = string.IsNullOrWhiteSpace(backupPath)
            ? string.Empty
            : $" Previous terrain was moved to {backupPath}.";
        return new TerrainBuildResult(pending.TerrainProjectPath, backupPath,
            $"Terrain built at {pending.TerrainProjectPath}.{backupMessage}");
    }

    private static TerrainBuildResult FinalizeCopyMode(
        string stagingTerrainPath,
        string terrainAbsolutePath,
        string terrainProjectPath)
    {
        var backupPath = ReplaceTerrainDirectory(stagingTerrainPath, terrainAbsolutePath);
        var backupMessage = string.IsNullOrWhiteSpace(backupPath)
            ? string.Empty
            : $" Previous terrain was moved to {backupPath}.";
        return new TerrainBuildResult(terrainProjectPath, backupPath,
            $"Terrain copied to {terrainProjectPath}.{backupMessage}");
    }

    private static (string HeightImagePath, string? ColorImagePath, float ImportScale, float HeightOffset, string? ClassImagePath, RasterGrid? Grid)
        PrepareHeightmapPipeline(GolfCourseProject project, string workspaceAbsolutePath)
    {
        var profile = project.ImportProfile ?? new TerrainImportProfile();
        var sourceHeightmapPath = profile.SourceHeightmapPath.Trim();
        if (string.IsNullOrWhiteSpace(sourceHeightmapPath))
            throw new InvalidOperationException("Set Source heightmap before building terrain.");

        var sourceHeightmapAbsolutePath = CourseFileUtilities.ToAbsolutePath(sourceHeightmapPath);
        if (!File.Exists(sourceHeightmapAbsolutePath))
            throw new FileNotFoundException($"Heightmap not found: {sourceHeightmapPath}");

        var sourceBoundaryPath = profile.SourceBoundaryPath.Trim();
        var hasBoundary = !string.IsNullOrWhiteSpace(sourceBoundaryPath);
        var shouldWarp = hasBoundary
            || !string.IsNullOrWhiteSpace(profile.SourceSpatialReference)
            || !string.IsNullOrWhiteSpace(profile.TargetSpatialReference);
        var preparedSource = sourceHeightmapAbsolutePath;

        if (shouldWarp)
        {
            string? sourceBoundaryAbsolutePath = null;
            if (hasBoundary)
            {
                sourceBoundaryAbsolutePath = CourseFileUtilities.ToAbsolutePath(sourceBoundaryPath);
                if (!File.Exists(sourceBoundaryAbsolutePath))
                    throw new FileNotFoundException($"Boundary not found: {sourceBoundaryPath}");
            }

            preparedSource = Path.Combine(workspaceAbsolutePath, "heightmap_prepared.tif");
            var arguments = new List<string>
            {
                "-overwrite", "-r", "bilinear", "-dstnodata", "-9999"
            };

            AddSpatialReferenceArguments(arguments, profile);
            AddRasterResolutionArguments(arguments, profile);
            if (!string.IsNullOrWhiteSpace(sourceBoundaryAbsolutePath))
                arguments.AddRange(["-cutline", sourceBoundaryAbsolutePath, "-crop_to_cutline"]);

            arguments.AddRange([sourceHeightmapAbsolutePath, preparedSource]);
            ExternalToolRunner.Run(profile.GdalWarpCommand, arguments, workspaceAbsolutePath);
        }

        var heightImagePath = BuildHeightExr(profile, preparedSource, workspaceAbsolutePath);
        var grid = QueryRasterGrid(profile, preparedSource, workspaceAbsolutePath);
        var colorImagePath = PrepareColorImage(profile, workspaceAbsolutePath)
            ?? BuildHoleOverlay(profile, preparedSource, workspaceAbsolutePath, project.Holes.Count);

        var classImagePath = ExportClassIdsPng(profile, workspaceAbsolutePath);
        var importScale = profile.MetersToGodotScale > 0.0f ? profile.MetersToGodotScale : 1.0f;
        var minElevation = QueryRasterMinimum(profile, heightImagePath, workspaceAbsolutePath);
        var heightOffset = profile.TerrainHeightOffset - minElevation * importScale;

        return (heightImagePath, colorImagePath, importScale, heightOffset, classImagePath, grid);
    }

    private static (string HeightImagePath, string? ColorImagePath, float ImportScale, float HeightOffset, string? ClassImagePath, RasterGrid? Grid)
        PreparePointCloudPipeline(GolfCourseProject project, string workspaceAbsolutePath)
    {
        var profile = project.ImportProfile ?? new TerrainImportProfile();
        var sourcePointCloudPath = profile.SourcePointCloudPath.Trim();
        if (string.IsNullOrWhiteSpace(sourcePointCloudPath))
            throw new InvalidOperationException("Set Source point cloud before building terrain.");

        var sourcePointCloudAbsolutePath = CourseFileUtilities.ToAbsolutePath(sourcePointCloudPath);
        if (!File.Exists(sourcePointCloudAbsolutePath))
            throw new FileNotFoundException($"Point cloud not found: {sourcePointCloudPath}");

        var heightTiffPath = Path.Combine(workspaceAbsolutePath, "height.tif");
        var pipelinePath = Path.Combine(workspaceAbsolutePath, "pdal_pipeline.json");
        var pipeline = BuildPdalPipeline(sourcePointCloudAbsolutePath, heightTiffPath, profile);

        File.WriteAllText(pipelinePath, JsonSerializer.Serialize(pipeline, new JsonSerializerOptions
        {
            WriteIndented = true
        }));

        ExternalToolRunner.Run(profile.PdalCommand, ["pipeline", pipelinePath], workspaceAbsolutePath);

        var heightImagePath = BuildHeightExr(profile, heightTiffPath, workspaceAbsolutePath);
        var grid = QueryRasterGrid(profile, heightTiffPath, workspaceAbsolutePath);
        var colorImagePath = PrepareColorImage(profile, workspaceAbsolutePath)
            ?? BuildHoleOverlay(profile, heightTiffPath, workspaceAbsolutePath, project.Holes.Count);

        var classImagePath = ExportClassIdsPng(profile, workspaceAbsolutePath);
        var importScale = profile.MetersToGodotScale > 0.0f ? profile.MetersToGodotScale : 1.0f;
        var minElevation = QueryRasterMinimum(profile, heightImagePath, workspaceAbsolutePath);
        var heightOffset = profile.TerrainHeightOffset - minElevation * importScale;

        return (heightImagePath, colorImagePath, importScale, heightOffset, classImagePath, grid);
    }

    private static string BuildHeightExr(
        TerrainImportProfile profile,
        string sourceRasterAbsolutePath,
        string workspaceAbsolutePath)
    {
        // Fill NoData first so masked cells do not survive as extreme pits in the float output.
        // The source DEM/raster carries a NoData value (e.g. -9999); leaving it unfilled and
        // letting gdal_translate auto-scale would crush the real elevation range into noise and
        // render NoData regions as a flat black blob.
        var filledPath = Path.Combine(workspaceAbsolutePath, "heightmap_filled.tif");
        DeleteIfExists(filledPath);
        var fillDistance = profile.NoDataFillDistancePixels > 0 ? profile.NoDataFillDistancePixels : 1000;
        ExternalToolRunner.Run(
            profile.GdalFillNodataCommand,
            [
                "raster", "fill-nodata",
                "--overwrite",
                "-d", fillDistance.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-s", "1",
                sourceRasterAbsolutePath,
                filledPath
            ],
            workspaceAbsolutePath);

        // Export real-world elevations (meters) as a 32-bit float EXR. Terrain3D reads the EXR's
        // red channel as height in meters, so no lossy normalization or magic scale factor is needed.
        // The elevation band is duplicated into R/G/B with explicit colour interpretation because
        // Godot's EXR loader requires a named "R" channel — a single-band ("Y") EXR fails to load.
        var heightExrPath = Path.Combine(workspaceAbsolutePath, "height.exr");
        DeleteIfExists(heightExrPath);
        ExternalToolRunner.Run(
            profile.GdalTranslateCommand,
            [
                "-ot", "Float32",
                "-of", "EXR",
                "-b", "1",
                "-b", "1",
                "-b", "1",
                "-colorinterp", "red,green,blue",
                filledPath,
                heightExrPath
            ],
            workspaceAbsolutePath);

        return heightExrPath;
    }

    private static float QueryRasterMinimum(
        TerrainImportProfile profile,
        string rasterAbsolutePath,
        string workspaceAbsolutePath)
    {
        var result = ExternalToolRunner.Run(
            profile.GdalInfoCommand,
            ["-json", "-stats", rasterAbsolutePath],
            workspaceAbsolutePath);

        using var document = JsonDocument.Parse(result.StandardOutput);
        if (document.RootElement.TryGetProperty("bands", out var bands)
            && bands.GetArrayLength() > 0
            && bands[0].TryGetProperty("minimum", out var minimum)
            && minimum.TryGetDouble(out var minimumValue))
            return (float)minimumValue;

        return 0.0f;
    }

    private static void DeleteIfExists(string absolutePath)
    {
        if (File.Exists(absolutePath))
            File.Delete(absolutePath);
    }

    private const int TeeClassId = 200;
    private const int GreenClassId = 201;
    private const int SandClassId = 202;
    private const int OutlineClassId = 203;

    private sealed record RasterGrid(
        double MinX,
        double MinY,
        double MaxX,
        double MaxY,
        int Width,
        int Height,
        string WellKnownText);

    // Generates a Terrain3D colour map from the hole centrelines: each hole's playing corridor gets a
    // distinct colour, with tee and green discs marking the start and end, against a muted-rough
    // background. Returns the colour PNG path, or null when overlay generation does not apply.
    private static string? BuildHoleOverlay(
        TerrainImportProfile profile,
        string alignmentRasterAbsolutePath,
        string workspaceAbsolutePath,
        int holeCount)
    {
        if (!profile.GenerateHoleOverlay)
            return null;

        var holesGeoJsonPath = profile.SourceHolesGeoJsonPath.Trim();
        if (string.IsNullOrWhiteSpace(holesGeoJsonPath))
            return null;

        var holesAbsolutePath = CourseFileUtilities.ToAbsolutePath(holesGeoJsonPath);
        if (!File.Exists(holesAbsolutePath))
            throw new FileNotFoundException($"Holes GeoJSON not found: {holesGeoJsonPath}");

        ExternalToolRunner.EnsureCommandAvailable(profile.OgrCommand, "OGR (ogr2ogr)");
        ExternalToolRunner.EnsureCommandAvailable(profile.GdalRasterizeCommand, "GDAL rasterize");
        // Note: gdaldem is not pre-checked because "gdaldem --version" exits non-zero (it expects a
        // subcommand); the color-relief call below surfaces a clear error if it is unavailable.

        var grid = QueryRasterGrid(profile, alignmentRasterAbsolutePath, workspaceAbsolutePath);
        var prjPath = string.Empty;
        if (!string.IsNullOrWhiteSpace(grid.WellKnownText))
        {
            prjPath = Path.Combine(workspaceAbsolutePath, "overlay_grid.prj");
            File.WriteAllText(prjPath, grid.WellKnownText);
        }

        // Reproject + convert the holes to a GPKG with a known layer name so later SQL does not depend
        // on the source layer name. The GPKG geometry column is "geom".
        var basePath = Path.Combine(workspaceAbsolutePath, "overlay_holes.gpkg");
        DeleteIfExists(basePath);
        var convertArguments = new List<string>
        {
            "-f", "GPKG", basePath, holesAbsolutePath, "-nln", "holes"
        };
        if (!string.IsNullOrWhiteSpace(prjPath))
            convertArguments.AddRange(["-t_srs", prjPath]);

        ExternalToolRunner.Run(profile.OgrCommand, convertArguments, workspaceAbsolutePath);

        var bunkerSourcePath = profile.SourceBunkersGeoJsonPath.Trim();
        var bunkersPath = Path.Combine(workspaceAbsolutePath, "overlay_bunkers.gpkg");
        var hasBunkers = !string.IsNullOrWhiteSpace(bunkerSourcePath);
        if (hasBunkers)
        {
            var bunkersAbsolutePath = CourseFileUtilities.ToAbsolutePath(bunkerSourcePath);
            if (!File.Exists(bunkersAbsolutePath))
                throw new FileNotFoundException($"Bunkers GeoJSON not found: {bunkerSourcePath}");

            DeleteIfExists(bunkersPath);
            var bunkerArguments = new List<string>
            {
                "-f", "GPKG", bunkersPath, bunkersAbsolutePath, "-nln", "bunkers"
            };
            if (!string.IsNullOrWhiteSpace(prjPath))
                bunkerArguments.AddRange(["-t_srs", prjPath]);

            ExternalToolRunner.Run(profile.OgrCommand, bunkerArguments, workspaceAbsolutePath);
        }

        // Buffer centrelines into corridors, and the start/end vertices into tee/green discs.
        var featuresPath = Path.Combine(workspaceAbsolutePath, "overlay_features.gpkg");
        DeleteIfExists(featuresPath);
        var corridorRadius = profile.HoleCorridorWidthMeters > 0 ? profile.HoleCorridorWidthMeters : 25.0f;
        var teeRadius = profile.TeeMarkerRadiusMeters > 0 ? profile.TeeMarkerRadiusMeters : 8.0f;
        var greenRadius = profile.GreenMarkerRadiusMeters > 0 ? profile.GreenMarkerRadiusMeters : 10.0f;

        RunOverlaySql(profile, featuresPath, basePath, "corridors", false, workspaceAbsolutePath,
            $"SELECT ST_Buffer(geom,{Inv(corridorRadius)}) AS geom, CAST(ref AS INTEGER) AS cid FROM holes");
        RunOverlaySql(profile, featuresPath, basePath, "tees", true, workspaceAbsolutePath,
            $"SELECT ST_Buffer(ST_StartPoint(geom),{Inv(teeRadius)}) AS geom, {TeeClassId} AS cid FROM holes");
        RunOverlaySql(profile, featuresPath, basePath, "greens", true, workspaceAbsolutePath,
            $"SELECT ST_Buffer(ST_EndPoint(geom),{Inv(greenRadius)}) AS geom, {GreenClassId} AS cid FROM holes");
        if (hasBunkers)
        {
            RunOverlaySql(profile, featuresPath, bunkersPath, "bunkers", true, workspaceAbsolutePath,
                $"SELECT geom AS geom, {SandClassId} AS cid FROM bunkers");
        }

        // Outline ring: every feature buffered outward by the outline width, all sharing the outline
        // class id. Written to a separate GPKG to avoid overwriting featuresPath with its corridors layer.
        var outlineWidth = 1.0f; // metres; could be promoted to TerrainImportProfile later
        var outlinesPath = Path.Combine(workspaceAbsolutePath, "overlay_outlines.gpkg");
        DeleteIfExists(outlinesPath);
        RunOverlaySql(profile, outlinesPath, basePath, "outlines", false, workspaceAbsolutePath,
            $"SELECT ST_Buffer(geom,{Inv(corridorRadius + outlineWidth)}) AS geom, {OutlineClassId} AS cid FROM holes");
        RunOverlaySql(profile, outlinesPath, basePath, "outlines", true, workspaceAbsolutePath,
            $"SELECT ST_Buffer(ST_StartPoint(geom),{Inv(teeRadius + outlineWidth)}) AS geom, {OutlineClassId} AS cid FROM holes");
        RunOverlaySql(profile, outlinesPath, basePath, "outlines", true, workspaceAbsolutePath,
            $"SELECT ST_Buffer(ST_EndPoint(geom),{Inv(greenRadius + outlineWidth)}) AS geom, {OutlineClassId} AS cid FROM holes");
        if (hasBunkers)
        {
            RunOverlaySql(profile, outlinesPath, bunkersPath, "outlines", true, workspaceAbsolutePath,
                $"SELECT ST_Buffer(geom,{Inv(outlineWidth)}) AS geom, {OutlineClassId} AS cid FROM bunkers");
        }

        // Rasterize class ids aligned to the height grid. Corridors are first, then bunkers and markers.
        var classPath = Path.Combine(workspaceAbsolutePath, "overlay_class.tif");
        DeleteIfExists(classPath);
        string[] extentArguments =
        [
            "-te", Inv(grid.MinX), Inv(grid.MinY), Inv(grid.MaxX), Inv(grid.MaxY),
            "-ts", grid.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
            grid.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)
        ];

        // Create the raster (background 0 = rough) and paint the outline ring under everything.
        var initArguments = new List<string> { "-a", "cid", "-init", "0", "-ot", "Byte", "-of", "GTiff" };
        initArguments.AddRange(extentArguments);
        initArguments.AddRange(["-l", "outlines", outlinesPath, classPath]);
        ExternalToolRunner.Run(profile.GdalRasterizeCommand, initArguments, workspaceAbsolutePath);

        // Paint corridors with their per-hole class id (restores the rainbow), then markers on top.
        ExternalToolRunner.Run(
            profile.GdalRasterizeCommand,
            ["-a", "cid", "-l", "corridors", featuresPath, classPath],
            workspaceAbsolutePath);

        var overlayLayers = hasBunkers
            ? new[] { "bunkers", "tees", "greens" }
            : new[] { "tees", "greens" };
        foreach (var layerName in overlayLayers)
        {
            ExternalToolRunner.Run(
                profile.GdalRasterizeCommand,
                ["-a", "cid", "-l", layerName, featuresPath, classPath],
                workspaceAbsolutePath);
        }

        // Map class ids to colours via a generated ramp and render the colour PNG.
        var rampPath = Path.Combine(workspaceAbsolutePath, "overlay_ramp.txt");
        File.WriteAllText(rampPath, BuildColorRamp(holeCount));
        var colorPath = Path.Combine(workspaceAbsolutePath, "overlay_color.png");
        DeleteIfExists(colorPath);
        ExternalToolRunner.Run(
            profile.GdalDemCommand,
            ["color-relief", classPath, rampPath, colorPath, "-of", "PNG", "-nearest_color_entry"],
            workspaceAbsolutePath);

        return colorPath;
    }

    private static void RunOverlaySql(
        TerrainImportProfile profile,
        string outputPath,
        string sourcePath,
        string layerName,
        bool append,
        string workspaceAbsolutePath,
        string sql)
    {
        var arguments = new List<string> { "-f", "GPKG" };
        if (append)
            arguments.AddRange(["-update", "-append"]);

        arguments.AddRange([outputPath, sourcePath, "-nln", layerName, "-dialect", "sqlite", "-sql", sql]);
        ExternalToolRunner.Run(profile.OgrCommand, arguments, workspaceAbsolutePath);
    }

    private static RasterGrid QueryRasterGrid(
        TerrainImportProfile profile,
        string rasterAbsolutePath,
        string workspaceAbsolutePath)
    {
        var result = ExternalToolRunner.Run(
            profile.GdalInfoCommand,
            ["-json", rasterAbsolutePath],
            workspaceAbsolutePath);

        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;

        var size = root.GetProperty("size");
        var width = size[0].GetInt32();
        var height = size[1].GetInt32();

        var corners = root.GetProperty("cornerCoordinates");
        var upperLeft = corners.GetProperty("upperLeft");
        var lowerRight = corners.GetProperty("lowerRight");
        var x0 = upperLeft[0].GetDouble();
        var y0 = upperLeft[1].GetDouble();
        var x1 = lowerRight[0].GetDouble();
        var y1 = lowerRight[1].GetDouble();

        var wkt = string.Empty;
        if (root.TryGetProperty("coordinateSystem", out var coordinateSystem)
            && coordinateSystem.TryGetProperty("wkt", out var wktElement))
            wkt = wktElement.GetString() ?? string.Empty;

        return new RasterGrid(
            Math.Min(x0, x1),
            Math.Min(y0, y1),
            Math.Max(x0, x1),
            Math.Max(y0, y1),
            width,
            height,
            wkt);
    }

    private static string BuildColorRamp(int holeCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine("0 120 150 90"); // rough / outside corridor (light grass green)

        // Spread 64 class IDs evenly across the hue wheel based on the actual hole count so that
        // all holes on any size course (9, 18, 27) occupy the full colour spectrum.
        for (var holeId = 1; holeId <= 64; holeId++)
        {
            var hue = (holeId * 360.0 / Math.Max(holeCount, 1)) % 360.0;
            var (red, green, blue) = HsvToRgb(hue, 0.65, 0.85);
            builder.AppendLine($"{holeId} {red} {green} {blue}");
        }

        builder.AppendLine($"{TeeClassId} 12 97 0"); // tee disc (dark green #0C6100)
        builder.AppendLine($"{GreenClassId} 64 143 51"); // green disc (bright green #408F33)
        builder.AppendLine($"{SandClassId} 216 190 135"); // sand / bunker (light brown)
        builder.AppendLine($"{OutlineClassId} 64 56 13"); // fairway / feature outline (dark olive #40380D)
        return builder.ToString();
    }

    private static (int Red, int Green, int Blue) HsvToRgb(double hueDegrees, double saturation, double value)
    {
        var chroma = value * saturation;
        var hue = hueDegrees / 60.0;
        var x = chroma * (1.0 - Math.Abs((hue % 2.0) - 1.0));
        var match = value - chroma;

        double r = 0, g = 0, b = 0;
        switch ((int)Math.Floor(hue) % 6)
        {
            case 0: r = chroma; g = x; break;
            case 1: r = x; g = chroma; break;
            case 2: g = chroma; b = x; break;
            case 3: g = x; b = chroma; break;
            case 4: r = x; b = chroma; break;
            default: r = chroma; b = x; break;
        }

        return (
            (int)Math.Round((r + match) * 255.0),
            (int)Math.Round((g + match) * 255.0),
            (int)Math.Round((b + match) * 255.0));
    }

    private static string Inv(double value)
    {
        return value.ToString("0.############", System.Globalization.CultureInfo.InvariantCulture);
    }

    // Converts overlay_class.tif (byte-valued class-ID raster) to a greyscale PNG that GDScript
    // can read without GDAL bindings. Returns the PNG path, or null if the class TIF does not exist
    // (i.e. hole overlay generation was skipped or the user provided a pre-made colour overlay).
    private static string? ExportClassIdsPng(TerrainImportProfile profile, string workspaceAbsolutePath)
    {
        var classTifPath = Path.Combine(workspaceAbsolutePath, "overlay_class.tif");
        if (!File.Exists(classTifPath))
            return null;

        var classIdPngPath = Path.Combine(workspaceAbsolutePath, "overlay_class_ids.png");
        DeleteIfExists(classIdPngPath);
        ExternalToolRunner.Run(
            profile.GdalTranslateCommand,
            ["-of", "PNG", "-ot", "Byte", classTifPath, classIdPngPath],
            workspaceAbsolutePath);

        return File.Exists(classIdPngPath) ? classIdPngPath : null;
    }

    private static string? PrepareColorImage(TerrainImportProfile profile, string workspaceAbsolutePath)
    {
        var sourceOverlayPath = profile.SourceOverlayPath.Trim();
        if (string.IsNullOrWhiteSpace(sourceOverlayPath))
            return null;

        var sourceOverlayAbsolutePath = CourseFileUtilities.ToAbsolutePath(sourceOverlayPath);
        if (!File.Exists(sourceOverlayAbsolutePath))
            throw new FileNotFoundException($"Overlay not found: {sourceOverlayPath}");

        var colorImagePath = Path.Combine(workspaceAbsolutePath, "color.png");
        ExternalToolRunner.Run(
            profile.GdalTranslateCommand,
            [sourceOverlayAbsolutePath, colorImagePath],
            workspaceAbsolutePath);

        return colorImagePath;
    }

    private static string CreateWorkspace(GolfCourseProject project)
    {
        var outputFolderProjectPath = CourseFileUtilities.NormalizeProjectPath(project.OutputFolder);
        if (string.IsNullOrWhiteSpace(outputFolderProjectPath))
            throw new InvalidOperationException("Set an output folder before building terrain.");

        var outputFolderAbsolutePath = CourseFileUtilities.ToAbsolutePath(outputFolderProjectPath);
        var workspaceAbsolutePath = Path.Combine(outputFolderAbsolutePath, ".golf_course_design");
        Directory.CreateDirectory(outputFolderAbsolutePath);
        Directory.CreateDirectory(workspaceAbsolutePath);
        return workspaceAbsolutePath;
    }

    private static void CopyBaseTerrain(TerrainImportProfile profile, string terrainAbsolutePath)
    {
        var sourceTerrainPath = profile.SourceTerrainDirectory.Trim();
        if (string.IsNullOrWhiteSpace(sourceTerrainPath))
            sourceTerrainPath = RangeTerrainPath;

        var sourceTerrainAbsolutePath = CourseFileUtilities.ToAbsolutePath(sourceTerrainPath);
        if (!Directory.Exists(sourceTerrainAbsolutePath))
            throw new DirectoryNotFoundException($"Terrain source not found: {sourceTerrainPath}");

        if (CourseFileUtilities.ArePathsSame(sourceTerrainAbsolutePath, terrainAbsolutePath))
            return;

        CourseFileUtilities.CopyDirectory(sourceTerrainAbsolutePath, terrainAbsolutePath);
    }

    private static object[] BuildPdalPipeline(string sourcePointCloudAbsolutePath, string heightTiffPath, TerrainImportProfile profile)
    {
        var pipeline = new List<object>
        {
            sourcePointCloudAbsolutePath
        };

        if (!string.IsNullOrWhiteSpace(profile.TargetSpatialReference))
        {
            var reprojection = new Dictionary<string, object?>
            {
                ["type"] = "filters.reprojection",
                ["out_srs"] = profile.TargetSpatialReference.Trim()
            };

            if (!string.IsNullOrWhiteSpace(profile.SourceSpatialReference))
                reprojection["in_srs"] = profile.SourceSpatialReference.Trim();

            pipeline.Add(reprojection);
        }

        pipeline.Add(new Dictionary<string, object?>
        {
            ["type"] = "writers.gdal",
            ["filename"] = heightTiffPath,
            ["resolution"] = profile.RasterResolutionMeters,
            ["output_type"] = "mean",
            ["data_type"] = "Float32",
            ["allow_empty"] = true,
            ["window_size"] = 3
        });

        return [.. pipeline];
    }

    private static void ValidateGdalTools(TerrainImportProfile profile)
    {
        ExternalToolRunner.EnsureCommandAvailable(profile.GdalTranslateCommand, "GDAL translate");
        ExternalToolRunner.EnsureCommandAvailable(profile.GdalWarpCommand, "GDAL warp");
        ExternalToolRunner.EnsureCommandAvailable(profile.GdalFillNodataCommand, "GDAL fillnodata");
        ExternalToolRunner.EnsureCommandAvailable(profile.GdalInfoCommand, "GDAL info");
    }

    private static void ValidateModeMatchesSources(TerrainImportProfile profile)
    {
        var hasHeightmap = !string.IsNullOrWhiteSpace(profile.SourceHeightmapPath);
        var hasPointCloud = !string.IsNullOrWhiteSpace(profile.SourcePointCloudPath);
        var hasBaseTerrain = !string.IsNullOrWhiteSpace(profile.SourceTerrainDirectory);

        var copiesBaseTerrain = profile.Mode
            is TerrainImportProfile.TerrainImportMode.Manual
            or TerrainImportProfile.TerrainImportMode.ExternalTerrainData;

        if (copiesBaseTerrain && !hasBaseTerrain && (hasHeightmap || hasPointCloud))
        {
            var sourceKind = hasHeightmap ? "heightmap" : "point cloud";
            var suggestedMode = hasHeightmap ? "Heightmap" : "Point cloud";
            throw new InvalidOperationException(
                $"Import mode is '{profile.Mode}' but a {sourceKind} source is set and no base terrain "
                + $"directory is provided. Building now would copy the base range terrain. Select "
                + $"'{suggestedMode}' mode to generate terrain from the {sourceKind}, or clear the "
                + $"{sourceKind} source to copy base terrain.");
        }
    }

    private static void AddSpatialReferenceArguments(List<string> arguments, TerrainImportProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.SourceSpatialReference))
            arguments.AddRange(["-s_srs", profile.SourceSpatialReference.Trim()]);

        if (!string.IsNullOrWhiteSpace(profile.TargetSpatialReference))
            arguments.AddRange(["-t_srs", profile.TargetSpatialReference.Trim()]);
    }

    private static void AddRasterResolutionArguments(List<string> arguments, TerrainImportProfile profile)
    {
        if (profile.RasterResolutionMeters <= 0.0f)
            return;

        var resolution = profile.RasterResolutionMeters.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        arguments.AddRange(["-tr", resolution, resolution]);
    }

    private static string? ReplaceTerrainDirectory(string stagingTerrainPath, string terrainAbsolutePath)
    {
        var parent = Path.GetDirectoryName(terrainAbsolutePath);
        if (string.IsNullOrWhiteSpace(parent))
            throw new InvalidOperationException($"Invalid terrain path: {terrainAbsolutePath}");

        Directory.CreateDirectory(parent);

        string? backupPath = null;
        if (Directory.Exists(terrainAbsolutePath))
        {
            if (CourseFileUtilities.IsDirectoryEmpty(terrainAbsolutePath))
            {
                Directory.Delete(terrainAbsolutePath, true);
            }
            else
            {
                backupPath = BuildBackupPath(terrainAbsolutePath);
                Directory.Move(terrainAbsolutePath, backupPath);
            }
        }

        try
        {
            Directory.Move(stagingTerrainPath, terrainAbsolutePath);
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(backupPath) && !Directory.Exists(terrainAbsolutePath))
                Directory.Move(backupPath, terrainAbsolutePath);
            throw;
        }

        return backupPath;
    }

    private static string BuildBackupPath(string terrainAbsolutePath)
    {
        var backupPath = $"{terrainAbsolutePath}.backup-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var suffix = 2;
        while (Directory.Exists(backupPath))
        {
            backupPath = $"{terrainAbsolutePath}.backup-{DateTime.UtcNow:yyyyMMddHHmmss}-{suffix}";
            suffix++;
        }

        return backupPath;
    }

    private static Node GetOrCreateRunner(Node helperHost)
    {
        var existingRunner = helperHost.GetNodeOrNull<Node>("TerrainImportRunner");
        if (existingRunner != null)
            return existingRunner;

        var runnerScript = GD.Load<GDScript>(ImportRunnerPath);
        if (runnerScript == null)
            throw new InvalidOperationException("Could not load the terrain import helper.");

        var runnerObject = runnerScript.New().AsGodotObject();
        if (runnerObject is not Node runner)
            throw new InvalidOperationException("Could not create terrain import helper.");

        runner.Name = "TerrainImportRunner";
        helperHost.AddChild(runner);
        return runner;
    }
}
