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
	_assert(not _button("TitleScreen/MenuGlass/MenuVBox/ContinueButton").visible,
		"Continue must remain hidden without an unfinished save.")
	_capture("01-title.png")

	_press("TitleScreen/MenuGlass/MenuVBox/NewGameButton")
	await get_tree().create_timer(0.25).timeout
	await _settle_frames()
	_assert(_main.get_node("GameplayHUD/ChapterOverlay").visible,
		"New Game did not route to the Chapter 0 splash.")
	_assert(not _main.has_node(
		"GameplayHUD/ChapterOverlay/ChapterGlass/ChapterVBox/ChapterContinue"),
		"The obsolete chapter continue button is still present.")
	_capture("02-chapter-0.png")

	_left_click()
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
	_assert(dialogue_text.visible_characters != -1,
		"Tutor dialogue did not begin with the typewriter animation.")
	_capture("02b-tutor-dialogue.png")

	for index in range(6):
		if dialogue_text.visible_characters != -1:
			_left_click()
			await _settle_frames()
		if index < 3:
			_capture("02c-background-%02d.png" % (index + 1))
		if index == 2:
			var speaker_name := _main.get_node(
				"TutorDialogueUI/SafeArea/Layout/Content/SpeakerCard/SpeakerVBox/SpeakerName") as Label
			_assert(speaker_name.text.contains("S-17"),
				"S-17 dialogue did not switch the visible speaker identity.")
		_left_click()
		await _settle_frames()

	_assert(_main.get_node("GameplayHUD/ChapterOverlay").visible,
		"Background dialogue did not route to the Chapter 1 splash.")
	_left_click()
	await _settle_frames()

	for index in range(6):
		await _advance_dialogue_page()

	var choice_one := _button(
		"GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ChoiceStack/ChoiceRow/Choice1")
	var confirm := _button(
		"GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ConfirmButton")
	var tutor_line: String = (_main.get_node(
		"GameplayHUD/SafeArea/Layout/Content/Center/DialoguePanel/DialogueVBox/Text") as RichTextLabel).text
	_assert(not _main.has_node("GameplayHUD/SafeArea/Layout/Header/HeaderRow/ScoreLabel"),
		"The removed top-centre score display is still present.")
	_assert(_main.has_node("GameplayHUD/SafeArea/Layout/Header/HeaderRow/HeaderSpacer"),
		"The top header no longer preserves its left/right alignment.")
	_assert(_main.has_node(
		"GameplayHUD/SafeArea/Layout/Content/Center/RemainingCard/RemainingVBox/LatticeView"),
		"The Stability Lattice visualization is missing from gameplay.")
	var system_log := _main.get_node(
		"GameplayHUD/SafeArea/Layout/Content/RightColumn/RightLog/RightVBox/Log") as RichTextLabel
	_assert(system_log.scroll_active,
		"The fixed SYSTEM panel does not allow overflow scrolling.")
	var system_panel := _main.get_node(
		"GameplayHUD/SafeArea/Layout/Content/RightColumn/RightLog") as PanelContainer
	_assert(system_panel.custom_minimum_size.y == 470.0,
		"The SYSTEM panel no longer has its fixed reference height.")
	_assert(choice_one.visible and not choice_one.disabled,
		"Bash gameplay did not open a legal player choice.")
	_assert(confirm.visible and confirm.disabled,
		"Bash confirm must wait for an explicit player selection.")
	_capture("03-bash-gameplay.png")
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
	_assert(not (_main.get_node(
		"GameplayHUD/SafeArea/Layout/Content/Center/DialoguePanel/DialogueVBox/Text") as RichTextLabel).text.is_empty(),
		"The optional selection-stage Tutor dialogue left the panel empty.")
	_capture("03a-bash-selected.png")
	_press("GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ConfirmButton")
	await get_tree().create_timer(0.08).timeout
	await _settle_frames()
	_assert((_main.get_node(
		"GameplayHUD/SafeArea/Layout/Content/Center/DialoguePanel/DialogueVBox/Text") as RichTextLabel).text != tutor_line,
		"Tutor dialogue did not advance after a confirmed gameplay event.")
	_assert(FileAccess.file_exists(SAVE_PATH), "A stable session checkpoint was not written.")
	await get_tree().create_timer(1.0).timeout

	_press("GameplayHUD/SafeArea/Layout/Content/RightColumn/BackButton")
	await _settle_frames()
	_assert(_main.get_node("TitleScreen").visible,
		"Save and Back did not return to the title screen.")
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

	if _failed:
		get_tree().quit(1)
	else:
		print("Formal demo Godot smoke passed: title, chapters, gameplay, resume, and overwrite guard.")
		get_tree().quit(0)


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
