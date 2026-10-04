"""Resolve portable tool paths relative to this checkout, independent of cwd."""
import os
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parents[1]


def repository_path(path='.'):
    value = Path(path)
    return (value if value.is_absolute() else REPOSITORY_ROOT / value).resolve()


def project_path(path='.'):
    project_file = os.environ.get('LYRA_UE_PROJECT_FILE')
    base = (repository_path(project_file).parent if project_file else
            repository_path(os.environ.get('LYRA_UE_PROJECT_ROOT') or '../GASP58'))
    return base / path


def engine_path(path='.'):
    return repository_path(os.environ.get('UE_ENGINE_ROOT') or '../UE_5.8') / path


def unreal_source_path(path='.'):
    return repository_path(os.environ.get('UNREAL_ENGINE_SOURCE_ROOT') or '../UnrealEngine') / path


def rig_reference_path(path='.'):
    return repository_path(os.environ.get('LYRA_RIG_REFERENCE_HOST') or '../GLRigRef/HostProject') / path


def rig_solver_path(path='.'):
    return repository_path(os.environ.get('LYRA_RIG_SOLVER_HOST') or '../GLRigSolve/HostProject') / path
