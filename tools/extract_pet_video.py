"""Extract a reviewable transparent action from a local video. No motion generation."""
from __future__ import annotations

import argparse
from collections import Counter
from fractions import Fraction
import json
import math
import os
from pathlib import Path
import re
import shutil
from statistics import median
import subprocess
import sys
import tempfile


EXTENSIONS = {'.mp4', '.mov', '.m4v', '.mkv', '.webm', '.avi'}
SLOTS = ('ambient', 'recent-activity', 'needs-user', 'stop-unknown', 'interrupt', 'hover', 'click')
INPUT_OPTIONS = ['-protocol_whitelist', 'file,pipe', '-format_whitelist', 'mov,matroska,webm,avi']


def run_media(command, timeout, cwd=None):
    program = shutil.which(command[0])
    if not program:
        raise ValueError(f'缺少 {command[0]}，请安装 FFmpeg 并将其 bin 目录放入 PATH。')
    result = subprocess.run([program, *command[1:]], cwd=cwd, stdin=subprocess.DEVNULL,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout,
                            creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
    if result.returncode:
        raise ValueError(f'{command[0]} 无法读取或处理该视频。' + result.stderr.decode('utf-8', 'replace')[-420:])
    return result.stdout


def rate(value):
    try:
        return float(Fraction(str(value)))
    except (ValueError, ZeroDivisionError, OverflowError):
        return 0.0


def probe(path):
    if path.suffix.lower() not in EXTENSIONS or not path.is_file():
        raise ValueError('请选择本机的 MP4、MOV、M4V、MKV、WebM 或 AVI 视频。')
    info = json.loads(run_media([
        'ffprobe', '-v', 'error', *INPUT_OPTIONS, '-select_streams', 'V:0', '-show_entries',
        'stream=width,height,codec_name,sample_aspect_ratio,avg_frame_rate,r_frame_rate,duration:'
        'stream_side_data=rotation:stream_tags=alpha_mode,rotate:format=duration', '-of', 'json', str(path)
    ], 20))
    if not info.get('streams'):
        raise ValueError('文件中没有可播放的视频轨道。')
    stream = info['streams'][0]
    w, h = int(stream['width']), int(stream['height'])
    if min(w, h) < 1 or max(w, h) > 8192 or w * h > 34_000_000:
        raise ValueError('视频分辨率超过支持范围。')
    duration = rate(stream.get('duration')) or rate(info.get('format', {}).get('duration'))
    if duration <= 0 or not math.isfinite(duration):
        raise ValueError('无法确认该视频的时长。')
    tags = stream.get('tags', {})
    rotation = float(tags.get('rotate', 0))
    for data in stream.get('side_data_list', []):
        if 'rotation' in data:
            rotation = float(data['rotation'])
    sar = rate(stream.get('sample_aspect_ratio', '1:1').replace(':', '/')) or 1
    if sar <= 0 or not math.isfinite(sar):
        raise ValueError('视频像素比例无效。')
    w = max(1, round(w * sar))
    if round(rotation) % 180 == 90:
        w, h = h, w
    ratio = min(1, 640 / max(w, h))
    return dict(width=w, height=h, duration=duration,
                fps=rate(stream.get('avg_frame_rate')) or rate(stream.get('r_frame_rate')),
                frameWidth=max(1, round(w*ratio)), frameHeight=max(1, round(h*ratio)),
                codec=stream.get('codec_name'), taggedAlpha=tags.get('alpha_mode') == '1')


def decode(path, info, start, destination, fps=None, duration=None):
    # FFmpeg's normal display-matrix autorotation matches the shown selection.
    # Numbered PNGs bound memory and retain the same sampled timeline for masks.
    command = ['ffmpeg', '-nostdin', '-v', 'error', '-xerror', *INPUT_OPTIONS,
               '-ss', str(start), '-threads', '2']
    if info['taggedAlpha'] and info['codec'] in ('vp8', 'vp9'):
        command += ['-c:v', 'libvpx-vp9' if info['codec'] == 'vp9' else 'libvpx']
    command += ['-i', str(path), '-map', '0:V:0', '-an', '-sn', '-dn']
    filters = []
    if fps is not None:
        # Round source timestamps upward onto the sampling grid. At t=0 this
        # selects the same first decoded frame shown by --thumbnail (round=near
        # can advance the first frame of fast 30/60-fps footage).
        # Containers such as MKV quantize timestamps to milliseconds, so a CFR
        # frame at 166.67ms may carry PTS=167ms. Allow one millisecond before
        # rounding to avoid shifting an already-aligned source or mask by a frame.
        filters.append(f'settb=AVTB,setpts=PTS-STARTPTS-0.001/TB,fps={fps}:start_time=0:round=up')
        command += ['-t', str(duration)]
    filters.append(f"scale={info['frameWidth']}:{info['frameHeight']},setsar=1")
    command += ['-vf', ','.join(filters), '-frames:v',
                str(math.ceil(duration * fps) if fps else 1), '-threads', '1',
                '-c:v', 'png', '-pix_fmt', 'rgba', '-nostats', 'frame-%03d.png']
    run_media(command, 100, destination)
    frames = sorted(destination.glob('frame-*.png'))
    if not frames:
        raise ValueError('选定位置未读到画面，请调整视频起点。')
    return frames


def background_color(frame, key):
    if key.lower() != 'auto':
        if not re.fullmatch(r'#[0-9a-fA-F]{6}', key):
            raise ValueError('背景颜色格式应为 #RRGGBB 或 auto。')
        return tuple(bytes.fromhex(key[1:]))
    small = frame.convert('RGB')
    small.thumbnail((96, 96))
    w, h = small.size
    pixels = list(small.crop((0, 0, w, 1)).getdata()) + list(small.crop((0, h-1, w, h)).getdata())
    pixels += list(small.crop((0, 0, 1, h)).getdata()) + list(small.crop((w-1, 0, w, h)).getdata())
    def bucket(rgb):
        return tuple(v // 16 for v in rgb)
    dominant = Counter(map(bucket, pixels)).most_common(1)[0][0]
    sample = [p for p in pixels if bucket(p) == dominant]
    return tuple(round(median(channel)) for channel in zip(*sample))


def chroma_matte(first, args):
    from PIL import Image, ImageChops
    color = background_color(first, args.color)
    background = Image.new('RGB', first.size, color)
    ramp = [0 if i <= args.tolerance else min(255, round(255 * (i - args.tolerance) / max(1, args.softness)))
            for i in range(256)]
    if args.softness == 0:
        ramp = [255 if i > args.tolerance else 0 for i in range(256)]
    def matte(frame):
        r, g, b = ImageChops.difference(frame.convert('RGB'), background).split()
        return ImageChops.lighter(r, ImageChops.lighter(g, b)).point(ramp)
    return matte


def parse_rect(value):
    try:
        x, y, w, h = map(float, value.split(','))
    except (AttributeError, ValueError):
        raise ValueError('框选抠像需要 x,y,width,height 四个 0 到 1 的数。') from None
    if not all(math.isfinite(n) for n in (x, y, w, h)) or min(x, y) < 0 or min(w, h) <= 0 or max(x+w, y+h) > 1.000001:
        raise ValueError('宠物选框必须位于预览画面内。')
    return x, y, w, h


def tracked_grabcut(first, rect, frames):
    try:
        import cv2
        import numpy as np
    except ImportError as error:
        raise ValueError('框选抠像需要在运行脚本的 Python 中安装 opencv-python 和 numpy。') from error
    from PIL import Image, ImageFilter
    x, y, w, h = parse_rect(rect)
    fw, fh = first.size
    box = (round(x*fw), round(y*fh), min(round(w*fw), fw-round(x*fw)), min(round(h*fh), fh-round(y*fh)))
    if min(box[2:]) < 4 or box[2]*box[3] >= fw*fh*.99:
        raise ValueError('请框住完整的宠物，并在选框外留出一些背景。')
    cv2.setNumThreads(1)
    cv2.setRNGSeed(0)
    # On tripod footage the animal often exposes the background as it moves.
    # Use a bounded temporal median as a second cue; skip it for moving cameras
    # or when too little of the selected animal moves for the estimate to help.
    samples = []
    for i in np.linspace(0, len(frames)-1, min(9, len(frames))).round().astype(int):
        with Image.open(frames[i]) as sample:
            samples.append(cv2.cvtColor(np.asarray(sample), cv2.COLOR_RGBA2BGR))
    backdrop = np.median(samples, axis=0).astype(np.uint8)
    bg, fg = np.zeros((1, 65), np.float64), np.zeros((1, 65), np.float64)
    yy, xx = np.indices((fh, fw), dtype=np.float32)
    previous_gray = previous_alpha = original_contrast = None
    def matte(frame):
        nonlocal previous_gray, previous_alpha, original_contrast
        bgr = cv2.cvtColor(np.asarray(frame), cv2.COLOR_RGBA2BGR)
        gray = cv2.cvtColor(bgr, cv2.COLOR_BGR2GRAY)
        if previous_gray is None:
            mask = np.zeros(gray.shape, np.uint8)
            cv2.grabCut(bgr, mask, box, bg, fg, 5, cv2.GC_INIT_WITH_RECT)
            search = np.zeros_like(mask)
            bx, by, bw, bh = box
            search[by:by+bh, bx:bx+bw] = 1
        else:
            # Backward optical flow propagates the selection only. Preserve RGB
            # from this actual frame, including genuine camera/animal movement.
            flow = cv2.calcOpticalFlowFarneback(gray, previous_gray, None, .5, 4, 21, 3, 5, 1.2, 0)
            prior = cv2.remap(previous_alpha, xx+flow[:,:,0], yy+flow[:,:,1], cv2.INTER_NEAREST)
            search = cv2.dilate(prior, np.ones((45, 45), np.uint8))
            mask = np.where(search, cv2.GC_PR_BGD, cv2.GC_BGD).astype(np.uint8)
            mask[prior > 0] = cv2.GC_PR_FGD
        delta = cv2.absdiff(bgr, backdrop).max(axis=2)
        moving = (delta > 24) & (search > 0)
        provisional = (mask & 1) > 0
        if ((delta < 12).mean() > .8 and moving.sum() > 16 and
                (moving & provisional).sum() > .33 * max(1, provisional.sum())):
            support = cv2.dilate(moving.astype(np.uint8), np.ones((9, 9), np.uint8))
            mask[(support == 0) & (delta < 8)] = cv2.GC_BGD
            mask[moving] = cv2.GC_PR_FGD
            core = cv2.erode(moving.astype(np.uint8), np.ones((3, 3), np.uint8))
            mask[core > 0] = cv2.GC_FGD
        elif previous_gray is not None:
            core = cv2.erode(prior, np.ones((5, 5), np.uint8))
            mask[core > 0] = cv2.GC_FGD
        cv2.grabCut(bgr, mask, None, bg, fg, 2, cv2.GC_EVAL)
        hard = (mask & 1).astype(np.uint8)
        # A nearby moving person or a shadow can enter the expanded search area.
        # Keep the same selected object across frames instead of admitting every
        # disconnected foreground component and growing the next search area.
        component_count, labels, stats, _ = cv2.connectedComponentsWithStats(hard, 8)
        if component_count > 1:
            areas = stats[1:, cv2.CC_STAT_AREA]
            if previous_gray is None:
                selected = 1 + int(np.argmax(areas))
            else:
                overlap = np.bincount(labels[prior != 0], minlength=component_count)[1:]
                iou = overlap / np.maximum(1, prior.sum() + areas - overlap)
                selected = 1 + int(np.argmax(iou))
                if iou[selected-1] < .10:
                    raise ValueError('框选跟踪丢失，请缩短动作片段或使用 Agent 遮罩。')
            hard = (labels == selected).astype(np.uint8)
        if hard.sum() < 16:
            raise ValueError('跟踪丢失了宠物，请缩短片段、调整选框，或交给 Agent 处理。')
        contrast = bgr[hard > 0].std(axis=0).mean()
        if original_contrast is None:
            original_contrast = contrast
        elif original_contrast > 20 and contrast < original_contrast * .28:
            raise ValueError('抠像很可能偏移到了背景，请缩短片段、调整选框或使用 Agent 抠像。')
        previous_gray, previous_alpha = gray, hard
        return Image.fromarray(hard*255).filter(ImageFilter.GaussianBlur(.55))
    return matte


def prepare_matte(first, args, work, frames):
    if args.method == 'chroma':
        return chroma_matte(first, args)
    if args.method == 'grabcut':
        return tracked_grabcut(first, args.rect, frames)
    if args.method == 'birefnet':
        from run_birefnet import DEFAULT_MODEL, load_birefnet, segment_photo
        model = args.model_dir or DEFAULT_MODEL
        if not (model / 'model.safetensors').is_file():
            raise ValueError('未找到本机 BiRefNet 权重，请指定已配置的本地模型目录。')
        runtime = load_birefnet(model, args.device, work/'module-cache')
        return lambda frame: segment_photo(frame.convert('RGB'), runtime, 1024)[0]
    return None


def publish(source, target):
    # Stage on the output volume. A concurrent run can't replace an existing
    # candidate between the earlier existence check and this atomic publish.
    if os.name == 'nt':
        os.rename(source, target)
    else:
        os.link(source, target)


def extract(args, path, info, target, work):
    from PIL import Image, ImageChops
    from theme_package import pack_multi
    if not (math.isfinite(args.duration) and 0 < args.duration <= 12 and 1 <= args.fps <= 30 and
            2 <= math.ceil(args.duration * args.fps) <= 120):
        raise ValueError('选段须大于 0 且不超过 12 秒，帧率 1–30，每段须有 2–120 帧。')
    if not 1 <= len(args.name.strip()) <= 48:
        raise ValueError('名称长度应为 1 到 48 字。')
    if min(args.tolerance, args.softness) < 0 or max(args.tolerance, args.softness) > 255:
        raise ValueError('颜色容差与柔和度范围为 0–255。')
    if args.method == 'grabcut':
        parse_rect(args.rect)
    if args.start + args.duration > info['duration'] + .001:
        raise ValueError('选段结束时间超过视频时长，请缩短时长或提前起点。')
    duration = min(args.duration, info['duration'] - args.start)
    decoded = work/'decoded'
    decoded.mkdir()
    frames = decode(path, info, args.start, decoded, args.fps, duration)
    if len(frames) < 2:
        raise ValueError('该片段不足两帧，请选择更长的视频片段。')
    masks = []
    if args.method == 'mask':
        if not args.mask:
            raise ValueError('遮罩模式需要与原片时间轴对齐的灰度视频。')
        mask_path = args.mask.absolute()
        mask_info = probe(mask_path)
        if abs((mask_info['width']/mask_info['height'])/(info['width']/info['height']) - 1) > .01:
            raise ValueError('遮罩画面比例与原片不符。')
        mask_dir = work/'masks'
        mask_dir.mkdir()
        masks = decode(mask_path, mask_info, args.start, mask_dir, args.fps, duration)
        if len(masks) != len(frames):
            raise ValueError('遮罩和视频的选段帧数不一致。')
    with Image.open(frames[0]) as first:
        matting = prepare_matte(first.convert('RGBA'), args, work, frames)
    action = work/args.slot
    action.mkdir()
    union = None
    different = False
    initial_bytes = None
    for i, frame_path in enumerate(frames):
        with Image.open(frame_path) as image:
            frame = image.convert('RGBA')
        alpha = frame.getchannel('A')
        if masks:
            with Image.open(masks[i]) as mask:
                alpha = ImageChops.multiply(alpha, mask.convert('L').resize(frame.size, Image.Resampling.BILINEAR))
        elif matting:
            alpha = ImageChops.multiply(alpha, matting(frame))
        histogram = alpha.histogram()
        bbox = alpha.getbbox()
        if not bbox or sum(histogram[32:]) < 8:
            raise ValueError(f'第 {i+1} 帧没有可见宠物，请调整选框或颜色。')
        if not sum(histogram[:32]):
            raise ValueError(f'第 {i+1} 帧没有抠掉背景，请调整选框、背景色或遮罩。')
        if union is None:
            union = bbox
        else:
            union = (min(union[0], bbox[0]), min(union[1], bbox[1]),
                     max(union[2], bbox[2]), max(union[3], bbox[3]))
        frame.putalpha(alpha)
        # Transparent RGB cannot count as visible motion or leak the removed background.
        frame.paste((0,0,0,0), mask=alpha.point(lambda a: 255 if not a else 0))
        if not different:
            if i == 0:
                initial_bytes = frame.tobytes()
            else:
                different = frame.tobytes() != initial_bytes
        frame.save(action / f'frame-{i:03d}.png')
    if not different:
        raise ValueError('选段只有重复静帧，没有可提取的动作。')
    margin = 6
    box = (union[0]-margin, union[1]-margin, union[2]+margin, union[3]+margin)
    for frame_path in sorted(action.glob('frame-*.png')):
        with Image.open(frame_path) as image:
            fitted = image.crop(box)
        fitted.save(frame_path)
    idle = work/'idle'
    idle.mkdir()
    shutil.copyfile(action/'frame-000.png', idle/'frame-000.png')
    spec = {'name': args.name.strip(), 'actions': [
        {'slot':'idle', 'fps':1, 'loop':True, 'directory':'idle'},
        {'slot':args.slot, 'fps':args.fps, 'loop':args.loop, 'directory':args.slot}]}
    spec_path = work/'spec.json'
    spec_path.write_text(json.dumps(spec, ensure_ascii=False), encoding='utf-8')
    candidate = work/'candidate.lotheme'
    pack_multi(argparse.Namespace(spec=spec_path, output=candidate))
    publish(candidate, target)
    return dict(valid=True, output=str(target), method=args.method, start=args.start, duration=duration,
                fps=args.fps, frames=len(frames), playbackSeconds=len(frames)/args.fps,
                slot=args.slot, loop=args.loop, review='required', generatedMotion=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--video', required=True, type=Path)
    commands = parser.add_mutually_exclusive_group(required=True)
    commands.add_argument('--probe', action='store_true')
    commands.add_argument('--thumbnail', type=Path)
    commands.add_argument('--output', type=Path)
    parser.add_argument('--start', type=float, default=0)
    parser.add_argument('--duration', type=float, default=2)
    parser.add_argument('--fps', type=int, default=12)
    parser.add_argument('--slot', choices=SLOTS, default='click')
    parser.add_argument('--name', default='视频宠物动作')
    parser.add_argument('--loop', action='store_true')
    parser.add_argument('--method', choices=('chroma','grabcut','birefnet','mask','alpha'), default='chroma')
    parser.add_argument('--color', default='auto')
    parser.add_argument('--tolerance', type=int, default=40)
    parser.add_argument('--softness', type=int, default=20)
    parser.add_argument('--rect', help='x,y,width,height in normalized upright display coordinates')
    parser.add_argument('--mask', type=Path)
    parser.add_argument('--device', choices=('cpu','cuda'), default='cuda')
    parser.add_argument('--model-dir', type=Path)
    args = parser.parse_args()
    try:
        path = args.video.absolute()
        info = probe(path)
        if args.probe:
            result = dict(valid=True, **info)
        else:
            if not math.isfinite(args.start) or not 0 <= args.start < info['duration']:
                raise ValueError('开始时间必须位于视频范围内。')
            target = (args.thumbnail or args.output).absolute()
            if target.exists():
                raise ValueError('目标文件已存在，请选择新的候选文件名。')
            target.parent.mkdir(parents=True, exist_ok=True)
            with tempfile.TemporaryDirectory(prefix='.ai-pet-video-', dir=target.parent) as temporary:
                work = Path(temporary)
                if args.thumbnail:
                    sample = decode(path, info, args.start, work)[0]
                    publish(sample, target)
                    result = dict(valid=True, thumbnail=str(target), **info)
                else:
                    result = extract(args, path, info, target, work)
        print(json.dumps(result, ensure_ascii=True))
        return 0
    except Exception as error:
        print(json.dumps({'valid':False, 'error':str(error)}, ensure_ascii=True))
        return 2


if __name__ == '__main__':
    sys.exit(main())
