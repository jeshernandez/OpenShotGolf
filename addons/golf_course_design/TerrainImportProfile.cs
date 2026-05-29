using Godot;

[Tool]
[GlobalClass]
public partial class TerrainImportProfile : Resource
{
    [Export]
    public TerrainImportMode Mode { get; set; } = TerrainImportMode.Manual;

    [Export]
    public bool CopySourceTerrainData { get; set; } = true;

    [Export]
    public string SourceTerrainDirectory { get; set; } = string.Empty;

    [Export]
    public string SourceHeightmapPath { get; set; } = string.Empty;

    [Export]
    public string SourceOverlayPath { get; set; } = string.Empty;

    [Export]
    public string SourceBoundaryPath { get; set; } = string.Empty;

    [Export]
    public string SourcePointCloudPath { get; set; } = string.Empty;

    [Export]
    public string SourceHolesGeoJsonPath { get; set; } = string.Empty;

    [Export]
    public string SourceBunkersGeoJsonPath { get; set; } = string.Empty;

    [Export]
    public double OriginLatitude { get; set; } = 0.0;

    [Export]
    public double OriginLongitude { get; set; } = 0.0;

    [Export]
    public float MetersToGodotScale { get; set; } = 1.0f;

    [Export]
    public float RasterResolutionMeters { get; set; } = 1.0f;

    [Export]
    public float TerrainHeightScale { get; set; } = 1.0f;

    [Export]
    public float TerrainHeightOffset { get; set; } = 0.0f;

    [Export]
    public string SourceSpatialReference { get; set; } = string.Empty;

    [Export]
    public string TargetSpatialReference { get; set; } = string.Empty;

    [Export]
    public float InnerRadiusMeters { get; set; } = 750.0f;

    [Export]
    public float OuterRadiusMeters { get; set; } = 950.0f;

    [Export]
    public string GdalTranslateCommand { get; set; } = "gdal_translate";

    [Export]
    public string GdalWarpCommand { get; set; } = "gdalwarp";

    // GDAL 3.11+ dropped the standalone gdal_fillnodata script; NoData filling now runs through
    // the unified CLI as "gdal raster fill-nodata", so this holds the base "gdal" executable.
    [Export]
    public string GdalFillNodataCommand { get; set; } = "gdal";

    [Export]
    public string GdalInfoCommand { get; set; } = "gdalinfo";

    [Export]
    public string GdalTransformCommand { get; set; } = "gdaltransform";

    [Export]
    public int NoDataFillDistancePixels { get; set; } = 1000;

    // Auto-generate a per-hole colour overlay (Terrain3D colour map) from the holes GeoJSON so the
    // course layout is readable. Skipped when a manual SourceOverlayPath is provided.
    [Export]
    public bool GenerateHoleOverlay { get; set; } = true;

    [Export]
    public float HoleCorridorWidthMeters { get; set; } = 25.0f;

    [Export]
    public float TeeMarkerRadiusMeters { get; set; } = 8.0f;

    [Export]
    public float GreenMarkerRadiusMeters { get; set; } = 10.0f;

    [Export]
    public string OgrCommand { get; set; } = "ogr2ogr";

    [Export]
    public string GdalRasterizeCommand { get; set; } = "gdal_rasterize";

    [Export]
    public string GdalDemCommand { get; set; } = "gdaldem";

    [Export]
    public string PdalCommand { get; set; } = "pdal";

    public enum TerrainImportMode
    {
        Manual = 0,
        ExternalTerrainData = 1,
        Heightmap = 2,
        PointCloud = 3
    }
}
