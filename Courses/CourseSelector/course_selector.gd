extends Control

@onready var _course_list: CourseList = $ContentPanel/ContentMargin/VBoxContainer/ScrollContainer/CourseList
@onready var _course_directory_text: TextEdit = $ContentPanel/ContentMargin/VBoxContainer/CourseDirectory/CourseDirectoryText
@onready var _status_label: Label = $ContentPanel/ContentMargin/VBoxContainer/StatusLabel
@onready var _refresh_button: Button = $ContentPanel/ContentMargin/VBoxContainer/CourseDirectory/RefreshButton
@onready var _tee_option_button: OptionButton = $ContentPanel/ContentMargin/VBoxContainer/CourseDirectory/TeeOptionButton
@onready var _play_button: Button = $ContentPanel/ContentMargin/VBoxContainer/CourseDirectory/PlayButton


func _ready() -> void:
	_refresh_button.mouse_entered.connect(_on_refresh_button_mouse_entered)
	_refresh_button.mouse_exited.connect(_on_refresh_button_mouse_exited)
	_request_course_reload()


func _on_main_menu_button_pressed() -> void:
	SceneManager.change_scene("res://UI/MainMenu/main_menu.tscn")


func _on_refresh_button_pressed() -> void:
	_flash_refresh_button()
	_request_course_reload()


func _on_course_list_item_selected(index: int) -> void:
	var colors: Array[String] = _course_list.get_tee_colors_for_index(index)
	_tee_option_button.clear()
	for color in colors:
		_tee_option_button.add_item(color)
	var white_idx := colors.find("White")
	_tee_option_button.selected = white_idx if white_idx >= 0 else 0
	_tee_option_button.visible = true
	_play_button.visible = true


func _on_course_list_empty_clicked(_at_position: Vector2, _mouse_button_index: int) -> void:
	_course_list.deselect_all()
	_tee_option_button.visible = false
	_play_button.visible = false


func _on_play_button_pressed() -> void:
	var selected: PackedInt32Array = _course_list.get_selected_items()
	if selected.is_empty():
		return
	_launch_course(selected[0])


func _on_course_list_item_activated(index: int) -> void:
	_launch_course(index)


func _launch_course(index: int) -> void:
	var scene_path: String = _course_list.get_scene_path_for_index(index)
	var config_path: String = _course_list.get_config_path_for_index(index)

	if scene_path.is_empty():
		printerr("[CourseSelector] Play requested with an empty scene scene_path.")
		return

	var tee_color := ""
	if _tee_option_button.visible and _tee_option_button.item_count > 0:
		tee_color = _tee_option_button.get_item_text(_tee_option_button.selected)

	SceneManager.load_course(scene_path, config_path, tee_color)


func _request_course_reload() -> void:
	_tee_option_button.visible = false
	_play_button.visible = false
	var status_text: String = _course_list.reload_courses(_course_directory_text.text)
	_status_label.text = status_text if not status_text.is_empty() else "Ready"


func _flash_refresh_button() -> void:
	_refresh_button.self_modulate = Color(1, 1, 1, 1)
	var tween := create_tween()
	tween.tween_property(_refresh_button, "self_modulate", Color(0.75, 0.9, 1.0, 1), 0.08)
	tween.tween_property(_refresh_button, "self_modulate", Color(1, 1, 1, 1), 0.16)


func _on_refresh_button_mouse_entered() -> void:
	_refresh_button.self_modulate = Color(0.8, 0.92, 1.0, 1)


func _on_refresh_button_mouse_exited() -> void:
	_refresh_button.self_modulate = Color(1, 1, 1, 1)
