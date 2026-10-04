@tool
extends EditorPlugin

const MENU_NAME := "Compile ALS Asset Set"
const BRIDGE_SCRIPT_PATH := "res://src/Als.Godot/Import/AlsImporterPlugin.cs"
var _lyra_math_export: EditorExportPlugin

func _enter_tree() -> void:
	add_tool_menu_item(MENU_NAME, _run_import)
	_lyra_math_export = preload("res://addons/als_importer/lyra_native_math_export.gd").new()
	add_export_plugin(_lyra_math_export)

func _exit_tree() -> void:
	remove_export_plugin(_lyra_math_export)
	_lyra_math_export = null
	remove_tool_menu_item(MENU_NAME)

func _run_import() -> void:
	var bridge_script := load(BRIDGE_SCRIPT_PATH) as Script
	if bridge_script == null or not bridge_script.can_instantiate():
		push_error("ALS importer requires the Godot .NET editor and a successful build of GodotALS.csproj.")
		return
	var bridge: Object = bridge_script.new()
	bridge.call("RunImport")
