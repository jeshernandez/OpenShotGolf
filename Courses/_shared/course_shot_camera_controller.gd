class_name CourseShotCameraController
extends RefCounted

const CAMERA_LOOK_OFFSET := Vector3(0.0, 1.5, 0.0)
const FLIGHT_FOLLOW_BACK := 8.5
const FOLLOW_HEIGHT := 2.0
const FOLLOW_HEIGHT_REFERENCE_BACK := 8.5
const ORBIT_HEIGHT := 1.5
const RESET_TWEEN_DURATION := 1.2
const MIN_DIRECTION_LENGTH := 0.000001
# Within this cup distance (20 yds) the dedicated Group "green" camera takes over,
# framing the ball and the flag together; outside it the chase camera follows.
const CLOSE_FOLLOW_TRIGGER_FT := 60.0
# How far back (ft) the green camera sits from the ball/cup group on the approach
# side so the flag stays in frame while the ball is framed close-up.
const GREEN_FOLLOW_BACK_FT := 10.0
# Priority the green camera is raised to so it outranks the chase camera (whose
# priority is 10, set in course_base.tscn); the host tweens between the two views.
const GREEN_PRIORITY := 20

var _host: Node = null
var _camera: Node3D = null
# Dedicated Group-mode PhantomCamera3D that frames the ball + flag near the cup.
var _green_camera: Node3D = null
# The active hole's flag-pole node; the green camera's group target alongside the ball.
var _cup_target: Node3D = null
var _green_active := false
var _ball: Node3D = null
var _camera_yaw_deg := 0.0
var _orbit_radius := 2.1336
var _reset_tween: Tween = null
var _is_orbit_mode := false
# Horizontal ball-travel direction captured at launch; sets the chase follow
# offset and is the fallback approach direction for the green camera's framing.
var _follow_direction := Vector3.ZERO
# Bumped whenever camera modes are reset; lets an in-flight follow coroutine detect
# that the shot/hole it was launched for is no longer current and bail out.
var _follow_generation := 0


# Drives only the PhantomCamera3D (`camera`). The PhantomCameraHost is the sole
# owner of the actual Camera3D and mirrors the PhantomCamera's transform onto it
# every frame, so this controller never writes to Camera3D directly.
func configure(host: Node, camera: Node3D, green_camera: Node3D, ball: Node3D) -> void:
	_host = host
	_camera = camera
	_green_camera = green_camera
	_ball = ball


# Points the green camera's Group follow/look-at at the live hole's flag (plus the
# ball). Called whenever the active hole changes so framing tracks the right cup.
func set_cup_target(post_node: Node3D) -> void:
	_cup_target = post_node
	if _green_camera == null or not is_instance_valid(_green_camera):
		return
	if post_node == null or _ball == null:
		var empty: Array[Node3D] = []
		_green_camera.follow_targets = empty
		_green_camera.look_at_targets = empty
		return
	var targets: Array[Node3D] = [_ball, post_node]
	_green_camera.follow_targets = targets
	_green_camera.look_at_targets = targets


func set_orbit_radius(radius: float) -> void:
	_orbit_radius = maxf(radius, 0.5)


func set_to_start_immediate(ball_position: Vector3, target_position: Vector3) -> void:
	if not _is_ready():
		return
	_stop_reset_tween()
	_disable_camera_modes()
	_align_camera_yaw_to_target(ball_position, target_position)
	_camera.global_position = _get_orbit_position(ball_position)
	_camera.look_at(ball_position + CAMERA_LOOK_OFFSET, Vector3.UP)


func begin_shot_launch() -> void:
	if not _is_ready():
		return
	_stop_reset_tween()
	_disable_camera_modes()


func enable_follow_after_launch(follow_direction: Vector3, delay_seconds: float) -> void:
	if not _is_ready():
		return
	_enable_follow_after_launch_async(follow_direction, delay_seconds)


func disable_follow() -> void:
	if not _is_ready():
		return
	_disable_camera_modes()


# Called every frame by CoursePlay while a hole is live. Once the ball has landed
# (no longer FLIGHT) and is within CLOSE_FOLLOW_TRIGGER_FT of the cup, hand the view
# to the Group "green" camera so it frames the ball and flag together; otherwise the
# chase camera keeps following. Switching is a priority change, so the host tweens
# between the two views instead of snapping. The chase follow is never teleported
# per-frame here — follow_damping smooths it the same way it does in flight.
func update_follow_distance_for_cup(cup_distance_ft: float, ball_state: int) -> void:
	if not _is_ready() or _is_orbit_mode:
		return
	if _camera.follow_mode != PhantomCamera3D.FollowMode.SIMPLE:
		return
	if _follow_direction.length_squared() < MIN_DIRECTION_LENGTH:
		return
	var should_frame_green := ball_state != PhysicsEnums.BallState.FLIGHT \
		and cup_distance_ft <= CLOSE_FOLLOW_TRIGGER_FT \
		and _cup_target != null and is_instance_valid(_cup_target)
	if should_frame_green:
		_activate_green_camera()
	else:
		_deactivate_green_camera()


