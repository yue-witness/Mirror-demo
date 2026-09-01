extends Node

const SAVE_PATH := "res://.godot/tests/tutor_speech_save.json"

var _failed := false
var _main: Control


func _ready() -> void:
	_prepare_save_path()
	var packed := load("res://scenes/main.tscn") as PackedScene
	_main = packed.instantiate() as Control
	_main.set("SavePath", SAVE_PATH)
	_main.set("FastMode", true)
	_main.set("TestSeed", 772774)
	add_child(_main)
	await _frames()

	_press("TitleScreen/MenuGlass/MenuVBox/NewGameButton")
	await get_tree().create_timer(0.22).timeout
	await _frames()
	_left_click()
	await _frames()

	var dialogue := _main.get_node(
		"TutorDialogueUI/SafeArea/Layout/Content/DialogueCard/DialogueVBox/DialogueText") as RichTextLabel
	var speech := _main.get_node("TutorSpeechPlayer") as AudioStreamPlayer
	_assert(speech.playing and speech.stream != null,
		"The first Tutor cue did not begin playback.")

	var duration := speech.stream.get_length()
	var total_characters := dialogue.get_total_character_count()
	_assert(duration > 2.0 and total_characters > 0,
		"The first Tutor cue does not expose a usable duration or text length.")

	await get_tree().create_timer(duration * 0.5).timeout
	await _frames()
	var progress := dialogue.visible_characters / float(total_characters)
	_assert(speech.playing,
		"Tutor speech ended before its configured midpoint.")
	_assert(progress > 0.38 and progress < 0.62,
		"Tutor text midpoint drifted from the audio midpoint: %.3f" % progress)

	await get_tree().create_timer(duration * 0.55 + 0.12).timeout
	await _frames()
	_assert(dialogue.visible_characters == -1,
		"Tutor text did not finish with the voice cue.")
	_assert(not speech.playing,
		"Tutor speech continued beyond its imported stream duration.")

	# Advance to the first Bash choice without depending on the separate visual
	# animation assertions in FormalDemoSmoke.
	for index in range(6):
		await _advance_dialogue_page(dialogue)

	_assert(_main.get_node("GameplayHUD/ChapterOverlay").visible,
		"Background dialogue did not reach the Chapter 1 splash.")
	_left_click()
	await _frames()

	for index in range(6):
		await _advance_dialogue_page(dialogue)

	var gameplay_dialogue := _main.get_node(
		"GameplayHUD/SafeArea/Layout/Content/Center/DialoguePanel/DialogueVBox/Text"
		) as RichTextLabel
	_main.set("FastMode", false)
	_press(
		"GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ChoiceStack/ChoiceRow/Choice1")
	await _frames()
	_press("GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ConfirmButton")
	await get_tree().process_frame

	_assert(not speech.playing and speech.stream == null,
		"Bash confirmation started an overlapping intermediate Tutor cue.")
	_assert(gameplay_dialogue.text.is_empty(),
		"Bash confirmation left an intermediate Tutor line visible.")

	await get_tree().create_timer(0.2).timeout
	await _frames()
	_assert(not speech.playing and gameplay_dialogue.text.is_empty(),
		"Bash lock delay unexpectedly triggered a Tutor cue.")

	await get_tree().create_timer(0.35).timeout
	await _frames()
	_assert(speech.playing and speech.stream != null
		and not gameplay_dialogue.text.is_empty(),
		"Bash resolution did not start its single final Tutor cue.")
	var resolved_stream := speech.stream

	await get_tree().create_timer(0.3).timeout
	await _frames()
	_assert(speech.playing and speech.stream == resolved_stream,
		"Bash resolution replaced its final Tutor cue with another dialogue.")

	_press("GameplayHUD/SafeArea/Layout/Content/RightColumn/BackButton")
	await _frames()
	speech.stop()
	speech.stream = null
	await get_tree().create_timer(0.12).timeout
	await _frames()

	var exit_code := 1 if _failed else 0
	if not _failed:
		print("Tutor speech smoke passed: timing aligned and Bash resolution emitted one cue.")
	_main.queue_free()
	await _frames()
	get_tree().quit(exit_code)


func _prepare_save_path() -> void:
	DirAccess.make_dir_recursive_absolute(
		ProjectSettings.globalize_path("res://.godot/tests"))
	for suffix in ["", ".bak", ".tmp"]:
		var resource_path: String = SAVE_PATH + suffix
		if FileAccess.file_exists(resource_path):
			DirAccess.remove_absolute(ProjectSettings.globalize_path(resource_path))


func _button(path: String) -> Button:
	return _main.get_node(path) as Button


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


func _frames() -> void:
	await get_tree().process_frame
	await get_tree().process_frame


func _advance_dialogue_page(dialogue: RichTextLabel) -> void:
	if dialogue.visible_characters != -1:
		_left_click()
		await _frames()
		_assert(dialogue.visible_characters == -1,
			"The first dialogue click did not complete the typewriter animation.")

	_left_click()
	await _frames()


func _assert(condition: bool, message: String) -> void:
	if condition:
		return
	_failed = true
	push_error(message)
