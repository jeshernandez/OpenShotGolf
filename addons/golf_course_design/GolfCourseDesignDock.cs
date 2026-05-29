using System;
using System.IO;
using Godot;

[Tool]
[GlobalClass]
public partial class GolfCourseDesignDock : ScrollContainer
{
    private const float CoordinateMin = -10000.0f;
    private const float CoordinateMax = 10000.0f;
    private const float CoordinateStep = 0.5f;
    private const string DefaultProjectPath = "res://Courses/UserCourses/NewCourse/course_design.tres";

    private EditorPlugin? _editorPlugin;
    private GolfCourseProject _project = GolfCourseProject.CreateDefault();
    private string _projectFilePath = DefaultProjectPath;
    private bool _isUpdatingUi;
    private int _selectedHoleIndex = 0;

    private LineEdit? _projectFilePathEdit;
    private LineEdit? _courseTitleEdit;
    private LineEdit? _outputFolderEdit;
    private LineEdit? _terrainFolderEdit;
    private LineEdit? _teeColorsEdit;
    private LineEdit? _sourceTerrainDirectoryEdit;
    private LineEdit? _sourceHeightmapEdit;
    private LineEdit? _sourceOverlayEdit;
    private LineEdit? _sourceBoundaryEdit;
    private LineEdit? _sourcePointCloudEdit;
    private LineEdit? _sourceHolesGeoJsonEdit;
    private LineEdit? _sourceBunkersGeoJsonEdit;
    private LineEdit? _gdalTranslateCommandEdit;
    private LineEdit? _gdalWarpCommandEdit;
    private LineEdit? _gdalFillNodataCommandEdit;
    private LineEdit? _gdalInfoCommandEdit;
    private SpinBox? _noDataFillDistanceSpin;
    private CheckBox? _generateHoleOverlayCheck;
    private SpinBox? _holeCorridorWidthSpin;
    private SpinBox? _teeMarkerRadiusSpin;
    private SpinBox? _greenMarkerRadiusSpin;
    private LineEdit? _ogrCommandEdit;
    private LineEdit? _gdalRasterizeCommandEdit;
    private LineEdit? _gdalDemCommandEdit;
    private LineEdit? _pdalCommandEdit;
    private OptionButton? _importModeButton;
    private CheckBox? _copyTerrainCheck;
    private SpinBox? _originLatitudeSpin;
    private SpinBox? _originLongitudeSpin;
    private SpinBox? _metersToGodotScaleSpin;
    private SpinBox? _rasterResolutionSpin;
    private SpinBox? _terrainHeightScaleSpin;
    private SpinBox? _terrainHeightOffsetSpin;
    private LineEdit? _sourceSpatialReferenceEdit;
    private LineEdit? _targetSpatialReferenceEdit;
    private SpinBox? _innerRadiusSpin;
    private SpinBox? _outerRadiusSpin;
    private ItemList? _holeList;
    private LineEdit? _holeNameEdit;
    private SpinBox? _parSpin;
    private SpinBox? _holeXSpin;
    private SpinBox? _holeZSpin;
    private readonly TeeRowControl[] _teeControls = new TeeRowControl[4];
    private Label? _statusLabel;

    public EditorPlugin? EditorPlugin
    {
        get => _editorPlugin;
        set
        {
            _editorPlugin = value;
            if (IsInsideTree())
            {
                UpdateStatus("Ready");
            }
        }
    }

    public override void _Ready()
    {
        BuildUi();
        LoadProject(_project);
    }

