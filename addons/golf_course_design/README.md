# Golf Course Design

This add-on adds a `Golf Course Design` bottom panel to the Godot editor. Use it to create a playable golf course folder from public map data, a prepared Terrain3D folder, a heightmap such as a GeoTIFF DEM, or a lidar point cloud.

The add-on does not download map or elevation files for you. Gather and clean source files first, then enter their paths in the Godot panel.

## Table Of Contents
- [Overview](#overview)
- [Choose A Workflow](#choose-a-workflow)
- [Install The Tools](#install-the-tools)
  - [Required Tools](#required-tools)
  - [Linux Installation](#linux-installation)
  - [Windows Installation](#windows-installation)
  - [Verify The Installation](#verify-the-installation)
- [Gather Course Data](#gather-course-data)
  - [Decide Which Files You Need](#decide-which-files-you-need)
  - [Find A Course In OpenStreetMap](#find-a-course-in-openstreetmap)
  - [Export OpenStreetMap Data With Overpass Turbo](#export-openstreetmap-data-with-overpass-turbo)
  - [Clean Or Trace Data In QGIS](#clean-or-trace-data-in-qgis)
  - [Download Elevation From USGS](#download-elevation-from-usgs)
  - [Use OpenTopography As An Alternative](#use-opentopography-as-an-alternative)
  - [Use APIs For Repeatable Downloads](#use-apis-for-repeatable-downloads)
    - [OpenStreetMap Known-Way API](#openstreetmap-known-way-api)
    - [Overpass API](#overpass-api)
    - [USGS TNM Access API](#usgs-tnm-access-api)
    - [OpenTopography API](#opentopography-api)
- [Use The Golf Course Design Dock](#use-the-golf-course-design-dock)
  - [Open The Dock](#open-the-dock)
  - [Top Bar Buttons](#top-bar-buttons)
  - [Course Tab](#course-tab)
  - [Import Tab](#import-tab)
- [Build Terrain](#build-terrain)
  - [Manual Mode](#manual-mode)
  - [External Terrain Data Mode](#external-terrain-data-mode)
  - [Heightmap Mode](#heightmap-mode)
  - [Point Cloud Mode](#point-cloud-mode)
  - [Generated Terrain Zones](#generated-terrain-zones)
- [Import Or Edit Holes](#import-or-edit-holes)
  - [Import Hole Lines From GeoJSON](#import-hole-lines-from-geojson)
  - [Add Or Edit Holes By Hand](#add-or-edit-holes-by-hand)
- [Export And Preview](#export-and-preview)
- [Airways Golf Course Example](#airways-golf-course-example)
  - [Step 1: Create The Working Folder](#step-1-create-the-working-folder)
  - [Step 2: Get The Boundary And Hole Lines](#step-2-get-the-boundary-and-hole-lines)
  - [Step 3: Get Elevation](#step-3-get-elevation)
  - [Step 4: Prepare The Data](#step-4-prepare-the-data)
  - [Step 5: Build And Export The Course](#step-5-build-and-export-the-course)
- [Files Written To Disk](#files-written-to-disk)
- [Diagram](#diagram)
- [Common Problems](#common-problems)
- [Sources](#sources)

## Overview
A finished course is one playable Godot scene for the full course. The add-on writes:

```text
res://Courses/UserCourses/<CourseName>/
  course.gd
  course.json
  course.tscn
  course_design.tres
  Terrain/
```

You can also keep a `source_data/` folder beside those files for the original GIS inputs. The game does not load files from `source_data/`.

The main applications have separate jobs:

| Application | Use it for | Does the add-on replace it? |
| --- | --- | --- |
| OpenStreetMap | Find a course and inspect existing public map features. | No. |
| Overpass Turbo | Query selected OpenStreetMap golf features and export them as GeoJSON. | No. |
| QGIS | Inspect, split, trace, edit, reproject, and clip GIS files. | No. QGIS is optional when your files already need no cleanup. |
| USGS The National Map | Download public United States elevation GeoTIFF and lidar LAS or LAZ files. | No. |
| OpenTopography | Find and download alternative topography data where your account has access. | No. |
| GDAL and OGR | Clip, reproject, fill DEM gaps, export Terrain3D inputs, and build generated terrain zones. | The add-on runs these commands for heightmap and point cloud builds. |
| PDAL | Turn LAS or LAZ point clouds into raster heightmaps. | The add-on runs PDAL only in `Point cloud` mode. |
| Godot `Golf Course Design` dock | Store course settings, import holes, build terrain, and export the playable course. | This is the add-on UI. |
| Terrain3D editor tools | Paint, sculpt, and repair fine terrain details after import. | No. |

## Choose A Workflow
Choose one terrain workflow before gathering files.

| Goal | Import mode | Required terrain input | Optional inputs | External commands used by the build |
| --- | --- | --- | --- | --- |
| Start quickly with a copy of the bundled driving range terrain | `Manual` | None | `Source terrain directory` if you want a different starting folder | None |
| Keep terrain that you already maintain in the output folder | `Manual` | Existing output `Terrain/` folder | None | None |
| Copy an existing Terrain3D folder into the course | `External terrain data` | `Source terrain directory` | None | None |
| Build from a DEM or GeoTIFF raster | `Heightmap` | `Source heightmap` such as `<course>_dem.tif` | Boundary, holes, bunkers | GDAL and OGR |
| Build from lidar | `Point cloud` | `Source point cloud` such as `<course>_lidar.laz` | Holes, bunkers, source and target CRS | PDAL, GDAL, and OGR |

Important choices:

- A GeoTIFF DEM and a lidar point cloud are alternatives. You do not need both.
- A `.tif` DEM is the simpler starting point. Use lidar only when you need its detail and are prepared for larger downloads and longer builds.
- A boundary GeoJSON is optional. In `Heightmap` mode it tells GDAL where to crop the source raster.
- A holes GeoJSON is optional if you will enter holes by hand. It is required for the `Import holes GeoJSON` button and for automatic fairway, tee, and green zone painting.
- A bunkers GeoJSON is optional. When supplied with holes GeoJSON, bunker polygons are painted as sand.
- A hand-made visual overlay is not currently applied to exported Terrain3D data. Leave `Source overlay` blank. See [Import Tab](#import-tab).

## Install The Tools

### Required Tools
The Godot editor always needs:

| Tool | Requirement | Why |
| --- | --- | --- |
| Godot | Godot `4.6.x` `.NET` editor, not the standard editor | The add-on and project contain C# scripts. |
| .NET SDK | .NET SDK `9.0` or later | `OpenFairway.csproj` targets `net9.0`. |
| Terrain3D | Already included in `res://addons/terrain_3d/` | The exported course stores terrain in Terrain3D region files. |

Install these tools for real-world GIS workflows:

| Tool | When it is needed | Notes |
| --- | --- | --- |
| QGIS | Recommended when inspecting, tracing, or cleaning GIS files | Optional if prepared files already line up. |
| GDAL `3.11` or later | Required for `Heightmap` and `Point cloud` builds | The add-on runs `gdal raster fill-nodata`, which was added in GDAL `3.11`. |
| OGR commands included with GDAL | Required when generating course zones from holes or bunkers GeoJSON | The add-on runs `ogr2ogr`, `gdal_rasterize`, and `gdaldem`. |
| PDAL | Required only for `Point cloud` mode and manual lidar preparation | It is not needed for a GeoTIFF-only workflow. |

The Airways example was prepared successfully with Godot `4.6.2`, .NET SDK `9.0.117`, GDAL `3.13.0`, QGIS `3.40.8`, and PDAL `2.10.0`. Exact patch versions do not need to match.

### Linux Installation
1. Install the Godot `4.6.x` `.NET` editor from https://godotengine.org/download.
2. Install the .NET `9.0` SDK using your distribution package manager or the Microsoft instructions at https://dotnet.microsoft.com/download/dotnet/9.0.
3. Install QGIS if you plan to inspect or edit GIS data. Use https://qgis.org/resources/installation-guide/ for your distribution.
4. Install Miniforge from https://github.com/conda-forge/miniforge if you do not already have a Conda-compatible package manager.
5. Create a dedicated GIS command environment:

```bash
conda create --yes --name openfairway-gis --channel conda-forge 'gdal>=3.11' pdal
conda activate openfairway-gis
```

6. Start Godot from the same activated terminal so the add-on can find the GIS commands:

```bash
godot --editor --path /path/to/OpenShotGolf
```

You may use distribution packages instead of Conda when they provide GDAL `3.11` or later and PDAL. Conda is recommended because it keeps the required commands together and avoids relying on an older system GDAL.

### Windows Installation
1. Install the Godot `4.6.x` `.NET` editor from https://godotengine.org/download.
2. Install the .NET `9.0` SDK from https://dotnet.microsoft.com/download/dotnet/9.0.
3. Install the QGIS Long Term Release from https://qgis.org/resources/installation-guide/. The standalone installer is the simplest choice for beginners.
4. Install Miniforge from https://github.com/conda-forge/miniforge.
5. Open the Miniforge Prompt and create a dedicated GIS command environment:

```bat
conda create --yes --name openfairway-gis --channel conda-forge "gdal>=3.11" pdal
conda activate openfairway-gis
```

6. Start the Godot `.NET` editor from the same prompt:

```bat
C:\path\to\Godot_v4.6-stable_mono_win64.exe --editor --path C:\path\to\OpenShotGolf
```

Starting Godot from the activated environment is the simplest way to make `gdal`, `ogr2ogr`, and `pdal` visible to the add-on. An advanced alternative is to enter full executable paths in the dock command fields, such as `C:\Users\<you>\miniforge3\envs\openfairway-gis\Library\bin\gdal_translate.exe`.

QGIS may include its own GDAL commands. Do not assume that those commands meet the add-on requirement. Verify that the `gdal` executable visible to Godot is version `3.11` or later.

### Verify The Installation
Run these commands from the same terminal or Miniforge Prompt that you will use to launch Godot:

```bash
godot --version
dotnet --version
gdal --version
gdal raster fill-nodata --help
gdal_translate --version
gdalwarp --version
gdalinfo --version
gdaltransform --version
ogr2ogr --version
gdal_rasterize --version
gdaldem --version
pdal --version
```

Expected results:

- `godot --version` starts with `4.6` and identifies a `.NET` or `mono` build.
- `dotnet --version` starts with `9` or a later supported major version.
- `gdal --version` reports `3.11` or later.
- `gdal raster fill-nodata --help` prints the fill command usage.
- Each standalone GDAL or OGR command prints a version or usage message.
- `pdal --version` prints a version when you plan to use lidar.

`gdaldem --version` may return usage text and a non-zero exit code because `gdaldem` expects a subcommand. That is acceptable if the executable is found.

## Gather Course Data
Use public data and keep a note of each source and license in `source_data/SOURCES.md`. Do not use private files, paid imagery, or copied course maps unless their licenses allow reuse.

### Decide Which Files You Need
Store raw and prepared inputs under the new course folder:

```text
res://Courses/UserCourses/<CourseName>/
  source_data/
    SOURCES.md
    <course>_boundary.geojson
    <course>_holes.geojson
    <course>_bunkers.geojson
    <course>_dem.tif
    <course>_lidar.laz
```

Every file except `SOURCES.md` is optional. Choose files based on your workflow:

| File | Use it for | Required? |
| --- | --- | --- |
| `<course>_boundary.geojson` | Course outline used to crop a DEM. | Optional. Recommended for heightmap builds. |
| `<course>_holes.geojson` | Hole center lines used for hole import and generated terrain zones. | Optional. Recommended when OpenStreetMap or QGIS can provide accurate lines. |
| `<course>_bunkers.geojson` | Bunker polygons painted as sand zones. | Optional. |
| `<course>_dem.tif` | Raster elevation input for `Heightmap` mode. | Required only for that mode. |
| `<course>_lidar.las` or `<course>_lidar.laz` | Point cloud input for `Point cloud` mode. | Required only for that mode. |
| `<course>_overlay.tif` | Hand-made color overlay. | Do not use yet. The current terrain build does not apply it. |

### Find A Course In OpenStreetMap
1. Open https://www.openstreetmap.org/.
2. Search for the course name and location.
3. Zoom until the full course is visible.
4. Click map features and look for a course polygon tagged `leisure=golf_course`.
5. Look for hole lines tagged `golf=hole`.

The main OpenStreetMap website is useful for finding and inspecting data. Its **Export** button downloads raw map data for the visible area. It does not export one selected course as clean GeoJSON. Use Overpass Turbo or QGIS for that.

Useful OpenStreetMap golf tags:

| Tag | Geometry you usually want | Use |
| --- | --- | --- |
| `leisure=golf_course` | Polygon or multipolygon | Course boundary |
| `golf=hole` | LineString | Hole center line |
| `golf=fairway` | Polygon | Optional reference layer |
| `golf=green` | Polygon | Optional reference layer |
| `golf=bunker` | Polygon | Optional sand-zone input |
| `golf=pin` | Point | Optional reference point |
| `route=golf` | Relation | Optional route reference |

### Export OpenStreetMap Data With Overpass Turbo
Overpass Turbo is the simplest browser-based way to export selected OpenStreetMap features as GeoJSON.

1. Open https://overpass-turbo.eu/.
2. Move its map to the course and zoom until the full course is visible.
3. Paste the boundary query below into the left panel:

```text
[out:json][timeout:25];
(
  way["leisure"="golf_course"]({{bbox}});
  relation["leisure"="golf_course"]({{bbox}});
);
out geom;
```

4. Click **Run**.
5. Confirm that the map shows the intended course boundary.
6. Click **Export** > **Data** > **as geoJSON**.
7. Save the file as `source_data/<course>_boundary.geojson`.
8. Open the file in QGIS and remove unrelated features if the query returned more than one polygon.

Run a separate query for holes:

```text
[out:json][timeout:25];
(
  way["golf"="hole"]({{bbox}});
);
out geom;
```

Export that result as `source_data/<course>_holes.geojson`.

Run separate queries for optional feature layers:

```text
[out:json][timeout:25];
(
  way["golf"="bunker"]({{bbox}});
  relation["golf"="bunker"]({{bbox}});
);
out geom;
```

Save bunker polygons as `source_data/<course>_bunkers.geojson`. You can replace `bunker` with `fairway` or `green`, or query `node["golf"="pin"]({{bbox}});`, when you want reference layers for manual cleanup.

Do not copy one mixed GeoJSON export to multiple filenames. Split it so each file contains the geometry expected by the add-on:

- Boundary file: one course polygon or multipolygon.
- Holes file: one line feature per hole.
- Bunkers file: bunker polygons only.

Example holes GeoJSON:

```json
{
  "type": "FeatureCollection",
  "features": [
    {
      "type": "Feature",
      "properties": {
        "golf": "hole",
        "ref": "1",
        "name": "Hole 1",
        "par": 4
      },
      "geometry": {
        "type": "LineString",
        "coordinates": [
          [-120.6227821, 35.6461199],
          [-120.6208224, 35.6467813],
          [-120.6199069, 35.6463858]
        ]
      }
    }
  ]
}
```

The first coordinate is the tee end. The last coordinate is the green or pin end. Keep each hole's `ref`, `name`, and `par` properties when available. The generated terrain-zone process also expects `ref` to be a numeric hole number.

### Clean Or Trace Data In QGIS
Use QGIS when source files contain unrelated features, missing hole lines, incorrect fields, or layers that do not line up.

Load existing files:

1. Open QGIS.
2. Choose **Layer** > **Add Layer** > **Add Vector Layer...** for GeoJSON, Shapefile, GeoPackage, or KML files.
3. Click **Browse**, choose the file, click **Add**, then click **Close**.
4. Choose **Layer** > **Add Layer** > **Add Raster Layer...** for GeoTIFF DEM files.
5. Use the built-in **OpenStreetMap** entry under **XYZ Tiles** in the Browser panel as an orientation layer.

Export selected features:

1. Select the features you want in the map or attribute table.
2. Right-click the layer in the Layers panel.
3. Choose **Export** > **Save Selected Features As...**.
4. Set **Format** to `GeoJSON`.
5. Set the coordinate reference system to `EPSG:4326 - WGS 84` for boundary, holes, and bunker GeoJSON files.
6. Save to the matching `source_data/` filename.

Trace a missing course boundary:

1. Choose **Layer** > **Create Layer** > **New Temporary Scratch Layer...**.
2. Set the geometry type to `Polygon`.
3. Set the CRS to `EPSG:4326`.
4. Select the new layer and choose **Edit** > **Toggle Editing**.
5. Use **Add Polygon Feature** on the Digitizing toolbar.
6. Click around the outside of the course. Right-click to finish.
7. Export the layer as `source_data/<course>_boundary.geojson`.

Trace missing hole lines:

1. Create a temporary scratch layer with geometry type `LineString` and CRS `EPSG:4326`.
2. Add fields named `ref`, `name`, and `par`.
3. Toggle editing and use **Add Line Feature**.
4. Click the tee point first, add any useful intermediate points, and click the green or pin point last.
5. Enter values such as `ref=1`, `name=Hole 1`, and `par=4`.
6. Repeat for each hole and export as `source_data/<course>_holes.geojson`.

Prepare a DEM manually in QGIS when needed:

1. Load the DEM and boundary.
2. Reproject a working boundary layer to a local meter-based CRS, such as the correct UTM zone.
3. Buffer the course boundary by roughly `50` to `200` meters so the terrain includes course edges, trees, and nearby roads.
4. Choose **Raster** > **Extraction** > **Clip Raster by Mask Layer**.
5. Use the DEM as the input layer and the buffered polygon as the mask layer.
6. Save the result as `source_data/<course>_dem.tif`.
7. Load the clipped result and check that the course lies inside it.

The add-on can also clip a DEM automatically when you set `Source boundary`. Manual QGIS clipping is useful when you want to inspect the result or choose a buffered boundary first.

### Download Elevation From USGS
For courses in the United States, start with USGS The National Map.

Use a DEM when possible:

1. Open https://apps.nationalmap.gov/downloader/.
2. Search for the course address or move the map to the course.
3. Draw or select an area that covers the course plus a border around it.
4. Select an elevation dataset under `3DEP`.
5. Prefer a `1 meter DEM` GeoTIFF product when it is available.
6. Show the matching products, inspect their footprints, and download the needed package.
7. Extract the `.tif` file into `source_data/`.
8. Rename or clip it to `source_data/<course>_dem.tif`.

Use lidar when you specifically need point-cloud detail:

1. Open https://apps.nationalmap.gov/lidar-explorer/.
2. Select **Lidar**.
3. Search or pan to the course.
4. Draw an area of interest around the course.
5. Expand the lidar results.
6. Download every LAZ tile intersecting the course and your intended border.
7. Merge and clip the files with PDAL before using them in the dock.

A course may cross several lidar tiles. One download URL is not always enough.

### Use OpenTopography As An Alternative
OpenTopography provides a browser map and APIs for topography data:

1. Open https://portal.opentopography.org/datasets.
2. Search by place name or enter lower-left and upper-right coordinates.
3. Select data sources and product filters.
4. Click **Get Data** for a matching dataset.
5. Select an area of interest.
6. Choose a raster DEM or point-cloud download when the dataset and your account allow it.

OpenTopography is optional. Access depends on the dataset and account type. Some USGS 3DEP, NOAA, ArcticDEM, and REMA access paths are limited to academic users or OpenTopography Plus subscribers. USGS 3DEP is also available publicly through USGS, so use The National Map when OpenTopography account restrictions get in the way.

### Use APIs For Repeatable Downloads
Browser workflows are easier for a first course. APIs are useful when you need a repeatable record of how a file was obtained. Save API responses in `source_data/` and record the commands in `source_data/SOURCES.md`.

#### OpenStreetMap Known-Way API
Use this only when you already know the numeric OpenStreetMap way ID. The main web interface does not provide a one-click clean GeoJSON download for a known course polygon.

Build the URL:

```text
https://www.openstreetmap.org/api/0.6/way/<way-id>/full
```

Download raw OSM XML:

```bash
curl -L -o <course>_boundary.osm \
  'https://www.openstreetmap.org/api/0.6/way/<way-id>/full'
```

Expected response: an XML `<osm>` document containing the requested way and its referenced nodes. This is not GeoJSON. Convert it with `ogr2ogr` or load it into QGIS and export the boundary as GeoJSON:

```bash
ogr2ogr -f GeoJSON <course>_boundary.geojson <course>_boundary.osm multipolygons
```

#### Overpass API
Overpass Turbo is the recommended manual approach because its **Export** menu converts results to GeoJSON in the browser. The direct Overpass API is useful for scripts, but it returns raw OSM JSON or XML rather than the browser-converted GeoJSON file.

Example payload for nearby holes, golf routes, and pins:

```text
[out:xml][timeout:25];
(
  way(around:1200,<latitude>,<longitude>)["golf"="hole"];
  relation(around:1200,<latitude>,<longitude>)["route"="golf"];
  node(around:1200,<latitude>,<longitude>)["golf"="pin"];
);
out body;
>;
out skel qt;
```

POST the URL-encoded `data` field:

```bash
curl -A 'OpenShotGolfCourseDesign/1.0' \
  -X POST \
  --data-urlencode 'data=[out:xml][timeout:25];(way(around:1200,<latitude>,<longitude>)["golf"="hole"];relation(around:1200,<latitude>,<longitude>)["route"="golf"];node(around:1200,<latitude>,<longitude>)["golf"="pin"];);out body;>;out skel qt;' \
  -o <course>_osm_golf_features.osm \
  'https://overpass-api.de/api/interpreter'
```

Expected response: raw OSM XML because the payload requests `[out:xml]`. If you change the payload to `[out:json]`, expect Overpass JSON. Neither direct response is the cleaned holes GeoJSON expected by the dock. Convert and review it in QGIS before importing.

#### USGS TNM Access API
USGS The National Map Downloader has a browser workflow. The TNM Access API is the scripted alternative for finding exact product download URLs.

The products endpoint is:

```text
https://tnmaccess.nationalmap.gov/api/v1/products
```

The bounding box order is:

```text
<west-longitude>,<south-latitude>,<east-longitude>,<north-latitude>
```

Use `curl --get --data-urlencode` so spaces, parentheses, and commas are encoded correctly.

Find one-meter GeoTIFF DEM products:

```bash
curl -L --get \
  --data-urlencode 'datasets=Digital Elevation Model (DEM) 1 meter' \
  --data-urlencode 'bbox=<west>,<south>,<east>,<north>' \
  --data-urlencode 'prodFormats=GeoTIFF' \
  --data-urlencode 'outputFormat=JSON' \
  -o usgs_dem_products.json \
  'https://tnmaccess.nationalmap.gov/api/v1/products'
```

Find lidar point-cloud products:

```bash
curl -L --get \
  --data-urlencode 'datasets=Lidar Point Cloud (LPC)' \
  --data-urlencode 'bbox=<west>,<south>,<east>,<north>' \
  --data-urlencode 'outputFormat=JSON' \
  -o usgs_lidar_products.json \
  'https://tnmaccess.nationalmap.gov/api/v1/products'
```

Expected response shape:

```json
{
  "total": 2,
  "items": [
    {
      "title": "USGS product title",
      "format": "GeoTIFF",
      "downloadURL": "https://example.usgs.gov/path/to/file.tif",
      "downloadLazURL": null,
      "urls": {
        "TIFF": "https://example.usgs.gov/path/to/file.tif"
      },
      "boundingBox": {
        "minX": -119.80,
        "maxX": -119.68,
        "minY": 36.74,
        "maxY": 36.83
      }
    }
  ],
  "errors": [],
  "messages": [
    "Retrieved 2 item(s) Retrieved (1 through 50)"
  ]
}
```

For DEM results, download the chosen item's `downloadURL`. For lidar results, download each intersecting tile using `downloadLazURL` when present, otherwise use `downloadURL`. Inspect each `boundingBox` before downloading.

The response is a product list, not the elevation file itself. Download the chosen files in a second step:

```bash
curl -L -o <local-file-name>.tif '<downloadURL-from-json>'
curl -L -o <local-file-name>.laz '<downloadLazURL-or-downloadURL-from-json>'
```

#### OpenTopography API
OpenTopography offers web downloads and APIs. Its USGS DEM API is useful when your account has access and you want a clipped DEM returned directly.

Create an OpenTopography account, request an API key from your account dashboard, and keep the key private. Do not commit a real API key to `README.md`, `SOURCES.md`, shell scripts, or source control.

Build a USGS DEM URL:

```text
https://portal.opentopography.org/API/usgsdem?datasetName=<dataset>&south=<south>&north=<north>&west=<west>&east=<east>&outputFormat=GTiff&API_Key=<your-api-key>
```

Example download:

```bash
curl -L \
  -o <course>_dem.tif \
  'https://portal.opentopography.org/API/usgsdem?datasetName=USGS10m&south=<south>&north=<north>&west=<west>&east=<east>&outputFormat=GTiff&API_Key=<your-api-key>'
```

Expected response: a GeoTIFF file, not a JSON product list. Open it in QGIS or inspect it with `gdalinfo` before using it. Dataset availability and API limits depend on your account. In particular, OpenTopography documents additional restrictions for USGS one-meter DEM API access.

## Use The Golf Course Design Dock

### Open The Dock
1. Open this project in the Godot `4.6.x` `.NET` editor.
2. Wait for C# scripts to compile.
3. Confirm that `Terrain3D` and `Golf Course Design` are enabled under **Project** > **Project Settings** > **Plugins**.
4. Click the bottom panel named `Golf Course Design`.
5. Widen the panel when needed so full paths are readable.

The dock saves settings to a Godot resource file such as:

```text
res://Courses/UserCourses/<CourseName>/course_design.tres
```

Use `res://` project paths in dock fields. On Windows, you can still use full executable paths for command fields when commands are not available on `PATH`.

### Top Bar Buttons
| Button | What it does | What to expect |
| --- | --- | --- |
| `New project` | Resets the dock to a new in-memory project with one default hole. | Existing files on disk are not deleted. Set a new project file and output folder before saving. |
| `Load` | Loads the `Project file` resource from disk. | The course and import fields change to the saved values. |
| `Save` | Saves the editable project resource. | Status shows `Saved project: <path>`. |
| `Build terrain` | Runs the selected terrain workflow. | The button is disabled while building. Heightmap and point-cloud builds may take time. |
| `Export course` | Saves the project and writes `course.gd`, `course.json`, and `course.tscn`. | Status shows the exported output folder. |
| `Open scene` | Opens the exported `course.tscn` in the Godot editor. | Export first. |
| `Open output` | Opens the output folder in the operating-system file browser. | Set `Output folder` first. |

### Course Tab
| Field | What to enter | Notes |
| --- | --- | --- |
| `Project file` | `res://Courses/UserCourses/<CourseName>/course_design.tres` | Editable add-on settings. This field appears above both tabs. |
| `Course title` | Player-facing name such as `Airways` | Written to `course.json`. |
| `Output folder` | `res://Courses/UserCourses/<CourseName>` | Set this before building terrain or exporting. |
| `Terrain folder` | Usually `Terrain` | Folder name inside the output folder. |
| `Tee colours (comma separated)` | For example `White, Red` | Keep only tee sets that the course uses. Imported holes start every enabled tee color at the same imported tee point; adjust them afterward. |
| `Hole name` | For example `Hole 1` | Used in exported metadata and marker nodes. |
| `Par` | Integer from `1` to `9` | Imported `par` values are clamped to this range. |
| `Hole location X`, `Z` | Godot terrain coordinates for the pin | X and Z are horizontal scene coordinates. |
| Tee-box `X`, `Z` values | Godot terrain coordinates for each tee color | Edit each tee separately after import. |

Hole buttons:

| Button | What it does |
| --- | --- |
| `Add` | Adds a new hole with default values. |
| `Duplicate` | Copies the selected hole and its tee boxes. |
| `Remove` | Removes the selected hole. The dock keeps at least one hole. |

### Import Tab
| Field | Used by | Required? | Notes |
| --- | --- | --- | --- |
| `Import mode` | Terrain build | Yes | Choose `Manual`, `External terrain data`, `Heightmap`, or `Point cloud`. |
| `Source terrain directory` | `Manual`, `External terrain data` | Optional in `Manual`; expected in `External terrain data` | Blank `Manual` mode falls back to `res://Courses/Range/Terrain`. |
| `Source heightmap` | `Heightmap`, hole alignment | Required for `Heightmap` | Usually a prepared `.tif` DEM. When present, hole import tries to align coordinates to the raster. |
| `Source overlay` | Reserved | Leave blank | The current build converts this file but intentionally does not apply it to exported Terrain3D data. Setting it also skips generated zone creation. |
| `Source boundary` | `Heightmap` | Optional | Polygon GeoJSON used by `gdalwarp` to crop the source heightmap. |
| `Source point cloud` | `Point cloud` | Required for `Point cloud` | LAS or LAZ file. Prefer a clipped file. |
| `Source holes GeoJSON` | Hole import, generated zones | Optional but recommended | LineString or MultiLineString features in longitude and latitude. |
| `Source bunkers GeoJSON` | Generated zones | Optional | Bunker polygons painted as sand when generated zones are enabled. |
| `Copy source terrain data into the exported course folder` | `Manual`, `External terrain data` | Optional | Turn off to leave the output `Terrain/` folder untouched. |
| `Origin latitude`, `Origin longitude` | Hole import fallback | Optional | Leave both at `0` to use the first hole coordinate. These values are used when raster alignment is unavailable. |
| `Meters to Godot scale` | Height, point-cloud, and hole import | Optional | Start with `1.0` for one Godot unit per meter. |
| `Raster resolution (m)` | Heightmap warp and PDAL raster output | Optional | Start with `1.0`. Smaller values create larger terrain data. |
| `Terrain height offset` | Terrain import | Optional | Start with `0.0`. Adjust if the imported terrain needs a vertical shift. |
| `Source CRS` | Heightmap warp and PDAL reprojection | Optional | Set only when the source file lacks correct CRS metadata or needs an override. |
| `Target CRS` | Heightmap warp and PDAL reprojection | Optional | Use a local meter-based CRS, such as the correct UTM zone. |
| `NoData fill distance (px)` | Heightmap and point-cloud raster fill | Optional | Maximum pixel search distance used to fill raster gaps. Default is `1000`. |
| `Generate per-hole colour overlay from holes GeoJSON` | Generated zones | Optional | Leave enabled to build class maps and paint terrain textures from holes GeoJSON. |
| `Hole corridor width (m)` | Generated zones | Optional | Controls the painted fairway width around each hole line. Default is `25`. |
| `Tee marker radius (m)` | Generated zones | Optional | Controls the painted tee disc radius. Default is `8`. |
| `Green marker radius (m)` | Generated zones | Optional | Controls the painted green disc radius. Default is `10`. |
| `GDAL translate command` | Heightmap and point-cloud builds | Required for those modes | Usually `gdal_translate`. |
| `GDAL warp command` | Heightmap builds when clipping or reprojecting | Required for generated terrain builds | Usually `gdalwarp`. |
| `GDAL CLI command (fill-nodata)` | Heightmap and point-cloud builds | Required for generated terrain builds | Usually `gdal`. Must support `gdal raster fill-nodata`. |
| `GDAL info command` | Raster inspection and hole alignment | Required for generated terrain builds | Usually `gdalinfo`. |
| `OGR command` | Generated zones | Required when generated zones are enabled | Usually `ogr2ogr`. |
| `GDAL rasterize command` | Generated zones | Required when generated zones are enabled | Usually `gdal_rasterize`. |
| `GDAL DEM command` | Generated zones | Required when generated zones are enabled | Usually `gdaldem`. |
| `PDAL command` | `Point cloud` | Required only for `Point cloud` | Usually `pdal`. |

The hole importer also runs `gdaltransform` automatically when `Source heightmap` points to an existing georeferenced raster. The dock does not currently expose a `gdaltransform` command field. Make sure `gdaltransform` is on `PATH` before launching Godot. If alignment cannot run, the importer logs a warning and falls back to the origin latitude and longitude.

## Build Terrain
Set `Output folder`, `Terrain folder`, and `Import mode` before clicking `Build terrain`.

Builds are staged first under:

```text
res://Courses/UserCourses/<CourseName>/.golf_course_design/
```

When a successful build replaces a non-empty `Terrain/` folder, the old folder is moved beside it:

```text
res://Courses/UserCourses/<CourseName>/Terrain.backup-<timestamp>/
```

### Manual Mode
Use `Manual` when you want a quick starting terrain or when you already maintain the output `Terrain/` folder yourself.

Copy the bundled range terrain:

1. Set `Import mode` to `Manual`.
2. Leave `Source terrain directory` blank to use `res://Courses/Range/Terrain`.
3. Leave `Copy source terrain data into the exported course folder` enabled.
4. Click `Build terrain`.

Leave an existing output terrain folder untouched:

1. Set `Import mode` to `Manual`.
2. Turn off `Copy source terrain data into the exported course folder`.
3. Click `Build terrain`.

### External Terrain Data Mode
Use this when you already have another Terrain3D folder to copy:

1. Set `Import mode` to `External terrain data`.
2. Set `Source terrain directory` to the existing Terrain3D folder.
3. Leave `Copy source terrain data into the exported course folder` enabled.
4. Click `Build terrain`.

Turn the copy checkbox off only when the desired `Terrain/` folder already exists in the output folder and should remain untouched.

### Heightmap Mode
Use this for DEM, GeoTIFF, or another GDAL-readable elevation raster:

1. Set `Import mode` to `Heightmap`.
2. Set `Source heightmap` to the prepared raster.
3. Set `Source boundary` if the add-on should crop the raster.
4. Set `Source holes GeoJSON` if you want automatic hole import or generated terrain zones.
5. Set `Source bunkers GeoJSON` if you want bunker polygons painted as sand.
6. Leave `Source overlay` blank.
7. Leave generated zones enabled unless you want plain imported terrain.
8. Set `Target CRS` to a local meter-based CRS when reprojection is needed.
9. Start with `Raster resolution (m)` set to `1.0`.
10. Start with `Meters to Godot scale` set to `1.0`.
11. Click `Build terrain`.

The add-on:

1. Optionally clips and reprojects the raster with `gdalwarp`.
2. Fills NoData gaps with `gdal raster fill-nodata`.
3. Writes a floating-point `height.exr`.
4. Optionally builds generated zone files from holes and bunkers.
5. Imports the heightmap through Terrain3D.
6. Paints the Terrain3D control maps when generated zone files exist.
7. Replaces the output terrain only after staging succeeds.

### Point Cloud Mode
Use this for LAS or compressed LAZ point-cloud files:

1. Download every tile needed for the course.
2. Merge and clip the point cloud first when the source is large.
3. Set `Import mode` to `Point cloud`.
4. Set `Source point cloud` to the clipped `.las` or `.laz` file.
5. Set `Source CRS` only when the file metadata is missing or incorrect.
6. Set `Target CRS` to the same local meter-based CRS used for the course.
7. Start with `Raster resolution (m)` set to `1.0`.
8. Set holes and bunker GeoJSON files if you want generated zones.
9. Click `Build terrain`.

The add-on writes `.golf_course_design/pdal_pipeline.json`, runs PDAL `writers.gdal` to create a raster heightmap, then follows the heightmap workflow.

Manual merge and clip example:

```bash
pdal merge tile-1.laz tile-2.laz tile-3.laz merged.laz
pdal translate merged.laz clipped.laz crop \
  --filters.crop.bounds='([<min-x>,<max-x>],[<min-y>,<max-y>])'
pdal info --summary clipped.laz
```

The crop bounds use the point cloud's projected X and Y coordinates, not longitude and latitude unless the file is actually stored in longitude and latitude.

### Generated Terrain Zones
When holes GeoJSON is set, generated zones are enabled, and `Source overlay` is blank, the add-on paints Terrain3D texture slots:

| Source area | Texture slot | Exported appearance |
| --- | --- | --- |
| Outside generated features | `2` | Rough |
| Hole corridors | `1` | Fairway |
| Tee discs | `0` | Green |
| Green discs | `0` | Green |
| Bunker polygons | `3` | Sand |
| Narrow outlines around generated features | `1` | Fairway |

The generated `overlay_color.png` is a design-time image that helps inspect the import. Terrain3D texture painting comes from the generated class map, not from baking that color image into the final terrain.

## Import Or Edit Holes

### Import Hole Lines From GeoJSON
1. Open the `Import` tab.
2. Set `Source holes GeoJSON`.
3. Set `Source heightmap` too when you want the importer to align holes to a georeferenced DEM.
4. Leave `Origin latitude` and `Origin longitude` at `0` when you want the importer to detect the first hole coordinate as the fallback origin.
5. Set `Meters to Godot scale` to `1.0` as a starting point.
6. Click `Import holes GeoJSON`.
7. Review the imported holes on the `Course` tab.
8. Click `Save`.

Importer behavior:

- Accepts one GeoJSON `Feature` or a `FeatureCollection`.
- Reads `LineString` and `MultiLineString` geometry.
- Uses the longest line from a `MultiLineString`.
- Uses the first coordinate as the tee.
- Uses the last coordinate as the pin.
- Sorts numeric `ref`, `hole`, or `golf:hole` properties when available.
- Reads `name`.
- Reads `par` or `golf:par`, defaulting to `4`.
- Gives every enabled tee color the same initial imported tee position.
- Replaces the current in-memory holes list. Save afterward to keep the change.

The importer expects holes GeoJSON coordinates in longitude and latitude (`EPSG:4326`). If the file is projected in meters, export a longitude-and-latitude copy from QGIS first.

### Add Or Edit Holes By Hand
Use this when no clean holes GeoJSON exists or when imported positions need correction:

1. Open the `Course` tab.
2. Click `Add`.
3. Enter `Hole name` and `Par`.
4. Enter the pin position with `Hole location X` and `Z`.
5. Enter the X and Z position for each enabled tee box.
6. Use `Duplicate` when a nearby hole is a useful starting point.
7. Use `Remove` to delete the selected hole.
8. Click `Save`.

Use the Godot 3D viewport and QGIS as visual references while adjusting positions.

## Export And Preview
1. Click `Save`.
2. Click `Build terrain` if the course does not already have a suitable `Terrain/` folder.
3. Click `Export course`.
4. Click `Open scene`.
5. Inspect the Terrain3D node in the 3D viewport.
6. Expand `HoleMarkers` and verify pin and tee positions.
7. Use Terrain3D editor tools for fine painting, sculpting, and repairs.
8. Run the game and select the course from the course selector.

The exported `course.tscn` inherits `res://Courses/_shared/course_base.tscn`, which provides shared player, camera, sky, and terrain nodes. The generated `course.gd` is a one-line subclass of `res://Courses/_shared/course_play.gd`. Shared gameplay handles hole management, stroke counting, scoring, the pin-distance indicator, and shot camera framing.

Hole geometry is written to `course.json` and `HoleMarkers` in `course.tscn`. The add-on does not create one scene per hole.

## Airways Golf Course Example
This example shows one real-world workflow for Airways Golf Course in Fresno, California.

Known public facts to verify before rebuilding:

- Course: `Airways Golf Course`
- Address: `5440 E Airways Blvd, Fresno, CA 93727`
- Approximate map center: `36.77724, -119.70619`
- OpenStreetMap boundary candidate: way `42013666`
- Public scorecard reference: 18 holes, par 69, 5301 yards from white tees

### Step 1: Create The Working Folder
Create:

```text
res://Courses/UserCourses/Airways/
  source_data/
    SOURCES.md
    airways_boundary.geojson
    airways_holes.geojson
    airways_bunkers.geojson
    airways_dem.tif
    airways_lidar.laz
    airways_lidar_clipped.laz
    usgs_dem_products.json
    usgs_lidar_products.json
    lidar/
  Terrain/
  course_design.tres
  course.json
  course.tscn
```

Only `course_design.tres`, `Terrain/`, `course.gd`, `course.json`, and `course.tscn` are needed by the game. Files under `source_data/` are raw or prepared design inputs.

### Step 2: Get The Boundary And Hole Lines
Browser workflow:

1. Run the Overpass Turbo boundary query from [Export OpenStreetMap Data With Overpass Turbo](#export-openstreetmap-data-with-overpass-turbo).
2. Keep only the Airways course polygon in QGIS.
3. Save it as `source_data/airways_boundary.geojson`.
4. Run the separate holes query.
5. Keep only the 18 `golf=hole` lines.
6. Confirm that each first point is a tee and each last point is a green.
7. Save the result as `source_data/airways_holes.geojson`.
8. Run the bunker query and save cleaned bunker polygons as `source_data/airways_bunkers.geojson` when you want sand zones.

API workflow used for the committed source files:

```bash
cd Courses/UserCourses/Airways/source_data
curl -L -o airways_osm_way_42013666.osm \
  'https://www.openstreetmap.org/api/0.6/way/42013666/full'
ogr2ogr -f GeoJSON airways_boundary.geojson airways_osm_way_42013666.osm multipolygons
curl -A 'OpenShotGolfCourseDesign/1.0' \
  -X POST \
  --data-urlencode 'data=[out:xml][timeout:25];(way(around:1200,36.77724,-119.70619)["golf"="hole"];relation(around:1200,36.77724,-119.70619)["route"="golf"];node(around:1200,36.77724,-119.70619)["golf"="pin"];);out body;>;out skel qt;' \
  -o airways_osm_golf_features.osm \
  'https://overpass-api.de/api/interpreter'
OSM_USE_CUSTOM_INDEXING=NO ogr2ogr \
  -f GeoJSON \
  /tmp/airways_holes_raw.geojson \
  airways_osm_golf_features.osm \
  lines
jq '.name="airways_holes" | .features |= map(.properties += {golf:"hole", ref: ((try (.properties.other_tags | capture("\\"ref\\"=>\\"(?<v>[^\\"]+)\\"").v) catch "")), par: ((try (.properties.other_tags | capture("\\"par\\"=>\\"(?<v>[^\\"]+)\\"").v | tonumber) catch null))} | .properties.name = ("Hole " + .properties.ref)) | .features |= sort_by(.properties.ref | tonumber)' \
  /tmp/airways_holes_raw.geojson \
  > airways_holes.geojson
ogr2ogr -f GeoJSON airways_pins.geojson airways_osm_golf_features.osm points
```

Review API-generated GeoJSON in QGIS. Scripted conversion does not remove the need to verify geometry and fields.

### Step 3: Get Elevation
The committed Airways DEM was found with this USGS query:

```text
https://tnmaccess.nationalmap.gov/api/v1/products?datasets=Digital%20Elevation%20Model%20%28DEM%29%201%20meter&bbox=-119.712,36.774,-119.701,36.781&prodFormats=GeoTIFF&outputFormat=JSON
```

The JSON response contains matching products under `items`. Choose an item whose `boundingBox` covers the course, then use its `downloadURL`.

The example DEM was cropped with:

```bash
cd Courses/UserCourses/Airways/source_data
curl -L -o usgs_dem_products.json \
  'https://tnmaccess.nationalmap.gov/api/v1/products?datasets=Digital%20Elevation%20Model%20%28DEM%29%201%20meter&bbox=-119.712,36.774,-119.701,36.781&prodFormats=GeoTIFF&outputFormat=JSON'
gdalwarp \
  -overwrite \
  -r bilinear \
  -cutline airways_boundary.geojson \
  -crop_to_cutline \
  -dstnodata -9999 \
  -t_srs EPSG:26911 \
  -tr 1 1 \
  /vsicurl/https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/1m/Projects/CA_SanJoaquin_2021_A21/TIFF/USGS_1M_11_x25y408_CA_SanJoaquin_2021_A21.tif \
  airways_dem.tif
gdalinfo -stats airways_dem.tif
```

The resulting `airways_dem.tif` is `932 x 610` pixels at one-meter resolution in `EPSG:26911`.

The DEM is enough for the normal heightmap workflow. Lidar is optional. The committed optional lidar path used:

```bash
cd Courses/UserCourses/Airways/source_data
mkdir -p lidar
curl -L -o usgs_lidar_products.json \
  'https://tnmaccess.nationalmap.gov/api/v1/products?datasets=Lidar%20Point%20Cloud%20%28LPC%29&bbox=-119.712,36.774,-119.701,36.781&outputFormat=JSON'
curl -L -o lidar/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA570730.laz \
  'https://rockyweb.usgs.gov/vdelivery/Datasets/Staged/Elevation/LPC/Projects/CA_FEMAR9Fresno_2019_D20/CA_FEMAR9Fresno_2_2019/LAZ/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA570730.laz'
curl -L -o lidar/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA570740.laz \
  'https://rockyweb.usgs.gov/vdelivery/Datasets/Staged/Elevation/LPC/Projects/CA_FEMAR9Fresno_2019_D20/CA_FEMAR9Fresno_2_2019/LAZ/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA570740.laz'
curl -L -o lidar/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA580730.laz \
  'https://rockyweb.usgs.gov/vdelivery/Datasets/Staged/Elevation/LPC/Projects/CA_FEMAR9Fresno_2019_D20/CA_FEMAR9Fresno_2_2019/LAZ/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA580730.laz'
curl -L -o lidar/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA580740.laz \
  'https://rockyweb.usgs.gov/vdelivery/Datasets/Staged/Elevation/LPC/Projects/CA_FEMAR9Fresno_2019_D20/CA_FEMAR9Fresno_2_2019/LAZ/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA580740.laz'
pdal merge \
  lidar/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA570730.laz \
  lidar/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA570740.laz \
  lidar/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA580730.laz \
  lidar/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA580740.laz \
  airways_lidar.laz
pdal translate \
  airways_lidar.laz \
  airways_lidar_clipped.laz \
  crop \
  --filters.crop.bounds='([258023.64,258955.64],[4073279.48,4073889.48])'
pdal info --summary airways_lidar_clipped.laz
```

Use `airways_lidar_clipped.laz` in `Point cloud` mode unless you specifically need the full merged file.

### Step 4: Prepare The Data
1. Load `airways_boundary.geojson`, `airways_holes.geojson`, optional `airways_bunkers.geojson`, and `airways_dem.tif` into QGIS.
2. Confirm that the boundary and DEM overlap.
3. Confirm that every hole line starts at a tee and ends at a green.
4. Confirm that every hole has a numeric `ref`.
5. Confirm that bunker input contains polygons only.
6. Use `EPSG:26911` or `EPSG:32611` for local meter-based terrain work around Fresno.
7. Save any corrected holes back to `airways_holes.geojson` in `EPSG:4326`.

Manual cleanup is expected. Public map lines are useful starting points, not surveyed tee and pin locations.

### Step 5: Build And Export The Course
1. Open the Godot `Golf Course Design` bottom panel.
2. Click `New project`.
3. Set `Project file` to `res://Courses/UserCourses/Airways/course_design.tres`.
4. Set `Course title` to `Airways`.
5. Set `Output folder` to `res://Courses/UserCourses/Airways`.
6. Set `Terrain folder` to `Terrain`.
7. Set `Tee colours (comma separated)` to `White, Red`.
8. Open the `Import` tab.
9. Set `Import mode` to `Heightmap`.
10. Set `Source heightmap` to `res://Courses/UserCourses/Airways/source_data/airways_dem.tif`.
11. Set `Source boundary` to `res://Courses/UserCourses/Airways/source_data/airways_boundary.geojson` only if you want the add-on to clip again.
12. Set `Source holes GeoJSON` to `res://Courses/UserCourses/Airways/source_data/airways_holes.geojson`.
13. Set `Source bunkers GeoJSON` to `res://Courses/UserCourses/Airways/source_data/airways_bunkers.geojson`.
14. Leave `Source overlay` blank.
15. Set `Target CRS` to `EPSG:26911`.
16. Leave scale and raster resolution at `1.0`.
17. Click `Import holes GeoJSON`.
18. Review holes and adjust tee boxes on the `Course` tab.
19. Click `Save`.
20. Click `Build terrain`.
21. Click `Export course`.
22. Click `Open scene`.

## Files Written To Disk
A finished folder usually contains:

```text
res://Courses/UserCourses/<CourseName>/
  course.gd
  course.json
  course.tscn
  course_design.tres
  Terrain/
  source_data/
  .golf_course_design/
  Terrain.backup-<timestamp>/
```

| Path | Purpose | Loaded by the game? |
| --- | --- | --- |
| `course.gd` | Generated one-line subclass of shared `course_play.gd`. | Yes |
| `course.json` | Course title, tee colors, texture indices, par, pin positions, and tee positions. | Yes |
| `course.tscn` | One playable scene for the full course. | Yes |
| `course_design.tres` | Editable dock project. | No |
| `Terrain/` | Terrain3D region files. | Yes |
| `source_data/` | Original and prepared GIS inputs plus source notes. | No |
| `.golf_course_design/` | Staging files created by terrain builds. | No |
| `Terrain.backup-<timestamp>/` | Previous terrain saved before replacement. | No |

Common staging files:

| File | Purpose |
| --- | --- |
| `heightmap_prepared.tif` | Cropped or reprojected heightmap. |
| `heightmap_filled.tif` | Heightmap after NoData filling. |
| `height.exr` | Floating-point Terrain3D height input. |
| `height.tif` | PDAL raster output in point-cloud mode. |
| `pdal_pipeline.json` | Generated PDAL pipeline. |
| `overlay_holes.gpkg` | Reprojected holes used for generated zones. |
| `overlay_bunkers.gpkg` | Reprojected bunkers used for generated zones. |
| `overlay_features.gpkg` | Buffered hole corridors, tees, greens, and optional bunkers. |
| `overlay_outlines.gpkg` | Narrow outlines around generated features. |
| `overlay_class.tif` | Numeric generated-zone raster. |
| `overlay_class_ids.png` | PNG copy used to paint Terrain3D control maps. |
| `overlay_color.png` | Design-time preview image. |
| `overlay_ramp.txt` | Color ramp for the design-time preview. |

## Diagram
The source diagram is in [assets/course_pipeline.puml](assets/course_pipeline.puml), and rendered images are in [assets/course_pipeline.svg](assets/course_pipeline.svg) and [assets/course_pipeline.png](assets/course_pipeline.png).

![Golf course design pipeline](assets/course_pipeline.svg)

## Common Problems
| Problem | What to check |
| --- | --- |
| The `Golf Course Design` panel does not appear. | Open the project in the Godot `.NET` editor, wait for C# compilation, and enable the add-on under **Project Settings** > **Plugins**. |
| `gdal raster fill-nodata` is unknown. | Install GDAL `3.11` or later and launch Godot from the environment containing that `gdal` executable. |
| A GDAL, OGR, or PDAL command is not found. | Launch Godot from the activated GIS environment or enter a full executable path in the matching dock field. |
| Hole import warns that raster alignment failed. | Make sure `Source heightmap` exists, has CRS metadata, and `gdaltransform` is on `PATH`. The dock does not currently expose a `gdaltransform` override field. |
| Terrain build copies the range instead of using the DEM or lidar file. | Change `Import mode` to `Heightmap` or `Point cloud`. Merely setting a source file does not select its mode. |
| The terrain build cannot clip the DEM. | Confirm that `Source boundary` exists and that GDAL can interpret both source coordinate systems. Set `Source CRS` or `Target CRS` only when needed. |
| The terrain has deep pits or blank gaps. | Increase `NoData fill distance (px)`, inspect the prepared raster in QGIS, and rebuild. |
| The terrain is too flat, too tall, or vertically misplaced. | Check the source elevation units, `Meters to Godot scale`, and `Terrain height offset`. |
| Holes are far away from the terrain. | Export holes GeoJSON as `EPSG:4326`, set the same source heightmap used for terrain, verify `gdaltransform`, and review fallback origin values. |
| Generated fairways are missing. | Set holes GeoJSON, leave generated zones enabled, keep `Source overlay` blank, and make sure each hole has a numeric `ref`. |
| Bunkers are not painted as sand. | Set `Source bunkers GeoJSON`, confirm it contains polygons, and rebuild with generated zones enabled. |
| A hand-made overlay does not appear. | The current build does not apply `Source overlay` to exported Terrain3D data. Leave the field blank and paint fine details with Terrain3D tools. |
| Lidar terrain is incomplete. | Download every intersecting LAZ tile, merge them, clip the merged file, inspect it with `pdal info --summary`, and rebuild. |
| `Open scene` does nothing. | Click `Export course` first and inspect the Godot Output panel for an error. |
| A course does not appear in the selector. | Confirm that both `course.json` and `course.tscn` exist in the course folder. |

## Sources
- Godot C#/.NET editor: https://docs.godotengine.org/en/4.6/tutorials/scripting/c_sharp/index.html
- Godot downloads: https://godotengine.org/download
- .NET 9 downloads: https://dotnet.microsoft.com/download/dotnet/9.0
- Terrain3D documentation: https://terrain3d.readthedocs.io/
- QGIS installation guide: https://qgis.org/resources/installation-guide/
- QGIS vector layer export: https://docs.qgis.org/latest/en/docs/user_manual/managing_data_source/create_layers.html#creating-new-layers-from-an-existing-layer
- QGIS raster clipping: https://docs.qgis.org/latest/en/docs/user_manual/processing_algs/gdal/rasterextraction.html#clip-raster-by-mask-layer
- QGIS XYZ tile layers: https://docs.qgis.org/latest/en/docs/user_manual/managing_data_source/opening_data.html#using-xyz-tile-services
- Conda installation guide: https://docs.conda.io/projects/conda/en/stable/user-guide/install/
- GDAL downloads: https://gdal.org/en/latest/download.html
- GDAL `gdal raster fill-nodata`: https://gdal.org/en/stable/programs/gdal_raster_fill_nodata.html
- GDAL `gdal_translate`: https://gdal.org/en/stable/programs/gdal_translate.html
- GDAL `gdalwarp`: https://gdal.org/en/stable/programs/gdalwarp.html
- GDAL `gdal_rasterize`: https://gdal.org/en/stable/programs/gdal_rasterize.html
- PDAL quickstart: https://pdal.io/en/stable/quickstart.html
- PDAL `writers.gdal`: https://pdal.io/en/stable/stages/writers.gdal.html
- PDAL reprojection filter: https://pdal.io/en/stable/stages/filters.reprojection.html
- USGS 3DEP: https://www.usgs.gov/3DEP
- USGS The National Map applications: https://apps.nationalmap.gov/
- USGS The National Map Downloader: https://apps.nationalmap.gov/downloader/
- USGS lidar explorer: https://apps.nationalmap.gov/lidar-explorer/
- USGS TNM Access API docs: https://tnmaccess.nationalmap.gov/api/v1/docs
- OpenStreetMap golf course tag: https://wiki.openstreetmap.org/wiki/Tag:leisure%3Dgolf_course
- OpenStreetMap hole tag: https://wiki.openstreetmap.org/wiki/Tag:golf%3Dhole
- OpenStreetMap pin tag: https://wiki.openstreetmap.org/wiki/Tag:golf%3Dpin
- OpenStreetMap route tag: https://wiki.openstreetmap.org/wiki/Tag:route%3Dgolf
- OpenStreetMap API v0.6: https://wiki.openstreetmap.org/wiki/API_v0.6
- Overpass Turbo: https://overpass-turbo.eu/
- Overpass Turbo exports: https://wiki.openstreetmap.org/wiki/Overpass-Turbo
- Overpass Turbo GeoJSON format: https://wiki.openstreetmap.org/wiki/Overpass_turbo/GeoJSON
- OpenTopography getting started: https://www.opentopography.org/start
- OpenTopography developer APIs: https://opentopography.org/developers
- OpenTopography USGS DEM API notes: https://opentopography.org/news/api-access-usgs-3dep-rasters-now-available
- Airways Golf Course: https://airways.golf/course-details/
- Airways FAQ: https://airways.golf/faqs-airways-golf-course/
- Airways scorecard reference: https://www.golflink.com/golf-courses/ca/fresno/airways-golf-course
