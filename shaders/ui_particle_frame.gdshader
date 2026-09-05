shader_type canvas_item;
render_mode unshaded, blend_add;

uniform vec4 matrix_color : source_color = vec4(0.22, 1.0, 0.23, 0.18);
uniform vec4 particle_color : source_color = vec4(0.68, 1.0, 0.72, 1.0);
uniform float aspect_ratio = 1.0;
uniform vec2 border_inset_uv = vec2(0.02);
uniform float pixel_size_y = 0.004;
uniform float phase_offset = 0.0;
uniform float speed : hint_range(0.01, 1.0) = 0.04;
uniform float corner_radius : hint_range(0.0, 1.0) = 0.11;
uniform float line_width : hint_range(0.001, 0.08) = 0.004;
uniform float dot_density : hint_range(12.0, 180.0) = 72.0;
uniform float trail_length : hint_range(0.02, 0.50) = 0.22;
uniform float trail_decay : hint_range(2.0, 40.0) = 14.0;
uniform float diffusion : hint_range(0.0, 10.0) = 3.0;
uniform float particle_width_scale : hint_range(1.0, 3.0) = 1.65;

float rounded_box_distance(vec2 point, vec2 half_size, float radius) {
    vec2 q = abs(point) - half_size + vec2(radius);
    return length(max(q, vec2(0.0)))
        + min(max(q.x, q.y), 0.0)
        - radius;
}

float rounded_perimeter_coordinate(
    vec2 point,
    vec2 half_size,
    float radius) {
    vec2 absolute_point = abs(point);
    vec2 straight_extent = max(half_size - vec2(radius), vec2(0.0));
    vec2 edge_distance = half_size - absolute_point;
    float horizontal_length = straight_extent.x * 2.0;
    float vertical_length = straight_extent.y * 2.0;
    float quarter_arc_length = radius * PI * 0.5;
    float perimeter = 2.0 * (horizontal_length + vertical_length)
        + quarter_arc_length * 4.0;
    float distance;

    bool inside_horizontal_span = absolute_point.x <= straight_extent.x;
    bool inside_vertical_span = absolute_point.y <= straight_extent.y;
    bool nearest_side_is_horizontal = edge_distance.y <= edge_distance.x;

    if (inside_horizontal_span
        && (!inside_vertical_span || nearest_side_is_horizontal)) {
        if (point.y < 0.0) {
            distance = point.x + straight_extent.x;
        } else {
            distance = horizontal_length
                + quarter_arc_length * 2.0
                + vertical_length
                + straight_extent.x
                - point.x;
        }
    } else if (inside_vertical_span) {
        if (point.x > 0.0) {
            distance = horizontal_length
                + quarter_arc_length
                + point.y
                + straight_extent.y;
        } else {
            distance = horizontal_length * 2.0
                + quarter_arc_length * 3.0
                + vertical_length
                + straight_extent.y
                - point.y;
        }
    } else {
        // Corner pixels use their polar angle around the true corner centre.
        // This keeps both the comet head and its tail on the same rounded arc.
        vec2 corner_delta = max(
            absolute_point - straight_extent,
            vec2(0.0));
        float corner_angle = clamp(
            atan(corner_delta.y, corner_delta.x),
            0.0,
            PI * 0.5);

        if (point.x >= 0.0 && point.y < 0.0) {
            distance = horizontal_length
                + radius * (PI * 0.5 - corner_angle);
        } else if (point.x >= 0.0) {
            distance = horizontal_length
                + quarter_arc_length
                + vertical_length
                + radius * corner_angle;
        } else if (point.y >= 0.0) {
            distance = horizontal_length * 2.0
                + quarter_arc_length * 2.0
                + vertical_length
                + radius * (PI * 0.5 - corner_angle);
        } else {
            distance = horizontal_length * 2.0
                + quarter_arc_length * 3.0
                + vertical_length * 2.0
                + radius * corner_angle;
        }
    }

    return fract(distance / max(perimeter, 0.0001));
}

float circular_distance(float from_value, float to_value) {
    float delta = abs(from_value - to_value);
    return min(delta, 1.0 - delta);
}

