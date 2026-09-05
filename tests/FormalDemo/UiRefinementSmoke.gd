extends Node

# Focused verification for the September UI/audio refinement, with its own save.
var main: Control
var failed := false
const CONTENT := "SafeArea/Layout/Content/"

func _ready() -> void:
	main = load("res://scenes/main.tscn").instantiate()
	main.set("SavePath", "res://.godot/tests/ui_refinement_save.json")
	add_child(main)
	await get_tree().create_timer(0.6).timeout
	check(main.has_method("StartBashRound"), "Controller entry is callable")
	if failed:
		get_tree().quit(1)
		return
	main.call("StartNewGame")
	main.call("ContinueChapter")
	main.get_node("TutorSpeechPlayer").call("StopDialogue", true)
	main.call("StartBashRound", 2)
	await get_tree().create_timer(0.8).timeout
	var hud := main.get_node("GameplayHUD")
	check(not hud.has_node("SafeArea/Layout/Header/HeaderRow/PlayTimeLabel"), "Timer removed")
	check(not hud.has_node(CONTENT + "RightColumn/RightLog/RightVBox/Status"), "Telemetry removed")
	var portrait := hud.get_node(CONTENT + "LeftColumn/TutorCard/TutorVBox/PortraitFrame/PortraitTexture")
	check(portrait.texture == null, "Portrait outer control does not draw a duplicate")
	check(portrait.get_node("FrameDisplay").texture != null, "Single portrait renderer has an atlas frame")
	check(not portrait.get_parent().has_node("ParticleFrame"), "Portrait ring has no duplicate effect")
	var heard_later_bash := false
	for tick in range(120):
		var cue: String = main.get_node("TutorSpeechPlayer").get("CurrentLineId")
		if cue.begins_with("bash_r2_tutor_") or cue.begins_with("bash_terminal_"):
			heard_later_bash = true
		await get_tree().create_timer(0.1).timeout
	var history := hud.get_node(CONTENT + "RightColumn/RightLog/RightVBox/Log") as RichTextLabel
	check(history.text.contains("Tutor"), "Bash action history records the Tutor turn")
	check(heard_later_bash, "Later Bash feedback is voiced")
	main.call("BackToTitle")
	main.call("ContinueGame")
	await get_tree().create_timer(0.7).timeout
	check(history.text.contains("Tutor"), "Bash history survives restore")
	await capture("bash")
	# Optional feedback must preserve an active spoken explanation.
	var voice := main.get_node("TutorSpeechPlayer")
	voice.call("StopDialogue", true)
	var tutorial: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/dialogue/tutorial.json"))
	var line: Dictionary = tutorial["randomPools"]["limit_terminal_approach"][0]
	voice.set("StandardSpeechGapSeconds", 0.0)
	check(float(voice.call("PlayDialogue", line["id"], "TUTOR", line["text"])) > 0, "Limit terminal feedback has audio")
	var previous: String = voice.get("CurrentLineId")
	var second: Dictionary = tutorial["randomPools"]["choice_hesitation"][0]
	voice.call("PlayDialogue", second["id"], "TUTOR", second["text"])
	check(voice.playing and voice.get("CurrentLineId") == previous, "Optional chatter does not interrupt")
	voice.call("StopDialogue", true)
	main.call("StartLimitBashGame", false)
	await get_tree().create_timer(1.0).timeout
	await capture("limit")
	main.call("SelectChoice", 2)
	main.call("ConfirmChoice")
	await get_tree().create_timer(3.0).timeout
	await capture("limit-reveal")
	for tick in range(150):
		if history.text.contains("You") and not history.text.contains("REVEALING"):
			break
		await get_tree().create_timer(0.2).timeout
	check(history.text.contains("You") and history.text.contains("Tutor"), "Limit reveal history records both requests")
	await capture("limit-history")
	# Save and restore the same public state; no personal save is touched.
	main.call("BackToTitle")
	main.call("ContinueGame")
	await get_tree().create_timer(0.7).timeout
	check(history.text.contains("You") and history.text.contains("Tutor"), "History survives restore")
	main.call("EnterDialoguePhase", 1, false)
	await get_tree().create_timer(0.6).timeout
	main.get_node("TutorDialogueUI").call("CompleteTyping")
	await capture("dialogue")
	main.call("BackToTitle")
	await get_tree().create_timer(0.7).timeout
	await capture("title")
	main.queue_free()
	await get_tree().process_frame
	print("UI_REFINEMENT_SMOKE " + ("FAIL" if failed else "PASS"))
	get_tree().quit(1 if failed else 0)

func capture(name: String) -> void:
	if main.get_node("GameplayHUD").visible:
		main.get_node("GameplayHUD").call("CompleteTutorTyping")
	await RenderingServer.frame_post_draw
	if DisplayServer.get_name() != "headless":
		DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://_qa/ui-refinement"))
		get_viewport().get_texture().get_image().save_png("res://_qa/ui-refinement/" + name + ".png")
	var hud := main.get_node("GameplayHUD") as Control
	if hud.visible:
		var rules := hud.get_node(CONTENT + "LeftColumn/LeftStatus/LeftVBox/Details") as RichTextLabel
		check(rules.get_content_height() <= rules.size.y + 1, "Rules are not clipped")
		var subtitle := hud.get_node(CONTENT + "Center/DialoguePanel/DialogueVBox/Text") as RichTextLabel
		check(subtitle.get_content_height() <= subtitle.size.y + 1, "Subtitle is not clipped")
		var area := hud.get_node("SafeArea") as Control
		check(area.get_global_rect().end.x <= get_viewport().get_visible_rect().end.x + 1,
			"HUD fits viewport width")
		check(area.get_global_rect().end.y <= get_viewport().get_visible_rect().end.y + 1,
			"HUD fits viewport height")

func check(ok: bool, message: String) -> void:
	if ok:
		print("PASS: " + message)
	else:
		failed = true
		push_error(message)
