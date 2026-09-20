"""Repeat both read-only Stop exports in one ordinary Editor session."""
import os
from pathlib import Path
import runpy
import unreal

directory = Path(__file__).parent
if os.environ.get("ALS_STOP_NOTIFY_QUIT") == "1":
    raise ValueError("The combined exporter owns the final editor exit")
runpy.run_path(str(directory / "export_movement_source_sequences.py"), run_name="__main__")
runpy.run_path(str(directory / "export_stop_notify_inputs.py"), run_name="__main__")
unreal.log("ALS_STOP_RUNTIME_SOURCES_OK assets_saved=0")
unreal.SystemLibrary.quit_editor()
