@tool
extends Node


func import_to_terrain(height_image: String, color_image: String, destination_directory: String, import_scale: float, height_offset: float) -> bool:
	if destination_directory.is_empty():
		push_error("Set a destination directory first.")
		return false

	# Terrain3D's colour map is RGBA8 (RGB = albedo, A = roughness). A 3-channel RGB image is
	# read with the wrong byte stride and renders as per-texel rainbow noise, so normalise the
	# colour image to RGBA8 on disk before importing.
	if not color_image.is_empty():
		_ensure_rgba8_image(color_image)

	var importer_scene: PackedScene = load("res://addons/terrain_3d/tools/importer.tscn")
	if importer_scene == null:
		push_error("Could not load Terrain3D importer scene.")
		return false

	var importer := importer_scene.instantiate()
	if importer == null:
		push_error("Could not create Terrain3D importer.")
		return false

	add_child(importer)
	importer.set("clear_terrain", true)
	importer.set("height_file_name", height_image)
	importer.set("control_file_name", "")
	importer.set("color_file_name", color_image)
	importer.set("destination_directory", destination_directory)
	importer.set("import_scale", import_scale)
	importer.set("height_offset", height_offset)
	importer.set("run_import", true)
	importer.set("save_to_disk", true)
	remove_child(importer)
	importer.queue_free()
	if not DirAccess.dir_exists_absolute(destination_directory):
		push_error("Terrain3D did not create the destination directory.")
		return false
	if not _directory_has_files(destination_directory):
		push_error("Terrain3D did not write any terrain files.")
		return false
	return true


# Reads overlay_class_ids.png (a grayscale byte image where each pixel = class ID) and paints
# the Terrain3D control map in the destination directory so each zone gets the correct texture
# slot. Must be called after import_to_terrain() has written the terrain .res files.
#
# Implementation note: we edit the region ".res" files DIRECTLY via ResourceLoader/ResourceSaver
# rather than through a live Terrain3D node. A node loads its region data asynchronously (only
# available a couple of frames after `data_directory` is set), so the previous node-based version
# always saw `region_locations` empty during this synchronous call and silently painted nothing —
# leaving every pixel at base texture 0. Direct .res editing is synchronous and the saved control
# maps are picked up correctly when the course scene loads.
#
# The height raster, the class raster, and the Terrain3D vertex/control grid are all the same
# resolution and origin (1 pixel = 1 vertex, imported at (0,0)), so a control pixel at global
# coordinate (loc * region_size + local) maps 1:1 to the same class-image pixel. The raster bounds
# and import_scale args are therefore no longer needed for the mapping (import_scale only affects
# height), but are kept in the signature for call-site compatibility.
#
# Class-to-control mapping (textures only — the full terrain stays present and paintable):
#   0 (background/off-course/padding) → base slot 2 (Rough)
#   1–64      → base slot 1 (Fairway — hole corridors)
#   200 (tee) → base slot 0 (Green)
#   201 (green disc) → base slot 0 (Green)
#   202 (bunker) → base slot 3 (Sand)
#   203 (outline) → base slot 1 (Fairway — margin ring around corridors)
#   anything else → base slot 2 (Rough)
func paint_control_map(
	class_image_path: String,
	destination_directory: String,
	_raster_min_x: float,
	_raster_min_y: float,
	_raster_max_x: float,
	_raster_max_y: float,
	_import_scale: float,
	blend_radius_pixels: int = 3
) -> bool:
	if class_image_path.is_empty() or destination_directory.is_empty():
		push_error("TerrainImportRunner: Missing class_image_path or destination_directory.")
		return false
	if not FileAccess.file_exists(class_image_path):
		push_error("TerrainImportRunner: Class image not found: " + class_image_path)
		return false

	var class_img: Image = Image.load_from_file(class_image_path)
	if class_img == null:
		push_error("TerrainImportRunner: Failed to load class image: " + class_image_path)
		return false

	class_img.convert(Image.FORMAT_L8)
	var class_bytes: PackedByteArray = class_img.get_data()
	var img_w: int = class_img.get_width()
	var img_h: int = class_img.get_height()
	var blend_radius: int = clampi(blend_radius_pixels, 1, 12)

	# Terrain3D needs a res:// path (or absolute within the project) for data_directory.
	var res_dir: String = ProjectSettings.localize_path(destination_directory)
	if res_dir.is_empty():
		res_dir = destination_directory

	var dir := DirAccess.open(res_dir)
	if dir == null:
		push_error("TerrainImportRunner: Cannot open terrain directory: " + res_dir)
		return false

	var painted_regions: int = 0
	for file_name in dir.get_files():
		if not file_name.ends_with(".res") or not file_name.begins_with("terrain3d_"):
			continue
		var region_loc: Vector2i = Terrain3DUtil.filename_to_location(file_name)
		var region_path: String = res_dir.path_join(file_name)
		var region: Resource = ResourceLoader.load(region_path, "", ResourceLoader.CACHE_MODE_IGNORE)
		if region == null:
			push_warning("TerrainImportRunner: Failed to load region: " + region_path)
			continue
		var ctrl_img: Image = region.get("control_map")
		if ctrl_img == null:
			continue

		var ctrl_w: int = ctrl_img.get_width()
		var ctrl_h: int = ctrl_img.get_height()
		var region_size: int = ctrl_w  # control map is region_size x region_size
		var region_changed: bool = false

		for ctrl_row in range(ctrl_h):
			var class_row: int = region_loc.y * region_size + ctrl_row
			var row_in_bounds: bool = class_row >= 0 and class_row < img_h
			var row_offset: int = class_row * img_w if row_in_bounds else 0
			for ctrl_col in range(ctrl_w):
				var class_col: int = region_loc.x * region_size + ctrl_col
				# Out-of-bounds = padding beyond the DEM; treat as background (class 0 → Rough).
				var class_id: int = 0
				if row_in_bounds and class_col >= 0 and class_col < img_w:
					class_id = class_bytes[row_offset + class_col]

				var packed_int: int = _encode_control_for_pixel(
					class_bytes,
					img_w,
					img_h,
					class_col,
					class_row,
					class_id,
					blend_radius
				)

				# Bitcast the uint32 control value to a float so it can be stored in FORMAT_RF.
				var buf := PackedByteArray([0, 0, 0, 0])
				buf.encode_u32(0, packed_int)
				ctrl_img.set_pixel(ctrl_col, ctrl_row, Color(buf.decode_float(0), 0.0, 0.0, 1.0))
				region_changed = true

		if region_changed:
			region.set("control_map", ctrl_img)
			region.set("edited", true)
			var err: int = ResourceSaver.save(region, region_path)
			if err != OK:
				push_error("TerrainImportRunner: Failed to save region %s (err %d)" % [region_path, err])
				return false
			painted_regions += 1

	if painted_regions == 0:
		push_warning("TerrainImportRunner: paint_control_map painted no regions (check class image / alignment).")
	else:
		print("TerrainImportRunner: painted control map across %d region(s)." % painted_regions)
	return true


