@tool
extends EditorPlugin

const MENU_NAME := "Compile ALS Asset Set"

func _enter_tree() -> void:
	add_tool_menu_item(MENU_NAME, _run_import)

func _exit_tree() -> void:
	remove_tool_menu_item(MENU_NAME)

func _run_import() -> void:
	var bridge := AlsImporterPlugin.new()
	bridge.run_import()
