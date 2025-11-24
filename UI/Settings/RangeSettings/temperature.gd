extends HBoxContainer


# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	$TemperatureSpinBox.min_value = GlobalSettings.range_settings.temperature._min
	$TemperatureSpinBox.max_value = GlobalSettings.range_settings.temperature._max
	$TemperatureSpinBox.value = GlobalSettings.range_settings.temperature.value
	
	$TemperatureSpinBox.step = 1.0
	
	GlobalSettings.range_settings.range_units.setting_changed.connect(update_units)


# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(_delta: float) -> void:
	pass


func _on_temperature_spin_box_value_changed(value: float) -> void:
	GlobalSettings.range_settings.temperature.set_value(value)
	
func update_units(value) -> void:
	if value == Enums.Units.IMPERIAL:
		$Label2.text = "F"
		$TemperatureSpinBox.value = GlobalSettings.range_settings.temperature.value*9/5 + 32
		GlobalSettings.range_settings.temperature.set_value($TemperatureSpinBox.value)
	else:
		$Label2.text = "C"
		$TemperatureSpinBox.value = (GlobalSettings.range_settings.temperature.value - 32) * 5/9
		GlobalSettings.range_settings.temperature.set_value($TemperatureSpinBox.value)
