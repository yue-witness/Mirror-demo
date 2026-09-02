extends Node

const CAPTURE_PATH := "res://_qa/formal-demo/04b-red-eye-anomaly.png"

var _failed := false


func _ready() -> void:
	await get_tree().process_frame
	await get_tree().process_frame

	var dialogue_ui := get_node("TutorDialogueUI") as Control

	var portrait_frame := dialogue_ui.get_node(
		"SafeArea/Layout/Content/SpeakerCard/SpeakerVBox/PortraitFrame") as Control
	var portrait := portrait_frame.get_node("PortraitTexture") as TextureRect
	var dialogue_text := dialogue_ui.get_node(
		"SafeArea/Layout/Content/DialogueCard/DialogueVBox/DialogueText") \
		as RichTextLabel
	_assert(dialogue_text.visible_characters_behavior == 1,
		"Tutor typewriter text is not shaped before character reveal.")
	_assert(portrait_frame.call("GetRingSegmentCount") >= 192,
		"Tutor portrait ring does not use a smooth high-detail arc.")

	_verify_choice_button_styles()

	portrait.call("SetState", 2)
	portrait.call("SetRedEyeAnomaly", true)
	await get_tree().process_frame
	await get_tree().process_frame

	var frame_display := portrait.get_node("FrameDisplay") as TextureRect
	_assert(_visual_center(portrait).distance_to(
			_visual_center(portrait_frame)) <= 1.0,
		"Tutor portrait control is not centred in its circular frame.")
	_assert(portrait.get("RedEyeAnomalyActive") == true,
		"Final Tutor line did not activate the red-eye pass.")
	_assert(frame_display.material is ShaderMaterial,
		"Red-eye pass is not isolated in a portrait shader material.")
	_assert(portrait.modulate == Color.WHITE,
		"Final anomaly still tints the entire portrait.")

	if DisplayServer.get_name() != "headless":
		var image := get_viewport().get_texture().get_image()
		var result := image.save_png(ProjectSettings.globalize_path(CAPTURE_PATH))
		_assert(result == OK, "Could not save visual QA capture: " + str(result))

	if not _failed:
		print("Tutor portrait visual smoke passed: centred atlas and local red-eye flash.")
	get_tree().quit(1 if _failed else 0)


func _visual_center(control: Control) -> Vector2:
	return control.get_global_transform() * (control.size / 2.0)


func _verify_choice_button_styles() -> void:
	var gameplay_scene := load("res://scenes/ui/GameplayHUD.tscn") as PackedScene
	var gameplay_hud := gameplay_scene.instantiate()
	var choice_row := gameplay_hud.get_node(
		"SafeArea/Layout/Content/Center/ActionRow/ChoiceStack/ChoiceRow")

	for choice_name in ["Choice1", "Choice2", "Choice3"]:
		var button := choice_row.get_node(choice_name) as Button
		var normal := button.get_theme_stylebox("normal") as StyleBoxFlat
		var selected := button.get_theme_stylebox("pressed") as StyleBoxFlat
		_assert(normal.bg_color.a <= 0.01,
			choice_name + " non-selected background is not transparent.")
		_assert(_minimum_border_width(normal) >= 4,
			choice_name + " selectable border is not visibly thick.")
		_assert(selected.bg_color.a >= 0.75,
			choice_name + " selected fill is not clearly contrasted.")
		_assert(_minimum_border_width(selected) > _minimum_border_width(normal),
			choice_name + " selected border is not stronger than normal.")

	gameplay_hud.free()


func _minimum_border_width(style: StyleBoxFlat) -> int:
	return mini(
		mini(style.border_width_left, style.border_width_top),
		mini(style.border_width_right, style.border_width_bottom))


func _assert(condition: bool, message: String) -> void:
	if condition:
		return

	_failed = true
	push_error(message)
