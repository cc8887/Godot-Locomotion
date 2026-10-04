@tool
extends EditorExportPlugin

func _get_name() -> String:
	return "LyraNativeMath"

func _export_begin(features: PackedStringArray, _is_debug: bool, _path: String, _flags: int) -> void:
	if features.has("windows") and features.has("x86_64"):
		var source := "res://assets/generated/lyra_als/native_win64/LyraNativeMath.dll"
		if FileAccess.file_exists(source):
			# A shared object is copied beside the executable, outside the PCK.
			add_shared_object(ProjectSettings.globalize_path(source), PackedStringArray(["windows", "x86_64"]), "")
		elif FileAccess.file_exists("res://assets/generated/lyra_als/locomotion_resources.json"):
			push_error("Build LyraNativeMath.dll with scripts/build-lyra-native-math.ps1 before exporting Lyra on Win64.")
