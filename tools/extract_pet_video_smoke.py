"""Check source pixels, timing and failure isolation through the video CLI."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import subprocess
import sys
import zipfile

from PIL import Image, ImageDraw


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    root = args.output.absolute()
    root.mkdir(parents=True, exist_ok=False)
    for plane in ('rgb', 'alpha', 'mask'):
        (root/plane).mkdir()
    for i in range(18):
        rgb = Image.new('RGB', (128, 72), '#00ff00')
        mask = Image.new('L', rgb.size, 0)
        box = (6+4*i, 19, 17+4*i, 30)
        ImageDraw.Draw(rgb).rectangle(box, fill=(220-i, 30+i, 60))
        ImageDraw.Draw(mask).rectangle(box, fill=255)
        rgb.save(root/'rgb'/f'{i+1:03d}.png')
        mask.save(root/'mask'/f'{i+1:03d}.png')
        rgb.putalpha(mask)
        rgb.save(root/'alpha'/f'{i+1:03d}.png')
    for plane, fmt in [('rgb','bgr0'), ('alpha','bgra'), ('mask','gray')]:
        subprocess.run(['ffmpeg','-nostdin','-v','error','-framerate','12','-start_number','1',
                        '-i',str(root/plane/'%03d.png'),'-c:v','ffv1','-pix_fmt',fmt,
                        str(root/(plane+'.mkv'))], check=True, timeout=30)

    cli = Path(__file__).absolute().with_name('extract_pet_video.py')
    def call(parts, success=True):
        process = subprocess.run([sys.executable, str(cli), *map(str, parts)], capture_output=True, timeout=45)
        result = json.loads(process.stdout)
        assert result['valid'] == success and process.returncode == (0 if success else 2), result
        return result

    rgb_source = root/'rgb.mkv'
    source_digest = hashlib.sha256(rgb_source.read_bytes()).hexdigest()
    info = call(['--video',rgb_source,'--probe'])
    assert (info['width'], info['height'], info['duration'], info['fps']) == (128, 72, 1.5, 12)
    thumb = root/'thumb.png'
    call(['--video',rgb_source,'--thumbnail',thumb,'--start','.25'])
    with Image.open(thumb) as image:
        assert image.convert('RGB').tobytes() == Image.open(root/'rgb'/'004.png').tobytes()

    checks = {}
    # 12fps MKV stores timestamps such as 166.67ms as 167ms. Deliberately
    # downsample a trimmed interval; neither the source nor the matching mask
    # may skip an action frame at this millisecond rounding boundary.
    for method, fps, samples in [('chroma', 6, [3,5,7]), ('mask', 12, [3,4,5,6,7,8]),
                                 ('alpha', 6, [3,5,7])]:
        source = root/'alpha.mkv' if method == 'alpha' else rgb_source
        output = root/(method+'.lotheme')
        command = ['--video',source,'--output',output,'--method',method,'--start','.25',
                   '--duration','.5','--fps',fps,'--slot','click','--loop']
        if method == 'mask':
            command += ['--mask',root/'mask.mkv']
        report = call(command)
        assert report['frames'] == len(samples) and report['playbackSeconds'] == .5
        with zipfile.ZipFile(output) as archive:
            manifest = json.loads(archive.read('manifest.json'))
            action = next(a for a in manifest['actions'] if a['slot'] == 'click')
            assert manifest['review'] == 'required' and action['loop'] and action['fps'] == fps
            for index, name in zip(samples, action['frames']):
                frame = Image.open(io.BytesIO(archive.read(name)))
                alpha = frame.getchannel('A')
                x = 6+4*(index-samples[0])
                assert alpha.getbbox() == (x,6,x+12,18), (name, alpha.getbbox(), x)
                assert frame.getpixel((x+1,7)) == (220-index,30+index,60,255)
                assert frame.getpixel((1,1)) == (0,0,0,0)
        digest = hashlib.sha256(output.read_bytes()).hexdigest()
        call(command, False)
        assert hashlib.sha256(output.read_bytes()).hexdigest() == digest
        checks[method] = {'sourcedFrames': samples, 'duration': .5, 'overwriteRefused': True}
    for name, extra in [('out-of-range',['--start','1.2','--duration','.6']),
                        ('too-many',['--duration','5','--fps','30']),
                        ('empty-matte',['--duration','.5','--tolerance','255']),
                        ('invalid-rect',['--duration','.5','--method','grabcut','--rect','1,0,.5,.5'])]:
        output = root/(name+'.lotheme')
        call(['--video',rgb_source,'--output',output,*extra], False)
        assert not output.exists()
    assert hashlib.sha256(rgb_source.read_bytes()).hexdigest() == source_digest
    assert not list(root.glob('.ai-pet-video-*'))
    result = {'passed':True,'sourcePreserved':True,'trimmedThumbnailMatches':True,
              'methods':checks,'failureOutputsAbsent':True}
    (root/'result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result))


if __name__ == '__main__':
    main()
