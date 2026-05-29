using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Godot;

public static class GeoJsonCourseLayoutImporter
{
    private const double MetersPerDegreeLatitude = 111_320.0;

    public static int ImportHoles(GolfCourseProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.EnsureDefaults();

        var profile = project.ImportProfile ?? new TerrainImportProfile();
        var sourcePath = profile.SourceHolesGeoJsonPath.Trim();
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new InvalidOperationException("Set Source holes GeoJSON before importing holes.");
        }

        var sourceAbsolutePath = CourseFileUtilities.ToAbsolutePath(sourcePath);
        if (!File.Exists(sourceAbsolutePath))
        {
            throw new FileNotFoundException($"Holes GeoJSON not found: {sourcePath}");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(sourceAbsolutePath));
        var root = document.RootElement;
        if (!TryFindFirstCoordinate(root, out var originLongitude, out var originLatitude))
        {
            throw new InvalidOperationException("The holes GeoJSON does not contain any LineString coordinates.");
        }

        if (Math.Abs(profile.OriginLatitude) < double.Epsilon
            && Math.Abs(profile.OriginLongitude) < double.Epsilon)
        {
            profile.OriginLatitude = originLatitude;
            profile.OriginLongitude = originLongitude;
        }

        var coordinateConverter = WorldPositionConverter.Create(
            profile,
            Path.GetDirectoryName(sourceAbsolutePath) ?? System.Environment.CurrentDirectory);

        var importedHoles = new List<ImportedHole>();
        foreach (var feature in EnumerateFeatures(root))
        {
            if (!feature.TryGetProperty("geometry", out var geometry)
                || !geometry.TryGetProperty("type", out var geometryTypeProperty))
            {
                continue;
            }

            var geometryType = geometryTypeProperty.GetString();
            if (string.Equals(geometryType, "LineString", StringComparison.OrdinalIgnoreCase))
            {
                AddLineStringFeature(feature, geometry, importedHoles, coordinateConverter);
            }
            else if (string.Equals(geometryType, "MultiLineString", StringComparison.OrdinalIgnoreCase))
            {
                AddMultiLineStringFeature(feature, geometry, importedHoles, coordinateConverter);
            }
        }

        importedHoles.Sort((left, right) =>
        {
            if (left.HoleNumber.HasValue && right.HoleNumber.HasValue)
            {
                return left.HoleNumber.Value.CompareTo(right.HoleNumber.Value);
            }

            if (left.HoleNumber.HasValue)
            {
                return -1;
            }

            if (right.HoleNumber.HasValue)
            {
                return 1;
            }

            return left.Sequence.CompareTo(right.Sequence);
        });

        if (importedHoles.Count == 0)
        {
            throw new InvalidOperationException("No hole lines were found in the GeoJSON. Add LineString features for each hole.");
        }

        var teeColors = project.GetEffectiveTeeColors();
        project.Holes.Clear();
        for (var index = 0; index < importedHoles.Count; index++)
        {
            var importedHole = importedHoles[index];
            var holeName = string.IsNullOrWhiteSpace(importedHole.Name)
                ? $"Hole {importedHole.HoleNumber ?? index + 1}"
                : importedHole.Name;

            var hole = new GolfHoleDefinition
            {
                HoleName = holeName,
                Par = importedHole.Par,
                HoleLocation = importedHole.PinPosition
            };

            hole.TeeBoxes.Clear();
            foreach (var teeColor in teeColors)
            {
                hole.TeeBoxes.Add(new GolfTeeBoxDefinition
                {
                    TeeColor = teeColor,
                    Position = importedHole.TeePosition
                });
            }

            project.Holes.Add(hole);
        }