    private void BuildUi()
    {
        var root = new VBoxContainer();
        root.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        root.SizeFlagsVertical = SizeFlags.ExpandFill;
        AddChild(root);

        root.AddChild(BuildTopBar());

        var tabs = new TabContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        root.AddChild(tabs);

        var courseTab = new VBoxContainer
        {
            Name = "Course",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        tabs.AddChild(courseTab);
        BuildCourseTab(courseTab);

        var importTab = new VBoxContainer
        {
            Name = "Import",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        tabs.AddChild(importTab);
        BuildImportTab(importTab);

        _statusLabel = new Label
        {
            Text = "Ready",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        root.AddChild(_statusLabel);
    }

    private Control BuildTopBar()
    {
        var panel = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };

        panel.AddChild(BuildFieldRow("Project file", out _projectFilePathEdit));
        panel.AddChild(BuildButtonRow());

        return panel;
    }

    private Control BuildButtonRow()
    {
        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };

        row.AddChild(MakeButton("New project", () => LoadProject(GolfCourseProject.CreateDefault())));
        row.AddChild(MakeButton("Load", LoadProjectFromDisk));
        row.AddChild(MakeButton("Save", SaveProjectToDisk));
        row.AddChild(MakeButton("Build terrain", BuildTerrain));
        row.AddChild(MakeButton("Export course", ExportCourse));
        row.AddChild(MakeButton("Open scene", OpenCourseScene));
        row.AddChild(MakeButton("Open output", OpenOutputFolder));

        return row;
    }

    private void BuildCourseTab(Container parent)
    {
        parent.AddChild(BuildFieldRow("Course title", out _courseTitleEdit));
        parent.AddChild(BuildFieldRow("Output folder", out _outputFolderEdit));
        parent.AddChild(BuildFieldRow("Terrain folder", out _terrainFolderEdit));
        parent.AddChild(BuildFieldRow("Tee colours (comma separated)", out _teeColorsEdit));

        var holeArea = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        parent.AddChild(holeArea);

        var listColumn = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(220, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        holeArea.AddChild(listColumn);

        _holeList = new ItemList
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single
        };
        _holeList.ItemSelected += OnHoleSelected;
        listColumn.AddChild(_holeList);

        var holeButtons = new HBoxContainer();
        holeButtons.AddChild(MakeButton("Add", AddHole));
        holeButtons.AddChild(MakeButton("Duplicate", DuplicateHole));
        holeButtons.AddChild(MakeButton("Remove", RemoveHole));
        listColumn.AddChild(holeButtons);

        var editorColumn = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        holeArea.AddChild(editorColumn);

        editorColumn.AddChild(BuildFieldRow("Hole name", out _holeNameEdit));

        var parRow = new HBoxContainer();
        parRow.AddChild(new Label { Text = "Par", CustomMinimumSize = new Vector2(120, 0) });
        _parSpin = MakeSpinBox(1, 9, 1, 4);
        parRow.AddChild(_parSpin);
        editorColumn.AddChild(parRow);

        var holeLocationRow = new HBoxContainer();
        holeLocationRow.AddChild(new Label { Text = "Hole location X", CustomMinimumSize = new Vector2(120, 0) });
        _holeXSpin = MakeSpinBox(CoordinateMin, CoordinateMax, CoordinateStep, 180.0);
        holeLocationRow.AddChild(_holeXSpin);
        holeLocationRow.AddChild(new Label { Text = "Z", CustomMinimumSize = new Vector2(24, 0) });
        _holeZSpin = MakeSpinBox(CoordinateMin, CoordinateMax, CoordinateStep, 0.0);
        holeLocationRow.AddChild(_holeZSpin);
        editorColumn.AddChild(holeLocationRow);

        var teeHeader = new Label { Text = "Tee boxes", ThemeTypeVariation = "HeaderSmall" };
        editorColumn.AddChild(teeHeader);

        var teeGrid = new GridContainer
        {
            Columns = 3,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        teeGrid.AddChild(new Label { Text = "Color" });
        teeGrid.AddChild(new Label { Text = "X" });
        teeGrid.AddChild(new Label { Text = "Z" });

        var teeColors = new[] { "Black", "Blue", "White", "Red" };
        for (var index = 0; index < teeColors.Length; index++)
        {
            var teeRow = new TeeRowControl(teeColors[index])
            {
                XSpin = MakeSpinBox(CoordinateMin, CoordinateMax, CoordinateStep, 0.0),
                ZSpin = MakeSpinBox(CoordinateMin, CoordinateMax, CoordinateStep, 0.0)
            };

            teeGrid.AddChild(teeRow.MakeColorLabel());
            teeGrid.AddChild(teeRow.XSpin);
            teeGrid.AddChild(teeRow.ZSpin);
            _teeControls[index] = teeRow;
        }

        editorColumn.AddChild(teeGrid);

        ConnectHoleEditorSignals();
    }

    private void BuildImportTab(Container parent)
    {
        parent.AddChild(BuildModeRow());
        parent.AddChild(BuildFieldRow("Source terrain directory", out _sourceTerrainDirectoryEdit));
        parent.AddChild(BuildFieldRow("Source heightmap", out _sourceHeightmapEdit));
        parent.AddChild(BuildFieldRow("Source overlay", out _sourceOverlayEdit));
        parent.AddChild(BuildFieldRow("Source boundary", out _sourceBoundaryEdit));
        parent.AddChild(BuildFieldRow("Source point cloud", out _sourcePointCloudEdit));
        parent.AddChild(BuildFieldRow("Source holes GeoJSON", out _sourceHolesGeoJsonEdit));
        parent.AddChild(BuildFieldRow("Source bunkers GeoJSON", out _sourceBunkersGeoJsonEdit));
        parent.AddChild(MakeButton("Import holes GeoJSON", ImportHolesFromGeoJson));

        var copyRow = new HBoxContainer();
        _copyTerrainCheck = new CheckBox { Text = "Copy source terrain data into the exported course folder" };
        copyRow.AddChild(_copyTerrainCheck);
        parent.AddChild(copyRow);

        var originLatitudeRow = new HBoxContainer();
        originLatitudeRow.AddChild(new Label { Text = "Origin latitude", CustomMinimumSize = new Vector2(180, 0) });
        _originLatitudeSpin = MakeSpinBox(-90.0, 90.0, 0.000001, 0.0);
        originLatitudeRow.AddChild(_originLatitudeSpin);
        parent.AddChild(originLatitudeRow);

        var originLongitudeRow = new HBoxContainer();
        originLongitudeRow.AddChild(new Label { Text = "Origin longitude", CustomMinimumSize = new Vector2(180, 0) });
        _originLongitudeSpin = MakeSpinBox(-180.0, 180.0, 0.000001, 0.0);
        originLongitudeRow.AddChild(_originLongitudeSpin);
        parent.AddChild(originLongitudeRow);

        var scaleRow = new HBoxContainer();
        scaleRow.AddChild(new Label { Text = "Meters to Godot scale", CustomMinimumSize = new Vector2(180, 0) });
        _metersToGodotScaleSpin = MakeSpinBox(0.001, 100.0, 0.1, 1.0);
        scaleRow.AddChild(_metersToGodotScaleSpin);
        parent.AddChild(scaleRow);

        var resolutionRow = new HBoxContainer();
        resolutionRow.AddChild(new Label { Text = "Raster resolution (m)", CustomMinimumSize = new Vector2(180, 0) });
        _rasterResolutionSpin = MakeSpinBox(0.1, 10.0, 0.1, 1.0);
        resolutionRow.AddChild(_rasterResolutionSpin);
        parent.AddChild(resolutionRow);

        var heightScaleRow = new HBoxContainer();
        heightScaleRow.AddChild(new Label { Text = "Terrain height scale", CustomMinimumSize = new Vector2(180, 0) });
        _terrainHeightScaleSpin = MakeSpinBox(0.001, 10000.0, 0.1, 1.0);
        heightScaleRow.AddChild(_terrainHeightScaleSpin);
        parent.AddChild(heightScaleRow);

        var heightOffsetRow = new HBoxContainer();
        heightOffsetRow.AddChild(new Label { Text = "Terrain height offset", CustomMinimumSize = new Vector2(180, 0) });
        _terrainHeightOffsetSpin = MakeSpinBox(-10000.0, 10000.0, 0.1, 0.0);
        heightOffsetRow.AddChild(_terrainHeightOffsetSpin);
        parent.AddChild(heightOffsetRow);

        parent.AddChild(BuildFieldRow("Source CRS", out _sourceSpatialReferenceEdit));
        parent.AddChild(BuildFieldRow("Target CRS", out _targetSpatialReferenceEdit));

        var innerRow = new HBoxContainer();
        innerRow.AddChild(new Label { Text = "Inner radius (m)", CustomMinimumSize = new Vector2(180, 0) });
        _innerRadiusSpin = MakeSpinBox(50.0, 10000.0, 10.0, 750.0);
        innerRow.AddChild(_innerRadiusSpin);
        parent.AddChild(innerRow);

        var outerRow = new HBoxContainer();
        outerRow.AddChild(new Label { Text = "Outer radius (m)", CustomMinimumSize = new Vector2(180, 0) });
        _outerRadiusSpin = MakeSpinBox(50.0, 12000.0, 10.0, 950.0);
        outerRow.AddChild(_outerRadiusSpin);
        parent.AddChild(outerRow);

        var fillDistanceRow = new HBoxContainer();
        fillDistanceRow.AddChild(new Label { Text = "NoData fill distance (px)", CustomMinimumSize = new Vector2(180, 0) });
        _noDataFillDistanceSpin = MakeSpinBox(1.0, 100000.0, 1.0, 1000.0);
        fillDistanceRow.AddChild(_noDataFillDistanceSpin);
        parent.AddChild(fillDistanceRow);

        var overlayRow = new HBoxContainer();
        _generateHoleOverlayCheck = new CheckBox { Text = "Generate per-hole colour overlay from holes GeoJSON" };
        overlayRow.AddChild(_generateHoleOverlayCheck);
        parent.AddChild(overlayRow);

        var corridorWidthRow = new HBoxContainer();
        corridorWidthRow.AddChild(new Label { Text = "Hole corridor width (m)", CustomMinimumSize = new Vector2(180, 0) });
        _holeCorridorWidthSpin = MakeSpinBox(1.0, 500.0, 1.0, 25.0);
        corridorWidthRow.AddChild(_holeCorridorWidthSpin);
        parent.AddChild(corridorWidthRow);

        var teeRadiusRow = new HBoxContainer();
        teeRadiusRow.AddChild(new Label { Text = "Tee marker radius (m)", CustomMinimumSize = new Vector2(180, 0) });
        _teeMarkerRadiusSpin = MakeSpinBox(1.0, 200.0, 1.0, 8.0);
        teeRadiusRow.AddChild(_teeMarkerRadiusSpin);
        parent.AddChild(teeRadiusRow);

        var greenRadiusRow = new HBoxContainer();
        greenRadiusRow.AddChild(new Label { Text = "Green marker radius (m)", CustomMinimumSize = new Vector2(180, 0) });
        _greenMarkerRadiusSpin = MakeSpinBox(1.0, 200.0, 1.0, 10.0);
        greenRadiusRow.AddChild(_greenMarkerRadiusSpin);
        parent.AddChild(greenRadiusRow);

        parent.AddChild(BuildFieldRow("GDAL translate command", out _gdalTranslateCommandEdit));
        parent.AddChild(BuildFieldRow("GDAL warp command", out _gdalWarpCommandEdit));
        parent.AddChild(BuildFieldRow("GDAL CLI command (fill-nodata)", out _gdalFillNodataCommandEdit));
        parent.AddChild(BuildFieldRow("GDAL info command", out _gdalInfoCommandEdit));
        parent.AddChild(BuildFieldRow("OGR command", out _ogrCommandEdit));
        parent.AddChild(BuildFieldRow("GDAL rasterize command", out _gdalRasterizeCommandEdit));
        parent.AddChild(BuildFieldRow("GDAL DEM command", out _gdalDemCommandEdit));
        parent.AddChild(BuildFieldRow("PDAL command", out _pdalCommandEdit));

        ConnectImportSignals();
    }

    private Control BuildModeRow()
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Import mode", CustomMinimumSize = new Vector2(180, 0) });

        _importModeButton = new OptionButton();
        _importModeButton.AddItem("Manual", (int)TerrainImportProfile.TerrainImportMode.Manual);
        _importModeButton.AddItem("External terrain data", (int)TerrainImportProfile.TerrainImportMode.ExternalTerrainData);
        _importModeButton.AddItem("Heightmap", (int)TerrainImportProfile.TerrainImportMode.Heightmap);
        _importModeButton.AddItem("Point cloud", (int)TerrainImportProfile.TerrainImportMode.PointCloud);
        row.AddChild(_importModeButton);

        return row;
    }

    private void ConnectHoleEditorSignals()
    {
        if (_holeNameEdit != null)
        {
            _holeNameEdit.TextChanged += _ => ApplyHoleEditorToProject();
        }

        if (_parSpin != null)
        {
            _parSpin.ValueChanged += _ => ApplyHoleEditorToProject();
        }

        if (_holeXSpin != null)
        {
            _holeXSpin.ValueChanged += _ => ApplyHoleEditorToProject();
        }

        if (_holeZSpin != null)
        {
            _holeZSpin.ValueChanged += _ => ApplyHoleEditorToProject();
        }

        foreach (var teeRow in _teeControls)
        {
            if (teeRow == null)
            {
                continue;
            }

            teeRow.XSpin.ValueChanged += _ => ApplyHoleEditorToProject();
            teeRow.ZSpin.ValueChanged += _ => ApplyHoleEditorToProject();
        }
    }

    private void ConnectImportSignals()
    {
        if (_importModeButton != null)
        {
            _importModeButton.ItemSelected += _ => ApplyImportEditorToProject();
        }

        if (_sourceTerrainDirectoryEdit != null)
        {
            _sourceTerrainDirectoryEdit.TextChanged += _ => ApplyImportEditorToProject();
        }

        if (_sourceHeightmapEdit != null)
        {
            _sourceHeightmapEdit.TextChanged += _ => ApplyImportEditorToProject();
        }

        if (_sourceOverlayEdit != null)
        {
            _sourceOverlayEdit.TextChanged += _ => ApplyImportEditorToProject();
        }

        if (_sourceBoundaryEdit != null)
        {
            _sourceBoundaryEdit.TextChanged += _ => ApplyImportEditorToProject();
        }

        if (_sourcePointCloudEdit != null)
        {
            _sourcePointCloudEdit.TextChanged += _ => ApplyImportEditorToProject();
        }

        if (_sourceHolesGeoJsonEdit != null)
        {
            _sourceHolesGeoJsonEdit.TextChanged += _ => ApplyImportEditorToProject();
        }

        if (_sourceBunkersGeoJsonEdit != null)
        {
            _sourceBunkersGeoJsonEdit.TextChanged += _ => ApplyImportEditorToProject();
        }

        if (_copyTerrainCheck != null)
        {
            _copyTerrainCheck.Toggled += _ => ApplyImportEditorToProject();
        }

        if (_originLatitudeSpin != null)
        {
            _originLatitudeSpin.ValueChanged += _ => ApplyImportEditorToProject();
        }

        if (_originLongitudeSpin != null)
        {
            _originLongitudeSpin.ValueChanged += _ => ApplyImportEditorToProject();
        }

        if (_metersToGodotScaleSpin != null)
        {
            _metersToGodotScaleSpin.ValueChanged += _ => ApplyImportEditorToProject();
        }

        if (_rasterResolutionSpin != null)
        {
            _rasterResolutionSpin.ValueChanged += _ => ApplyImportEditorToProject();
        }

        if (_terrainHeightScaleSpin != null)
        {
            _terrainHeightScaleSpin.ValueChanged += _ => ApplyImportEditorToProject();
        }

        if (_terrainHeightOffsetSpin != null)
        {
            _terrainHeightOffsetSpin.ValueChanged += _ => ApplyImportEditorToProject();
        }

        if (_sourceSpatialReferenceEdit != null)
        {
            _sourceSpatialReferenceEdit.TextChanged += _ => ApplyImportEditorToProject();
        }

        if (_targetSpatialReferenceEdit != null)
        {
            _targetSpatialReferenceEdit.TextChanged += _ => ApplyImportEditorToProject();
        }

        if (_innerRadiusSpin != null)
        {
            _innerRadiusSpin.ValueChanged += _ => ApplyImportEditorToProject();
        }

        if (_outerRadiusSpin != null)
        {
            _outerRadiusSpin.ValueChanged += _ => ApplyImportEditorToProject();
        }

        if (_gdalTranslateCommandEdit != null)
        {
            _gdalTranslateCommandEdit.TextChanged += _ => ApplyImportEditorToProject();
        }

        if (_gdalWarpCommandEdit != null)
        {
            _gdalWarpCommandEdit.TextChanged += _ => ApplyImportEditorToProject();
        }

        if (_gdalFillNodataCommandEdit != null)
        {
            _gdalFillNodataCommandEdit.TextChanged += _ => ApplyImportEditorToProject();
        }

        if (_gdalInfoCommandEdit != null)
        {
            _gdalInfoCommandEdit.TextChanged += _ => ApplyImportEditorToProject();
        }

        if (_noDataFillDistanceSpin != null)
        {
            _noDataFillDistanceSpin.ValueChanged += _ => ApplyImportEditorToProject();
        }

        if (_generateHoleOverlayCheck != null)
        {
            _generateHoleOverlayCheck.Toggled += _ => ApplyImportEditorToProject();
        }

        if (_holeCorridorWidthSpin != null)
        {
            _holeCorridorWidthSpin.ValueChanged += _ => ApplyImportEditorToProject();
        }

        if (_teeMarkerRadiusSpin != null)
        {
            _teeMarkerRadiusSpin.ValueChanged += _ => ApplyImportEditorToProject();
        }

        if (_greenMarkerRadiusSpin != null)
        {
            _greenMarkerRadiusSpin.ValueChanged += _ => ApplyImportEditorToProject();
        }

        if (_ogrCommandEdit != null)
        {
            _ogrCommandEdit.TextChanged += _ => ApplyImportEditorToProject();
        }

        if (_gdalRasterizeCommandEdit != null)
        {
            _gdalRasterizeCommandEdit.TextChanged += _ => ApplyImportEditorToProject();
        }

        if (_gdalDemCommandEdit != null)
        {
            _gdalDemCommandEdit.TextChanged += _ => ApplyImportEditorToProject();
        }

        if (_pdalCommandEdit != null)
        {
            _pdalCommandEdit.TextChanged += _ => ApplyImportEditorToProject();
        }
    }

    private void LoadProjectFromDisk()
    {
        var path = GetProjectFilePath();
        if (!Godot.FileAccess.FileExists(path))
        {
            UpdateStatus($"Project file not found: {_projectFilePath}");
            return;
        }

        var loaded = ResourceLoader.Load<GolfCourseProject>(path);
        if (loaded == null)
        {
            UpdateStatus($"Could not load project: {_projectFilePath}");
            return;
        }

        _projectFilePath = path;
        LoadProject(loaded);
        UpdateStatus($"Loaded project: {path}");
    }

    private void SaveProjectToDisk()
    {
        ApplyUiToProject();
        var path = GetProjectFilePath();
        EnsureParentDirectory(path);

        var error = ResourceSaver.Save(_project, path);
        if (error != Error.Ok)
        {
            UpdateStatus($"Could not save project: {error}");
            return;
        }

        _projectFilePath = path;
        UpdateStatus($"Saved project: {path}");
    }

    private void ExportCourse()
    {
        try
        {
            ApplyUiToProject();
            SaveProjectToDisk();
            var result = CourseExportService.ExportCourse(_project);
            UpdateStatus($"Exported course to {result.OutputFolder}");
        }
        catch (Exception exception)
        {
            UpdateStatus(exception.Message);
        }
    }

    private void BuildTerrain()
    {
        try
        {
            ApplyUiToProject();
            SaveProjectToDisk();
            var result = BuildTerrainCore();
            UpdateStatus(result.Message);
        }
        catch (Exception exception)
        {
            UpdateStatus(exception.Message);
        }
    }

    private TerrainBuildResult BuildTerrainCore()
    {
        return TerrainImportService.BuildTerrain(_project, this);
    }

    private void ImportHolesFromGeoJson()
    {
        try
        {
            ApplyUiToProject();
            var importedCount = GeoJsonCourseLayoutImporter.ImportHoles(_project);
            _selectedHoleIndex = 0;
            RefreshAllUi();
            UpdateStatus($"Imported {importedCount} holes from GeoJSON. Click Save to keep the updated course project.");
        }
        catch (Exception exception)
        {
            UpdateStatus(exception.Message);
        }
    }

    private void OpenCourseScene()
    {
        try
        {
            ApplyUiToProject();
            var outputFolder = CourseFileUtilities.NormalizeProjectPath(_project.OutputFolder);
            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                UpdateStatus("Set an output folder first.");
                return;
            }

            var scenePath = $"{outputFolder.TrimEnd('/')}/course.tscn";
            if (!Godot.FileAccess.FileExists(scenePath))
            {
                UpdateStatus($"Scene not found: {scenePath}. Export the course first.");
                return;
            }

            var editorInterface = Engine.GetSingleton("EditorInterface");
            if (editorInterface == null)
            {
                UpdateStatus("Open this from the Godot editor to preview the course scene.");
                return;
            }

            editorInterface.Call("open_scene_from_path", scenePath);
            UpdateStatus($"Opened {scenePath}");
        }
        catch (Exception exception)
        {
            UpdateStatus(exception.Message);
        }
    }

