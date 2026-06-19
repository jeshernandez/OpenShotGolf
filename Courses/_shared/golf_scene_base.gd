extends Node3D
## Base class for golf game scenes (Range, Course Play).
##
## Owns the shared HUD shot-display pipeline: it stores the raw ball data, formats
## it via ShotFormatter, and pushes it to RangeUI. The display refreshes every frame
## while the ball is not at rest so Distance, Carry, Apex, and Side update live during
## flight/rollout, then lock to final values when the ball comes to rest.
##
## Subclasses add game-mode behavior (camera follow, scoring, hole management) and
## must call super on the overridden virtual/handler methods to keep live HUD updates.

const BallIndicatorOverlayScript := preload("res://UI/ball_indicator_overlay.gd")

var display_data: Dictionary = {
	"Distance": "---",
	"Carry": "---",
	"Offline": "---",
	"Apex": "---",
	"VLA": "---",
	"HLA": "---",
	"Speed": "---",
	"BackSpin": "---",
	"SideSpin": "---",
	"TotalSpin": "---",
	"SpinAxis": "---"
}
var raw_ball_data: Dictionary = {}
var last_display: Dictionary = {}
var _ball_indicator_overlay: BallIndicatorOverlay = null

# Cached once instead of resolving $Player (a get_node call) every _process frame.
@onready var _player: Node = $Player


func _ready() -> void:
	if _uses_ball_indicator():
		_ball_indicator_overlay = BallIndicatorOverlayScript.new()
		add_child(_ball_indicator_overlay)
	if has_node("/root/LaunchMonitorManager"):
		var launch_monitor = get_node("/root/LaunchMonitorManager")
		if not launch_monitor.hit_ball.is_connected(_on_launch_monitor_hit_ball):
			launch_monitor.hit_ball.connect(_on_launch_monitor_hit_ball)


func _process(_delta: float) -> void:
	# Refresh UI during flight/rollout so carry/apex update live; distance updates only at rest.
	if _player.get_ball_state() != PhysicsEnums.BallState.REST:
		_update_ball_display()


func _on_tcp_client_hit_ball(data: Dictionary) -> void:
	_hide_ball_indicator()
	raw_ball_data = data.duplicate()
	_update_ball_display()


func _on_launch_monitor_hit_ball(data: Dictionary) -> void:
	_on_tcp_client_hit_ball(data)
	$Player._on_tcp_client_hit_ball(data)


func _on_range_ui_hit_shot(data: Dictionary) -> void:
	# For local injected shots, prime the display immediately with the payload data.
	_hide_ball_indicator()
	raw_ball_data = data.duplicate()
	_update_ball_display()


func _on_golf_ball_rest(_ball_data) -> void:
	raw_ball_data = _ball_data.duplicate()
	# Show final shot numbers immediately on rest
	_update_ball_display()
	_show_ball_indicator()


func _on_player_manual_hit() -> void:
	# Subclasses override to add game-mode behavior.
	pass


func _show_ball_indicator() -> void:
	if not _should_show_ball_indicator():
		return
	if _ball_indicator_overlay == null or _player == null or _player.ball == null:
		return
	_ball_indicator_overlay.show_for_ball(_player.ball)


func _hide_ball_indicator() -> void:
	if _ball_indicator_overlay != null:
		_ball_indicator_overlay.hide_indicator()


func _should_show_ball_indicator() -> bool:
	return false


func _uses_ball_indicator() -> bool:
	return false


func _update_ball_display() -> void:
	# Show distance continuously (updates during flight/rollout, final at rest).
	var show_distance: bool = true
	display_data = ShotFormatter.format_ball_display(raw_ball_data, _player, GlobalSettings.range_settings.range_units.value, show_distance, display_data)
	last_display = display_data.duplicate()
	$RangeUI.set_data(display_data)


func _reset_display_data() -> void:
	raw_ball_data.clear()
	last_display.clear()
	for key in display_data.keys():
		display_data[key] = "---"
