class_name HoleScoreOverlay
extends CanvasLayer

signal completed

const DISPLAY_DURATION := 3.0
const ANIM_IN_DURATION := 0.28
const ANIM_OUT_DURATION := 0.3

var _vbox: VBoxContainer
var _score_label: Label
var _detail_label: Label


func _ready() -> void:
	layer = 10

	var root := Control.new()
	root.set_anchors_preset(Control.PRESET_FULL_RECT)
	root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(root)

	_vbox = VBoxContainer.new()
	_vbox.anchor_left = 0.5
	_vbox.anchor_right = 0.5
	_vbox.anchor_top = 0.5
	_vbox.anchor_bottom = 0.5
	_vbox.grow_horizontal = Control.GROW_DIRECTION_BOTH
	_vbox.grow_vertical = Control.GROW_DIRECTION_BOTH
	_vbox.alignment = BoxContainer.ALIGNMENT_CENTER
	root.add_child(_vbox)

	_score_label = Label.new()
	_score_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_score_label.add_theme_font_size_override("font_size", 96)
	_score_label.add_theme_color_override("font_color", Color.WHITE)
	_score_label.add_theme_color_override("font_shadow_color", Color(0.0, 0.0, 0.0, 0.6))
	_score_label.add_theme_constant_override("shadow_offset_x", 3)
	_score_label.add_theme_constant_override("shadow_offset_y", 3)
	_vbox.add_child(_score_label)

	_detail_label = Label.new()
	_detail_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_detail_label.add_theme_font_size_override("font_size", 36)
	_detail_label.add_theme_color_override("font_color", Color(0.85, 0.85, 0.85, 1.0))
	_vbox.add_child(_detail_label)

	visible = false


func show_result(label_text: String, strokes: int, par: int) -> void:
	_score_label.text = label_text.to_upper()
	_detail_label.text = "%d stroke%s · par %d" % [
		strokes,
		"s" if strokes != 1 else "",
		par
	]
	_vbox.modulate.a = 0.0
	visible = true

	# Wait one frame so the VBox reports a real size, then anchor the scale pivot to
	# its centre *before* applying the initial scale. (Setting scale first would pivot
	# from the top-left for a frame — currently masked only by the 0 alpha above.)
	await get_tree().process_frame
	_vbox.pivot_offset = _vbox.size / 2.0
	_vbox.scale = Vector2(0.82, 0.82)

	var tween_in := create_tween().set_parallel(true)
	tween_in.tween_property(_vbox, "scale", Vector2.ONE, ANIM_IN_DURATION) \
		.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	tween_in.tween_property(_vbox, "modulate:a", 1.0, ANIM_IN_DURATION * 0.7) \
		.set_trans(Tween.TRANS_LINEAR)
	await tween_in.finished

	await get_tree().create_timer(DISPLAY_DURATION).timeout

	var tween_out := create_tween()
	tween_out.tween_property(_vbox, "modulate:a", 0.0, ANIM_OUT_DURATION) \
		.set_trans(Tween.TRANS_LINEAR)
	await tween_out.finished

	visible = false
	completed.emit()
