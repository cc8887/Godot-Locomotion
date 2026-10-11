"""Limit Godot scanning to originals, Lyra, active ALS pack and requested candidates.

Only .gdignore markers are changed; payloads and import caches are preserved.
Run without --apply to preview. --include may be repeated for diagnostic packs.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MARKER = b"# Managed by tools/godot_resource_scan.py; historical resources retained.\n"


def generated_directory(root: Path, value: str) -> Path:
    generated = (root / 'assets/generated').resolve()
    path = (root / value[6:]).resolve() if value.startswith('res://') else Path(value)
    if not value.startswith('res://'):
        path = (root / path).resolve()
    if not path.exists():
        raise ValueError(f'Missing generated path: {value}')
    if path.is_file():
        path = path.parent
    if not path.is_relative_to(generated) or path == generated:
        raise ValueError(f'Expected a directory or pack inside assets/generated: {value}')
    directory = generated / path.relative_to(generated).parts[0]
    if not directory.is_dir():
        raise ValueError(f'Missing generated directory: {directory}')
    return directory


def configure(root: Path = ROOT, *, includes=(), apply=False) -> dict:
    root = root.resolve()
    generated = root / 'assets/generated'
    keep = {generated_directory(root, 'res://assets/generated/' + name)
            for name in ('als_v4', 'als_v4_raw', 'lyra_als')}
    pointer = root / 'assets/config/als_resource_pack.json'
    if pointer.exists():
        selected = json.loads(pointer.read_text(encoding='utf-8-sig'))['pack']
        if not (root / selected.removeprefix('res://')).is_file():
            raise ValueError(f'Missing active pack: {selected}')
        keep.add(generated_directory(root, selected))
    keep.update(generated_directory(root, value) for value in includes)
    # Validate all removals before making changes. Preserve custom ignore policies.
    for directory in keep:
        marker = directory / '.gdignore'
        if marker.exists() and marker.read_bytes() not in (b'', MARKER):
            raise ValueError(f'Refusing to replace custom ignore marker: {marker}')
    visible, ignored, changes = [], [], []
    for directory in sorted(generated.iterdir()):
        if not directory.is_dir():
            continue
        if directory.is_symlink() or directory.resolve().parent != generated.resolve():
            raise ValueError(f'Linked generated directory is not supported: {directory}')
        marker = directory / '.gdignore'
        if directory.resolve() in keep:
            visible.append(directory.name)
            if marker.exists():
                changes.append(dict(directory=directory.name, action='unignore'))
        else:
            ignored.append(directory.name)
            if not marker.exists():
                changes.append(dict(directory=directory.name, action='ignore'))
    if apply:
        for change in changes:
            marker = generated / change['directory'] / '.gdignore'
            if change['action'] == 'unignore':
                marker.unlink()
            else:
                marker.write_bytes(MARKER)
    return dict(applied=apply, visible=visible, ignored=ignored, changes=changes)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--include', action='append', default=[],
                        help='Additional generated directory or resource-pack.json path')
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    report = configure(includes=args.include, apply=args.apply)
    text = json.dumps(report, indent=2, ensure_ascii=False) + '\n'
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(text, encoding='utf-8')
    print(text)


if __name__ == '__main__':
    main()
