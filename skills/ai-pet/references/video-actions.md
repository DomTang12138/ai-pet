# Extract an action from a pet video

The desktop video workflow saves a local copy of the selected video, clip start, duration, sampling rate, destination slot, loop setting and optional instructions. The user can adjust a local cutout or hand the same clip to their Agent for masking. Treat the handoff as a request to isolate the recorded pet and preserve its motion. Keep frame order, timing, proportions and appearance.

Use the executable, data directory and request ID from the handoff. `ai-pet.ps1 -Action video-requests` lists video requests. Read the selected `request.json` and check that its `kind` is `pet-video-cutout`, the ID matches, and the saved video's SHA-256 matches `source.sha256`. Resolve `source.path` against the request directory. The source is a user video. Use local processing unless the user approved uploading it to a named service.

For headless creation, `-Action video-create -CreationSpec FILE` accepts this JSON shape and returns the new ID. Supported extensions are MP4, MOV, M4V, WebM, MKV and AVI. The app copies up to 1 GiB. Select at most 12 seconds, 1–30 fps and 2–120 sampled frames. Probe the actual media to confirm a decodable video stream and usable timestamps.

```json
{"video":"C:\\media\\cat.mp4","startSeconds":1.0,"durationSeconds":2.0,"fps":12,"slot":"needs-user","loop":false,"instructions":"保留抬爪的橘猫"}
```

Use `tools/extract_pet_video.py --help` to check available options and dependencies. For example, use `--method grabcut --rect x,y,w,h` with a normalized box around the target on the first selected frame, `--method chroma` for a uniform background or `--method alpha` for a source with an existing alpha channel. Existing local BiRefNet weights can be used with `--method birefnet --model-dir DIR` when that environment is available. If your Agent has a segmentation or rotoscoping tool, export a grayscale mask video aligned to the source video's full timeline, resolution and orientation. White keeps the pet, black removes the background. Apply that mask to the original video:

```powershell
python tools/extract_pet_video.py --video 'C:\work\source.mp4' --mask 'C:\work\mask.mp4' --method mask --start 1 --duration 2 --fps 12 --slot needs-user --name '抬爪招呼' --output 'C:\work\raise-paw.lotheme'
.\tools\ai-pet.ps1 -Executable 'C:\app\AIPetDesktop.exe' -DataDirectory 'C:\user-data' -Action video-result -RequestId REQUEST_ID -Theme 'C:\work\raise-paw.lotheme'
```

Substitute the actual request values and include `--loop` only if requested. Write the output to a new work file. Review the entire segment at desktop size on light and dark backgrounds. Check moving paws, whiskers, tail, identity, background leakage and edges. An occlusion or a second pet may require a corrected tracking mask. Submit only the requested slot and an optional single-frame idle pose. The receiver validates the source fingerprint, selected action, frame rate, loop flag and distinct visible frames. The desktop preview lets the user apply that one action to their pet. The Agent must leave that choice to the user.
