@tool
extends Node3D
## Lifts the exported hole markers onto the terrain surface.
##
## The course exporter authors every marker transform assuming flat ground at y=0
## (see addons/golf_course_design/CourseExportService.cs). On elevated terrain that
## buries the pin Post and the HoleNumber Label3D below the surface. This @tool script
## runs in both the editor (fixing the scene preview) and at runtime (fixing the
## still-visible posts) by sampling the live Terrain3D height and snapping each Hole
## node and its tee anchors to the surface.
##
## Visibility is left untouched: HoleNumber labels are hidden during play by
## course_play.gd:_hide_hole_numbers(); here we only correct elevation.

# Terrain3D loads its region data a couple of frames after data_directory is set, so
# get_height returns NAN until then. Poll a bounded number of frames before giving up.
const MAX_WAIT_FRAMES := 120
const TERRAIN_NODE_PATH := "../Terrain3D"
# Cup rim sits flush with the green surface (0). The snap below also tilts the cup to the
# sampled terrain normal, so the rim follows the green slope. Dial up a hair (e.g. 0.001)
# only if the upslope edge of the rim pokes above the surface on steep pins.
const CUP_TERRAIN_INSET_METERS := 0.0
const CUP_NORMAL_SAMPLE_METERS := 0.25


func _ready() -> void:
	_snap_when_ready()


func _snap_when_ready() -> void:
	# Probe readiness at the first hole's location rather than the HoleMarkers origin,
	# which may fall outside any terrain region and yield NAN forever.
	var probe := _first_hole_xz()
	var terrain: Node = null
	var frames := 0
	while frames < MAX_WAIT_FRAMES:
		terrain = get_node_or_null(TERRAIN_NODE_PATH)
		if terrain != null and _terrain_height(terrain, probe) != null:
			break
		await get_tree().process_frame
		frames += 1
	if terrain == null:
		return
	_snap_markers(terrain)


func _first_hole_xz() -> Vector2:
	for hole in get_children():
		if hole is Node3D:
			return _global_position_xz(hole)
	return _global_position_xz(self)


func _snap_markers(terrain: Node) -> void:
	for hole in get_children():
		if not (hole is Node3D):
			continue
		var hole_height = _terrain_height(terrain, _global_position_xz(hole))
		if hole_height == null:
			continue
		hole.position.y = hole_height
		_snap_cup_to_green_slope(terrain, hole)
		# Lift each tee anchor (and its child HoleNumber label) to its own ground height,
		# which can differ from the green when tee and pin sit at different elevations.
		# The Post is a MeshInstance3D child and rides up with the Hole node, so skip it.
		for tee in hole.get_children():
			if not (tee is Node3D) or tee is MeshInstance3D:
				continue
			if StringName(tee.name) == &"Cup":
				continue
			var tee_height = _terrain_height(terrain, _global_position_xz(tee))
			if tee_height == null:
				continue
			tee.position.y = tee_height - hole_height


func _snap_cup_to_green_slope(terrain: Node, hole: Node3D) -> void:
	var cup := hole.get_node_or_null("Cup") as Node3D
	if cup == null:
		return

	var cup_xz := _global_position_xz(cup)
	var cup_height = _terrain_height(terrain, cup_xz)
	if cup_height == null:
		return

	var normal := _terrain_normal(terrain, cup_xz)
	var cup_position := Vector3(cup_xz.x, cup_height - CUP_TERRAIN_INSET_METERS, cup_xz.y)
	cup.global_transform = Transform3D(_basis_from_normal(normal), cup_position)


func _terrain_normal(terrain: Node, point: Vector2) -> Vector3:
	var sample := CUP_NORMAL_SAMPLE_METERS
	var left = _terrain_height(terrain, point + Vector2.LEFT * sample)
	var right = _terrain_height(terrain, point + Vector2.RIGHT * sample)
	var z_minus = _terrain_height(terrain, point + Vector2.UP * sample)
	var z_plus = _terrain_height(terrain, point + Vector2.DOWN * sample)
	if left == null or right == null or z_minus == null or z_plus == null:
		return Vector3.UP

	var dx := Vector3(sample * 2.0, float(right) - float(left), 0.0)
	var dz := Vector3(0.0, float(z_plus) - float(z_minus), sample * 2.0)
	var normal := dz.cross(dx)
	if normal.length_squared() < 0.000001:
		return Vector3.UP
	return normal.normalized()


func _basis_from_normal(normal: Vector3) -> Basis:
	var y_axis := normal.normalized() if normal.length_squared() > 0.000001 else Vector3.UP
	var x_axis := Vector3.RIGHT - y_axis * Vector3.RIGHT.dot(y_axis)
	if x_axis.length_squared() < 0.000001:
		x_axis = Vector3.FORWARD - y_axis * Vector3.FORWARD.dot(y_axis)
	x_axis = x_axis.normalized()
	var z_axis := x_axis.cross(y_axis).normalized()
	return Basis(x_axis, y_axis, z_axis).orthonormalized()


# Returns the terrain height at the given world X/Z, or null when the terrain data is
# unavailable or not yet loaded (get_height yields a non-finite value for those).
func _terrain_height(terrain: Node, point: Vector2):
	var data = terrain.get("data")
	if not (data is Object) or not data.has_method("get_height"):
		return null
	var height = data.call("get_height", Vector3(point.x, 0.0, point.y))
	if typeof(height) == TYPE_FLOAT or typeof(height) == TYPE_INT:
		var value := float(height)
		if is_finite(value):
			return value
	return null


# World X/Z of a node as a Vector2. The parent Y shift does not affect X/Z, so this is
# stable to read even after sibling holes have already been lifted.
func _global_position_xz(node: Node3D) -> Vector2:
	var pos := node.global_position
	return Vector2(pos.x, pos.z)
