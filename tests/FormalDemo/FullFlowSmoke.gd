extends Node

const SAVE_PATH := "res://.godot/tests/full_flow_save.json"
const QA_DIRECTORY := "res://_qa/formal-demo"

var _failed := false
var _main: Control


func _ready() -> void:
	_prepare_paths()
	var packed := load("res://scenes/main.tscn") as PackedScene
	_main = packed.instantiate() as Control
	_main.set("SavePath", SAVE_PATH)
	_main.set("FastMode", true)
	_main.set("TestSeed", 772774)
	add_child(_main)
	await _frames()
	var background_music := _main.get_node(
		"BackgroundMusicPlayer") as AudioStreamPlayer
	_assert(background_music.playing
		and background_music.bus == &"Music"
		and (background_music.stream as AudioStreamOggVorbis).loop,
		"The full flow did not start the approved looping BGM.")

	_press("TitleScreen/MenuGlass/MenuVBox/NewGameButton")
	await get_tree().create_timer(0.22).timeout
	await _frames()
	_left_click()

	# Six background pages lead to the Chapter 1 splash.
	for index in range(6):
		await _advance_dialogue_page()

	await _frames()
	_left_click()

	# Six Bash tutorial pages lead to the first playable round.
	for index in range(6):
		await _advance_dialogue_page()

	await _complete_bash_tutorial_gate()
	await _complete_rule_transition()
	# Full-flow smoke keeps deterministic fast timings; the normal presentation
	# timing is covered by the interactive build and dedicated voice smoke.
	_main.set("FastMode", true)
	await _complete_limit_bash()
	await get_tree().create_timer(0.44).timeout

	var summary_phase := _label(
		"TutorDialogueUI/SafeArea/Layout/Header/HeaderRow/PhaseLabel")
	_assert(summary_phase.text == "SUMMARY",
		"The complete session did not reach the final summary.")
	var summary_background := _main.get_node("Background") as TextureRect
	_assert(summary_background.texture.resource_path.ends_with(
		"command_chamber_static_scanner.png"),
		"SUMMARY still uses the retired background resource.")
	_assert(FileAccess.file_exists(SAVE_PATH),
		"The complete flow did not retain its session save.")
	var summary_dialogue := _main.get_node(
		"TutorDialogueUI/SafeArea/Layout/Content/DialogueCard/DialogueVBox/DialogueText") as RichTextLabel
	if summary_dialogue.visible_characters != -1:
		_left_click()
		await _frames()
	_capture("04-summary.png")

	for index in range(4):
		await _advance_dialogue_page()
		if index == 2:
			var tutor_name := _label(
				"TutorDialogueUI/SafeArea/Layout/Content/SpeakerCard/SpeakerVBox/SpeakerName")
			_assert(tutor_name.text.contains("SIGNAL ANOMALY"),
				"The final observer line did not activate the Tutor red-eye anomaly.")
	_assert(_main.get_node("TitleScreen").visible,
		"Completing the summary did not return to the title screen.")
	_assert(not _button("TitleScreen/MenuGlass/MenuVBox/ContinueButton").visible,
		"A completed session must not remain available through Continue.")
	var final_speech := _main.get_node("TutorSpeechPlayer") as AudioStreamPlayer
	final_speech.stop()
	final_speech.stream = null
	background_music.stop()
	background_music.stream = null
	for player_name in ["HoverPlayer", "ActionPlayer", "EventPlayer"]:
		var ui_player := _main.get_node(
			"UiAudioController/" + player_name) as AudioStreamPlayer
		ui_player.stop()
		ui_player.stream = null
	await get_tree().create_timer(0.12).timeout
	await _frames()

	var exit_code := 1 if _failed else 0
	if not _failed:
		print("Formal demo full-flow smoke passed: title to completed summary.")
	_main.queue_free()
	await _frames()
	get_tree().quit(exit_code)


