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
		# Lift each tee anchor (and its child HoleNumber label) to its own ground height,
		# which can differ from the green when tee and pin sit at different elevations.
		# The Post is a MeshInstance3D child and rides up with the Hole node, so skip it.
		for tee in hole.get_children():
			if not (tee is Node3D) or tee is MeshInstance3D:
				continue
			var tee_height = _terrain_height(terrain, _global_position_xz(tee))
			if tee_height == null:
				continue
			tee.position.y = tee_height - hole_height


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
