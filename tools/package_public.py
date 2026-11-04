"""Build a portable public preview without private photos, models or QA records."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--version', default='0.3.0-preview.6')
    args = parser.parse_args()
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?', args.version):
        parser.error('Invalid version')
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    stage = output / ('AI-PET-' + args.version)
    stage.mkdir()  # Refuse to mix in files from an earlier packaging attempt.
    subprocess.run(['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
                    str(ROOT / 'runtime/build.ps1'), '-Public'], cwd=ROOT, check=True)
    for name in ['AIPetDesktop.exe', 'AIPetDesktop.exe.config', 'reference-cat.jpg', 'public-default.lotheme']:
        shutil.copy2(ROOT / 'runtime/public-publish' / name, stage / name)
    for name in ['README.md', 'IMPLEMENTATION-PLAN.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md',
                 'docs/media/ai-pet-codex-demo.gif', 'docs/media/ai-pet-action-preview.png']:
        target = stage / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / name, target)
    shutil.copy2(ROOT / 'runtime/install-public.ps1', stage / 'install.ps1')
    (stage / 'tools').mkdir()
    shutil.copy2(ROOT / 'tools/ai-pet.ps1', stage / 'tools/ai-pet.ps1')
    shutil.copy2(ROOT / 'tools/codex-connection.ps1', stage / 'tools/codex-connection.ps1')
    shutil.copy2(ROOT / 'tools/codex_desktop_probe.ps1', stage / 'tools/codex_desktop_probe.ps1')
    for name in ['extract_pet_video.py', 'generate_pet_comfy.py', 'configure_local_generator.ps1', 'run_birefnet.py', 'theme_package.py', 'import_sprite_atlas.py', 'birefnet-requirements.txt']:
        shutil.copy2(ROOT / 'tools' / name, stage / 'tools' / name)
    shutil.copytree(ROOT / 'skills/ai-pet', stage / 'skills/ai-pet')
    files = [{'path': str(p.relative_to(stage)).replace('\\', '/'), 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()}
             for p in sorted(stage.rglob('*')) if p.is_file()]
    (stage / 'public-package.json').write_text(json.dumps({'productId': 'ai-pet', 'version': args.version, 'files': files}, indent=2), encoding='utf-8')
    archive = output / (stage.name + '-windows-x64.zip')
    with zipfile.ZipFile(archive, 'x', compression=zipfile.ZIP_DEFLATED) as package:
        for p in sorted(stage.rglob('*')):
            if p.is_file():
                package.write(p, str(p.relative_to(stage)))
    print(json.dumps({'archive': str(archive), 'sha256': hashlib.sha256(archive.read_bytes()).hexdigest(), 'files': len(files) + 1}))


if __name__ == '__main__':
    main()
