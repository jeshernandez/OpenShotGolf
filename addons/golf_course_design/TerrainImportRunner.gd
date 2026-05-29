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
