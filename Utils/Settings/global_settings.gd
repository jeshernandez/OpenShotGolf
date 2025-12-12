extends Node

signal settings_changed

# Range Settings
var range_settings := RangeSettings.new()


func resett_defaults():
	range_settings.reset_defaults()
	emit_signal("settings_changed")


func _ready() -> void:
	range_settings.fullscreen.setting_changed.connect(_on_fullscreen_changed)
	_apply_fullscreen(range_settings.fullscreen.value)


func _on_fullscreen_changed(value: bool) -> void:
	_apply_fullscreen(value)


func _apply_fullscreen(enabled: bool) -> void:
	var main_window: Window = get_tree().root
	if main_window == null:
		return
	if enabled:
		main_window.mode = Window.MODE_FULLSCREEN
	else:
		main_window.mode = Window.MODE_WINDOWED
