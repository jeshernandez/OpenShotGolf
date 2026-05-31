extends "res://Courses/_shared/golf_scene_base.gd"
## Range game mode: free-hit practice with optional camera follow and auto ball reset.
##
## Inherits the shared HUD shot-display pipeline from golf_scene_base.gd and layers on
## Range-specific camera behavior. Overridden handlers call super to preserve live HUD
## updates, then apply camera follow.


# Bumped on every shot and every rest so a queued reset/camera coroutine from a
# previous shot cancels itself instead of resetting a ball that is already in flight.
var _rest_sequence := 0


func _ready() -> void:
	super._ready()
	GlobalSettings.range_settings.camera_follow_mode.setting_changed.connect(set_camera_follow_mode)
	set_camera_follow_mode(GlobalSettings.range_settings.camera_follow_mode.value)


func _exit_tree() -> void:
	# GlobalSettings is an Autoload that outlives this scene; drop the connection so a
	# later setting_changed emission does not call into this freed node.
	var setting := GlobalSettings.range_settings.camera_follow_mode
	if setting.setting_changed.is_connected(set_camera_follow_mode):
		setting.setting_changed.disconnect(set_camera_follow_mode)


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("reset"):
		_reset_display_data()
		$RangeUI.set_data(display_data)


func _on_tcp_client_hit_ball(data: Dictionary) -> void:
	super._on_tcp_client_hit_ball(data)
	# Invalidate any pending rest coroutine from the previous shot.
	_rest_sequence += 1

	# Re-enable camera follow if the setting is on
	if GlobalSettings.range_settings.camera_follow_mode.value:
		set_camera_follow_mode(true)


func _on_golf_ball_rest(_ball_data) -> void:
	super._on_golf_ball_rest(_ball_data)

	var follow_on: bool = GlobalSettings.range_settings.camera_follow_mode.value
	var auto_reset: bool = GlobalSettings.range_settings.auto_ball_reset.value
	# No auto behavior: leave the final numbers and the ball where they are.
	if not follow_on and not auto_reset:
		return

	_rest_sequence += 1
	var sequence := _rest_sequence
	var delay: float = GlobalSettings.range_settings.ball_reset_timer.value
	if delay > 0.0:
		await get_tree().create_timer(delay).timeout
		if sequence != _rest_sequence:
			return

	if follow_on:
		await reset_camera_to_start()
		if sequence != _rest_sequence:
			return

	if auto_reset:
		_reset_display_data()
		$RangeUI.set_data(display_data)

	# Reset the ball exactly once, whichever mode(s) triggered this path. (Previously
	# both follow and auto-reset each reset the ball, doubling the reset.)
	var player = $Player
	if player.ball != null:
		player.reset_ball()


func set_camera_follow_mode(value) -> void:
	var camera = $PhantomCamera3D

	if value:
		camera.follow_mode = PhantomCamera3D.FollowMode.FRAMED
		var player = $Player
		camera.follow_target = player.ball
	else:
		camera.follow_mode = PhantomCamera3D.FollowMode.NONE


func reset_camera_to_start() -> void:
	var camera = $PhantomCamera3D

	# Temporarily disable follow mode
	camera.follow_mode = PhantomCamera3D.FollowMode.NONE

	# Tween camera back to starting position
	var start_pos := Vector3(-2.5, 1.5, 0)  # Starting camera offset from ball at origin
	var tween := create_tween()
	tween.set_trans(Tween.TRANS_CUBIC)
	tween.set_ease(Tween.EASE_IN_OUT)
	tween.tween_property(camera, "global_position", start_pos, 1.5)

	await tween.finished
	# Ball reset is handled by the caller (_on_golf_ball_rest) so it happens exactly once.


func _on_range_ui_hit_shot(data: Dictionary) -> void:
	super._on_range_ui_hit_shot(data)
	# Invalidate any pending rest coroutine from the previous shot.
	_rest_sequence += 1

	# Re-enable camera follow if the setting is on
	if GlobalSettings.range_settings.camera_follow_mode.value:
		set_camera_follow_mode(true)


func _on_player_manual_hit() -> void:
	# Invalidate any pending rest coroutine from the previous shot.
	_rest_sequence += 1

	# Re-enable camera follow if the setting is on
	if GlobalSettings.range_settings.camera_follow_mode.value:
		set_camera_follow_mode(true)
