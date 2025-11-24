extends HBoxContainer


# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	$ResetSpinBox.min_value = GlobalSettings.range_settings.ball_reset_timer._min
	$ResetSpinBox.max_value = GlobalSettings.range_settings.ball_reset_timer._max
	$ResetSpinBox.value = GlobalSettings.range_settings.ball_reset_timer.value
	
	$ResetSpinBox.step = 0.5


# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(_delta: float) -> void:
	pass


func _on_reset_spin_box_value_changed(value: float) -> void:
	GlobalSettings.range_settings.ball_reset_timer.set_value(value)
