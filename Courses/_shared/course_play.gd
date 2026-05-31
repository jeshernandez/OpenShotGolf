extends "res://Courses/_shared/golf_scene_base.gd"
class_name CoursePlay

const CameraControllerScript := preload("res://Courses/_shared/course_shot_camera_controller.gd")
const HoleScoreOverlayScript := preload("res://Courses/_shared/hole_score_overlay.gd")
const PinDistanceIndicatorScript := preload("res://Courses/_shared/pin_distance_indicator.gd")

## Emitted after the final hole is completed, before the course loops back to Hole 1.
signal course_completed

const COURSE_INFO_KEY := "Course Info"
const HOLE_INFO_KEY := "Hole Info"
const DEFAULT_TEE_COLOR := "White"
const HOLE_MARKERS_PATH := "HoleMarkers"
const BALL_START_HEIGHT := 0.02
const COMPLETION_DISTANCE_FEET := 20.0
const GIMME_NEAR_FEET := 3.0
const HOLE_OUT_FEET := 0.1
const DEFAULT_CAMERA_ORBIT_DISTANCE := 2.1336
const DEFAULT_CAMERA_FOLLOW_DELAY_SECONDS := 0.0
const MIN_DIRECTION_LENGTH := 0.000001
const CAMERA_ROTATE_SPEED_DEG_PER_SEC := 90.0

var _course_config: Dictionary = {}
var _course_config_path := ""
var _hole_info: Dictionary = {}
var _hole_numbers: Array[int] = []
var _current_hole_index := 0
var _current_hole_number := 1
var _course_ready := false
var _selected_tee_color: String = DEFAULT_TEE_COLOR
var _active_tee_position := Vector3.ZERO
var _active_flag_position := Vector3.ZERO
var _active_target_direction := Vector3.RIGHT
var _shot_camera = null
var _rest_sequence := 0
var _stroke_count := 0
var _overlay_active := false
var _hole_score_overlay: HoleScoreOverlay = null
var _pin_distance_indicator: PinDistanceIndicator = null


func _process(delta: float) -> void:
	# Keep the shared live HUD refresh (Distance/Carry/Apex/Side update during flight).
	super._process(delta)

	if _shot_camera != null and not _overlay_active:
		if Input.is_action_pressed("camera_rotate_left"):
			_shot_camera.rotate_yaw(CAMERA_ROTATE_SPEED_DEG_PER_SEC * delta)
		if Input.is_action_pressed("camera_rotate_right"):
			_shot_camera.rotate_yaw(-CAMERA_ROTATE_SPEED_DEG_PER_SEC * delta)

	if _pin_distance_indicator != null and _pin_distance_indicator.visible and _course_ready:
		var ball_pos := _get_ball_global_position()
		var dist := Vector2(ball_pos.x, ball_pos.z).distance_to(
			Vector2(_active_flag_position.x, _active_flag_position.z)
		)
		_pin_distance_indicator.update_distance(dist)


func set_course_config(config: Dictionary, config_path: String = "") -> void:
	_course_config = config.duplicate(true)
	_course_config_path = config_path


func set_selected_tee(color: String) -> void:
	_selected_tee_color = color


func _ready() -> void:
	super._ready()
	GlobalSettings.range_settings.camera_follow_mode.setting_changed.connect(set_camera_follow_mode)
	# Course play uses a tube tracer instead of the Range's flat ribbon so the trail
	# stays visible while the camera follows directly behind the ball during flight.
	_player.BallTrailScript = preload("res://Courses/_shared/course_ball_trail.gd")
	# Sample the trail far more often than the Range's 0.1s so the tube grows smoothly
	# during flight instead of jumping forward in large chunks behind the fast ball.
	_player.trail_resolution = 0.02
	_shot_camera = CameraControllerScript.new()
	_shot_camera.configure(self, $PhantomCamera3D, $Player.ball)
	_hole_score_overlay = HoleScoreOverlayScript.new()
	add_child(_hole_score_overlay)
	_pin_distance_indicator = PinDistanceIndicatorScript.new()
	add_child(_pin_distance_indicator)
	assert(_hole_score_overlay != null and _pin_distance_indicator != null,
		"Course overlays must be instantiated before play begins.")
	_configure_ball_for_course()
	call_deferred("_initialize_course_deferred")


func _exit_tree() -> void:
	# GlobalSettings is an Autoload that outlives this scene; drop the connection so a
	# later setting_changed emission does not call into this freed node.
	var setting := GlobalSettings.range_settings.camera_follow_mode
	if setting.setting_changed.is_connected(set_camera_follow_mode):
		setting.setting_changed.disconnect(set_camera_follow_mode)


func _initialize_course_deferred() -> void:
	await get_tree().process_frame
	_ensure_course_config()
	_load_holes_from_config()
	_hide_hole_numbers()
	_start_hole_by_number(1)


