shader_type canvas_item;
render_mode unshaded, blend_add;

uniform vec4 matrix_color : source_color = vec4(0.22, 1.0, 0.23, 0.58);
uniform vec4 particle_color : source_color = vec4(0.68, 1.0, 0.72, 1.0);
uniform float aspect_ratio = 1.0;
uniform float phase_offset = 0.0;
uniform float speed : hint_range(0.01, 1.0) = 0.075;
uniform float corner_radius : hint_range(0.01, 0.40) = 0.11;
uniform float line_width : hint_range(0.001, 0.08) = 0.007;
uniform float dot_density : hint_range(12.0, 180.0) = 92.0;
uniform float trail_length : hint_range(0.02, 0.50) = 0.14;
uniform float trail_decay : hint_range(2.0, 40.0) = 24.0;
uniform float diffusion : hint_range(0.0, 10.0) = 1.8;

float rounded_box_distance(vec2 point, vec2 half_size, float radius) {
    vec2 q = abs(point) - half_size + vec2(radius);
    return length(max(q, vec2(0.0)))
        + min(max(q.x, q.y), 0.0)
        - radius;
}

float perimeter_coordinate(vec2 point, vec2 half_size) {
    vec2 edge_distance = half_size - abs(point);
    float width = half_size.x * 2.0;
    float height = half_size.y * 2.0;
    float perimeter = 2.0 * (width + height);
    float distance;

    if (edge_distance.y < edge_distance.x) {
        if (point.y < 0.0) {
            distance = point.x + half_size.x;
        } else {
            distance = width + height + half_size.x - point.x;
        }
    } else {
        if (point.x > 0.0) {
            distance = width + point.y + half_size.y;
        } else {
            distance = width * 2.0 + height + half_size.y - point.y;
        }
    }

    return fract(distance / perimeter);
}

float circular_distance(float from_value, float to_value) {
    float delta = abs(from_value - to_value);
    return min(delta, 1.0 - delta);
}

float particle_stream(
    float path_position,
    float border_distance,
    float head_position,
    float stream_seed) {
    float behind = fract(head_position - path_position);
    float active = 1.0 - smoothstep(
        trail_length * 0.82,
        trail_length,
        behind);
    float fade = exp(-behind * trail_decay) * active;

    // Discrete bright particles sit inside a continuous, widening trail.
    float particle_wave = 0.5 + 0.5 * cos(
        behind * dot_density * 1.35 * TAU + stream_seed);
    float particles = pow(particle_wave, 18.0) * fade;
    float trail_width = line_width * (1.25 + behind * diffusion * 4.0);
    float normalized_border = border_distance / max(0.0001, trail_width);
    float trail_glow = exp(-normalized_border * normalized_border) * fade;

    float head_distance = circular_distance(path_position, head_position);
    float head = exp(-head_distance * head_distance * 15000.0)
        * exp(-border_distance / max(0.0001, line_width * 2.8));

    return particles * 0.58 + trail_glow * 0.32 + head * 0.92;
}

void fragment() {
    float safe_aspect = max(aspect_ratio, 0.12);
    vec2 point = UV * 2.0 - 1.0;
    point.x *= safe_aspect;

    vec2 half_size = vec2(safe_aspect, 1.0) - vec2(0.055);
    float radius = min(corner_radius, min(half_size.x, half_size.y) * 0.48);
    float signed_distance = rounded_box_distance(point, half_size, radius);
    float border_distance = abs(signed_distance);
    float path_position = perimeter_coordinate(point, half_size);

    float matrix_wave = 0.5 + 0.5 * cos(path_position * dot_density * TAU);
    float matrix_dots = pow(matrix_wave, 22.0);
    float matrix_line = exp(-border_distance / max(0.0001, line_width * 0.62));
    float matrix_intensity = matrix_dots * matrix_line * matrix_color.a;

    float time_phase = fract(TIME * speed + phase_offset);
    float first_stream = particle_stream(
        path_position,
        border_distance,
        time_phase,
        0.0);
    float second_stream = particle_stream(
        path_position,
        border_distance,
        fract(time_phase + 0.47),
        2.17);
    float particle_intensity = (first_stream + second_stream * 0.64) * 0.66;

    vec3 rgb = matrix_color.rgb * matrix_intensity
        + particle_color.rgb * particle_intensity;
    float alpha = clamp(matrix_intensity + particle_intensity, 0.0, 1.0);
    COLOR = vec4(rgb, alpha);
}