        return importedHoles.Count;
    }

    private static IEnumerable<JsonElement> EnumerateFeatures(JsonElement root)
    {
        if (root.TryGetProperty("type", out var typeProperty)
            && string.Equals(typeProperty.GetString(), "Feature", StringComparison.OrdinalIgnoreCase))
        {
            yield return root;
            yield break;
        }

        if (!root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var feature in features.EnumerateArray())
        {
            yield return feature;
        }
    }

    private static void AddLineStringFeature(
        JsonElement feature,
        JsonElement geometry,
        List<ImportedHole> importedHoles,
        WorldPositionConverter coordinateConverter)
    {
        if (!geometry.TryGetProperty("coordinates", out var coordinates)
            || !TryGetLineEndpoints(coordinates, out var startLongitude, out var startLatitude, out var endLongitude, out var endLatitude))
        {
            return;
        }

        importedHoles.Add(BuildImportedHole(feature, importedHoles.Count, startLongitude, startLatitude, endLongitude, endLatitude, coordinateConverter));
    }

    private static void AddMultiLineStringFeature(
        JsonElement feature,
        JsonElement geometry,
        List<ImportedHole> importedHoles,
        WorldPositionConverter coordinateConverter)
    {
        if (!geometry.TryGetProperty("coordinates", out var coordinates) || coordinates.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        JsonElement? longestLine = null;
        var longestCount = 0;
        foreach (var line in coordinates.EnumerateArray())
        {
            var count = line.GetArrayLength();
            if (count > longestCount)
            {
                longestLine = line;
                longestCount = count;
            }
        }

        if (longestLine is not { } selectedLine
            || !TryGetLineEndpoints(selectedLine, out var startLongitude, out var startLatitude, out var endLongitude, out var endLatitude))
        {
            return;
        }

        importedHoles.Add(BuildImportedHole(feature, importedHoles.Count, startLongitude, startLatitude, endLongitude, endLatitude, coordinateConverter));
    }

    private static ImportedHole BuildImportedHole(
        JsonElement feature,
        int sequence,
        double startLongitude,
        double startLatitude,
        double endLongitude,
        double endLatitude,
        WorldPositionConverter coordinateConverter)
    {
        var properties = feature.TryGetProperty("properties", out var foundProperties)
            ? foundProperties
            : default;

        var holeNumber = TryReadIntProperty(properties, "ref")
            ?? TryReadIntProperty(properties, "hole")
            ?? TryReadIntProperty(properties, "golf:hole");
        var name = TryReadStringProperty(properties, "name")
            ?? (holeNumber.HasValue ? $"Hole {holeNumber.Value}" : string.Empty);
        var par = TryReadIntProperty(properties, "par")
            ?? TryReadIntProperty(properties, "golf:par")
            ?? 4;

        return new ImportedHole(
            sequence,
            holeNumber,
            name,
            Math.Clamp(par, 1, 9),
            coordinateConverter.Convert(startLongitude, startLatitude),
            coordinateConverter.Convert(endLongitude, endLatitude));
    }

    private static bool TryGetLineEndpoints(
        JsonElement coordinates,
        out double startLongitude,
        out double startLatitude,
        out double endLongitude,
        out double endLatitude)
    {
        startLongitude = 0.0;
        startLatitude = 0.0;
        endLongitude = 0.0;
        endLatitude = 0.0;

        if (coordinates.ValueKind != JsonValueKind.Array || coordinates.GetArrayLength() < 2)
        {
            return false;
        }

        var first = coordinates[0];
        var last = coordinates[coordinates.GetArrayLength() - 1];
        return TryReadCoordinate(first, out startLongitude, out startLatitude)
            && TryReadCoordinate(last, out endLongitude, out endLatitude);
    }

    private static bool TryFindFirstCoordinate(JsonElement root, out double longitude, out double latitude)
    {
        longitude = 0.0;
        latitude = 0.0;

        foreach (var feature in EnumerateFeatures(root))
        {
            if (!feature.TryGetProperty("geometry", out var geometry)
                || !geometry.TryGetProperty("coordinates", out var coordinates))
            {
                continue;
            }

            if (TryFindFirstCoordinateInArray(coordinates, out longitude, out latitude))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFindFirstCoordinateInArray(JsonElement element, out double longitude, out double latitude)
    {
        longitude = 0.0;
        latitude = 0.0;

        if (TryReadCoordinate(element, out longitude, out latitude))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var child in element.EnumerateArray())
        {
            if (TryFindFirstCoordinateInArray(child, out longitude, out latitude))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadCoordinate(JsonElement coordinate, out double longitude, out double latitude)
    {
        longitude = 0.0;
        latitude = 0.0;
        if (coordinate.ValueKind != JsonValueKind.Array || coordinate.GetArrayLength() < 2)
        {
            return false;
        }

        var lon = coordinate[0];
        var lat = coordinate[1];
        if (lon.ValueKind != JsonValueKind.Number || lat.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        longitude = lon.GetDouble();
        latitude = lat.GetDouble();
        return true;
    }

    private static string? TryReadStringProperty(JsonElement properties, string propertyName)
    {
        if (properties.ValueKind != JsonValueKind.Object
            || !properties.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }

    private static int? TryReadIntProperty(JsonElement properties, string propertyName)
    {
        var raw = TryReadStringProperty(properties, propertyName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private sealed class WorldPositionConverter
    {
        private readonly TerrainImportProfile _profile;
        private readonly RasterTransform? _rasterTransform;
        private readonly string _workingDirectory;
        private readonly Dictionary<string, Vector2> _cache = new(StringComparer.Ordinal);
        private bool _projectionWarningShown;

        private WorldPositionConverter(
            TerrainImportProfile profile,
            RasterTransform? rasterTransform,
            string workingDirectory)
        {
            _profile = profile;
            _rasterTransform = rasterTransform;
            _workingDirectory = workingDirectory;
        }

        public static WorldPositionConverter Create(TerrainImportProfile profile, string workingDirectory)
        {
            return new WorldPositionConverter(profile, TryReadRasterTransform(profile, workingDirectory), workingDirectory);
        }

        public Vector2 Convert(double longitude, double latitude)
        {
            if (_rasterTransform != null && TryConvertWithRaster(longitude, latitude, out var rasterPosition))
            {
                return rasterPosition;
            }

            return ConvertFromOrigin(longitude, latitude, _profile);
        }

        private bool TryConvertWithRaster(double longitude, double latitude, out Vector2 position)
        {
            var key = $"{FormatDouble(longitude)},{FormatDouble(latitude)}";
            if (_cache.TryGetValue(key, out position))
            {
                return true;
            }

            try
            {
                var projectedPoint = ProjectToRasterSrs(longitude, latitude);
                var transform = _rasterTransform!;
                var dx = projectedPoint.X - transform.OriginX;
                var dy = projectedPoint.Y - transform.OriginY;
                var determinant = transform.PixelWidth * transform.PixelHeight
                    - transform.RotationX * transform.RotationY;
                if (Math.Abs(determinant) < double.Epsilon)
                {
                    return false;
                }

                var pixel = (transform.PixelHeight * dx - transform.RotationX * dy) / determinant;
                var line = (-transform.RotationY * dx + transform.PixelWidth * dy) / determinant;
                var scale = _profile.MetersToGodotScale > 0.0f ? _profile.MetersToGodotScale : 1.0f;
                position = new Vector2((float)(pixel * scale), (float)(line * scale));
                _cache[key] = position;
                return true;
            }
            catch (Exception exception)
            {
                if (!_projectionWarningShown)
                {
                    GD.PushWarning($"Could not align hole coordinates to the heightmap raster. Falling back to the import origin. {exception.Message}");
                    _projectionWarningShown = true;
                }

                position = Vector2.Zero;
                return false;
            }
        }

        private ProjectedPoint ProjectToRasterSrs(double longitude, double latitude)
        {
            var transform = _rasterTransform!;
            var result = ExternalToolRunner.RunWithInput(
                _profile.GdalTransformCommand,
                ["-s_srs", "EPSG:4326", "-t_srs", transform.TargetSpatialReference, "-output_xy"],
                $"{FormatDouble(longitude)} {FormatDouble(latitude)}{System.Environment.NewLine}",
                _workingDirectory);

            var tokens = result.StandardOutput.Split(
                [' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2
                || !double.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var projectedX)
                || !double.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var projectedY))
            {
                throw new InvalidOperationException($"Unexpected gdaltransform output: {result.StandardOutput}");
            }

            return new ProjectedPoint(projectedX, projectedY);
        }

        private static RasterTransform? TryReadRasterTransform(TerrainImportProfile profile, string workingDirectory)
        {
            var heightmapPath = profile.SourceHeightmapPath.Trim();
            if (string.IsNullOrWhiteSpace(heightmapPath))
            {
                return null;
            }

            var heightmapAbsolutePath = CourseFileUtilities.ToAbsolutePath(heightmapPath);
            if (!File.Exists(heightmapAbsolutePath))
            {
                return null;
            }

            try
            {
                var result = ExternalToolRunner.Run(
                    profile.GdalInfoCommand,
                    ["-json", heightmapAbsolutePath],
                    workingDirectory);

                using var document = JsonDocument.Parse(result.StandardOutput);
                var root = document.RootElement;
                if (!root.TryGetProperty("geoTransform", out var geoTransform)
                    || geoTransform.GetArrayLength() < 6)
                {
                    return null;
                }

                var targetSpatialReference = ReadTargetSpatialReference(root);
                if (string.IsNullOrWhiteSpace(targetSpatialReference))
                {
                    return null;
                }

                return new RasterTransform(
                    geoTransform[0].GetDouble(),
                    geoTransform[1].GetDouble(),
                    geoTransform[2].GetDouble(),
                    geoTransform[3].GetDouble(),
                    geoTransform[4].GetDouble(),
                    geoTransform[5].GetDouble(),
                    targetSpatialReference);
            }
            catch (Exception exception)
            {
                GD.PushWarning($"Could not read heightmap coordinates. Falling back to the import origin. {exception.Message}");
                return null;
            }
        }

        private static string ReadTargetSpatialReference(JsonElement root)
        {
            if (root.TryGetProperty("stac", out var stac)
                && stac.TryGetProperty("proj:epsg", out var epsg)
                && epsg.ValueKind == JsonValueKind.Number
                && epsg.TryGetInt32(out var epsgCode)
                && epsgCode > 0)
            {
                return $"EPSG:{epsgCode}";
            }

            if (root.TryGetProperty("coordinateSystem", out var coordinateSystem)
                && coordinateSystem.TryGetProperty("wkt", out var wkt))
            {
                return wkt.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static Vector2 ConvertFromOrigin(double longitude, double latitude, TerrainImportProfile profile)
        {
            var originLatitudeRadians = profile.OriginLatitude * Math.PI / 180.0;
            var metersPerDegreeLongitude = MetersPerDegreeLatitude * Math.Cos(originLatitudeRadians);
            var scale = profile.MetersToGodotScale;
            var x = (longitude - profile.OriginLongitude) * metersPerDegreeLongitude * scale;
            var z = -(latitude - profile.OriginLatitude) * MetersPerDegreeLatitude * scale;
            return new Vector2((float)x, (float)z);
        }
    }

    private sealed record RasterTransform(
        double OriginX,
        double PixelWidth,
        double RotationX,
        double OriginY,
        double RotationY,
        double PixelHeight,
        string TargetSpatialReference);

    private sealed record ProjectedPoint(double X, double Y);

    private static string FormatDouble(double value)
    {
        return value.ToString("G17", CultureInfo.InvariantCulture);
    }

    private sealed record ImportedHole(
        int Sequence,
        int? HoleNumber,
        string Name,
        int Par,
        Vector2 TeePosition,
        Vector2 PinPosition);
}
