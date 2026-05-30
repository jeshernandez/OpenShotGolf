extends "res://Courses/Range/range.gd"
class_name CoursePlay

const CameraControllerScript := preload("res://Courses/_shared/course_shot_camera_controller.gd")

const COURSE_INFO_KEY := "Course Info"
const HOLE_INFO_KEY := "Hole Info"
const DEFAULT_TEE_COLOR := "White"
const HOLE_MARKERS_PATH := "HoleMarkers"
const BALL_START_HEIGHT := 0.02
const COMPLETION_DISTANCE_FEET := 20.0
const FEET_PER_METER := 3.28084
const DEFAULT_CAMERA_ORBIT_DISTANCE := 2.1336
const DEFAULT_CAMERA_FOLLOW_DELAY_SECONDS := 0.0
const MIN_DIRECTION_LENGTH := 0.000001

var _course_config: Dictionary = {}
var _course_config_path := ""
var _hole_info: Dictionary = {}
var _hole_numbers: Array[int] = []
var _current_hole_index := 0
var _current_hole_number := 1
var _course_ready := false
var _active_tee_position := Vector3.ZERO
var _active_flag_position := Vector3.ZERO
var _active_target_direction := Vector3.RIGHT
var _shot_camera = null
var _rest_sequence := 0


func set_course_config(config: Dictionary, config_path: String = "") -> void:
	_course_config = config.duplicate(true)
	_course_config_path = config_path


func _ready() -> void:
	super._ready()
	_shot_camera = CameraControllerScript.new()
	_shot_camera.configure(self, $PhantomCamera3D, $Player.ball)
	_configure_ball_for_course()
	call_deferred("_initialize_course_deferred")


func _initialize_course_deferred() -> void:
	await get_tree().process_frame
	_ensure_course_config()
	_load_holes_from_config()
	_start_hole_by_number(1)


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("reset"):
		_reset_display_data()
		$RangeUI.set_data(display_data)
		_start_hole_by_index(_current_hole_index)


func _on_tcp_client_hit_ball(data: Dictionary) -> void:
	raw_ball_data = data.duplicate()
	_update_ball_display()
	_prepare_course_shot_start(data)


func _on_range_ui_hit_shot(data: Dictionary) -> void:
	raw_ball_data = data.duplicate()
	_update_ball_display()
	_prepare_course_shot_start(data)


func _on_player_manual_hit() -> void:
	_prepare_course_shot_start({})


func _on_golf_ball_rest(ball_data: Dictionary) -> void:
	raw_ball_data = ball_data.duplicate()
	_update_ball_display()
	if _shot_camera != null:
		_shot_camera.freeze_on_ball()

	_rest_sequence += 1
	var sequence := _rest_sequence
	var delay := float(GlobalSettings.range_settings.ball_reset_timer.value)
	if delay > 0.0:
		await get_tree().create_timer(delay).timeout
		if sequence != _rest_sequence:
			return

	if _is_hole_complete():
		_advance_to_next_hole()
		return

	if _shot_camera != null and bool(GlobalSettings.range_settings.camera_follow_mode.value):
		_apply_camera_settings()
		_shot_camera.reset_to_ball(_get_ball_global_position(), _active_flag_position)


func set_camera_follow_mode(value) -> void:
	if _shot_camera == null:
		return
	if not bool(value):
		_shot_camera.disable_follow()


func _prepare_course_shot_start(data: Dictionary) -> void:
	if not _course_ready:
		_start_hole_by_number(1)

	_rest_sequence += 1
	_configure_ball_for_course()
	_apply_camera_settings()

	var ball = $Player.ball
	if ball == null:
		return

	var aim_offset: float = _shot_camera.get_aim_yaw_offset_deg() if _shot_camera != null else _get_target_yaw_offset()
	ball.aim_yaw_offset_deg = aim_offset
	var hla := float(data.get("HLA", 0.0))
	var follow_direction := _active_target_direction
	if _shot_camera != null:
		follow_direction = _shot_camera.get_launch_follow_direction(hla, aim_offset, _active_target_direction)
		_shot_camera.begin_shot_launch()
		if bool(GlobalSettings.range_settings.camera_follow_mode.value):
			_shot_camera.enable_follow_after_launch(follow_direction, _get_camera_follow_delay_seconds())