func _hide_hole_numbers() -> void:
	var markers := get_node_or_null(HOLE_MARKERS_PATH)
	if markers == null:
		return
	for label in markers.find_children("HoleNumber", "Label3D", true, false):
		label.visible = false


# Fully replaces range.gd's reset handling: in course mode "reset" restarts the
# current hole rather than the range, so we intentionally do not call super here.
func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("reset"):
		_reset_display_data()
		$RangeUI.set_data(display_data)
		_start_hole_by_index(_current_hole_index)


func _on_tcp_client_hit_ball(data: Dictionary) -> void:
	super._on_tcp_client_hit_ball(data)
	_prepare_course_shot_start(data)


func _on_range_ui_hit_shot(data: Dictionary) -> void:
	super._on_range_ui_hit_shot(data)
	_prepare_course_shot_start(data)


func _on_player_manual_hit() -> void:
	_prepare_course_shot_start({})


func _on_golf_ball_rest(ball_data: Dictionary) -> void:
	super._on_golf_ball_rest(ball_data)
	if _shot_camera != null:
		_shot_camera.freeze_on_ball()

	# Remove the tracer once the ball stops so it no longer blocks the resting view.
	_player.clear_tracers()

	_rest_sequence += 1
	var sequence := _rest_sequence
	var delay := float(GlobalSettings.range_settings.ball_reset_timer.value)
	if delay > 0.0:
		await get_tree().create_timer(delay).timeout
		if sequence != _rest_sequence:
			return

	if _is_hole_complete():
		var seq := _rest_sequence
		var hole_data := _get_current_hole_data()
		var par := int(hole_data.get("Par", 3))
		var distance_feet := _get_distance_to_pin_feet()
		var gimme := _get_gimme_strokes(distance_feet)
		var final_strokes := _stroke_count + gimme
		var label := ScoreMapper.map_score(final_strokes, par)
		_pin_distance_indicator.visible = false
		_overlay_active = true
		_hole_score_overlay.show_result(label, final_strokes, par)
		await _hole_score_overlay.completed
		_overlay_active = false
		if seq != _rest_sequence:
			return
		_advance_to_next_hole()
		return

	if _shot_camera != null:
		_apply_camera_settings()
		_shot_camera.reset_to_ball(_get_ball_global_position(), _active_flag_position)


# Course mode manages follow per-shot: _prepare_course_shot_start re-reads the live
# camera_follow_mode value on every swing (see _on_*_hit -> enable_follow_after_launch),
# so enabling it mid-round self-heals on the next shot. We only need to react to the
# OFF transition here to immediately drop out of follow; the ON branch is a deliberate no-op.
func set_camera_follow_mode(value) -> void:
	if _shot_camera == null:
		return
	if not bool(value):
		_shot_camera.disable_follow()


func _prepare_course_shot_start(data: Dictionary) -> void:
	if not _course_ready:
		_start_hole_by_number(1)

	_rest_sequence += 1
	_stroke_count += 1
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
	_stroke_count = 0
	_current_hole_index = clampi(index, 0, _hole_numbers.size() - 1)
	_current_hole_number = _hole_numbers[_current_hole_index]

	var hole_data := _get_current_hole_data()
	_active_tee_position = _resolve_tee_position(hole_data)
	_active_flag_position = _resolve_flag_position(hole_data, _active_tee_position)
	_active_target_direction = _flat_direction(_active_tee_position, _active_flag_position)
	_course_ready = true

	if _pin_distance_indicator != null:
		_pin_distance_indicator.visible = true
		_pin_distance_indicator.update_hole(_current_hole_number)

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
		course_completed.emit()
		_current_hole_index = 0
	else:
		print("Hole %d complete. Moving to Hole %d." % [completed_hole, _hole_numbers[_current_hole_index + 1]])
		_current_hole_index += 1
	_reset_display_data()
	$RangeUI.set_data(display_data)
	_start_hole_by_index(_current_hole_index)


func _get_distance_to_pin_feet() -> float:
	var ball_position := _get_ball_global_position()
	var ball_xz := Vector2(ball_position.x, ball_position.z)
	var flag_xz := Vector2(_active_flag_position.x, _active_flag_position.z)
	return ball_xz.distance_to(flag_xz) * GolfUnits.FEET_PER_METER


func _get_gimme_strokes(distance_feet: float) -> int:
	if distance_feet <= HOLE_OUT_FEET:
		return 0
	elif distance_feet <= GIMME_NEAR_FEET:
		return 1
	else:
		return 2


func _is_hole_complete() -> bool:
	return _get_distance_to_pin_feet() <= COMPLETION_DISTANCE_FEET


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
	if _player == null or _player.ball == null:
		return _active_tee_position + Vector3.UP * BALL_START_HEIGHT
	return _player.ball.global_position


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

	var preferred := hole_node.get_node_or_null("%s Tee" % _selected_tee_color) as Node3D
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
	if tee_boxes.has(_selected_tee_color):
		return _resolve_point_xz(tee_boxes[_selected_tee_color], Vector2.ZERO)
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