    private void OpenOutputFolder()
    {
        var outputFolder = _outputFolderEdit?.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            UpdateStatus("Set an output folder first.");
            return;
        }

        OS.ShellOpen(ProjectSettings.GlobalizePath(outputFolder));
    }

    private void AddHole()
    {
        ApplyUiToProject();
        _project.Holes.Add(new GolfHoleDefinition
        {
            HoleName = $"Hole {_project.Holes.Count + 1}",
            Par = 4,
            HoleLocation = new Vector2(180.0f + _project.Holes.Count * 30.0f, 0.0f)
        });
        RefreshHoleList(_project.Holes.Count - 1);
    }

    private void DuplicateHole()
    {
        ApplyUiToProject();
        if (_selectedHoleIndex < 0 || _selectedHoleIndex >= _project.Holes.Count)
        {
            return;
        }

        var copy = _project.Holes[_selectedHoleIndex].DuplicateHole();
        copy.HoleName = $"{copy.HoleName} Copy";
        _project.Holes.Insert(_selectedHoleIndex + 1, copy);
        RefreshHoleList(_selectedHoleIndex + 1);
    }

    private void RemoveHole()
    {
        if (_project.Holes.Count <= 1)
        {
            UpdateStatus("Keep at least one hole in the project.");
            return;
        }

        if (_selectedHoleIndex < 0 || _selectedHoleIndex >= _project.Holes.Count)
        {
            return;
        }

        _project.Holes.RemoveAt(_selectedHoleIndex);
        _selectedHoleIndex = Math.Clamp(_selectedHoleIndex, 0, _project.Holes.Count - 1);
        RefreshHoleList(_selectedHoleIndex);
    }

    private void OnHoleSelected(long index)
    {
        if (_isUpdatingUi)
        {
            return;
        }

        ApplyUiToProject();
        _selectedHoleIndex = (int)index;
        LoadHoleToUi();
    }

    private void LoadProject(GolfCourseProject project)
    {
        _project = project;
        _project.EnsureDefaults();
        _selectedHoleIndex = 0;
        RefreshAllUi();
    }

    private void RefreshAllUi()
    {
        _isUpdatingUi = true;

        if (_projectFilePathEdit != null)
        {
            _projectFilePathEdit.Text = _projectFilePath;
        }

        if (_courseTitleEdit != null)
        {
            _courseTitleEdit.Text = _project.CourseTitle;
        }

        if (_outputFolderEdit != null)
        {
            _outputFolderEdit.Text = _project.OutputFolder;
        }

        if (_terrainFolderEdit != null)
        {
            _terrainFolderEdit.Text = _project.TerrainFolderName;
        }

        if (_teeColorsEdit != null)
        {
            _teeColorsEdit.Text = string.Join(", ", _project.GetEffectiveTeeColors());
        }

        if (_importModeButton != null)
        {
            _importModeButton.Selected = (int)_project.ImportProfile.Mode;
        }

        if (_sourceTerrainDirectoryEdit != null)
        {
            _sourceTerrainDirectoryEdit.Text = _project.ImportProfile.SourceTerrainDirectory;
        }

        if (_sourceHeightmapEdit != null)
        {
            _sourceHeightmapEdit.Text = _project.ImportProfile.SourceHeightmapPath;
        }

        if (_sourceOverlayEdit != null)
        {
            _sourceOverlayEdit.Text = _project.ImportProfile.SourceOverlayPath;
        }

        if (_sourceBoundaryEdit != null)
        {
            _sourceBoundaryEdit.Text = _project.ImportProfile.SourceBoundaryPath;
        }

        if (_sourcePointCloudEdit != null)
        {
            _sourcePointCloudEdit.Text = _project.ImportProfile.SourcePointCloudPath;
        }

        if (_sourceHolesGeoJsonEdit != null)
        {
            _sourceHolesGeoJsonEdit.Text = _project.ImportProfile.SourceHolesGeoJsonPath;
        }

        if (_sourceBunkersGeoJsonEdit != null)
        {
            _sourceBunkersGeoJsonEdit.Text = _project.ImportProfile.SourceBunkersGeoJsonPath;
        }

        if (_copyTerrainCheck != null)
        {
            _copyTerrainCheck.ButtonPressed = _project.ImportProfile.CopySourceTerrainData;
        }

        if (_originLatitudeSpin != null)
        {
            _originLatitudeSpin.Value = _project.ImportProfile.OriginLatitude;
        }

        if (_originLongitudeSpin != null)
        {
            _originLongitudeSpin.Value = _project.ImportProfile.OriginLongitude;
        }

        if (_metersToGodotScaleSpin != null)
        {
            _metersToGodotScaleSpin.Value = _project.ImportProfile.MetersToGodotScale;
        }

        if (_rasterResolutionSpin != null)
        {
            _rasterResolutionSpin.Value = _project.ImportProfile.RasterResolutionMeters;
        }

        if (_terrainHeightScaleSpin != null)
        {
            _terrainHeightScaleSpin.Value = _project.ImportProfile.TerrainHeightScale;
        }

        if (_terrainHeightOffsetSpin != null)
        {
            _terrainHeightOffsetSpin.Value = _project.ImportProfile.TerrainHeightOffset;
        }

        if (_sourceSpatialReferenceEdit != null)
        {
            _sourceSpatialReferenceEdit.Text = _project.ImportProfile.SourceSpatialReference;
        }

        if (_targetSpatialReferenceEdit != null)
        {
            _targetSpatialReferenceEdit.Text = _project.ImportProfile.TargetSpatialReference;
        }

        if (_innerRadiusSpin != null)
        {
            _innerRadiusSpin.Value = _project.ImportProfile.InnerRadiusMeters;
        }

        if (_outerRadiusSpin != null)
        {
            _outerRadiusSpin.Value = _project.ImportProfile.OuterRadiusMeters;
        }

        if (_gdalTranslateCommandEdit != null)
        {
            _gdalTranslateCommandEdit.Text = _project.ImportProfile.GdalTranslateCommand;
        }

        if (_gdalWarpCommandEdit != null)
        {
            _gdalWarpCommandEdit.Text = _project.ImportProfile.GdalWarpCommand;
        }

        if (_gdalFillNodataCommandEdit != null)
        {
            _gdalFillNodataCommandEdit.Text = _project.ImportProfile.GdalFillNodataCommand;
        }

        if (_gdalInfoCommandEdit != null)
        {
            _gdalInfoCommandEdit.Text = _project.ImportProfile.GdalInfoCommand;
        }

        if (_noDataFillDistanceSpin != null)
        {
            _noDataFillDistanceSpin.Value = _project.ImportProfile.NoDataFillDistancePixels;
        }

        if (_generateHoleOverlayCheck != null)
        {
            _generateHoleOverlayCheck.ButtonPressed = _project.ImportProfile.GenerateHoleOverlay;
        }

        if (_holeCorridorWidthSpin != null)
        {
            _holeCorridorWidthSpin.Value = _project.ImportProfile.HoleCorridorWidthMeters;
        }

        if (_teeMarkerRadiusSpin != null)
        {
            _teeMarkerRadiusSpin.Value = _project.ImportProfile.TeeMarkerRadiusMeters;
        }

        if (_greenMarkerRadiusSpin != null)
        {
            _greenMarkerRadiusSpin.Value = _project.ImportProfile.GreenMarkerRadiusMeters;
        }

        if (_ogrCommandEdit != null)
        {
            _ogrCommandEdit.Text = _project.ImportProfile.OgrCommand;
        }

        if (_gdalRasterizeCommandEdit != null)
        {
            _gdalRasterizeCommandEdit.Text = _project.ImportProfile.GdalRasterizeCommand;
        }

        if (_gdalDemCommandEdit != null)
        {
            _gdalDemCommandEdit.Text = _project.ImportProfile.GdalDemCommand;
        }

        if (_pdalCommandEdit != null)
        {
            _pdalCommandEdit.Text = _project.ImportProfile.PdalCommand;
        }

        RefreshHoleList(_selectedHoleIndex);
        LoadHoleToUi();

        _isUpdatingUi = false;
    }

    private void RefreshHoleList(int selectedIndex)
    {
        if (_holeList == null)
        {
            return;
        }

        _isUpdatingUi = true;
        _holeList.Clear();
        for (var index = 0; index < _project.Holes.Count; index++)
        {
            var hole = _project.Holes[index];
            _holeList.AddItem(string.IsNullOrWhiteSpace(hole.HoleName) ? $"Hole {index + 1}" : hole.HoleName);
        }

        if (_project.Holes.Count > 0)
        {
            _holeList.Select(Math.Clamp(selectedIndex, 0, _project.Holes.Count - 1));
        }

        _isUpdatingUi = false;
    }

    private void LoadHoleToUi()
    {
        if (_selectedHoleIndex < 0 || _selectedHoleIndex >= _project.Holes.Count)
        {
            return;
        }

        var hole = _project.Holes[_selectedHoleIndex];
        _isUpdatingUi = true;

        if (_holeNameEdit != null)
        {
            _holeNameEdit.Text = hole.HoleName;
        }

        if (_parSpin != null)
        {
            _parSpin.Value = hole.Par;
        }

        if (_holeXSpin != null)
        {
            _holeXSpin.Value = hole.HoleLocation.X;
        }

        if (_holeZSpin != null)
        {
            _holeZSpin.Value = hole.HoleLocation.Y;
        }

        for (var index = 0; index < _teeControls.Length; index++)
        {
            var teeRow = _teeControls[index];
            if (teeRow == null)
            {
                continue;
            }

            if (index < hole.TeeBoxes.Count)
            {
                teeRow.TeeColor = hole.TeeBoxes[index].TeeColor;
                teeRow.XSpin.Value = hole.TeeBoxes[index].Position.X;
                teeRow.ZSpin.Value = hole.TeeBoxes[index].Position.Y;
            }
        }

        _isUpdatingUi = false;
    }

    private void ApplyUiToProject()
    {
        if (_isUpdatingUi)
        {
            return;
        }

        if (_courseTitleEdit != null)
        {
            _project.CourseTitle = _courseTitleEdit.Text.Trim();
        }

        if (_outputFolderEdit != null)
        {
            _project.OutputFolder = _outputFolderEdit.Text.Trim();
        }

        if (_terrainFolderEdit != null)
        {
            _project.TerrainFolderName = string.IsNullOrWhiteSpace(_terrainFolderEdit.Text) ? "Terrain" : _terrainFolderEdit.Text.Trim();
        }

        if (_teeColorsEdit != null)
        {
            var parsed = new Godot.Collections.Array<string>();
            foreach (var token in _teeColorsEdit.Text.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
            {
                parsed.Add(token);
            }

            if (parsed.Count > 0)
            {
                _project.TeeColors = parsed;
            }
        }

        ApplyHoleEditorToProject();
        ApplyImportEditorToProject();
    }

    private void ApplyHoleEditorToProject()
    {
        if (_isUpdatingUi || _selectedHoleIndex < 0 || _selectedHoleIndex >= _project.Holes.Count)
        {
            return;
        }

        var hole = _project.Holes[_selectedHoleIndex];
        if (_holeNameEdit != null)
        {
            hole.HoleName = _holeNameEdit.Text.Trim();
        }

        if (_parSpin != null)
        {
            hole.Par = (int)_parSpin.Value;
        }

        if (_holeXSpin != null && _holeZSpin != null)
        {
            hole.HoleLocation = new Vector2((float)_holeXSpin.Value, (float)_holeZSpin.Value);
        }

        hole.TeeBoxes.Clear();
        foreach (var teeRow in _teeControls)
        {
            if (teeRow == null)
            {
                continue;
            }

            hole.TeeBoxes.Add(new GolfTeeBoxDefinition
            {
                TeeColor = teeRow.TeeColor,
                Position = new Vector2((float)teeRow.XSpin.Value, (float)teeRow.ZSpin.Value)
            });
        }

        RefreshHoleList(_selectedHoleIndex);
    }

    private void ApplyImportEditorToProject()
    {
        if (_isUpdatingUi)
        {
            return;
        }

        var profile = _project.ImportProfile ??= new TerrainImportProfile();
        if (_importModeButton != null)
        {
            profile.Mode = (TerrainImportProfile.TerrainImportMode)_importModeButton.GetSelectedId();
        }

        if (_sourceTerrainDirectoryEdit != null)
        {
            profile.SourceTerrainDirectory = _sourceTerrainDirectoryEdit.Text.Trim();
        }

        if (_sourceHeightmapEdit != null)
        {
            profile.SourceHeightmapPath = _sourceHeightmapEdit.Text.Trim();
        }

        if (_sourceOverlayEdit != null)
        {
            profile.SourceOverlayPath = _sourceOverlayEdit.Text.Trim();
        }

        if (_sourceBoundaryEdit != null)
        {
            profile.SourceBoundaryPath = _sourceBoundaryEdit.Text.Trim();
        }

        if (_sourcePointCloudEdit != null)
        {
            profile.SourcePointCloudPath = _sourcePointCloudEdit.Text.Trim();
        }

        if (_sourceHolesGeoJsonEdit != null)
        {
            profile.SourceHolesGeoJsonPath = _sourceHolesGeoJsonEdit.Text.Trim();
        }

        if (_sourceBunkersGeoJsonEdit != null)
        {
            profile.SourceBunkersGeoJsonPath = _sourceBunkersGeoJsonEdit.Text.Trim();
        }

        if (_copyTerrainCheck != null)
        {
            profile.CopySourceTerrainData = _copyTerrainCheck.ButtonPressed;
        }

        if (_originLatitudeSpin != null)
        {
            profile.OriginLatitude = _originLatitudeSpin.Value;
        }

        if (_originLongitudeSpin != null)
        {
            profile.OriginLongitude = _originLongitudeSpin.Value;
        }

        if (_metersToGodotScaleSpin != null)
        {
            profile.MetersToGodotScale = (float)_metersToGodotScaleSpin.Value;
        }

        if (_rasterResolutionSpin != null)
        {
            profile.RasterResolutionMeters = (float)_rasterResolutionSpin.Value;
        }

        if (_terrainHeightScaleSpin != null)
        {
            profile.TerrainHeightScale = (float)_terrainHeightScaleSpin.Value;
        }

        if (_terrainHeightOffsetSpin != null)
        {
            profile.TerrainHeightOffset = (float)_terrainHeightOffsetSpin.Value;
        }

        if (_sourceSpatialReferenceEdit != null)
        {
            profile.SourceSpatialReference = _sourceSpatialReferenceEdit.Text.Trim();
        }

        if (_targetSpatialReferenceEdit != null)
        {
            profile.TargetSpatialReference = _targetSpatialReferenceEdit.Text.Trim();
        }

        if (_innerRadiusSpin != null)
        {
            profile.InnerRadiusMeters = (float)_innerRadiusSpin.Value;
        }

        if (_outerRadiusSpin != null)
        {
            profile.OuterRadiusMeters = (float)_outerRadiusSpin.Value;
        }

        if (_gdalTranslateCommandEdit != null)
        {
            profile.GdalTranslateCommand = string.IsNullOrWhiteSpace(_gdalTranslateCommandEdit.Text) ? "gdal_translate" : _gdalTranslateCommandEdit.Text.Trim();
        }

        if (_gdalWarpCommandEdit != null)
        {
            profile.GdalWarpCommand = string.IsNullOrWhiteSpace(_gdalWarpCommandEdit.Text) ? "gdalwarp" : _gdalWarpCommandEdit.Text.Trim();
        }

        if (_gdalFillNodataCommandEdit != null)
        {
            profile.GdalFillNodataCommand = string.IsNullOrWhiteSpace(_gdalFillNodataCommandEdit.Text) ? "gdal" : _gdalFillNodataCommandEdit.Text.Trim();
        }

        if (_gdalInfoCommandEdit != null)
        {
            profile.GdalInfoCommand = string.IsNullOrWhiteSpace(_gdalInfoCommandEdit.Text) ? "gdalinfo" : _gdalInfoCommandEdit.Text.Trim();
        }

        if (_noDataFillDistanceSpin != null)
        {
            profile.NoDataFillDistancePixels = (int)_noDataFillDistanceSpin.Value;
        }

        if (_generateHoleOverlayCheck != null)
        {
            profile.GenerateHoleOverlay = _generateHoleOverlayCheck.ButtonPressed;
        }

        if (_holeCorridorWidthSpin != null)
        {
            profile.HoleCorridorWidthMeters = (float)_holeCorridorWidthSpin.Value;
        }

        if (_teeMarkerRadiusSpin != null)
        {
            profile.TeeMarkerRadiusMeters = (float)_teeMarkerRadiusSpin.Value;
        }

        if (_greenMarkerRadiusSpin != null)
        {
            profile.GreenMarkerRadiusMeters = (float)_greenMarkerRadiusSpin.Value;
        }

        if (_ogrCommandEdit != null)
        {
            profile.OgrCommand = string.IsNullOrWhiteSpace(_ogrCommandEdit.Text) ? "ogr2ogr" : _ogrCommandEdit.Text.Trim();
        }

        if (_gdalRasterizeCommandEdit != null)
        {
            profile.GdalRasterizeCommand = string.IsNullOrWhiteSpace(_gdalRasterizeCommandEdit.Text) ? "gdal_rasterize" : _gdalRasterizeCommandEdit.Text.Trim();
        }

        if (_gdalDemCommandEdit != null)
        {
            profile.GdalDemCommand = string.IsNullOrWhiteSpace(_gdalDemCommandEdit.Text) ? "gdaldem" : _gdalDemCommandEdit.Text.Trim();
        }

        if (_pdalCommandEdit != null)
        {
            profile.PdalCommand = string.IsNullOrWhiteSpace(_pdalCommandEdit.Text) ? "pdal" : _pdalCommandEdit.Text.Trim();
        }

        WarnIfModeIgnoresSources(profile);
    }

    private void WarnIfModeIgnoresSources(TerrainImportProfile profile)
    {
        var hasHeightmap = !string.IsNullOrWhiteSpace(profile.SourceHeightmapPath);
        var hasPointCloud = !string.IsNullOrWhiteSpace(profile.SourcePointCloudPath);
        var hasBaseTerrain = !string.IsNullOrWhiteSpace(profile.SourceTerrainDirectory);
        var copiesBaseTerrain = profile.Mode
            is TerrainImportProfile.TerrainImportMode.Manual
            or TerrainImportProfile.TerrainImportMode.ExternalTerrainData;

        if (copiesBaseTerrain && !hasBaseTerrain && (hasHeightmap || hasPointCloud))
        {
            var suggestedMode = hasHeightmap ? "Heightmap" : "Point cloud";
            UpdateStatus($"Heads up: a source is set but mode is '{profile.Mode}', which copies base terrain. Switch to '{suggestedMode}' to generate terrain from it.");
        }
    }

    private string GetProjectFilePath()
    {
        var path = _projectFilePathEdit?.Text.Trim();
        if (!string.IsNullOrWhiteSpace(path))
        {
            _projectFilePath = path;
        }

        return string.IsNullOrWhiteSpace(_projectFilePath) ? DefaultProjectPath : _projectFilePath;
    }

    private void EnsureParentDirectory(string path)
    {
        var parent = Path.GetDirectoryName(ProjectSettings.GlobalizePath(path));
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }
    }

    private static Control BuildFieldRow(string labelText, out LineEdit lineEdit)
    {
        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };

        row.AddChild(new Label
        {
            Text = labelText,
            CustomMinimumSize = new Vector2(180, 0)
        });

        lineEdit = new LineEdit
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        row.AddChild(lineEdit);

        return row;
    }

    private static Button MakeButton(string text, Action pressed)
    {
        var button = new Button
        {
            Text = text
        };
        button.Pressed += pressed;
        return button;
    }

    private static SpinBox MakeSpinBox(double minValue, double maxValue, double step, double value)
    {
        return new SpinBox
        {
            MinValue = minValue,
            MaxValue = maxValue,
            Step = step,
            Value = value,
            AllowGreater = true,
            AllowLesser = true
        };
    }

    private void UpdateStatus(string message)
    {
        if (_statusLabel != null)
        {
            _statusLabel.Text = message;
        }
    }

    private sealed class TeeRowControl
    {
        public TeeRowControl(string teeColor)
        {
            TeeColor = teeColor;
        }

        public string TeeColor { get; set; }
        public SpinBox XSpin { get; init; } = null!;
        public SpinBox ZSpin { get; init; } = null!;

        public Label MakeColorLabel()
        {
            return new Label
            {
                Text = TeeColor,
                CustomMinimumSize = new Vector2(120, 0)
            };
        }
    }
}
