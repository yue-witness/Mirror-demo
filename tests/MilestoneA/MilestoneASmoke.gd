extends Node

const MAIN_SCENE := preload("res://scenes/main.tscn")
const MAX_PLAYER_TURNS := 20
const PROFILE_PATH := "res://.godot/tests/player_profile.json"

var _main: Control
var _take_buttons: Array[Button]
var _remaining_label: Label
var _event_banner: Label
var _restart_button: Button


func _ready() -> void:
	_prepare_profile_directory()
	_cleanup_profile_files()

	_main = MAIN_SCENE.instantiate()
	_main.set("ProfileSavePath", PROFILE_PATH)
	add_child(_main)
	await get_tree().process_frame

	_take_buttons = [
		_main.get_node("SafeArea/RootVBox/ChoicePanel/Take1Button"),
		_main.get_node("SafeArea/RootVBox/ChoicePanel/Take2Button"),
		_main.get_node("SafeArea/RootVBox/ChoicePanel/Take3Button")
	]
	_remaining_label = _main.get_node(
		"SafeArea/RootVBox/GamePanel/GameVBox/RemainingLabel"
	)
	_event_banner = _main.get_node(
		"SafeArea/RootVBox/GamePanel/GameVBox/EventBanner"
	)
	_restart_button = _main.get_node(
		"SafeArea/RootVBox/GamePanel/GameVBox/RestartButton"
	)

	if not _expect_initial_round():
		return

	for turn in range(MAX_PLAYER_TURNS):
		if _event_banner.text.contains("ROUND COMPLETE"):
			break

		var selected_button := _first_enabled_take_button()
		if selected_button == null:
			_fail("No legal player input was available on player turn %d." % (turn + 1))
			return

		selected_button.mouse_entered.emit()
		await get_tree().create_timer(0.02).timeout
		selected_button.mouse_exited.emit()
		selected_button.pressed.emit()

		if not _all_take_buttons_disabled():
			_fail("Player input was not locked synchronously for the AI turn.")
			return

		if not await _wait_for_player_input_or_completion():
			return

	if not _event_banner.text.contains("ROUND COMPLETE"):
		_fail("The scene did not reach settlement within the turn limit.")
		return

	if _remaining_label.text != "REMAINING: 0":
		_fail("Settlement did not stop exactly at zero remaining units.")
		return

	if not _all_take_buttons_disabled():
		_fail("Choice buttons remained active after settlement.")
		return

	_restart_button.pressed.emit()
	await get_tree().process_frame

	if not _expect_initial_round():
		return

	var first_profile := _read_profile()
	if first_profile.is_empty():
		return

	var saved_choice_count: int = first_profile["history"].size()
	if saved_choice_count == 0:
		_fail("Scene interactions were not written to PlayerModel history.")
		return

	if first_profile["behaviorHistory"].size() <= saved_choice_count:
		_fail("Detailed hover/session/round behavior events were not persisted.")
		return

	_main.queue_free()
	await get_tree().process_frame
	await get_tree().process_frame

	# Recreate the entire game root to model closing and reopening the process.
	var reopened_main: Control = MAIN_SCENE.instantiate()
	reopened_main.set("ProfileSavePath", PROFILE_PATH)
	add_child(reopened_main)
	await get_tree().process_frame

	var reopened_profile := _read_profile()
	if reopened_profile.is_empty():
		return

	if reopened_profile["history"].size() != saved_choice_count:
		_fail("Choice history changed or disappeared after reopening the scene.")
		return

	if reopened_profile["sessions"].size() < 2:
		_fail("Reopening the scene did not append a new persistent session.")
		return

	reopened_main.queue_free()
	await get_tree().process_frame
	await get_tree().process_frame
	_cleanup_profile_files()

	print("Milestone A + PlayerModel scene smoke passed: play, persist, reopen, and replay.")
	get_tree().quit(0)


func _expect_initial_round() -> bool:
	if _remaining_label.text != "REMAINING: 15":
		_fail("A new scene or replay did not start with 15 units.")
		return false

	if _all_take_buttons_disabled():
		_fail("TAKE 1/2/3 were not available at round start.")
		return false

	return true


func _first_enabled_take_button() -> Button:
	# Prefer the largest legal action so the smoke test completes quickly.
	for index in range(_take_buttons.size() - 1, -1, -1):
		if not _take_buttons[index].disabled:
			return _take_buttons[index]

	return null


func _all_take_buttons_disabled() -> bool:
	return _take_buttons.all(func(button: Button) -> bool: return button.disabled)


func _wait_for_player_input_or_completion() -> bool:
	var deadline := Time.get_ticks_msec() + 2000

	while Time.get_ticks_msec() < deadline:
		if _event_banner.text.contains("ROUND COMPLETE"):
			return true

		if not _all_take_buttons_disabled():
			return true

		await get_tree().create_timer(0.05).timeout

	_fail("The AI turn did not complete within two seconds.")
	return false


func _prepare_profile_directory() -> void:
	var directory_path := ProjectSettings.globalize_path(PROFILE_PATH.get_base_dir())
	DirAccess.make_dir_recursive_absolute(directory_path)


func _cleanup_profile_files() -> void:
	var directory_path := ProjectSettings.globalize_path(PROFILE_PATH.get_base_dir())
	var directory := DirAccess.open(directory_path)
	if directory == null:
		return

	for file_name in directory.get_files():
		if file_name.begins_with("player_profile.json"):
			directory.remove(file_name)


func _read_profile() -> Dictionary:
	var file := FileAccess.open(PROFILE_PATH, FileAccess.READ)
	if file == null:
		_fail("PlayerModel profile JSON was not created.")
		return {}

	var profile_json := file.get_as_text()
	file.close()

	var parsed = JSON.parse_string(profile_json)
	if not parsed is Dictionary:
		_fail("PlayerModel profile JSON could not be parsed.")
		return {}

	return parsed


func _fail(message: String) -> void:
	push_error(message)
	get_tree().quit(1)