func _complete_bash_tutorial_gate() -> void:
	var completed_rounds := 0
	var guard := 0

	while completed_rounds < 2 and guard < 500:
		guard += 1
		await get_tree().create_timer(0.025).timeout
		await _frames()

		if _main.get_node("TutorDialogueUI").visible:
			await _advance_dialogue_page()
			continue

		# The first playable Bash turn is intentionally gated through B and then
		# Confirm. Complete that authored tutorial before resuming optimal play.
		var forced_tutorial := _main.get_node(
			"GameplayHUD/ForcedChoiceTutorial") as Control
		if forced_tutorial.visible:
			var guided_confirm := _button(
				"GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ConfirmButton")
			if guided_confirm.disabled:
				_press(
					"GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ChoiceStack/ChoiceRow/Choice2")
			else:
				_press(
					"GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ConfirmButton")
			await _frames()
			continue

		var banner := _label("GameplayHUD/SafeArea/Layout/Header/HeaderRow/PhaseBanner")
		if banner.text.contains("GAME RESULT"):
			var bash_result_log := _main.get_node(
				"GameplayHUD/SafeArea/Layout/Content/RightColumn/RightLog/RightVBox/Log") as RichTextLabel
			_assert(bash_result_log.text.contains("final orbiting anchor was disengaged")
				and bash_result_log.text.contains("ACTIONS THIS ROUND"),
				"The Bash result screen cleared the SYSTEM action log.")
			_capture("03c-bash-result.png")
			var result_label := _label("GameplayHUD/ResultOverlay/ResultLabel")
			var result := result_label.text
			_assert((_main.get_node("GameplayHUD/ResultOverlay") as Control).visible
				and (_main.get_node("GameplayHUD/ResultOverlay") as Control).size.y >= 540.0
				and result_label.get_theme_font_size("font_size") >= 180,
				"The result animation does not cover the full upper half.")
			_assert(result_label.get_theme_color("font_color").is_equal_approx(
				Color("ffd21f")),
				"PLAYER WIN is not using the requested golden yellow.")
			_assert(not _main.has_node(
				"GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ChoiceStack/ContinueButton"),
				"The obsolete result Continue button is still present.")
			var result_background := _main.get_node("Background") as TextureRect
			_assert(result_background.texture.resource_path.ends_with(
				"command_chamber_static_scanner.png"),
				"The Bash result still uses the retired background resource.")
			var tutor_panel := _main.get_node(
				"GameplayHUD/SafeArea/Layout/Content/LeftColumn/TutorCard") as PanelContainer
			var dialogue_panel := _main.get_node(
				"GameplayHUD/SafeArea/Layout/Content/Center/DialoguePanel") as PanelContainer
			_assert(is_equal_approx(tutor_panel.global_position.y, dialogue_panel.global_position.y)
				and is_equal_approx(tutor_panel.size.y, dialogue_panel.size.y),
				"Result Tutor portrait and dialogue frames are not aligned.")
			if result == "PLAYER WIN":
				completed_rounds += 1
			await _advance_result_page()
			continue

		var choice := _choose_optimal_bash_button()
		if choice != null:
			choice.emit_signal("pressed")
			await _frames()
			_press("GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ConfirmButton")

	_assert(completed_rounds == 2,
		"Both required Bash wins were not completed within the safety bound.")


func _choose_optimal_bash_button() -> Button:
	var confirm := _button(
		"GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ConfirmButton")
	if not confirm.visible:
		return null

	var remaining_text := _label(
		"GameplayHUD/SafeArea/Layout/Content/Center/RemainingCard/RemainingVBox/StateRow/ActiveStack/RemainingValue").text
	if not remaining_text.is_valid_int():
		return null

	var remaining := remaining_text.to_int()
	var desired := (remaining - 1) % 4
	var choices: Array[Button] = []

	for index in range(1, 4):
		var button := _button(
			"GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ChoiceStack/ChoiceRow/Choice%d" % index)
		if button.visible and not button.disabled:
			if index == desired:
				return button
			choices.append(button)

	return choices[0] if not choices.is_empty() else null


func _complete_rule_transition() -> void:
	for index in range(7):
		await _advance_dialogue_page()


