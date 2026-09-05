extends Node

# Targeted release smoke only: no complete-session or domain regression run.
const CONTENT := "SafeArea/Layout/Content/"
const ACTIONS := CONTENT + "Center/ActionRow/"
const LATTICE := CONTENT + "Center/RemainingCard/RemainingVBox/StateRow/LatticeView"
var main: Control
var failed := false


func _ready() -> void:
	main = load("res://scenes/main.tscn").instantiate()
	# A unique empty slot proves first-launch behaviour without touching user saves.
	main.set("SavePath", "res://.godot/tests/v015_%s.json" % Time.get_ticks_usec())
	add_child(main)
	await pause(0.6)
	check(not main.get_node("TitleScreen/MenuGlass/MenuVBox/ContinueButton").visible,
		"Empty save starts without Continue")
	check_text_layers(main)
	await capture("title-empty")
	main.call("StartNewGame")
	main.call("ContinueChapter")
	main.call("StartLimitBashGame", false)
	await pause(0.6)
	var hud := main.get_node("GameplayHUD")
	var lattice := hud.get_node(LATTICE)
	var status := hud.get_node(CONTENT + "Center/DialoguePanel/DialogueVBox/TutorCommitmentStatus")
	check(status.visible and status.text == hud.get("TutorSealedText"),
		"Tutor commitment is displayed before player selection")
	check(float(lattice.get("OrbitScale")) > 1.0, "Orbit field is enlarged")
	var gold: Color = lattice.get("PlayerPreview")
	var green: Color = lattice.get("TutorPreview")
	check(gold.r > 0.85 and gold.g > 0.65 and gold.b < 0.5,
		"Player selection uses gold")
	check(green.g > 0.8 and green.g > green.r and green.g > green.b,
		"Tutor selection uses green")
	var buttons: Array[Button] = []
	for index in range(1, 4):
		var button := hud.get_node(ACTIONS + "ChoiceStack/ChoiceRow/Choice%d" % index) as Button
		buttons.append(button)
		var first_line := button.text.split("\n")[0]
		main.call("SelectChoice", index)
		await pause(0.05)
		check(button.text.split("\n")[0] == first_line,
			"Choice %d first line stays unchanged" % index)
		for state in ["normal", "hover", "pressed", "disabled"]:
			var fill := (button.get_theme_stylebox(state) as StyleBoxFlat).bg_color
			check(fill.a == 0.0 if state in ["normal", "disabled"] else fill.a > 0.7,
				"Choice %d has the requested background in %s" % [index, state])
		var cross := button.get_node("DisabledCross") as TextureRect
		check(cross.z_index > button.get_node("MatrixCaption").z_index,
			"Choice %d X renders above its caption" % index)
		var border := (button.get_theme_stylebox("disabled") as StyleBoxFlat).border_color
		check(cross.modulate.a >= 0.85 and Vector3(cross.modulate.r, cross.modulate.g, cross.modulate.b).is_equal_approx(
			Vector3(border.r, border.g, border.b)),
			"Choice %d has a bright X matching its border" % index)
	var first_fill := (buttons[0].get_theme_stylebox("hover") as StyleBoxFlat).bg_color
	var second_fill := (buttons[1].get_theme_stylebox("hover") as StyleBoxFlat).bg_color
	var third_fill := (buttons[2].get_theme_stylebox("hover") as StyleBoxFlat).bg_color
	check(first_fill.g > first_fill.r and second_fill.r > second_fill.b
		and second_fill.g > second_fill.b and third_fill.r > third_fill.g,
		"Options use green, yellow and red backgrounds")
	var confirm := hud.get_node(ACTIONS + "ConfirmButton") as Button
	var caption := confirm.get_node("MatrixCaption") as Label
	for state in ["normal", "disabled", "hover", "pressed"]:
		var fill := (confirm.get_theme_stylebox(state) as StyleBoxFlat).bg_color
		check(fill.a == 0.0 if state in ["normal", "disabled"] else fill.a > 0.7,
			"Confirm has the requested background in " + state)
	var confirm_cross: Color = confirm.get_node("DisabledCross").modulate
	check(confirm.get_node("DisabledCross").z_index > caption.z_index,
		"Confirm X renders above its caption")
	var confirm_border := (confirm.get_theme_stylebox("disabled") as StyleBoxFlat).border_color
	check(confirm_cross.a >= 0.85 and Vector3(confirm_cross.r, confirm_cross.g, confirm_cross.b).is_equal_approx(
		Vector3(confirm_border.r, confirm_border.g, confirm_border.b)),
		"Confirm has a bright X matching its blue border")
	check(caption.horizontal_alignment == HORIZONTAL_ALIGNMENT_CENTER
		and caption.vertical_alignment == VERTICAL_ALIGNMENT_CENTER
		and caption.get_global_rect().get_center().distance_to(
			confirm.get_global_rect().get_center()) < 1.0,
		"Confirm caption is centred horizontally and vertically")
	await capture("limit-selected")
	main.call("ConfirmChoice")
	await pause(0.15)
	check(status.visible and status.text == hud.get("BothSealedText"),
		"Both choices show locked after confirmation")
	await capture("limit-locked")
	var extraction := main.get_node("UiAudioController/ExtractionPlayer") as AudioStreamPlayer
	var numbers := lattice.get_node("LimitRevealResult") as RichTextLabel
	var heard_extraction := false
	var saw_tutor := false
	for tick in range(500):
		heard_extraction = heard_extraction or extraction.playing
		saw_tutor = saw_tutor or int(lattice.call("GetTutorMarkedAnchorCount")) > 0
		if numbers.visible:
			break
		await pause(0.05)
	check(heard_extraction and extraction.stream != null,
		"Node movement plays the assigned extraction sound")
	check(saw_tutor, "Tutor nodes are marked before extraction")
	check(numbers.visible, "Quantity results appear after extraction")
	await pause(1.2)
	check(numbers.visible and numbers.modulate.a > 0.8,
		"Quantity results remain readable beyond the previous short hold")
	await capture("quantity-hold")
	await pause(2.0)
	var history := hud.get_node(CONTENT + "RightColumn/RightLog/RightVBox/Log") as RichTextLabel
	check(history.text.contains("You") and history.text.contains("Tutor"),
		"Round resolves and keeps both choices in history")
	# Exercise the two Bash sound entry points without replaying the whole tutorial.
	hud.call("BeginBashPlayerExtraction", 1)
	check(extraction.playing, "Bash player extraction has sound")
	hud.call("ShowBashTutorSelection", 1)
	hud.call("BeginBashTutorExtraction")
	check(extraction.playing, "Bash Tutor extraction has sound")
	main.call("BackToTitle")
	await pause(0.6)
	main.queue_free()
	await pause(0.2)
	print("RELEASE_V015_SMOKE " + ("FAIL" if failed else "PASS"))
	get_tree().quit(1 if failed else 0)


func check_text_layers(node: Node) -> void:
	if node is Label or node is RichTextLabel:
		check(node.material != null
			and node.material.resource_path.ends_with("DotMatrixTextMaterial.tres"),
			"Dot matrix text: " + str(node.name))
	if node is Button:
		check(node.has_node("MatrixCaption")
			and node.get_theme_color("font_color").a == 0.0,
			"Button has one dedicated matrix caption: " + str(node.name))
	for child in node.get_children():
		check_text_layers(child)


func capture(name: String) -> void:
	var hud := main.get_node("GameplayHUD") as Control
	if hud.visible:
		hud.call("CompleteTutorTyping")
		var text := hud.get_node(CONTENT + "Center/DialoguePanel/DialogueVBox/Text") as RichTextLabel
		check(text.get_content_height() <= text.size.y + 1.0, "Subtitle fits its panel")
	await RenderingServer.frame_post_draw
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://_qa/v015-ui"))
	get_viewport().get_texture().get_image().save_png("res://_qa/v015-ui/" + name + ".png")


func pause(seconds: float) -> void:
	await get_tree().create_timer(seconds).timeout


func check(ok: bool, message: String) -> void:
	if ok:
		print("PASS: " + message)
	else:
		failed = true
		push_error(message)
