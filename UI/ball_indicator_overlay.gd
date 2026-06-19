class_name BallIndicatorOverlay
extends CanvasLayer

const INDICATOR_LAYER := 6
const SCREEN_MARGIN := 24.0


class IndicatorCanvas:
	extends Control

	const BALL_SCREEN_OFFSET := Vector2(0.0, 4.0)
	const SIDE_GAP := 34.0
	const CHEVRON_SIZE := 14.0
	const CHEVRON_SPACING := 11.0
	const CHEVRON_COUNT := 3
	const LINE_WIDTH := 4.0
	const INDICATOR_COLOR := Color(1.0, 1.0, 1.0, 0.82)
	const SHADOW_COLOR := Color(0.0, 0.0, 0.0, 0.35)

	var ball_screen_position := Vector2.ZERO

	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_IGNORE
		set_anchors_preset(Control.PRESET_FULL_RECT)

	func _draw() -> void:
		var position := ball_screen_position + BALL_SCREEN_OFFSET
		var left_start := position - Vector2(SIDE_GAP, 0.0)
		var right_start := position + Vector2(SIDE_GAP, 0.0)

		for index in range(CHEVRON_COUNT):
			var left_center := left_start - Vector2(CHEVRON_SPACING * index, 0.0)
			var right_center := right_start + Vector2(CHEVRON_SPACING * index, 0.0)
			_draw_chevron(left_center, -1.0, SHADOW_COLOR, LINE_WIDTH + 2.0, Vector2(1.5, 1.5))
			_draw_chevron(right_center, 1.0, SHADOW_COLOR, LINE_WIDTH + 2.0, Vector2(1.5, 1.5))
			_draw_chevron(left_center, -1.0, INDICATOR_COLOR, LINE_WIDTH, Vector2.ZERO)
			_draw_chevron(right_center, 1.0, INDICATOR_COLOR, LINE_WIDTH, Vector2.ZERO)

	func _draw_chevron(center: Vector2, direction: float, color: Color, width: float, offset: Vector2) -> void:
		var half := CHEVRON_SIZE * 0.5
		var tip := center + Vector2(direction * half, 0.0) + offset
		var top := center - Vector2(direction * half, half) + offset
		var bottom := center - Vector2(direction * half, -half) + offset
		draw_line(top, tip, color, width, true)
		draw_line(tip, bottom, color, width, true)


var _ball: Node3D = null
var _canvas: IndicatorCanvas = null


func _ready() -> void:
	layer = INDICATOR_LAYER
	_canvas = IndicatorCanvas.new()
	add_child(_canvas)
	hide_indicator()


func _process(_delta: float) -> void:
	if not visible:
		return
	if _ball == null or not is_instance_valid(_ball):
		hide_indicator()
		return

	var camera := get_viewport().get_camera_3d()
	if camera == null or camera.is_position_behind(_ball.global_position):
		_canvas.visible = false
		return

	var screen_position := camera.unproject_position(_ball.global_position)
	if not _is_on_screen(screen_position):
		_canvas.visible = false
		return

	_canvas.visible = true
	_canvas.ball_screen_position = screen_position
	_canvas.queue_redraw()


func show_for_ball(ball: Node3D) -> void:
	_ball = ball
	visible = true
	set_process(true)


func hide_indicator() -> void:
	visible = false
	set_process(false)
	if _canvas != null:
		_canvas.visible = false


func _is_on_screen(screen_position: Vector2) -> bool:
	var viewport_size := get_viewport().get_visible_rect().size
	return screen_position.x >= SCREEN_MARGIN \
		and screen_position.y >= SCREEN_MARGIN \
		and screen_position.x <= viewport_size.x - SCREEN_MARGIN \
		and screen_position.y <= viewport_size.y - SCREEN_MARGIN
