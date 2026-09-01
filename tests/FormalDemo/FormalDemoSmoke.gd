extends Node

const SAVE_PATH := "res://.godot/tests/formal_demo_save.json"
const QA_DIRECTORY := "res://_qa/formal-demo"

var _failed := false
var _main: Control


func _ready() -> void:
	_prepare_test_paths()

	var packed := load("res://scenes/main.tscn") as PackedScene
	_assert(packed != null, "The formal main scene could not be loaded.")
	if _failed:
		get_tree().quit(1)
		return

	_main = packed.instantiate() as Control
	_main.set("SavePath", SAVE_PATH)
	_main.set("FastMode", true)
	_main.set("TestSeed", 772774)
	add_child(_main)

	await _settle_frames()
	_assert(_main.get_node("TitleScreen").visible, "Title screen is not visible at startup.")
	_assert(not _main.get_node("GameplayHUD").visible, "Gameplay HUD leaked onto the title screen.")
	var transition_overlay := _main.get_node("UiTransitionOverlay") as Control
	_assert(transition_overlay.visible,
		"The primary-screen transition did not cover the startup cut.")
	_assert(_main.has_node("UiAudioController/HoverPlayer")
		and _main.has_node("UiAudioController/ActionPlayer")
		and _main.has_node("UiAudioController/EventPlayer"),
		"The UI sound router did not create its three playback channels.")
	var background := _main.get_node("Background") as TextureRect
	_assert(background.texture.resource_path.ends_with(
		"command_chamber_static_scanner.png"),
		"The chamber background with the restored static scanner is not active.")
	_assert(background.texture.get_width() == 1920
		and background.texture.get_height() == 1080,
		"The active chamber background is not stored at 1920x1080.")
	var container_glow := _main.get_node(
		"BackgroundVfx/ContainerGlow") as TextureRect
	_assert(is_equal_approx(container_glow.position.x, 1595.0)
		and container_glow.size.is_equal_approx(Vector2(330.0, 380.0)),
		"The upper cage is no longer horizontally aligned with its ceiling mount.")
	var container_frame := container_glow.texture as AtlasTexture
	_assert(container_frame != null
		and container_frame.atlas.resource_path.ends_with("container_glow_120f.png"),
		"The authored upper-container 120-frame sequence is missing.")
	_assert(container_frame.atlas.get_width() == 3840
		and container_frame.atlas.get_height() == 4096
		and container_frame.region.size == Vector2(256.0, 512.0),
		"The upper-container atlas is not laid out as 120 frames in a 15x8 grid.")
	_assert(not _main.has_node("BackgroundVfx/ScannerGlow"),
		"A dynamic scanner layer still covers the original static background disk.")
	var container_cleanup := container_glow.material as ShaderMaterial
	_assert(container_cleanup != null
		and container_cleanup.shader.resource_path.ends_with(
			"background_vfx_cleanup.gdshader"),
		"The upper sequence no longer suppresses non-node glow wash.")
	var title_particle_frame := _main.get_node(
		"TitleScreen/MenuGlass/ParticleFrame") as ColorRect
	var title_particle_material := title_particle_frame.material as ShaderMaterial
	_assert(title_particle_material != null
		and title_particle_material.shader.resource_path.ends_with(
			"ui_particle_frame.gdshader"),
		"The title frame does not use the particle-trail dot-matrix shader.")
	var trail_length: float = title_particle_material.get_shader_parameter(
		"trail_length")
	var trail_diffusion: float = title_particle_material.get_shader_parameter(
		"diffusion")
	var particle_width_scale: float = title_particle_material.get_shader_parameter(
		"particle_width_scale")
	var matrix_color: Color = title_particle_material.get_shader_parameter(
		"matrix_color")
	_assert(trail_length >= 0.20 and trail_diffusion >= 2.99
		and particle_width_scale >= 1.5 and matrix_color.a <= 0.181,
		"The frame shader no longer has a visible diffusing fade trail.")
	var border_inset: Vector2 = title_particle_material.get_shader_parameter(
		"border_inset_uv")
	var expected_inset := Vector2(
		17.0 / title_particle_frame.size.x,
		17.0 / title_particle_frame.size.y)
	_assert(border_inset.is_equal_approx(expected_inset),
		"The moving particles are not centred on the original frame border.")
	var initial_container_region := container_frame.region
	var fixed_container_position := container_glow.position
	var fixed_container_rotation := container_glow.rotation
	await get_tree().create_timer(0.24).timeout
	_assert(container_frame.region != initial_container_region,
		"The upper-container 120-frame sequence is present but not advancing.")
	var floating_delta := container_glow.position - fixed_container_position
	_assert(is_zero_approx(floating_delta.x)
		and absf(floating_delta.y) > 0.2
		and absf(floating_delta.y) <= 6.1
		and container_glow.scale == Vector2.ONE
		and container_glow.rotation == fixed_container_rotation
		and is_zero_approx(container_glow.rotation),
		"The upper background VFX no longer performs only its bounded vertical float.")
	_assert(not _button("TitleScreen/MenuGlass/MenuVBox/ContinueButton").visible,
		"Continue must remain hidden without an unfinished save.")
	_assert((_main.get_node(
		"TitleScreen/MenuGlass/MenuVBox/NewGameButton/DotMatrixText") as Label).material is ShaderMaterial,
		"Title button text is not rendered through the dot-matrix shader.")
	_assert((_main.get_node(
		"TitleScreen/MenuGlass/MenuVBox/LaserRule") as ColorRect).material is ShaderMaterial,
		"The title divider is not rendered through the dot-matrix line shader.")
	_capture("01-title.png")
	await get_tree().create_timer(0.42).timeout
	_assert(not transition_overlay.visible,
		"The primary-screen transition did not release input after its reveal.")
	_capture("01b-title-motion.png")

	_press("TitleScreen/MenuGlass/MenuVBox/NewGameButton")
	await get_tree().create_timer(0.46).timeout
	await _settle_frames()
	_assert(_main.get_node("GameplayHUD/ChapterOverlay").visible,
		"New Game did not route to the Chapter 0 splash.")
	_assert(not _main.has_node(
		"GameplayHUD/ChapterOverlay/ChapterGlass/ChapterVBox/ChapterContinue"),
		"The obsolete chapter continue button is still present.")
	_capture("02-chapter-0.png")

	_left_click()
	await get_tree().create_timer(0.44).timeout
	await _settle_frames()
	_assert(_main.get_node("TutorDialogueUI").visible,
		"Dialogue-only phase did not use TutorDialogueUI.")
	_assert(not _main.get_node("GameplayHUD").visible,
		"Gameplay HUD remained visible on a dialogue-only page.")
	_assert(not _main.has_node(
		"TutorDialogueUI/SafeArea/Layout/Content/DialogueCard/DialogueVBox/ContinueButton"),
		"The obsolete dialogue continue button is still present.")
	var dialogue_text := _main.get_node(
		"TutorDialogueUI/SafeArea/Layout/Content/DialogueCard/DialogueVBox/DialogueText") as RichTextLabel
	var tutor_speech := _main.get_node("TutorSpeechPlayer") as AudioStreamPlayer
	var completion_cue := _main.get_node(
		"TutorDialogueUI/SafeArea/Layout/Content/DialogueCard/DialogueVBox/DialogueText/CompletionCue") as Label
	var opening_speaker_name := _main.get_node(
		"TutorDialogueUI/SafeArea/Layout/Content/SpeakerCard/SpeakerVBox/SpeakerName") as Label
	var opening_portrait_frame := _main.get_node(
		"TutorDialogueUI/SafeArea/Layout/Content/SpeakerCard/SpeakerVBox/PortraitFrame") as Control
	var opening_portrait := opening_portrait_frame.get_node("PortraitTexture") as Control
	_assert(not opening_speaker_name.text.to_lower().contains("online"),
		"The dialogue-only Tutor identity still exposes ONLINE wording.")
	_assert(_visual_center(opening_portrait).distance_to(
			_visual_center(opening_portrait_frame)) <= 1.0,
		"The dialogue-only Tutor portrait is not centred in its frame.")
	_assert(dialogue_text.get_theme_font_size("normal_font_size") >= 32
		and dialogue_text.vertical_alignment == VERTICAL_ALIGNMENT_CENTER,
		"TutorDialogue text is not enlarged and vertically centred.")
	_assert(dialogue_text.visible_characters != -1,
		"Tutor dialogue did not begin with the typewriter animation.")
	_assert(tutor_speech.playing and tutor_speech.stream != null,
		"The revised physical-space arrival did not use its regenerated cue.")
	_capture("02b-tutor-dialogue.png")

	for index in range(6):
		var stream_before_click := tutor_speech.stream
		if dialogue_text.visible_characters != -1:
			_left_click()
			await _settle_frames()
			if index == 0:
				_assert(dialogue_text.visible_characters == -1,
					"The first dialogue click did not reveal the complete line.")
				_assert(completion_cue.visible,
					"The completed Tutor line did not reveal the animated advance cue.")
				_assert(tutor_speech.playing
					and tutor_speech.stream == stream_before_click,
					"Completing the arrival text interrupted its regenerated cue.")
		if index < 3:
			_capture("02c-background-%02d.png" % (index + 1))
		if index == 2:
			var speaker_name := _main.get_node(
				"TutorDialogueUI/SafeArea/Layout/Content/SpeakerCard/SpeakerVBox/SpeakerName") as Label
			_assert(speaker_name.text.contains("S-17"),
				"S-17 dialogue did not switch the visible speaker identity.")
			_assert(not tutor_speech.playing and tutor_speech.stream == null,
				"S-17 incorrectly received a generated Tutor voice cue.")
		_left_click()
		await _settle_frames()
		if index == 0:
			_assert(tutor_speech.playing
				and tutor_speech.stream != stream_before_click,
				"The second dialogue click did not replace the skipped voice cue.")

	_assert(_main.get_node("GameplayHUD/ChapterOverlay").visible,
		"Background dialogue did not route to the Chapter 1 splash.")
	_left_click()
	await _settle_frames()

	for index in range(6):
		await _advance_dialogue_page()
	await get_tree().create_timer(0.44).timeout

	var choice_one := _button(
		"GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ChoiceStack/ChoiceRow/Choice1")
	var choice_two := _button(
		"GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ChoiceStack/ChoiceRow/Choice2")
	var choice_three := _button(
		"GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ChoiceStack/ChoiceRow/Choice3")
	var confirm := _button(
		"GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ConfirmButton")
	var choice_center_y := choice_one.global_position.y + choice_one.size.y / 2.0
	var confirm_center_y := confirm.global_position.y + confirm.size.y / 2.0
	var choice_positions := [
		choice_one.global_position,
		choice_two.global_position,
		choice_three.global_position]
	var confirm_position := confirm.global_position
	_assert(is_equal_approx(choice_center_y, confirm_center_y),
		"The three central choices are not vertically centred between the panels.")
	var choice_one_text := choice_one.get_node("DotMatrixText") as Label
	var choice_two_text := choice_two.get_node("DotMatrixText") as Label
	var choice_three_text := choice_three.get_node("DotMatrixText") as Label
	_assert(choice_one_text.get_theme_color("font_color").is_equal_approx(
		Color(0.22, 1.0, 0.23, 1.0))
		and choice_two_text.get_theme_color("font_color").is_equal_approx(
			Color(1.0, 0.76, 0.12, 1.0))
		and choice_three_text.get_theme_color("font_color").is_equal_approx(
			Color(1.0, 0.22, 0.38, 1.0)),
		"Choice text colors no longer match their corresponding frames.")
	_assert(choice_one.text.begins_with("A\n")
		and choice_two.text.begins_with("B\n")
		and choice_three.text.begins_with("C\n"),
		"The fixed A/B/C choice headings are missing.")
	for button in [choice_one, choice_two, choice_three, confirm]:
		var shader_text := button.get_node("DotMatrixText") as Label
		var disabled_cross := button.get_node("DisabledCross") as TextureRect
		_assert(shader_text.material is ShaderMaterial
			and disabled_cross.texture.resource_path.ends_with("dot_matrix_x.png"),
			"A central button caption or pre-rendered dot-matrix X is missing.")
		_assert(button.get_node("ParticleFrame").material is ShaderMaterial,
			"An original action-button border is missing its particle dot matrix.")
	var status_particle_frame := _main.get_node(
		"GameplayHUD/SafeArea/Layout/Content/LeftColumn/LeftStatus/ParticleFrame"
		) as ColorRect
	_assert(status_particle_frame.material is ShaderMaterial,
		"An original gameplay panel is missing its particle dot matrix.")
	var tutor_line: String = (_main.get_node(
		"GameplayHUD/SafeArea/Layout/Content/Center/DialoguePanel/DialogueVBox/Text") as RichTextLabel).text
	var gameplay_dialogue := _main.get_node(
		"GameplayHUD/SafeArea/Layout/Content/Center/DialoguePanel/DialogueVBox/Text") as RichTextLabel
	_assert(gameplay_dialogue.get_theme_font_size("normal_font_size") >= 30
		and gameplay_dialogue.vertical_alignment == VERTICAL_ALIGNMENT_CENTER,
		"Gameplay Tutor dialogue is not enlarged and vertically centred.")
	_assert(gameplay_dialogue.visible_characters != -1
		and not tutor_speech.playing,
		"High-frequency gameplay guidance must type without Tutor speech.")
	_assert(not _main.has_node("GameplayHUD/SafeArea/Layout/Header/HeaderRow/ScoreLabel"),
		"The removed top-centre score display is still present.")
	_assert(_main.has_node("GameplayHUD/SafeArea/Layout/Header/HeaderRow/HeaderSpacer"),
		"The top header no longer preserves its left/right alignment.")
	_assert(_main.has_node(
		"GameplayHUD/SafeArea/Layout/Content/Center/RemainingCard/RemainingVBox/StateRow/LatticeView"),
		"The Stability Lattice visualization is missing from gameplay.")
	var active_value := _main.get_node(
		"GameplayHUD/SafeArea/Layout/Content/Center/RemainingCard/RemainingVBox/StateRow/ActiveStack/RemainingValue") as Label
	var selection_value := _main.get_node(
		"GameplayHUD/SafeArea/Layout/Content/Center/RemainingCard/RemainingVBox/StateRow/SelectionStack/SelectionLabel") as Label
	_assert(active_value.global_position.x < selection_value.global_position.x,
		"Active and selected quantities are no longer positioned on opposite sides of the lattice.")
	_assert(active_value.material is ShaderMaterial,
		"The fluorescent text no longer has its dot-matrix shader material.")
	_assert((_main.get_node(
		"GameplayHUD/SafeArea/Layout/Content/LeftColumn/LeftStatus/LeftVBox/Rule") as ColorRect).material is ShaderMaterial
		and (_main.get_node(
			"GameplayHUD/SafeArea/Layout/Content/RightColumn/RightLog/RightVBox/Rule") as ColorRect).material is ShaderMaterial,
		"CURRENT STATUS or SYSTEM divider is missing its dot-matrix line shader.")
	var system_log := _main.get_node(
		"GameplayHUD/SafeArea/Layout/Content/RightColumn/RightLog/RightVBox/Log") as RichTextLabel
	_assert(system_log.scroll_active,
		"The fixed SYSTEM panel does not allow overflow scrolling.")
	var system_panel := _main.get_node(
		"GameplayHUD/SafeArea/Layout/Content/RightColumn/RightLog") as PanelContainer
	_assert(system_panel.custom_minimum_size.y == 390.0,
		"The SYSTEM panel no longer has its fixed reference height.")
	var center_panel := _main.get_node(
		"GameplayHUD/SafeArea/Layout/Content/Center/RemainingCard") as PanelContainer
	_assert(is_equal_approx(system_panel.size.y, center_panel.size.y),
		"The SYSTEM frame is not aligned with the central lattice frame.")
	var tutor_panel := _main.get_node(
		"GameplayHUD/SafeArea/Layout/Content/LeftColumn/TutorCard") as PanelContainer
	var gameplay_portrait_frame := _main.get_node(
		"GameplayHUD/SafeArea/Layout/Content/LeftColumn/TutorCard/TutorVBox/PortraitFrame") as Control
	var gameplay_portrait := gameplay_portrait_frame.get_node("PortraitTexture") as Control
	var gameplay_tutor_name := _main.get_node(
		"GameplayHUD/SafeArea/Layout/Content/LeftColumn/TutorCard/TutorVBox/TutorName") as Label
	_assert(not gameplay_tutor_name.text.to_lower().contains("online"),
		"The gameplay Tutor identity still exposes ONLINE wording.")
	_assert(_visual_center(gameplay_portrait).distance_to(
			_visual_center(gameplay_portrait_frame)) <= 1.0,
		"The gameplay Tutor portrait is not centred in its frame.")
	var dialogue_panel := _main.get_node(
		"GameplayHUD/SafeArea/Layout/Content/Center/DialoguePanel") as PanelContainer
	_assert(is_equal_approx(tutor_panel.global_position.y, dialogue_panel.global_position.y)
		and is_equal_approx(tutor_panel.size.y, dialogue_panel.size.y),
		"The Tutor portrait and dialogue frames are not aligned: Tutor y/height "
		+ str(tutor_panel.global_position.y) + "/" + str(tutor_panel.size.y)
		+ ", dialogue y/height " + str(dialogue_panel.global_position.y)
		+ "/" + str(dialogue_panel.size.y))
	_assert(choice_one.visible and not choice_one.disabled,
		"Bash gameplay did not open a legal player choice.")
	for button in [choice_one, choice_two, choice_three, confirm]:
		_assert(button.get_theme_stylebox("hover")
			!= button.get_theme_stylebox("normal"),
			"A central action button reuses its normal style while hovering.")
	_assert(confirm.visible and confirm.disabled,
		"Bash confirm must wait for an explicit player selection.")
	_capture("03-bash-gameplay.png")
	if DisplayServer.get_name() != "headless":
		Input.warp_mouse(choice_two.global_position + choice_two.size / 2.0)
		await _settle_frames()
		_capture("03-hover-choice.png")
	var live_system_log := system_log.text
	system_log.text = ("OVERFLOW ENTRY / SCROLL VERIFICATION\n").repeat(80)
	await _settle_frames()
	var system_scroll := system_log.get_v_scroll_bar()
	_assert(system_scroll.visible and system_scroll.max_value > system_scroll.page,
		"SYSTEM overflow did not activate its vertical scrollbar.")
	system_log.text = live_system_log

	_press(
		"GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ChoiceStack/ChoiceRow/Choice1")
	await _settle_frames()
	_assert(not confirm.disabled, "Selecting a legal Bash action did not enable confirm.")
	_assert(choice_one.text == "A\nSTAGED" and not choice_one.text.contains("✓"),
		"The selected choice changed its fixed A heading or retained a checkmark.")
	_assert(choice_one.scale == Vector2.ONE
		and choice_two.scale == Vector2.ONE
		and choice_three.scale == Vector2.ONE,
		"Selecting a choice changed the three-button geometry.")
	_assert(not (_main.get_node(
		"GameplayHUD/SafeArea/Layout/Content/Center/DialoguePanel/DialogueVBox/Text") as RichTextLabel).text.is_empty(),
		"The optional selection-stage Tutor dialogue left the panel empty.")
	_capture("03a-bash-selected.png")
	if DisplayServer.get_name() != "headless":
		Input.warp_mouse(confirm.global_position + confirm.size / 2.0)
		await _settle_frames()
		_capture("03a-confirm-hover.png")
	_main.set("FastMode", false)
	_press("GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ConfirmButton")
	await get_tree().process_frame
	_assert(not tutor_speech.playing and tutor_speech.stream == null,
		"Bash confirmation started overlapping intermediate Tutor speech.")
	_assert(gameplay_dialogue.text.is_empty(),
		"Bash confirmation left more than one Tutor line active.")
	_assert(choice_one.disabled and choice_two.disabled and choice_three.disabled,
		"Tutor selection did not immediately lock all three player choices.")
	_assert(confirm.visible and confirm.global_position == confirm_position,
		"Tutor selection hid or moved the Confirm button.")
	_assert(choice_one.global_position == choice_positions[0]
		and choice_two.global_position == choice_positions[1]
		and choice_three.global_position == choice_positions[2],
		"Tutor selection moved one or more choice buttons.")
	_assert((choice_one.get_node("DisabledCross") as TextureRect).visible
		and (choice_two.get_node("DisabledCross") as TextureRect).visible
		and (choice_three.get_node("DisabledCross") as TextureRect).visible,
		"Tutor selection did not expose the disabled X overlays.")
	for button in [choice_one, choice_two, choice_three, confirm]:
		var shader_text := button.get_node("DotMatrixText") as Label
		var disabled_cross := button.get_node("DisabledCross") as TextureRect
		_assert(shader_text.text == button.text and not shader_text.text.is_empty(),
			"A disabled X replaced the button's original caption.")
		_assert(disabled_cross.z_index > shader_text.z_index,
			"A disabled X is not layered above its original caption.")
	_assert(choice_one.get_theme_stylebox("disabled").get_bg_color().a == 0.0
		and choice_two.get_theme_stylebox("disabled").get_bg_color().a == 0.0
		and choice_three.get_theme_stylebox("disabled").get_bg_color().a == 0.0
		and confirm.get_theme_stylebox("disabled").get_bg_color().a == 0.0,
		"A disabled action button still draws an opaque mask.")
	var choice_cross := choice_one.get_node("DisabledCross") as TextureRect
	var confirm_cross := confirm.get_node("DisabledCross") as TextureRect
	_assert(choice_cross.size.x >= choice_one.size.x - 8.0
		and choice_cross.size.y >= choice_one.size.y - 8.0
		and confirm_cross.size.x >= 100.0
		and confirm_cross.size.y >= 100.0,
		"The disabled X texture does not align with the surrounding frame.")
	_assert(choice_one.scale == Vector2.ONE
		and choice_two.scale == Vector2.ONE
		and choice_three.scale == Vector2.ONE,
		"Tutor selection changed the three-button geometry.")
	await get_tree().create_timer(0.2).timeout
	await _settle_frames()
	_capture("03b-tutor-locked.png")
	_main.set("FastMode", true)
	_assert(gameplay_dialogue.text != tutor_line,
		"The resolved Bash event retained the pre-confirmation Tutor line.")
	_assert(FileAccess.file_exists(SAVE_PATH), "A stable session checkpoint was not written.")
	await get_tree().create_timer(0.35).timeout
	await _settle_frames()
	_assert(not tutor_speech.playing and tutor_speech.stream == null
		and not gameplay_dialogue.text.is_empty(),
		"Routine post-action guidance must remain visible but text-only.")

	_press("GameplayHUD/SafeArea/Layout/Content/RightColumn/BackButton")
	await _settle_frames()
	_assert(_main.get_node("TitleScreen").visible,
		"Save and Back did not return to the title screen.")
	_assert(not tutor_speech.playing,
		"Tutor speech continued after returning to the title screen.")
	_assert(_button("TitleScreen/MenuGlass/MenuVBox/ContinueButton").visible,
		"A valid unfinished save did not reveal Continue.")
	_press("TitleScreen/MenuGlass/MenuVBox/NewGameButton")
	await _settle_frames()
	_assert(_main.get_node("TitleScreen/OverwriteOverlay").visible,
		"New Game did not ask before replacing the active save.")
	_press("TitleScreen/OverwriteOverlay/Center/ConfirmGlass/ConfirmVBox/CancelButton")
	await _settle_frames()
	_assert(not _main.get_node("TitleScreen/OverwriteOverlay").visible,
		"Cancel did not close the overwrite confirmation.")
	_press("TitleScreen/MenuGlass/MenuVBox/ContinueButton")
	await get_tree().create_timer(0.22).timeout
	await _settle_frames()
	var restored_time: String = (_main.get_node(
		"GameplayHUD/SafeArea/Layout/Header/HeaderRow/PlayTimeLabel") as Label).text
	_assert(restored_time != "PLAY TIME · 00:00",
		"Continue did not restore the save's accumulated play time.")
	var final_speech := _main.get_node("TutorSpeechPlayer") as AudioStreamPlayer
	final_speech.stop()
	final_speech.stream = null
	await get_tree().create_timer(0.12).timeout
	await _settle_frames()

	var exit_code := 1 if _failed else 0
	if not _failed:
		print("Formal demo Godot smoke passed: title, chapters, gameplay, resume, and overwrite guard.")
	_main.queue_free()
	await _settle_frames()
	get_tree().quit(exit_code)