# Codifies a Terrain3D texture asset's material settings in the shared assets resource so the
# Golf Course Design pipeline can guarantee them in code instead of relying on a hand-edited
# .tres. The texture "slots" map to the same indices paint_control_map() paints (0=Green,
# 1=Fairway, 2=Rough, 3=Sand). Loads with the default cache mode (REUSE) so the editor's live
# resource updates immediately, then persists to disk. Idempotent: re-running with the same
# values is a no-op on the rendered result. Returns false (with a pushed error) on failure.
func apply_texture_asset_settings(
	assets_path: String,
	texture_slot: int,
	normal_depth: float,
	ao_strength: float,
	roughness: float,
	uv_scale: float,
	detiling_rotation: float,
	detiling_shift: float
) -> bool:
	var assets: Resource = ResourceLoader.load(assets_path)
	if assets == null:
		push_error("apply_texture_asset_settings: cannot load " + assets_path)
		return false
	var texture: Resource = assets.get_texture(texture_slot)
	if texture == null:
		push_error("apply_texture_asset_settings: no texture at slot %d in %s" % [texture_slot, assets_path])
		return false

	texture.set("normal_depth", normal_depth)
	texture.set("ao_strength", ao_strength)
	texture.set("roughness", roughness)
	texture.set("uv_scale", uv_scale)
	texture.set("detiling_rotation", detiling_rotation)
	texture.set("detiling_shift", detiling_shift)

	var err: int = ResourceSaver.save(assets, assets_path)
	if err != OK:
		push_error("apply_texture_asset_settings: failed to save %s (err %d)" % [assets_path, err])
		return false
	print("apply_texture_asset_settings: applied settings to texture slot %d in %s" % [texture_slot, assets_path])
	return true


func _class_to_texture_slot(class_id: int) -> int:
	if class_id >= 1 and class_id <= 64:
		return 1  # Fairway
	match class_id:
		200: return 0  # Green (tee disc)
		201: return 0  # Green (putting green disc)
		202: return 3  # Sand (bunker)
		203: return 1  # Fairway (outline ring)
		_: return 2   # Rough (background and anything unrecognised)