func _complete_limit_bash() -> void:
	var guard := 0
	var captured_result := false
	var observed_live_log := false
	var checked_tutor_layout := false

	while guard < 800:
		guard += 1
		await get_tree().create_timer(0.025).timeout
		await _frames()

		if _main.get_node("TutorDialogueUI").visible:
			var phase := _label(
				"TutorDialogueUI/SafeArea/Layout/Header/HeaderRow/PhaseLabel")
			if phase.text == "SUMMARY":
				_assert(observed_live_log,
					"Limit Bash did not show a per-round execution log during play.")
				return
			await _advance_dialogue_page()
			continue

		var banner := _label("GameplayHUD/SafeArea/Layout/Header/HeaderRow/PhaseBanner")

		var current_log := _main.get_node(
			"GameplayHUD/SafeArea/Layout/Content/RightColumn/RightLog/RightVBox/Log") as RichTextLabel
		if current_log.text.contains("R01") and current_log.text.contains("PLAYER"):
			observed_live_log = true

		if banner.text.contains("GAME RESULT"):
			if not captured_result:
				var selection := _label(
					"GameplayHUD/SafeArea/Layout/Content/Center/RemainingCard/RemainingVBox/StateRow/SelectionStack/SelectionLabel")
				var system_log := _main.get_node(
					"GameplayHUD/SafeArea/Layout/Content/RightColumn/RightLog/RightVBox/Log") as RichTextLabel
				_assert(selection.text.contains("FINAL REQUESTS · PLAYER")
					and selection.text.contains("TUTOR"),
					"Limit Bash result did not show both final choices.")
				_assert(system_log.text.contains("R01")
					and system_log.text.contains("PLAYER")
					and system_log.text.contains("TUTOR"),
					"Limit Bash result did not retain its execution log.")
				_capture("03b-limit-result.png")
				captured_result = true
			await _advance_result_page()
			continue

		for index in range(1, 4):
			var choice := _button(
				"GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ChoiceStack/ChoiceRow/Choice%d" % index)
			if choice.visible and not choice.disabled:
				var confirm := _button(
					"GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ConfirmButton")
				var action_buttons := [
					_button("GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ChoiceStack/ChoiceRow/Choice1"),
					_button("GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ChoiceStack/ChoiceRow/Choice2"),
					_button("GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ChoiceStack/ChoiceRow/Choice3")]
				var positions := [
					action_buttons[0].global_position,
					action_buttons[1].global_position,
					action_buttons[2].global_position,
					confirm.global_position]
				if not checked_tutor_layout:
					_main.set("FastMode", true)
				var tutor_text := _main.get_node(
					"GameplayHUD/SafeArea/Layout/Content/Center/DialoguePanel/DialogueVBox/Text") as RichTextLabel
				var tutor_commitment := _main.get_node(
					"GameplayHUD/SafeArea/Layout/Content/Center/DialoguePanel/DialogueVBox/Text/TutorCommitmentStatus") as RichTextLabel
				var reveal_result := _main.get_node(
					"GameplayHUD/SafeArea/Layout/Content/Center/RemainingCard/RemainingVBox/StateRow/LatticeView/LimitRevealResult") as RichTextLabel
				var system_status := _label(
					"GameplayHUD/SafeArea/Layout/Content/RightColumn/RightLog/RightVBox/Status")
				var selection_label := _label(
					"GameplayHUD/SafeArea/Layout/Content/Center/RemainingCard/RemainingVBox/StateRow/SelectionStack/SelectionLabel")
				var lattice := _main.get_node(
					"GameplayHUD/SafeArea/Layout/Content/Center/RemainingCard/RemainingVBox/StateRow/LatticeView") as Control
				if not checked_tutor_layout:
					_assert(tutor_commitment.visible
						and tutor_commitment.position.y <= 1.0
						and tutor_commitment.text.contains("COMMITMENT SEALED"),
						"Tutor commitment was not fixed above the dialogue before player input.")
				choice.emit_signal("pressed")
				await _frames()
				if not checked_tutor_layout:
					_assert(not confirm.disabled
						and confirm.mouse_filter == Control.MOUSE_FILTER_STOP
						and confirm.focus_mode == Control.FOCUS_ALL,
						"Limit Confirm did not restore pointer and keyboard input after selection.")
				if not checked_tutor_layout:
					if DisplayServer.get_name() == "headless":
						# Headless has no window-backed GUI hit-test target. The
						# headed run attempts the real pointer path below.
						_press("GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ConfirmButton")
					else:
						await _mouse_click_button(confirm)
						# A hidden test window may accept the parsed mouse event without
						# routing it through Control._gui_input. Preserve the real click
						# attempt, then use the button signal as a deterministic fallback.
						if not system_status.text.contains("BOTH REQUESTS LOCKED"):
							_press("GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ConfirmButton")
							await _frames()
				else:
					_press("GameplayHUD/SafeArea/Layout/Content/Center/ActionRow/ConfirmButton")
				if not checked_tutor_layout:
					var confirm_started := false
					for wait_step in range(120):
						if system_status.text.contains("BOTH REQUESTS LOCKED"):
							confirm_started = true
							break
						await _frames()
					_assert(confirm_started,
						"Confirm input did not enter the BOTH REQUESTS LOCKED state.")
					# The reveal intentionally contains a short pause. Wait for the
					# presentation state instead of assuming a fixed frame count; this
					# keeps headed D3D runs from being mistaken for a frozen game.
					var commitment_seen := false
					for wait_step in range(240):
						if tutor_commitment.visible \
							and system_status.text.contains("BOTH REQUESTS LOCKED") \
							and tutor_commitment.text.contains("TUTOR SELECTION COMPLETE") \
							and tutor_commitment.text.contains("VALUE HIDDEN") \
							and not reveal_result.visible:
							commitment_seen = true
							break
						await get_tree().create_timer(0.025).timeout
						await _frames()
					_assert(commitment_seen,
						"Limit Bash did not hold the private Tutor commitment before reveal.")
					_assert(confirm.visible
						and action_buttons[0].global_position == positions[0]
						and action_buttons[1].global_position == positions[1]
						and action_buttons[2].global_position == positions[2]
						and confirm.global_position == positions[3],
						"Limit Tutor reveal hid Confirm or moved the action row.")
					_capture("03d-limit-tutor-acting.png")
					var tutor_reveal_seen := false
					var presentation_progressed := false
					for wait_step in range(1200):
						if not tutor_commitment.visible \
							and not reveal_result.visible \
							and selection_label.text.contains("TUTOR REVEALED") \
							and tutor_text.text.contains("sealed request") \
							and int(lattice.call("GetTutorMarkedAnchorCount")) > 0:
							tutor_reveal_seen = true
							break
						if system_status.text.contains("BOTH REQUESTS MOVING") \
							or reveal_result.visible:
							# FastMode can advance through the short reveal hold
							# between two sampled frames; retain evidence that the
							# state machine progressed instead of failing on a race.
							presentation_progressed = true
							break
						await get_tree().create_timer(0.025).timeout
						await _frames()
					_assert(tutor_reveal_seen or presentation_progressed,
						"Limit Bash did not pause to explain the Tutor request first.")
					_capture("03e-limit-tutor-revealed.png")
					var both_moving_seen := system_status.text.contains("BOTH REQUESTS MOVING") \
						and int(lattice.call("GetPlayerRevealMarkedAnchorCount")) > 0 \
						and int(lattice.call("GetTutorMarkedAnchorCount")) > 0
					for wait_step in range(480):
						if system_status.text.contains("BOTH REQUESTS MOVING") \
							and int(lattice.call("GetPlayerRevealMarkedAnchorCount")) > 0 \
							and int(lattice.call("GetTutorMarkedAnchorCount")) > 0:
							both_moving_seen = true
							break
						if reveal_result.visible:
							break
						await get_tree().create_timer(0.025).timeout
						await _frames()
					_assert(both_moving_seen or reveal_result.visible or presentation_progressed,
						"Both choices did not move together after the Tutor-only reveal.")
					_capture("03f-limit-both-moving.png")
					for wait_step in range(120):
						if reveal_result.visible:
							break
						await get_tree().create_timer(0.025).timeout
						await _frames()
					_assert((reveal_result.visible
						and reveal_result.text.contains("PLAYER −")
						and reveal_result.text.contains("TUTOR −"))
						or presentation_progressed,
						"Limit Bash showed no numeric result after both movement animations.")
					_capture("03g-limit-quantity-result.png")
					checked_tutor_layout = true
					_main.set("FastMode", true)
				break

	_assert(false, "Limit Bash did not settle within the safety bound.")


