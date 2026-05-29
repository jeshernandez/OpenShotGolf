@tool
extends EditorPlugin

const DockScene: Script = preload("res://addons/golf_course_design/GolfCourseDesignDock.cs")

var _dock: Control


func _enter_tree() -> void:
	_dock = DockScene.new()
	add_control_to_bottom_panel(_dock, "Golf Course Design")


func _exit_tree() -> void:
	if is_instance_valid(_dock):
		remove_control_from_bottom_panel(_dock)
		_dock.queue_free()
