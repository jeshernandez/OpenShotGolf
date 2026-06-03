# Hunter Ranch Source Data

These files are public design inputs for the Hunter Ranch course prototype.

## Course
- Course: Hunter Ranch Golf Course
- Address: 4041 Highway 46 East, Paso Robles, CA 93446
- Public scorecard reference: https://www.hunterranchgolf.com/golf-course/scorecard
- Public par reference used for cleanup: https://www.golfify.io/courses/hunter-ranch-golf-course

## Files
- `hunter_ranch_boundary.geojson`: One OpenStreetMap `leisure=golf_course` polygon for Hunter Ranch Golf Course.
- `hunter_ranch_osm_golf_features.geojson`: Original mixed OpenStreetMap export. It includes fairways, greens, bunkers, and hole lines.
- `hunter_ranch_osm_holes_overpass.json`: Raw Overpass API response used to recover the missing hole 3 line.
- `hunter_ranch_holes.geojson`: Clean holes-only file with 18 `golf=hole` LineString features, refs `1` through `18`, names, and par values.
- `hunter_ranch_bunkers.geojson`: Clean bunkers-only file with 60 bunker polygons.
- `usgs_dem_products.json`: USGS TNM Access API product search response for one-meter DEM candidates.
- `usgs_lidar_products.json`: USGS TNM Access API product search response for optional lidar candidates. Lidar was not used.
- `hunter_ranch_dem.tif`: Cropped 1 meter USGS DEM in `EPSG:26910`, built from the `CA_AZ_FEMA_R9_Lidar_2017_D18` GeoTIFF product.

## Commands Used
Run these from this `source_data` folder.

```bash
curl -A 'OpenShotGolfCourseDesign/1.0' -X POST --data-urlencode 'data=[out:json][timeout:25];(way(around:1500,35.64884765,-120.6210249)["golf"="hole"];);out geom;' -o hunter_ranch_osm_holes_overpass.json 'https://overpass-api.de/api/interpreter'
curl -L --get --data-urlencode 'datasets=Digital Elevation Model (DEM) 1 meter' --data-urlencode 'bbox=-120.629,35.643,-120.613,35.654' --data-urlencode 'prodFormats=GeoTIFF' --data-urlencode 'outputFormat=JSON' -o usgs_dem_products.json 'https://tnmaccess.nationalmap.gov/api/v1/products'
curl -L --get --data-urlencode 'datasets=Lidar Point Cloud (LPC)' --data-urlencode 'bbox=-120.629,35.643,-120.613,35.654' --data-urlencode 'outputFormat=JSON' -o usgs_lidar_products.json 'https://tnmaccess.nationalmap.gov/api/v1/products'
gdalwarp -overwrite -r bilinear -cutline hunter_ranch_boundary.geojson -crop_to_cutline -dstnodata -9999 -t_srs EPSG:26910 -tr 1 1 /vsicurl/https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/1m/Projects/CA_AZ_FEMA_R9_Lidar_2017_D18/TIFF/USGS_1M_10_x71y395_CA_AZ_FEMA_R9_Lidar_2017_D18.tif hunter_ranch_dem.tif
gdalinfo -stats hunter_ranch_dem.tif
```

## Required And Optional Steps
- README workflow coverage:
  - Chosen workflow: `Heightmap`.
  - Required game files were produced: `course.gd`, `course.json`, `course.tscn`, and `Terrain/`.
  - Editable dock project was produced: `course_design.tres`.
  - Source notes were produced here in `source_data/SOURCES.md`.
  - Boundary, holes, bunkers, DEM, generated terrain zones, export, and preview validation steps were covered.
- The game requires `course.gd`, `course.json`, `course.tscn`, and `Terrain/`.
- The course-design dock requires `course_design.tres` when editing or rebuilding the course.
- `source_data/` is not loaded by the game. It is kept so the course can be audited and rebuilt.
- API calls are optional. Overpass Turbo, the USGS browser downloader, and QGIS can produce equivalent files manually.
- No external API is absolutely required once the source GeoJSON and DEM files exist.
- For the chosen heightmap workflow, GDAL/OGR processing and the Godot Terrain3D import are required. These cannot be replaced by only hand-editing text files because Terrain3D region resources must be generated from raster data.
- `gdaltransform` must be available when importing holes from a georeferenced heightmap in the dock.
- Lidar and PDAL are optional for this course. The DEM workflow was used instead.
- OpenTopography was not used. If used later, keep API keys private and do not commit them.

## Notes
- OpenStreetMap data is available under the Open Database License.
- The original mixed OSM export had a duplicate `ref=12` hole line and was missing `ref=3`.
- A fresh Overpass hole query returned way `1316500996` for `ref=3`.
- Duplicate way `1364624718` was excluded from the clean holes file.
- The first USGS DEM candidate, `CA_FEMAR9Estrella_2019_D20`, covered the area by product footprint but produced no valid pixels over this course. The `CA_AZ_FEMA_R9_Lidar_2017_D18` DEM was used because it produced valid elevation data.
- `hunter_ranch_dem.tif` is `1160 x 963` pixels at 1 meter resolution. Elevation stats are roughly min `244.786 m`, max `284.669 m`, mean `263.272 m`.
- Imported tee positions currently seed Black, Blue, White, and Red at the same OSM tee-line start. Adjust individual tee boxes by hand in the Godot dock when more precise tee markers are available.
