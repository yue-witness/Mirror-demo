shader_type canvas_item;

// The final anomaly is deliberately restricted to the Tutor's two eye zones.
// The metallic body, green emitters, transparent background, and circular UI
// frame all retain their authored colours.
uniform vec4 eye_red : source_color = vec4(1.0, 0.02, 0.045, 1.0);
uniform float flash_cycles_per_second : hint_range(0.25, 6.0) = 2.15;
uniform vec2 frame_uv_origin = vec2(0.0);
uniform vec2 frame_uv_size = vec2(1.0);

float ellipse_mask(vec2 uv, vec2 center, vec2 radius) {
	vec2 relative = (uv - center) / radius;
	return 1.0 - smoothstep(0.72, 1.0, dot(relative, relative));
}

void fragment() {
	vec4 source = texture(TEXTURE, UV);
	vec2 portrait_uv = (UV - frame_uv_origin) / max(frame_uv_size, vec2(0.0001));

	float left_eye = ellipse_mask(
		portrait_uv,
		vec2(0.355, 0.485),
		vec2(0.155, 0.105));
	float right_eye = ellipse_mask(
		portrait_uv,
		vec2(0.655, 0.485),
		vec2(0.155, 0.105));
	float eye_region = max(left_eye, right_eye);

	// Tutor eye light is green in the source art. Colour dominance prevents the
	// red pass from affecting the surrounding silver shell or black faceplate.
	float green_dominance = source.g - max(source.r, source.b);
	float luminous_eye = smoothstep(0.07, 0.42, green_dominance)
		* smoothstep(0.16, 0.72, source.g)
		* eye_region;

	float wave = sin(TIME * 6.2831853 * flash_cycles_per_second);
	float pulse = 0.5 + 0.5 * wave;
	float red_strength = luminous_eye;
	float flash_brightness = mix(0.76, 1.32, pulse);
	vec3 red_eye_colour = eye_red.rgb
		* max(0.62, source.g * 1.28)
		* flash_brightness;
	vec3 result = mix(source.rgb, red_eye_colour, red_strength);

	// A restrained local halo makes the flash readable without colouring the
	// portrait or frame as a whole.
	float halo = eye_region * luminous_eye * pulse * 0.32;
	result += eye_red.rgb * halo;

	COLOR = vec4(result, source.a);
}
