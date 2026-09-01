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


func _assert(condition: bool, message: String) -> void:
	if condition:
		return

	_failed = true
	push_error(message)