func _configure_ball_for_course() -> void:
	if $Player.ball == null:
		return
	$Player.ball.reset_position_on_hit = false


func _start_hole_by_number(hole_number: int) -> void:
	if _hole_numbers.is_empty():
		push_warning("Course has no playable holes.")
		return
	var index := _hole_numbers.find(hole_number)
	if index < 0:
		index = 0
	_start_hole_by_index(index)


func _start_hole_by_index(index: int) -> void:
	if _hole_numbers.is_empty():
		push_warning("Course has no playable holes.")
		return

	_rest_sequence += 1
	_current_hole_index = clampi(index, 0, _hole_numbers.size() - 1)
	_current_hole_number = _hole_numbers[_current_hole_index]

	var hole_data := _get_current_hole_data()
	_active_tee_position = _resolve_tee_position(hole_data)
	_active_flag_position = _resolve_flag_position(hole_data, _active_tee_position)
	_active_target_direction = _flat_direction(_active_tee_position, _active_flag_position)
	_course_ready = true

	_position_player_at_tee()
	_apply_camera_settings()
	if _shot_camera != null:
		_shot_camera.set_to_start_immediate(_get_ball_global_position(), _active_flag_position)
	print("Starting Hole %d" % _current_hole_number)


func _position_player_at_tee() -> void:
	_configure_ball_for_course()
	$Player.global_position = _active_tee_position
	if $Player.has_method("reset_ball"):
		$Player.reset_ball(false)
	elif $Player.ball != null:
		$Player.ball.reset()


func _advance_to_next_hole() -> void:
	var completed_hole := _current_hole_number
	if _current_hole_index >= _hole_numbers.size() - 1:
		print("Hole %d complete. Course complete." % completed_hole)
		_current_hole_index = 0
	else:
		print("Hole %d complete. Moving to Hole %d." % [completed_hole, _hole_numbers[_current_hole_index + 1]])
		_current_hole_index += 1
	_reset_display_data()
	$RangeUI.set_data(display_data)
	_start_hole_by_index(_current_hole_index)


func _is_hole_complete() -> bool:
	var ball_position := _get_ball_global_position()
	var ball_xz := Vector2(ball_position.x, ball_position.z)
	var flag_xz := Vector2(_active_flag_position.x, _active_flag_position.z)
	var completion_meters := COMPLETION_DISTANCE_FEET / FEET_PER_METER
	return ball_xz.distance_to(flag_xz) <= completion_meters


func _apply_camera_settings() -> void:
	if _shot_camera == null:
		return
	_shot_camera.set_orbit_radius(_get_camera_orbit_distance())


func _get_camera_orbit_distance() -> float:
	if GlobalSettings == null or GlobalSettings.app_settings == null:
		return DEFAULT_CAMERA_ORBIT_DISTANCE
	return float(GlobalSettings.app_settings.camera_orbit_distance.value)


func _get_camera_follow_delay_seconds() -> float:
	if GlobalSettings == null or GlobalSettings.app_settings == null:
		return DEFAULT_CAMERA_FOLLOW_DELAY_SECONDS
	return float(GlobalSettings.app_settings.camera_follow_delay_seconds.value)


func _get_target_yaw_offset() -> float:
	return -rad_to_deg(atan2(_active_target_direction.z, _active_target_direction.x))


func _get_ball_global_position() -> Vector3:
	if $Player.ball == null:
		return _active_tee_position + Vector3.UP * BALL_START_HEIGHT
	return $Player.ball.global_position


func _ensure_course_config() -> void:
	if not _course_config.is_empty():
		return
	var config_path := _course_config_path
	if config_path.is_empty() and not scene_file_path.is_empty():
		config_path = scene_file_path.get_base_dir().path_join("course.json")
	if config_path.is_empty() or not FileAccess.file_exists(config_path):
		return
	var file := FileAccess.open(config_path, FileAccess.READ)
	if file == null:
		return
	var parsed = JSON.parse_string(file.get_as_text())
	if typeof(parsed) == TYPE_DICTIONARY:
		_course_config = parsed
		_course_config_path = config_path


func _load_holes_from_config() -> void:
	_hole_info.clear()
	_hole_numbers.clear()
	var holes = _course_config.get(HOLE_INFO_KEY, {})
	if typeof(holes) != TYPE_DICTIONARY:
		return
	_hole_info = holes
	for key in _hole_info.keys():
		var hole_number := _parse_hole_number(str(key))
		if hole_number > 0:
			_hole_numbers.append(hole_number)
	_hole_numbers.sort()