func _prepare_test_paths() -> void:
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(QA_DIRECTORY))
	DirAccess.make_dir_recursive_absolute(
		ProjectSettings.globalize_path("res://.godot/tests"))

	for suffix in ["", ".bak", ".tmp"]:
		var absolute := ProjectSettings.globalize_path(SAVE_PATH + suffix)
		if FileAccess.file_exists(SAVE_PATH + suffix):
			DirAccess.remove_absolute(absolute)


func _button(path: String) -> Button:
	return _main.get_node(path) as Button


func _visual_center(control: Control) -> Vector2:
	return control.get_global_transform() * (control.size / 2.0)


func _press(path: String) -> void:
	var button := _button(path)
	_assert(button != null, "Missing button: " + path)
	if button != null:
		button.emit_signal("pressed")


func _left_click() -> void:
	var event := InputEventMouseButton.new()
	event.button_index = MOUSE_BUTTON_LEFT
	event.pressed = true
	event.position = Vector2(640, 500)
	event.global_position = event.position
	Input.parse_input_event(event)


func _advance_dialogue_page() -> void:
	var dialogue := _main.get_node(
		"TutorDialogueUI/SafeArea/Layout/Content/DialogueCard/DialogueVBox/DialogueText") as RichTextLabel

	if dialogue.visible_characters != -1:
		_left_click()
		await _settle_frames()
		_assert(dialogue.visible_characters == -1,
			"The first dialogue click did not complete the typewriter animation.")

	_left_click()
	await _settle_frames()


func _capture(file_name: String) -> void:
	# The headless display driver has no render texture. Visual runs still write
	# the same QA captures when a real display driver is available.
	if DisplayServer.get_name() == "headless":
		return

	var image := get_viewport().get_texture().get_image()
	var result := image.save_png(
		ProjectSettings.globalize_path(QA_DIRECTORY + "/" + file_name))
	_assert(result == OK, "Could not save QA capture: " + file_name)


func _settle_frames() -> void:
	await get_tree().process_frame
	await get_tree().process_frame


func _assert(condition: bool, message: String) -> void:
	if condition:
		return

	_failed = true
	push_error(message)