func freeze_on_ball() -> void:
	if not _is_ready():
		return
	_disable_camera_modes()


func rotate_yaw(delta_deg: float) -> void:
	if not _is_ready() or not _is_orbit_mode:
		return
	_stop_reset_tween()
	_camera_yaw_deg = wrapf(_camera_yaw_deg + delta_deg, -180.0, 180.0)
	_camera.global_position = _get_orbit_position(_ball.global_position)
	_camera.look_at(_ball.global_position + CAMERA_LOOK_OFFSET, Vector3.UP)


func reset_to_ball(ball_position: Vector3, target_position: Vector3) -> void:
	if not _is_ready():
		return
	_reset_to_ball_async(ball_position, target_position)


func get_aim_yaw_offset_deg() -> float:
	if not _is_ready():
		return 0.0
	var forward := -_camera.global_basis.z
	var flat_forward := Vector3(forward.x, 0.0, forward.z)
	if flat_forward.length_squared() < MIN_DIRECTION_LENGTH:
		return _camera_yaw_deg
	flat_forward = flat_forward.normalized()
	var camera_aim_deg := rad_to_deg(atan2(flat_forward.z, flat_forward.x))
	return -camera_aim_deg


func get_launch_follow_direction(shot_hla_deg: float, world_yaw_offset_deg: float, fallback_direction: Vector3) -> Vector3:
	var world_hla_deg := shot_hla_deg + world_yaw_offset_deg
	var hla_rad := deg_to_rad(world_hla_deg)
	var direction := Vector3(cos(hla_rad), 0.0, -sin(hla_rad))
	if direction.length_squared() < MIN_DIRECTION_LENGTH:
		direction = fallback_direction
	direction.y = 0.0
	if direction.length_squared() < MIN_DIRECTION_LENGTH:
		return Vector3.RIGHT
	return direction.normalized()


func _enable_follow_after_launch_async(follow_direction: Vector3, delay_seconds: float) -> void:
	var tree := _host.get_tree()
	if tree == null:
		return
	# Snapshot the current generation; if camera modes are reset (next shot, ball rest,
	# next hole) while we await, this coroutine is stale and must not touch the camera.
	var generation := _follow_generation
	if delay_seconds > 0.0:
		await tree.create_timer(delay_seconds).timeout
		if generation != _follow_generation:
			return
	for _i in range(8):
		await tree.physics_frame
		if generation != _follow_generation:
			return
		if _ball_has_started_moving():
			break

	var direction := _get_ball_velocity_direction()
	if direction.length_squared() < MIN_DIRECTION_LENGTH:
		direction = follow_direction
	if direction.length_squared() < MIN_DIRECTION_LENGTH:
		direction = Vector3.RIGHT
	direction = direction.normalized()
	_follow_direction = direction

	_is_orbit_mode = false
	_camera.follow_mode = PhantomCamera3D.FollowMode.SIMPLE
	_camera.follow_target = _ball
	_set_follow_offset(FLIGHT_FOLLOW_BACK)
	_camera.follow_damping = true
	_camera.look_at_mode = PhantomCamera3D.LookAtMode.SIMPLE
	_camera.look_at_target = _ball
	# Snap the real Camera3D straight to the follow position (and zero its damping
	# velocity) so smooth_damp starts from behind the ball instead of drifting in
	# from the pre-shot orbit position — that drift is the launch jitter.
	_camera.teleport_position()


func _reset_to_ball_async(ball_position: Vector3, target_position: Vector3) -> void:
	_stop_reset_tween()
	_disable_camera_modes()
	_align_camera_yaw_to_target(ball_position, target_position)

	var start_position := _camera.global_position
	var end_position := _get_orbit_position(ball_position)
	var start_look_position := _camera.global_position + (-_camera.global_basis.z * 15.0)
	var end_look_position := ball_position + CAMERA_LOOK_OFFSET

	_reset_tween = _host.create_tween()
	_reset_tween.set_trans(Tween.TRANS_CUBIC)
	_reset_tween.set_ease(Tween.EASE_IN_OUT)
	_reset_tween.set_parallel(true)
	_reset_tween.tween_method(Callable(self, "_set_camera_position"), start_position, end_position, RESET_TWEEN_DURATION)
	_reset_tween.tween_method(Callable(self, "_set_camera_look_position"), start_look_position, end_look_position, RESET_TWEEN_DURATION)
	# Godot 4 Tween.kill() does not emit `finished`, so we poll per frame to
	# avoid a coroutine that hangs indefinitely when rotation input cancels the tween.
	var tree := _host.get_tree()
	if tree == null:
		return
	var active_tween := _reset_tween
	while _reset_tween == active_tween and _reset_tween != null and _reset_tween.is_running():
		await tree.process_frame
		tree = _host.get_tree()
		if tree == null:
			return
	if _reset_tween != active_tween:
		return
	_reset_tween = null
	_camera.look_at(end_look_position, Vector3.UP)


