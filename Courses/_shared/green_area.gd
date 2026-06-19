@tool
extends Area3D
class_name GreenArea

@export var surface_type: int = PhysicsEnums.SurfaceType.GREEN
@export var preview_color: Color = Color(0.2, 1.0, 0.25, 0.28):
	set(value):
		preview_color = value
		_update_preview_material()

@onready var _collision_shape: CollisionShape3D = _find_collision_shape()
@onready var _preview_mesh: MeshInstance3D = _find_preview_mesh()

var _last_shape: Shape3D
var _last_shape_state := ""


func _ready() -> void:
	if not Engine.is_editor_hint():
		body_entered.connect(_on_body_entered)
		body_exited.connect(_on_body_exited)

	_update_preview()


func _process(_delta: float) -> void:
	if Engine.is_editor_hint():
		_update_preview()


func _on_body_entered(body: Node3D) -> void:
	if body.has_method("enter_surface_zone"):
		body.enter_surface_zone(surface_type)


func _on_body_exited(body: Node3D) -> void:
	if body.has_method("exit_surface_zone"):
		body.exit_surface_zone(surface_type)


func _update_preview() -> void:
	_collision_shape = _find_collision_shape()
	_preview_mesh = _find_preview_mesh()

	if _collision_shape == null or _preview_mesh == null:
		return

	var shape := _collision_shape.shape
	if shape == null:
		_preview_mesh.mesh = null
		return

	var shape_state := _shape_state(shape)
	if shape == _last_shape and shape_state == _last_shape_state:
		return

	_last_shape = shape
	_last_shape_state = shape_state
	_preview_mesh.transform = _collision_shape.transform
	_preview_mesh.mesh = shape.get_debug_mesh()
	_preview_mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_update_preview_material()


func _update_preview_material() -> void:
	var preview := _find_preview_mesh()
	if preview == null:
		return

	var material := StandardMaterial3D.new()
	material.albedo_color = preview_color
	material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	material.no_depth_test = false
	preview.material_override = material


func _shape_state(shape: Shape3D) -> String:
	if shape is ConvexPolygonShape3D:
		return str((shape as ConvexPolygonShape3D).points)

	return str(shape.get_rid())


func _find_collision_shape() -> CollisionShape3D:
	return get_node_or_null("CollisionShape3D") as CollisionShape3D


func _find_preview_mesh() -> MeshInstance3D:
	return get_node_or_null("PreviewMesh") as MeshInstance3D
