extends "res://Player/ball_trail.gd"

# Course-play shot tracer. Unlike the Range's flat ribbon (which goes edge-on and
# disappears when the follow camera looks straight down the flight line), this builds
# a solid 3D tube. A tube has a circular cross-section, so it stays visible from every
# angle — including head-on while the camera follows directly behind the ball.

@export var tube_radius: float = 0.09
@export var tube_sides: int = 6


func _ready():
	super._ready()
	# Red with a slight glow. Transparency MUST be driven by albedo_color.a: vertex
	# colors are ignored for alpha unless vertex_color_use_as_albedo is on (it isn't),
	# so the alpha lives on the material here.
	color = Color(0.7, 0.0, 0.0, 0.35)
	material.vertex_color_use_as_albedo = false
	material.albedo_color = color
	# Bold, clearly-visible red with a real glow, while still slightly see-through.
	material.emission = Color(color.r, color.g, color.b, 1.0)
	material.emission_energy_multiplier = 1.0


# Override the Range's ribbon builder with tube geometry. Reuses the base class's
# points/material and the _process() -> draw() loop unchanged.
func create_ribbon_mesh():
	var sides: int = max(3, tube_sides)
	var point_count: int = points.size()

	var vertices := PackedVector3Array()
	var colors := PackedColorArray()
	var indices := PackedInt32Array()

	# Emit a ring of vertices perpendicular to the path tangent at each point.
	for i in range(point_count):
		var point: Vector3 = points[i]

		var forward: Vector3 = Vector3.ZERO
		if i < point_count - 1:
			forward = (points[i + 1] - point).normalized()
		elif i > 0:
			forward = (point - points[i - 1]).normalized()
		else:
			forward = Vector3.FORWARD

		# Stable perpendicular frame around the tangent.
		var right: Vector3 = forward.cross(Vector3.UP)
		if right.length() < 0.01:
			right = Vector3.RIGHT
		right = right.normalized()
		var up: Vector3 = right.cross(forward).normalized()

		for j in range(sides):
			var angle: float = TAU * float(j) / float(sides)
			var offset: Vector3 = (right * cos(angle) + up * sin(angle)) * tube_radius
			vertices.append(point + offset)
			colors.append(color)

	# Connect consecutive rings with quad pairs (two triangles each).
	for i in range(1, point_count):
		var prev_base: int = (i - 1) * sides
		var curr_base: int = i * sides
		for j in range(sides):
			var next_j: int = (j + 1) % sides
			var a: int = prev_base + j
			var b: int = prev_base + next_j
			var c: int = curr_base + j
			var d: int = curr_base + next_j
			indices.append(a)
			indices.append(c)
			indices.append(b)
			indices.append(b)
			indices.append(c)
			indices.append(d)

	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_COLOR] = colors
	arrays[Mesh.ARRAY_INDEX] = indices

	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	mesh.surface_set_material(0, material)