float particle_stream(
    float path_position,
    float signed_distance,
    float head_position,
    float stream_seed,
    float core_width) {
    float behind = fract(head_position - path_position);
    float active = 1.0 - smoothstep(
        trail_length * 0.82,
        trail_length,
        behind);
    float fade = exp(-behind * trail_decay) * active;

    // The tail travels backward along the perimeter. Its centre drifts only
    // outward as it ages, so no light ray can project into the frame centre.
    float particle_wave = 0.5 + 0.5 * cos(
        behind * dot_density * 1.35 * TAU + stream_seed);
    float particle_beads = pow(particle_wave, 12.0);
    float outward_offset = behind * core_width * diffusion * 4.0;
    float trail_width = core_width * (1.0 + behind * diffusion * 3.2);
    float lateral = (signed_distance - outward_offset)
        / max(0.0001, trail_width);
    float outward_gate = smoothstep(
        -core_width * 0.55,
        core_width * 0.45,
        signed_distance);
    float diffused_tail = exp(-lateral * lateral) * fade * outward_gate;

    float head_distance = circular_distance(path_position, head_position);
    float head = exp(-head_distance * head_distance * 9000.0)
        * exp(-pow(
            signed_distance / max(0.0001, core_width * 1.22),
            2.0));
    float head_halo = exp(-head_distance * head_distance * 3000.0)
        * exp(-pow(
            signed_distance / max(0.0001, core_width * 2.2),
            2.0));

    // A second fade factor prevents isolated bright beads from surviving near
    // the tail cutoff after the surrounding glow has already disappeared.
    float bead_fade = fade * fade;
    return particle_beads * diffused_tail * bead_fade * 0.72
        + diffused_tail * 0.42
        + head * 1.38
        + head_halo * 0.28;
}

void fragment() {
    float safe_aspect = max(aspect_ratio, 0.12);
    vec2 point = UV * 2.0 - 1.0;
    point.x *= safe_aspect;

    vec2 half_size = vec2(
        safe_aspect * (1.0 - border_inset_uv.x * 2.0),
        1.0 - border_inset_uv.y * 2.0);
    // A fully rounded square has radius == half_size and must remain a circle.
    // The former 48% cap flattened portrait frames into rounded rectangles.
    float radius = clamp(
        corner_radius,
        0.0,
        min(half_size.x, half_size.y));
    float signed_distance = rounded_box_distance(point, half_size, radius);
    float border_distance = abs(signed_distance);
    float path_position = rounded_perimeter_coordinate(
        point,
        half_size,
        radius);
    float core_width = max(line_width, pixel_size_y * 1.25);
    float particle_core_width = core_width * particle_width_scale;

    // Hard safety boundary: the particle field may straddle the original line
    // by about one pixel, but can never project toward the frame centre.
    if (signed_distance < -core_width * 1.25) {
        discard;
    }

    float matrix_wave = 0.5 + 0.5 * cos(path_position * dot_density * TAU);
    float matrix_dots = pow(matrix_wave, 22.0);
    float normalized_matrix_distance = border_distance
        / max(0.0001, core_width * 0.72);
    float matrix_line = exp(
        -normalized_matrix_distance * normalized_matrix_distance);
    float matrix_intensity = matrix_dots * matrix_line * matrix_color.a;

    float time_phase = fract(TIME * speed + phase_offset);
    float first_stream = particle_stream(
        path_position,
        signed_distance,
        time_phase,
        0.0,
        particle_core_width);
    float second_stream = particle_stream(
        path_position,
        signed_distance,
        fract(time_phase + 0.47),
        2.17,
        particle_core_width);
    float third_stream = particle_stream(
        path_position,
        signed_distance,
        fract(time_phase + 0.69),
        4.31,
        particle_core_width);
    float particle_intensity = (
        first_stream
        + second_stream * 0.76
        + third_stream * 0.64) * 0.92 * particle_color.a;

    vec3 rgb = matrix_color.rgb * matrix_intensity
        + particle_color.rgb * particle_intensity;
    float alpha = clamp(matrix_intensity + particle_intensity, 0.0, 1.0);
    COLOR = vec4(rgb, alpha);
}