func _encode_control_for_pixel(
	class_bytes: PackedByteArray,
	img_w: int,
	img_h: int,
	x: int,
	y: int,
	class_id: int,
	blend_radius: int
) -> int:
	var texture_slot: int = _class_to_texture_slot(class_id)
	var nearest := _find_nearest_other_texture_slot(class_bytes, img_w, img_h, x, y, texture_slot, blend_radius)
	if nearest.x < 0:
		return Terrain3DUtil.enc_base(texture_slot)

	var base_slot: int = texture_slot
	var overlay_slot: int = nearest.x
	if _texture_priority(texture_slot) > _texture_priority(nearest.x):
		base_slot = nearest.x
		overlay_slot = texture_slot

	var blend: int = _edge_blend_value(texture_slot, overlay_slot, nearest.y, blend_radius)
	return _encode_blended_control(base_slot, overlay_slot, blend)


func _find_nearest_other_texture_slot(
	class_bytes: PackedByteArray,
	img_w: int,
	img_h: int,
	x: int,
	y: int,
	texture_slot: int,
	blend_radius: int
) -> Vector2i:
	for distance in range(1, blend_radius + 1):
		var left_slot := _sample_texture_slot(class_bytes, img_w, img_h, x - distance, y)
		if left_slot >= 0 and left_slot != texture_slot:
			return Vector2i(left_slot, distance)
		var right_slot := _sample_texture_slot(class_bytes, img_w, img_h, x + distance, y)
		if right_slot >= 0 and right_slot != texture_slot:
			return Vector2i(right_slot, distance)
		var up_slot := _sample_texture_slot(class_bytes, img_w, img_h, x, y - distance)
		if up_slot >= 0 and up_slot != texture_slot:
			return Vector2i(up_slot, distance)
		var down_slot := _sample_texture_slot(class_bytes, img_w, img_h, x, y + distance)
		if down_slot >= 0 and down_slot != texture_slot:
			return Vector2i(down_slot, distance)
	return Vector2i(-1, 0)


func _sample_texture_slot(class_bytes: PackedByteArray, img_w: int, img_h: int, x: int, y: int) -> int:
	if x < 0 or x >= img_w or y < 0 or y >= img_h:
		return -1
	var class_id: int = class_bytes[y * img_w + x]
	return _class_to_texture_slot(class_id)


func _texture_priority(texture_slot: int) -> int:
	match texture_slot:
		2:
			return 0  # Rough sits under most playable surfaces.
		1:
			return 1  # Fairway overlays rough.
		0:
			return 2  # Greens and tees overlay grass surfaces.
		3:
			return 3  # Sand overlays adjacent turf.
		_:
			return 0


func _edge_blend_value(texture_slot: int, overlay_slot: int, distance: int, blend_radius: int) -> int:
	var radius_span: int = maxi(blend_radius - 1, 1)
	var t: float = clampf(float(distance - 1) / float(radius_span), 0.0, 1.0)
	if texture_slot == overlay_slot:
		return clampi(roundi(lerpf(168.0, 255.0, t)), 0, 255)
	return clampi(roundi(lerpf(87.0, 0.0, t)), 0, 255)


func _encode_blended_control(base_slot: int, overlay_slot: int, blend: int) -> int:
	return ((base_slot & 0x1F) << 27) | ((overlay_slot & 0x1F) << 22) | ((blend & 0xFF) << 14)


func _ensure_rgba8_image(image_path: String) -> void:
	if not FileAccess.file_exists(image_path):
		return
	var image := Image.load_from_file(image_path)
	if image == null:
		push_warning("Could not load colour image for RGBA8 normalisation: " + image_path)
		return
	if image.get_format() == Image.FORMAT_RGBA8:
		return
	image.convert(Image.FORMAT_RGBA8)
	var error := image.save_png(image_path)
	if error != OK:
		push_warning("Could not re-save colour image as RGBA8: " + image_path)


func _directory_has_files(directory_path: String) -> bool:
	var directory := DirAccess.open(directory_path)
	if directory == null:
		return false
	directory.list_dir_begin()
	var entry := directory.get_next()
	while not entry.is_empty():
		if entry != "." and entry != "..":
			var child_path := directory_path.path_join(entry)
			if directory.current_is_dir():
				if _directory_has_files(child_path):
					directory.list_dir_end()
					return true
			else:
				directory.list_dir_end()
				return true
		entry = directory.get_next()
	directory.list_dir_end()
	return false
