"""Export an explicit source allowlist; never include local works or model weights."""
from __future__ import annotations
import argparse
import ast
import hashlib
import json
from pathlib import Path
import shutil
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def public_files() -> list[Path]:
    names = {'.gitignore', '.gitattributes', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'README.md', 'IMPLEMENTATION-PLAN.md',
             'index.html', 'app.js', 'styles.css', 'simple-ui.js', 'simple-ui.css', 'theme-frame-player.js',
             'runtime/DesktopPet.cs', 'runtime/PetExperience.cs', 'runtime/PetCodex.cs', 'runtime/build.ps1', 'runtime/install-public.ps1',
             'runtime/PetVideoExperience.cs', 'runtime/PetVideoRequests.cs', 'runtime/PetVideoImport.cs', 'tools/extract_pet_video.py', 'tools/extract_pet_video_smoke.py',
             'runtime/PetGeneration.cs', 'runtime/PetDisplay.cs', 'runtime/PetAppearance.cs', 'runtime/PetCodexTitles.cs', 'runtime/PetCodexFollow.cs', 'runtime/app.manifest', 'runtime/AIPetDesktop.exe.config',
             'tools/generate_pet_comfy.py', 'tools/configure_local_generator.ps1', 'tools/generation_smoke.py', 'tools/birefnet-requirements.txt',
             'tools/import_sprite_atlas.py', 'tools/import_sprite_atlas_smoke.py', 'tools/research_vace_benchmark.py',
             'tools/codex-connection.ps1', 'tools/codex_connection_smoke.py', 'tools/codex_desktop_probe.ps1',
             'tools/ai-pet.ps1', 'tools/simple_experience_smoke.ps1', 'tools/export_public_source.py',
             'tools/build_creator.py', 'tools/package_public.py', 'tools/creator-requirements.txt', 'tools/matting-requirements.txt'}
    tree = ast.parse((ROOT / 'tools/build_creator.py').read_text(encoding='utf-8-sig'))
    for statement in tree.body:
        if isinstance(statement, ast.Assign) and any(isinstance(t, ast.Name) and t.id == 'SOURCE_FILES' for t in statement.targets):
            names.update('tools/' + name for name in ast.literal_eval(statement.value))
    # Optional local-model workers: weights and environments remain external.
    for name in ['creator_segmentation_worker.py', 'birefnet_service.py', 'evaluate_face_landmarks.py',
                 'prepare_face_landmark_draft.py', 'prepare_opaque_pet_interior.py', 'repair_model_ear_contour.py',
                 'refine_model_detached_fringe.py', 'model_head_workflow.py', 'model_head_plan.py',
                 'run_animal_head_driver.py', 'run_animal_eye_driver.py', 'segment_model_frames_sam2_image.py',
                 'refine_model_gray_matte.py', 'transfer_model_matte_residual.py', 'video_alpha.py',
                 'segment_video_sam2.py', 'repair_matte_interior_opacity.py', 'codex_hook_relay.py']:
        names.add('tools/' + name)
    names.update(str(p.relative_to(ROOT)).replace('\\', '/') for p in (ROOT / 'skills/ai-pet').rglob('*') if p.is_file())
    return [ROOT / name for name in sorted(names)]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    records = []
    for source in public_files():
        relative = source.relative_to(ROOT)
        target = output / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
        records.append({'path': str(relative).replace('\\', '/'), 'sha256': hashlib.sha256(source.read_bytes()).hexdigest()})
    report = output.parent / (output.name + '-manifest.json')
    report.write_text(json.dumps({'files': records, 'excludes': ['private photos', 'works', 'weights', 'environments', 'QA history']}, indent=2), encoding='utf-8')
    archive = output.parent / (output.name + '.zip')
    with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED) as package:
        for record in records:
            package.write(output / record['path'], record['path'])
    print(json.dumps({'files': len(records), 'source': str(output), 'archive': str(archive)}))


if __name__ == '__main__':
    main()
