class_name PinDistanceIndicator
extends CanvasLayer

const FEET_PER_METER := 3.28084
const YARDS_PER_METER := 1.09361
const FEET_THRESHOLD_METERS := 9.144  # 30 ft

var _hole_label: Label
var _label: Label


func _ready() -> void:
	layer = 5

	var root := Control.new()
	root.set_anchors_preset(Control.PRESET_FULL_RECT)
	root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(root)

	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.0, 0.0, 0.0, 0.65)
	style.corner_radius_top_left = 20
	style.corner_radius_top_right = 20
	style.corner_radius_bottom_left = 20
	style.corner_radius_bottom_right = 20
	style.content_margin_left = 18.0
	style.content_margin_right = 18.0
	style.content_margin_top = 6.0
	style.content_margin_bottom = 6.0

	_hole_label = Label.new()
	_hole_label.anchor_left = 0.5
	_hole_label.anchor_right = 0.5
	_hole_label.anchor_top = 1.0
	_hole_label.anchor_bottom = 1.0
	_hole_label.grow_horizontal = Control.GROW_DIRECTION_BOTH
	_hole_label.grow_vertical = Control.GROW_DIRECTION_BEGIN
	_hole_label.offset_top = -84.0
	_hole_label.offset_bottom = -52.0
	_hole_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_hole_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_hole_label.add_theme_font_size_override("font_size", 14)
	_hole_label.add_theme_color_override("font_color", Color(0.85, 0.85, 0.85, 1.0))
	_hole_label.add_theme_stylebox_override("normal", style.duplicate())
	_hole_label.text = "HOLE 1"
	root.add_child(_hole_label)

	_label = Label.new()
	_label.anchor_left = 0.5
	_label.anchor_right = 0.5
	_label.anchor_top = 1.0
	_label.anchor_bottom = 1.0
	_label.grow_horizontal = Control.GROW_DIRECTION_BOTH
	_label.grow_vertical = Control.GROW_DIRECTION_BEGIN
	_label.offset_top = -48.0
	_label.offset_bottom = -12.0
	_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_label.add_theme_font_size_override("font_size", 20)
	_label.add_theme_color_override("font_color", Color.WHITE)
	_label.add_theme_stylebox_override("normal", style.duplicate())
	root.add_child(_label)

	visible = false


func update_hole(hole_number: int) -> void:
	_hole_label.text = "HOLE %d" % hole_number


func update_distance(meters: float) -> void:
	if not visible:
		return
	if meters < FEET_THRESHOLD_METERS:
		var feet := int(round(meters * FEET_PER_METER))
		_label.text = "%d FT TO PIN" % feet
	else:
		var yards := int(round(meters * YARDS_PER_METER))
		_label.text = "%d YDS TO PIN" % yards
