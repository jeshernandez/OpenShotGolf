extends Node3D
class_name GolfCupMarker

signal ball_holed(ball: Node3D)

const CUP_DIAMETER_METERS := 0.108
const CUP_RADIUS_METERS := CUP_DIAMETER_METERS * 0.5
const CUP_DEPTH_METERS := 0.1015
const BALL_RADIUS_METERS := 0.021335
const MAX_ENTRY_HEIGHT_METERS := 0.35
const MAX_ENTRY_SPEED_METERS_PER_SECOND := 12.0
const CUP_SETTLE_FROM_BOTTOM_METERS := BALL_RADIUS_METERS * 0.75

var _ball: Node3D = null
var _holed := false
var _has_last_position := false
var _last_ball_position := Vector3.ZERO


func watch_ball(ball: Node3D) -> void:
	_ball = ball
	reset()


func reset() -> void:
	_holed = false
	_has_last_position = false
	if _ball != null and is_instance_valid(_ball):
		_last_ball_position = _ball.global_position
		_has_last_position = true


func _physics_process(_delta: float) -> void:
	if Engine.is_editor_hint() or _holed:
		return
	if _ball == null or not is_instance_valid(_ball):
		return

	var current_position := _ball.global_position
	var previous_position := _last_ball_position if _has_last_position else current_position
	if _ball_reached_cup(previous_position, current_position):
		_holed = true
		ball_holed.emit(_ball)
		_settle_ball()

	_last_ball_position = current_position
	_has_last_position = true


func _ball_reached_cup(previous_position: Vector3, current_position: Vector3) -> bool:
	var closest_position := _closest_segment_point_xz(previous_position, current_position)
	var cup_position := global_position
	var cup_normal := _cup_normal()
	var cup_delta := closest_position - cup_position
	var height_above_cup := cup_delta.dot(cup_normal)
	var radial_delta := cup_delta - cup_normal * height_above_cup
	if radial_delta.length() > CUP_RADIUS_METERS:
		return false

	if height_above_cup < -BALL_RADIUS_METERS or height_above_cup > MAX_ENTRY_HEIGHT_METERS:
		return false

	var velocity_value = _ball.get("velocity")
	var ball_velocity: Vector3 = velocity_value if velocity_value is Vector3 else Vector3.ZERO
	if ball_velocity.length() > MAX_ENTRY_SPEED_METERS_PER_SECOND:
		return false
	if ball_velocity.y > 0.25 and height_above_cup > BALL_RADIUS_METERS * 2.0:
		return false

	return true


func _cup_normal() -> Vector3:
	var normal := global_transform.basis.y
	if normal.length_squared() < 0.000001:
		return Vector3.UP
	return normal.normalized()


func _closest_segment_point_xz(previous_position: Vector3, current_position: Vector3) -> Vector3:
	var cup_xz := Vector2(global_position.x, global_position.z)
	var start_xz := Vector2(previous_position.x, previous_position.z)
	var end_xz := Vector2(current_position.x, current_position.z)
	var segment := end_xz - start_xz
	var segment_length_squared := segment.length_squared()
	if segment_length_squared < 0.0000001:
		return current_position

	var t := clampf((cup_xz - start_xz).dot(segment) / segment_length_squared, 0.0, 1.0)
	return previous_position.lerp(current_position, t)


func _settle_ball() -> void:
	if _ball == null or not is_instance_valid(_ball):
		return

	var cup_normal := _cup_normal()
	_ball.global_position = global_position - cup_normal * (CUP_DEPTH_METERS - CUP_SETTLE_FROM_BOTTOM_METERS)
	_ball.set("velocity", Vector3.ZERO)
	_ball.set("omega", Vector3.ZERO)
	if _ball.has_method("_enter_rest_state"):
		_ball.call("_enter_rest_state")
	else:
		_ball.set("state", PhysicsEnums.BallState.REST)