func _parse_hole_number(label: String) -> int:
	var digits := ""
	for index in range(label.length()):
		var character := label.substr(index, 1)
		if character >= "0" and character <= "9":
			digits += character
	return int(digits) if not digits.is_empty() else -1


func _get_current_hole_data() -> Dictionary:
	var key := "Hole %d" % _current_hole_number
	var value = _hole_info.get(key, {})
	return value if typeof(value) == TYPE_DICTIONARY else {}


func _resolve_tee_position(hole_data: Dictionary) -> Vector3:
	var tee_node := _resolve_tee_marker_node()
	if tee_node != null:
		return _snap_world_position_to_terrain(tee_node.global_position)

	var tee_xz := _resolve_tee_xz(hole_data)
	return _point_on_terrain(tee_xz, 0.0)


func _resolve_flag_position(hole_data: Dictionary, tee_position: Vector3) -> Vector3:
	var hole_node := _resolve_hole_marker_node()
	if hole_node != null:
		# Aim at the hole's Post (the pin) when present; every hole has one.
		var post_node := hole_node.get_node_or_null("Post") as Node3D
		var pin_node: Node3D = post_node if post_node != null else hole_node
		return _snap_world_position_to_terrain(pin_node.global_position)

	var tee_xz := Vector2(tee_position.x, tee_position.z)
	var flag_xz := _resolve_point_xz(hole_data.get("Hole Location", []), tee_xz + Vector2.RIGHT)
	return _point_on_terrain(flag_xz, 0.0)


func _resolve_hole_marker_node() -> Node3D:
	return get_node_or_null("%s/Hole %d" % [HOLE_MARKERS_PATH, _current_hole_number]) as Node3D


func _resolve_tee_marker_node() -> Node3D:
	var hole_node := _resolve_hole_marker_node()
	if hole_node == null:
		return null

	var preferred := hole_node.get_node_or_null("%s Tee" % DEFAULT_TEE_COLOR) as Node3D
	if preferred != null:
		return preferred

	for child in hole_node.get_children():
		if child is Node3D and str(child.name).ends_with(" Tee"):
			return child
	return null


func _resolve_tee_xz(hole_data: Dictionary) -> Vector2:
	var tee_boxes = hole_data.get("Tee Boxes", {})
	if typeof(tee_boxes) != TYPE_DICTIONARY or tee_boxes.is_empty():
		return Vector2.ZERO
	if tee_boxes.has(DEFAULT_TEE_COLOR):
		return _resolve_point_xz(tee_boxes[DEFAULT_TEE_COLOR], Vector2.ZERO)
	var tee_names: Array = tee_boxes.keys()
	tee_names.sort()
	return _resolve_point_xz(tee_boxes[tee_names[0]], Vector2.ZERO)


func _resolve_point_xz(value, fallback: Vector2) -> Vector2:
	if typeof(value) != TYPE_ARRAY or value.size() < 2:
		return fallback
	return Vector2(float(value[0]), float(value[1]))


func _point_on_terrain(point: Vector2, height_offset: float) -> Vector3:
	var height := _get_terrain_height_at(point, 0.0)
	return Vector3(point.x, height + height_offset, point.y)


func _snap_world_position_to_terrain(world_position: Vector3) -> Vector3:
	var point := Vector2(world_position.x, world_position.z)
	return _point_on_terrain(point, 0.0)


func _get_terrain_height_at(point: Vector2, fallback: float) -> float:
	var terrain := get_node_or_null("Terrain3D")
	if terrain == null:
		return fallback
	var data = terrain.get("data")
	if not (data is Object) or not data.has_method("get_height"):
		return fallback
	var height = data.call("get_height", Vector3(point.x, 0.0, point.y))
	if typeof(height) == TYPE_FLOAT or typeof(height) == TYPE_INT:
		var value := float(height)
		if is_finite(value):
			return value
	return fallback


func _flat_direction(from_position: Vector3, to_position: Vector3) -> Vector3:
	var direction := to_position - from_position
	direction.y = 0.0
	if direction.length_squared() < MIN_DIRECTION_LENGTH:
		return Vector3.RIGHT
	return direction.normalized()
