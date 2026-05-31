extends "res://Courses/_shared/golf_scene_base.gd"
## Range game mode: free-hit practice with optional camera follow and auto ball reset.
##
## Inherits the shared HUD shot-display pipeline from golf_scene_base.gd and layers on
## Range-specific camera behavior. Overridden handlers call super to preserve live HUD
## updates, then apply camera follow.


func _ready() -> void:
	super._ready()
	GlobalSettings.range_settings.camera_follow_mode.setting_changed.connect(set_camera_follow_mode)
	set_camera_follow_mode(GlobalSettings.range_settings.camera_follow_mode.value)


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("reset"):
		_reset_display_data()
		$RangeUI.set_data(display_data)


func _on_tcp_client_hit_ball(data: Dictionary) -> void:
	super._on_tcp_client_hit_ball(data)

	# Re-enable camera follow if the setting is on
	if GlobalSettings.range_settings.camera_follow_mode.value:
		set_camera_follow_mode(true)


func _on_golf_ball_rest(_ball_data) -> void:
	super._on_golf_ball_rest(_ball_data)

	# Return camera to starting position if follow mode is enabled
	if GlobalSettings.range_settings.camera_follow_mode.value:
		var camera_reset_delay: float = GlobalSettings.range_settings.ball_reset_timer.value
		await get_tree().create_timer(camera_reset_delay).timeout
		reset_camera_to_start()

	if GlobalSettings.range_settings.auto_ball_reset.value:
		await get_tree().create_timer(GlobalSettings.range_settings.ball_reset_timer.value).timeout
		_reset_display_data()
		$RangeUI.set_data(display_data)
		var player = $Player
		player.reset_ball()
		return

	# No auto reset: leave final numbers visible


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

	# Reset ball to starting position
	var player = $Player
	if player.ball != null:
		player.ball.reset()


func _on_range_ui_hit_shot(data: Dictionary) -> void:
	super._on_range_ui_hit_shot(data)

	# Re-enable camera follow if the setting is on
	if GlobalSettings.range_settings.camera_follow_mode.value:
		set_camera_follow_mode(true)


func _on_player_manual_hit() -> void:
	# Re-enable camera follow if the setting is on
	if GlobalSettings.range_settings.camera_follow_mode.value:
		set_camera_follow_mode(true)
