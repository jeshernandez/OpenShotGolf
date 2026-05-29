extends "res://Courses/Range/range.gd"

const COURSE_TEE := Vector2(370.7934, 231.3497)
const COURSE_DIRECTION := Vector3(0.994389, 0.0, 0.10579)
const COURSE_CAMERA_BACK_DISTANCE := 6.0
const COURSE_CAMERA_HEIGHT := 3.0
const COURSE_CAMERA_LOOKAHEAD := 20.0
const COURSE_FOLLOW_DISTANCE := 2.5
const COURSE_FOLLOW_HEIGHT := 1.5

var _course_start_ready := false
var _course_start_position := Vector3.ZERO
var _course_start_direction := COURSE_DIRECTION


func _ready() -> void:
	super._ready()
	call_deferred("_apply_course_start_deferred")


func _apply_course_start_deferred() -> void:
	await get_tree().process_frame
	_apply_course_start()


func _apply_course_start() -> void:
	var tee_height := _get_terrain_height_at(COURSE_TEE, 0.0)
	_course_start_position = Vector3(COURSE_TEE.x, tee_height, COURSE_TEE.y)
	_course_start_direction = COURSE_DIRECTION.normalized()
	_course_start_ready = true
	_position_player_at_course_start(true)
	_apply_ball_aim()
	_sync_camera_to_course_start()
	if GlobalSettings.range_settings.camera_follow_mode.value:
		set_camera_follow_mode(true)


func _on_tcp_client_hit_ball(data: Dictionary) -> void:
	_prepare_course_shot_start()
	super._on_tcp_client_hit_ball(data)


func _on_range_ui_hit_shot(data: Dictionary) -> void:
	_prepare_course_shot_start()
	super._on_range_ui_hit_shot(data)


func _on_player_manual_hit() -> void:
	_prepare_course_shot_start()
	super._on_player_manual_hit()


func set_camera_follow_mode(value) -> void:
	super.set_camera_follow_mode(value)
	if value and _course_start_ready:
		$PhantomCamera3D.follow_offset = _get_camera_follow_offset()


func reset_camera_to_start() -> void:
	if not _course_start_ready:
		super.reset_camera_to_start()
		return

	var camera = $PhantomCamera3D
	camera.follow_mode = PhantomCamera3D.FollowMode.NONE
	var tween := create_tween()
	tween.set_trans(Tween.TRANS_CUBIC)
	tween.set_ease(Tween.EASE_IN_OUT)
	tween.tween_property(camera, "global_position", _get_camera_start_position(), 1.5)
	await tween.finished
	_position_player_at_course_start(false)
	if $Player.ball != null:
		$Player.ball.reset()


func _prepare_course_shot_start() -> void:
	if not _course_start_ready:
		_apply_course_start()
	_position_player_at_course_start(false)
	_apply_ball_aim()


func _position_player_at_course_start(reset_ball: bool) -> void:
	if not _course_start_ready:
		return
	$Player.global_position = _course_start_position
	if reset_ball and $Player.ball != null:
		$Player.ball.reset()


func _apply_ball_aim() -> void:
	if not _course_start_ready or $Player.ball == null:
		return
	$Player.ball.aim_yaw_offset_deg = rad_to_deg(atan2(-_course_start_direction.z, _course_start_direction.x))


func _sync_camera_to_course_start() -> void:
	var camera_start := _get_camera_start_position()
	var camera_target := _get_camera_target_position()
	$PhantomCamera3D.global_position = camera_start
	$PhantomCamera3D.look_at(camera_target, Vector3.UP)
	$PhantomCamera3D.follow_offset = _get_camera_follow_offset()
	$Camera3D.global_position = camera_start
	$Camera3D.look_at(camera_target, Vector3.UP)


func _get_camera_start_position() -> Vector3:
	var camera_point := _course_start_position - _course_start_direction * COURSE_CAMERA_BACK_DISTANCE
	var camera_xz := Vector2(camera_point.x, camera_point.z)
	var camera_height := _get_terrain_height_at(camera_xz, _course_start_position.y) + COURSE_CAMERA_HEIGHT
	return Vector3(camera_point.x, camera_height, camera_point.z)


func _get_camera_target_position() -> Vector3:
	var target_point := _course_start_position + _course_start_direction * COURSE_CAMERA_LOOKAHEAD
	var target_xz := Vector2(target_point.x, target_point.z)
	var target_height := _get_terrain_height_at(target_xz, _course_start_position.y) + 1.0
	return Vector3(target_point.x, target_height, target_point.z)


func _get_camera_follow_offset() -> Vector3:
	return -_course_start_direction * COURSE_FOLLOW_DISTANCE + Vector3.UP * COURSE_FOLLOW_HEIGHT


func _get_terrain_height_at(point: Vector2, fallback: float) -> float:
	var terrain := get_node_or_null("Terrain3D")
	if terrain == null:
		return fallback
	var data = terrain.get("data")
	if data == null or not data.has_method("get_height"):
		return fallback
	var height = data.call("get_height", Vector3(point.x, 0.0, point.y))
	if typeof(height) == TYPE_FLOAT or typeof(height) == TYPE_INT:
		var value := float(height)
		if is_finite(value):
			return value
	return fallback
