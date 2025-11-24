extends PanelContainer


# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	pass # Replace with function body.


# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(_delta: float) -> void:
	pass


func _on_settings_button_pressed() -> void:
	visible = not visible


func _on_exit_button_pressed() -> void:
	SceneManager.change_scene("res://UI/MainMenu/main_menu.tscn")