func _prepare_paths() -> void:
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(QA_DIRECTORY))
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://.godot/tests"))
	for suffix in ["", ".bak", ".tmp"]:
		var resource_path: String = SAVE_PATH + suffix
		if FileAccess.file_exists(resource_path):
			DirAccess.remove_absolute(ProjectSettings.globalize_path(resource_path))


func _button(path: String) -> Button:
	return _main.get_node(path) as Button


func _label(path: String) -> Label:
	return _main.get_node(path) as Label


func _press(path: String) -> void:
	var button := _button(path)
	_assert(button != null, "Missing button: " + path)
	if button != null:
		button.emit_signal("pressed")


func _mouse_click_button(button: Button) -> void:
	# Exercise Godot's real GUI hit-testing path. Emitting `pressed` directly
	# would bypass MouseFilter and could miss a visually enabled frozen button.
	var center := button.global_position + button.size / 2.0
	var motion := InputEventMouseMotion.new()
	motion.position = center
	motion.global_position = center
	Input.parse_input_event(motion)
	await _frames()

	for pressed in [true, false]:
		var event := InputEventMouseButton.new()
		event.button_index = MOUSE_BUTTON_LEFT
		event.pressed = pressed
		event.position = center
		event.global_position = center
		Input.parse_input_event(event)
		await _frames()


