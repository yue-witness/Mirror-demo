shader_type canvas_item;
render_mode unshaded, blend_mix;

uniform float luminance_cutoff : hint_range(0.0, 1.0) = 0.70;
uniform float transition_width : hint_range(0.01, 0.50) = 0.20;
uniform float maximum_brightness : hint_range(0.10, 1.0) = 0.72;
uniform float highlight_suppression : hint_range(0.0, 0.90) = 0.0;

void fragment() {
    vec4 source = texture(TEXTURE, UV);
    float light_energy = max(source.r, max(source.g, source.b));
    float visible_feature = smoothstep(
        luminance_cutoff,
        min(1.0, luminance_cutoff + transition_width),
        light_energy);

    // The authored atlas provides rotation and node-level flicker. This
    // material only suppresses low-energy fill between visible features.
    vec3 controlled_color = min(
        source.rgb,
        vec3(maximum_brightness));
    float highlight_energy = smoothstep(0.80, 1.0, light_energy);
    float highlight_control = 1.0
        - highlight_energy * highlight_suppression;
    COLOR = vec4(
        controlled_color * visible_feature * highlight_control,
        source.a * visible_feature * highlight_control);
}