func _set_camera_position(position: Vector3) -> void:
	if _camera == null:
		return
	_camera.global_position = position


func _set_camera_look_position(look_position: Vector3) -> void:
	if _camera == null:
		return
	_camera.look_at(look_position, Vector3.UP)


func _align_camera_yaw_to_target(ball_position: Vector3, target_position: Vector3) -> void:
	var to_target := target_position - ball_position
	to_target.y = 0.0
	if to_target.length_squared() < MIN_DIRECTION_LENGTH:
		return
	var target_aim_deg := rad_to_deg(atan2(to_target.z, to_target.x))
	_camera_yaw_deg = wrapf(-target_aim_deg, -180.0, 180.0)


func _get_orbit_position(center: Vector3) -> Vector3:
	var yaw_rad := deg_to_rad(_camera_yaw_deg)
	return center + Vector3(
		-cos(yaw_rad) * _orbit_radius,
		ORBIT_HEIGHT,
		sin(yaw_rad) * _orbit_radius
	)


func _get_ball_velocity_direction() -> Vector3:
	if _ball == null:
		return Vector3.ZERO
	var value = _ball.get("velocity")
	if typeof(value) != TYPE_VECTOR3:
		return Vector3.ZERO
	var direction: Vector3 = value
	direction.y = 0.0
	if direction.length_squared() < 0.25:
		return Vector3.ZERO
	return direction.normalized()


func _set_follow_offset(follow_back: float) -> void:
	var follow_height := follow_back * (FOLLOW_HEIGHT / FOLLOW_HEIGHT_REFERENCE_BACK)
	_camera.follow_offset = -_follow_direction * follow_back + Vector3.UP * follow_height


# Raise the green camera above the chase camera so the host tweens to it. The
# group offset is set once on activation, placing the camera on the ball's far
# side from the cup (looking back over the ball toward the flag) so the flag
# stays framed even when the ball comes to rest past the cup.
func _activate_green_camera() -> void:
	if _green_active:
		return
	if _green_camera == null or not is_instance_valid(_green_camera):
		return
	_green_active = true
	_update_green_offset()
	_green_camera.priority = GREEN_PRIORITY


func _deactivate_green_camera() -> void:
	if not _green_active:
		return
	_green_active = false
	if _green_camera == null or not is_instance_valid(_green_camera):
		return
	_green_camera.priority = 0
	# Resync the chase camera to the live follow position so resuming it mid-roll
	# tweens from the ball rather than from a stale (inactive) transform.
	if _is_ready() and _camera.follow_mode == PhantomCamera3D.FollowMode.SIMPLE:
		_camera.teleport_position()


func _update_green_offset() -> void:
	var approach := _ball.global_position - _cup_target.global_position
	approach.y = 0.0
	if approach.length_squared() < MIN_DIRECTION_LENGTH:
		approach = _follow_direction
	if approach.length_squared() < MIN_DIRECTION_LENGTH:
		approach = Vector3.BACK
	approach = approach.normalized()
	var back := GREEN_FOLLOW_BACK_FT / GolfUnits.FEET_PER_METER
	_green_camera.follow_offset = approach * back + Vector3.UP * FOLLOW_HEIGHT


func _ball_has_started_moving() -> bool:
	if _ball == null:
		return false
	var value = _ball.get("velocity")
	if typeof(value) != TYPE_VECTOR3:
		return false
	var ball_velocity: Vector3 = value
	return ball_velocity.length_squared() > 0.0001


func _disable_camera_modes() -> void:
	_deactivate_green_camera()
	_camera.follow_mode = PhantomCamera3D.FollowMode.NONE
	_camera.look_at_mode = PhantomCamera3D.LookAtMode.NONE
	_is_orbit_mode = true
	_follow_direction = Vector3.ZERO
	_follow_generation += 1


func _stop_reset_tween() -> void:
	if _reset_tween != null:
		_reset_tween.kill()
		_reset_tween = null


func _is_ready() -> bool:
	return _host != null and _camera != null and _ball != null