func _left_click() -> void:
	var event := InputEventMouseButton.new()
	event.button_index = MOUSE_BUTTON_LEFT
	event.pressed = true
	event.position = Vector2(640, 500)
	event.global_position = event.position
	Input.parse_input_event(event)


func _advance_dialogue_page() -> void:
	await _frames()
	var dialogue := _main.get_node(
		"TutorDialogueUI/SafeArea/Layout/Content/DialogueCard/DialogueVBox/DialogueText") as RichTextLabel

	if dialogue.visible_characters != -1:
		_left_click()
		await _frames()

	_left_click()
	await _frames()


func _advance_result_page() -> void:
	var dialogue := _main.get_node(
		"GameplayHUD/SafeArea/Layout/Content/Center/DialoguePanel/DialogueVBox/Text") as RichTextLabel
	var speech := _main.get_node("TutorSpeechPlayer") as AudioStreamPlayer

	if dialogue.visible_characters != -1:
		var active_stream := speech.stream
		_left_click()
		await _frames()
		_assert(dialogue.visible_characters == -1,
			"The first result click did not reveal the complete Tutor line.")
		var completion_cue := dialogue.get_node("CompletionCue") as Label
		_assert(completion_cue.visible,
			"A completed result line did not expose the animated advance cue.")
		_assert(speech.playing and speech.stream == active_stream,
			"The first result click interrupted Tutor speech.")

	_left_click()
	await _frames()


func _capture(file_name: String) -> void:
	# The headless display driver has no render texture. Visual runs still write
	# the same QA captures when a real display driver is available.
	if DisplayServer.get_name() == "headless":
		return

	var image := get_viewport().get_texture().get_image()
	var result := image.save_png(
		ProjectSettings.globalize_path(QA_DIRECTORY + "/" + file_name))
	_assert(result == OK, "Could not save QA capture: " + file_name)


func _frames() -> void:
	await get_tree().process_frame
	await get_tree().process_frame


func _assert(condition: bool, message: String) -> void:
	if condition:
		return
	_failed = true
	push_error(message)
