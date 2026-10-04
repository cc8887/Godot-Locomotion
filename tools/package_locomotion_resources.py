"""Package local ALS/Lyra resources without rewriting exported file bytes."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import time
import zipfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--tag', required=True)
    parser.add_argument('--max-part-bytes', type=int, default=1536 * 2**20,
                        help='Maximum uncompressed bytes per part; a larger single file gets its own part.')
    args = parser.parse_args()
    if not re.fullmatch(r'[A-Za-z0-9._-]+', args.tag):
        parser.error('Tag must be a safe filename component.')
    if not 0 < args.max_part_bytes < 2**31:
        parser.error('Part budget must be positive and below 2 GiB.')
    root = args.root.resolve()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    revision = subprocess.check_output(['git', '-C', str(root), 'rev-parse', 'HEAD'], text=True).strip()
    suffixes = {'.json', '.fbx', '.png', '.tres', '.dll'}
    groups = {'als': ['als_v4', 'als_v4_raw'], 'lyra': ['lyra_als']}
    summary = {'tag': args.tag, 'sourceCommit': revision, 'bundles': []}
    for group, directories in groups.items():
        files = []
        for directory in directories:
            source = root / 'assets/generated' / directory
            if not source.is_dir():
                raise FileNotFoundError(source)
            files.extend(p for p in source.rglob('*') if p.is_file()
                         and p.suffix.lower() in suffixes
                         and 'partial' not in p.relative_to(source).parts)
        files.sort(key=lambda p: p.relative_to(root).as_posix())
        parts = [[]]
        part_bytes = 0
        for path in files:
            size = path.stat().st_size
            if size >= 2**31:
                raise RuntimeError(f'Single resource exceeds the upload limit: {path}')
            if parts[-1] and part_bytes + size > args.max_part_bytes:
                parts.append([])
                part_bytes = 0
            parts[-1].append(path)
            part_bytes += size
        manifest = {'schemaVersion': 1, 'tag': args.tag, 'sourceCommit': revision,
                    'bundle': group, 'directories': directories, 'parts': len(parts), 'files': []}
        total = sum(p.stat().st_size for p in files)
        print(f'{group}: {len(files)} resources, {total / 2**30:.2f} GiB before compression', flush=True)
        last_update = time.monotonic()
        completed = 0
        for part_number, part_files in enumerate(parts, 1):
            suffix = f'-part{part_number:02d}' if len(parts) > 1 else ''
            target = output / f'godot-{group}-assets-{args.tag}{suffix}.zip'
            part_rows = []
            with zipfile.ZipFile(target, 'x', compression=zipfile.ZIP_DEFLATED,
                                 compresslevel=3, allowZip64=True) as archive:
                for path in part_files:
                    name = path.relative_to(root).as_posix()
                    digest = hashlib.sha256()
                    size = 0
                    with path.open('rb') as source, archive.open(name, 'w', force_zip64=True) as destination:
                        while chunk := source.read(2**20):
                            digest.update(chunk)
                            size += len(chunk)
                            destination.write(chunk)
                    if size != path.stat().st_size:
                        raise RuntimeError(f'Resource changed while packaging: {name}')
                    manifest['files'].append({'path': name, 'size': size, 'sha256': digest.hexdigest()})
                    part_rows.append(manifest['files'][-1])
                    completed += 1
                    if time.monotonic() - last_update >= 20:
                        print(f'{group}: {completed}/{len(files)} files packaged', flush=True)
                        last_update = time.monotonic()
                for notice in ['ASSET_LICENSE.md', 'THIRD_PARTY_NOTICES.md']:
                    archive.write(root / notice, notice)
                if part_number == len(parts):
                    manifest_name = f'resource-bundles/{group}-assets-manifest.json'
                    archive.writestr(manifest_name, json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
            print(f'{group} part {part_number}/{len(parts)}: verifying archived resource SHA256', flush=True)
            with zipfile.ZipFile(target) as archive:
                for row in part_rows:
                    digest = hashlib.sha256()
                    with archive.open(row['path']) as source:
                        while chunk := source.read(2**20):
                            digest.update(chunk)
                    if digest.hexdigest() != row['sha256'] or archive.getinfo(row['path']).file_size != row['size']:
                        raise RuntimeError(f'Archive content mismatch: {row["path"]}')
            with target.open('rb') as source:
                digest = hashlib.file_digest(source, 'sha256').hexdigest()
            summary['bundles'].append({'file': target.name, 'size': target.stat().st_size,
                                       'sha256': digest, 'resourceFiles': len(part_files),
                                       'uncompressedBytes': sum(p.stat().st_size for p in part_files)})
            if target.stat().st_size >= 2**31:
                raise RuntimeError('Bundle exceeds the GitHub Release single-asset limit.')
            print(f'{group}: verified {target.stat().st_size / 2**20:.1f} MiB archive', flush=True)
    checksums = output / 'SHA256SUMS.txt'
    checksums.write_text(''.join(f'{b["sha256"]}  {b["file"]}\n' for b in summary['bundles']), encoding='utf-8')
    (output / 'resource-bundle-summary.json').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
