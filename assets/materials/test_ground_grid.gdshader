shader_type spatial;

uniform vec4 base_color : source_color = vec4(0.19, 0.23, 0.21, 1.0);
uniform vec4 minor_line_color : source_color = vec4(0.31, 0.38, 0.34, 1.0);
uniform vec4 major_line_color : source_color = vec4(0.48, 0.59, 0.52, 1.0);
uniform vec2 grid_origin = vec2(0.0);
uniform float minor_grid_spacing = 1.0;
uniform float major_grid_spacing = 5.0;
uniform float minor_line_width = 0.018;
uniform float major_line_width = 0.035;
uniform float grid_fade_start = 24.0;
uniform float grid_fade_end = 80.0;

varying vec3 world_position;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float grid_line(vec2 world_xz, float spacing, float width) {
	vec2 coordinate = (world_xz - grid_origin) / spacing;
	vec2 distance_to_line = abs(fract(coordinate - vec2(0.5)) - vec2(0.5)) * spacing;
	vec2 antialias_width = fwidth(coordinate) * spacing;
	vec2 coverage = 1.0 - smoothstep(vec2(width) - antialias_width,
		vec2(width) + antialias_width, distance_to_line);
	return max(coverage.x, coverage.y);
}

void fragment() {
	float minor_grid = grid_line(world_position.xz, minor_grid_spacing, minor_line_width);
	float major_grid = grid_line(world_position.xz, major_grid_spacing, major_line_width);
	float distance_to_camera = distance(CAMERA_POSITION_WORLD, world_position);
	float grid_fade = 1.0 - smoothstep(grid_fade_start, grid_fade_end, distance_to_camera);

	vec3 color = mix(base_color.rgb, minor_line_color.rgb, minor_grid * grid_fade);
	color = mix(color, major_line_color.rgb, major_grid * grid_fade);
	ALBEDO = color;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
}
