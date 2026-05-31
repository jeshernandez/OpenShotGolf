class_name CourseValidator

const COURSE_CONFIG_FILE := "course.json"
const COURSE_SCENE_FILE := "course.tscn"
const COURSE_TITLE_KEY := "Title"
const COURSE_INFO_KEY := "Course Info"
const TEE_COLORS_KEY := "Tee Colors"
const DEFAULT_TEE_COLORS: Array[String] = ["Black", "Blue", "White", "Red"]


## Validates a single course directory. Returns a dictionary with "title",
## "scene_path", "config_path", and "tee_colors" on success, or an empty dictionary on failure.
static func validate(course_dir: String, dir_name: String) -> Dictionary:
	var config_path := "%s/%s/%s" % [course_dir, dir_name, COURSE_CONFIG_FILE]

	if not FileAccess.file_exists(config_path):
		printerr("[CourseValidator] Missing %s for course '%s'." % [COURSE_CONFIG_FILE, dir_name])
		return {}

	var file := FileAccess.open(config_path, FileAccess.READ)
	if file == null:
		printerr("[CourseValidator] Unable to read course config: %s" % config_path)
		return {}

	var json_text := file.get_as_text()
	var parsed = JSON.parse_string(json_text)
	if typeof(parsed) != TYPE_DICTIONARY:
		printerr("[CourseValidator] Invalid JSON in %s." % config_path)
		return {}

	var title := _extract_title(parsed, dir_name)
	var scene_path := "%s/%s/%s" % [course_dir, dir_name, COURSE_SCENE_FILE]
	if not FileAccess.file_exists(scene_path):
		printerr("[CourseValidator] Missing %s for course '%s'." % [COURSE_SCENE_FILE, dir_name])
		return {}

	var tee_colors := _extract_tee_colors(parsed)
	return { "title": title, "scene_path": scene_path, "config_path": config_path, "tee_colors": tee_colors }


static func _extract_tee_colors(parsed: Dictionary) -> Array[String]:
	var info = parsed.get(COURSE_INFO_KEY, {})
	if typeof(info) == TYPE_DICTIONARY:
		var colors = info.get(TEE_COLORS_KEY, null)
		if typeof(colors) == TYPE_ARRAY and not colors.is_empty():
			var result: Array[String] = []
			for c in colors:
				if typeof(c) == TYPE_STRING:
					result.append(c)
			if not result.is_empty():
				return result
	return DEFAULT_TEE_COLORS.duplicate()


static func _extract_title(parsed: Dictionary, dir_name: String) -> String:
	var top_level_title = parsed.get(COURSE_TITLE_KEY, "")
	if typeof(top_level_title) == TYPE_STRING:
		var normalized := String(top_level_title).strip_edges()
		if not normalized.is_empty():
			return normalized

	var course_info = parsed.get(COURSE_INFO_KEY, {})
	if typeof(course_info) == TYPE_DICTIONARY:
		var legacy_title = course_info.get(COURSE_TITLE_KEY, "")
		if typeof(legacy_title) == TYPE_STRING:
			var normalized_legacy := String(legacy_title).strip_edges()
			if not normalized_legacy.is_empty():
				return normalized_legacy

	return dir_name


