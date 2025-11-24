extends HBoxContainer


# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	$AltitudeSpinBox.min_value = GlobalSettings.range_settings.altitude._min
	$AltitudeSpinBox.max_value = GlobalSettings.range_settings.altitude._max
	$AltitudeSpinBox.value = GlobalSettings.range_settings.altitude.value
	$AltitudeSpinBox.step = 10
	
	GlobalSettings.range_settings.range_units.setting_changed.connect(update_units)


# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(_delta: float) -> void:
	pass


func _on_altitude_spin_box_value_changed(value: float) -> void:
	GlobalSettings.range_settings.altitude.set_value(value)

func update_units(value) -> void:
	const m2ft = 3.28084
	if value == Enums.Units.IMPERIAL:
		$Label2.text = "ft"
		$AltitudeSpinBox.value = GlobalSettings.range_settings.altitude.value*m2ft
		GlobalSettings.range_settings.altitude.set_value($AltitudeSpinBox.value)
	else:
		$Label2.text = "m"
		$AltitudeSpinBox.value = GlobalSettings.range_settings.altitude.value/m2ft
		GlobalSettings.range_settings.altitude.set_value($AltitudeSpinBox.value)
