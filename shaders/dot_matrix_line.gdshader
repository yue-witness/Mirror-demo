shader_type canvas_item;
render_mode unshaded;

// Screen-space phosphor cells keep the divider visibly segmented even though
// ColorRect has no glyph texture atlas to provide pixel coordinates.
uniform float dot_pitch : hint_range(2.0, 8.0) = 4.0;
uniform float brightness : hint_range(1.0, 2.0) = 1.3;

void fragment() {
	vec2 cell = mod(FRAGCOORD.xy, vec2(dot_pitch));
	float horizontal_gap = step(1.0, cell.x);
	float vertical_glow = mix(0.58, 1.0, smoothstep(0.0, dot_pitch, cell.y));
	float matrix = horizontal_gap * vertical_glow;
	COLOR = vec4(COLOR.rgb * brightness, COLOR.a * matrix);
}
