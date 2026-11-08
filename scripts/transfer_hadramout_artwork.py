"""Temporary transfer of two original Hadramout images, losslessly encoded.

Chunk base64 into small ASCII files to allow byte-for-byte transfer through
repository file APIs. This script runs ONLY on the throwaway transfer branch.
"""
import base64
import hashlib
import json
from pathlib import Path

FILES = {
    "default_card.png": ("c1bcb76cf37d626e39bd65e72e45ddd2286f39bf", 1262691),
    "login_hero.png": ("9c5fbc6117eeef514f78f4ac9a87dcb417e915f0", 1153878),
}
output = Path(".transfer-hadramout-b64")
manifest = {}
for name, (expected_git_sha, expected_size) in FILES.items():
    original = Path("Hadramout_Cards/assets") / name
    raw = original.read_bytes()
    actual_git_sha = hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\x00" + raw).hexdigest()
    if len(raw) != expected_size or actual_git_sha != expected_git_sha:
        raise ValueError(f"Source image verification failed: {name}")
    encoded = base64.b64encode(raw).decode("ascii")
    folder = output / name.replace(".png", "")
    folder.mkdir(parents=True, exist_ok=True)
    parts = []
    for start in range(0, len(encoded), 120000):
        part = f"part-{start//120000:03d}.txt"
        (folder / part).write_text(encoded[start:start+120000], encoding="ascii")
        parts.append(f"{folder.name}/{part}")
    manifest[name] = {"bytes": len(raw), "git_sha1": actual_git_sha,
                      "sha256": hashlib.sha256(raw).hexdigest(),
                      "parts": parts}
output.mkdir(exist_ok=True)
(output / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
print(json.dumps(manifest, indent=2))
