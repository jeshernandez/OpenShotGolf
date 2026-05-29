# Airways Source Data

These files are public design inputs for the Airways Golf Course prototype.

## Files
- `airways_boundary.geojson`: Course boundary from OpenStreetMap way `42013666`.
- `airways_holes.geojson`: Eighteen `golf=hole` OSM way features around Airways, converted to GeoJSON with top-level `ref`, `name`, and `par` fields.
- `airways_pins.geojson`: Empty OSM pin query result. No `golf=pin` points were found in the fetched OSM data.
- `airways_dem.tif`: Cropped 1 meter USGS DEM for the course boundary, reprojected to `EPSG:26911`.
- `airways_lidar.laz`: Full merged LAZ from the four USGS Fresno 2019 lidar tiles that cover the course.
- `airways_lidar_clipped.laz`: Smaller LAZ clipped to the DEM extent. Use this file first in the plugin.
- `airways_osm_way_42013666.osm`: Raw OSM boundary source.
- `airways_osm_golf_features.osm`: Raw OSM hole query source.
- `usgs_dem_products.json`: USGS product search response used to choose the DEM tile.
- `usgs_lidar_products.json`: USGS product search response used to choose the lidar tiles.
- `lidar/*.laz`: Raw USGS Fresno 2019 lidar tiles.
- `lidar/*.retry.laz`: Retry copy of a raw tile if the first download attempt timed out. It is kept so no downloaded source data is discarded.

## Commands Used
Run these from this `source_data` folder:

```bash
curl -L -o airways_osm_way_42013666.osm https://www.openstreetmap.org/api/0.6/way/42013666/full
ogr2ogr -f GeoJSON airways_boundary.geojson airways_osm_way_42013666.osm multipolygons
curl -A OpenShotGolfCourseDesign/1.0 -X POST --data-urlencode 'data=[out:xml][timeout:25];(way(around:1200,36.77724,-119.70619)["golf"="hole"];relation(around:1200,36.77724,-119.70619)["route"="golf"];node(around:1200,36.77724,-119.70619)["golf"="pin"];);out body;>;out skel qt;' -o airways_osm_golf_features.osm https://overpass-api.de/api/interpreter
OSM_USE_CUSTOM_INDEXING=NO ogr2ogr -f GeoJSON /tmp/airways_holes_raw.geojson airways_osm_golf_features.osm lines
jq '.name="airways_holes" | .features |= map(.properties += {golf:"hole", ref: ((try (.properties.other_tags | capture("\\"ref\\"=>\\"(?<v>[^\\"]+)\\"").v) catch "")), par: ((try (.properties.other_tags | capture("\\"par\\"=>\\"(?<v>[^\\"]+)\\"").v | tonumber) catch null))} | .properties.name = ("Hole " + .properties.ref)) | .features |= sort_by(.properties.ref | tonumber)' /tmp/airways_holes_raw.geojson > airways_holes.geojson
ogr2ogr -f GeoJSON airways_pins.geojson airways_osm_golf_features.osm points
curl -L -o usgs_dem_products.json 'https://tnmaccess.nationalmap.gov/api/v1/products?datasets=Digital%20Elevation%20Model%20%28DEM%29%201%20meter&bbox=-119.712,36.774,-119.701,36.781&prodFormats=GeoTIFF&outputFormat=JSON'
gdalwarp -overwrite -r bilinear -cutline airways_boundary.geojson -crop_to_cutline -dstnodata -9999 -t_srs EPSG:26911 -tr 1 1 /vsicurl/https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/1m/Projects/CA_SanJoaquin_2021_A21/TIFF/USGS_1M_11_x25y408_CA_SanJoaquin_2021_A21.tif airways_dem.tif
gdalinfo -stats airways_dem.tif
mkdir -p lidar
curl -L -o usgs_lidar_products.json 'https://tnmaccess.nationalmap.gov/api/v1/products?datasets=Lidar%20Point%20Cloud%20%28LPC%29&bbox=-119.712,36.774,-119.701,36.781&outputFormat=JSON'
curl -L --connect-timeout 10 --max-time 120 --retry 3 --speed-limit 1024 --speed-time 15 -o lidar/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA570730.laz https://rockyweb.usgs.gov/vdelivery/Datasets/Staged/Elevation/LPC/Projects/CA_FEMAR9Fresno_2019_D20/CA_FEMAR9Fresno_2_2019/LAZ/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA570730.laz
curl -L --connect-timeout 10 --max-time 120 --retry 3 --speed-limit 1024 --speed-time 15 -o lidar/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA570740.laz https://rockyweb.usgs.gov/vdelivery/Datasets/Staged/Elevation/LPC/Projects/CA_FEMAR9Fresno_2019_D20/CA_FEMAR9Fresno_2_2019/LAZ/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA570740.laz
curl -L --connect-timeout 10 --max-time 120 --retry 3 --speed-limit 1024 --speed-time 15 -o lidar/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA580730.laz https://rockyweb.usgs.gov/vdelivery/Datasets/Staged/Elevation/LPC/Projects/CA_FEMAR9Fresno_2019_D20/CA_FEMAR9Fresno_2_2019/LAZ/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA580730.laz
curl -L --connect-timeout 10 --max-time 120 --retry 3 --speed-limit 1024 --speed-time 15 -o lidar/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA580740.laz https://rockyweb.usgs.gov/vdelivery/Datasets/Staged/Elevation/LPC/Projects/CA_FEMAR9Fresno_2019_D20/CA_FEMAR9Fresno_2_2019/LAZ/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA580740.laz
pdal merge lidar/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA570730.laz lidar/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA570740.laz lidar/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA580730.laz lidar/USGS_LPC_CA_FEMAR9Fresno_2019_D20_11SKA580740.laz airways_lidar.laz
pdal translate airways_lidar.laz airways_lidar_clipped.laz crop --filters.crop.bounds="([258023.64,258955.64],[4073279.48,4073889.48])"
pdal info --summary airways_lidar_clipped.laz
```

## Notes
- OpenStreetMap data is available under the Open Database License.
- The DEM source is USGS 3DEP 1 meter data from the `CA_SanJoaquin_2021_A21` product.
- The lidar source is USGS 3DEP data from the `CA_FEMAR9Fresno_2019_D20` product.
- `airways_dem.tif` is `932 x 610` pixels at 1 meter resolution.
- DEM elevation stats are roughly min `100.886 m`, max `104.154 m`, mean `102.158 m`.
- `airways_lidar_clipped.laz` contains about `2,816,188` points and covers the DEM extent.
- The hole line data should be reviewed in QGIS or Godot because OSM hole lines are map approximations, not surveyed tee and pin points.
