shader_type canvas_item;
render_mode unshaded;

// Recreates the thin phosphor-dot breakup visible in the supplied reference.
// The floor keeps small text readable while the 3 px cell creates a visible
// scanline/dot rhythm at the project's native 1920 x 1080 viewport.
uniform float dot_pitch : hint_range(2.0, 6.0) = 3.0;
uniform float pattern_floor : hint_range(0.0, 1.0) = 0.44;
uniform float brightness : hint_range(1.0, 2.0) = 1.24;
uniform float halo_strength : hint_range(0.0, 0.5) = 0.035;

void fragment() {
	vec4 glyph = texture(TEXTURE, UV);
	vec4 tinted = glyph * COLOR;
	vec2 pixel = UV / max(TEXTURE_PIXEL_SIZE, vec2(0.00001));
	vec2 cell = fract(pixel / dot_pitch) - vec2(0.5);

	float dot = 1.0 - smoothstep(0.24, 0.54, length(cell));
	float scanline = mix(
		0.62,
		1.0,
		smoothstep(0.14, 0.70, fract(pixel.y / dot_pitch)));
	float matrix = mix(pattern_floor, 1.0, dot * scanline);

	vec2 halo_step = TEXTURE_PIXEL_SIZE * 1.35;
	float halo = texture(TEXTURE, UV + vec2(halo_step.x, 0.0)).a;
	halo = max(halo, texture(TEXTURE, UV - vec2(halo_step.x, 0.0)).a);
	halo = max(halo, texture(TEXTURE, UV + vec2(0.0, halo_step.y)).a);
	halo = max(halo, texture(TEXTURE, UV - vec2(0.0, halo_step.y)).a);

	float alpha = max(tinted.a * matrix, halo * COLOR.a * halo_strength);
	COLOR = vec4(tinted.rgb * brightness, alpha);
}
