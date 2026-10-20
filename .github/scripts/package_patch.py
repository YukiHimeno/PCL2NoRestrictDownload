#!/usr/bin/env python3
"""Package only a binary delta for a hash-verified official PCL executable."""

import hashlib
import json
import os
from pathlib import Path
import sys

import bsdiff4


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def package(official, patched, destination, version):
    original = Path(official).read_bytes()
    output = Path(patched).read_bytes()
    if original == output or len(output) < 1_000_000:
        raise ValueError("No usable patched assembly")
    delta = bsdiff4.diff(original, output)
    if bsdiff4.patch(original, delta) != output or len(delta) >= len(output) // 2:
        raise ValueError("Binary delta does not reconstruct the patched official assembly")
    target = Path(destination)
    target.mkdir(parents=True, exist_ok=True)
    (target / "pcl.bsdiff").write_bytes(delta)
    (target / "manifest.json").write_text(json.dumps({
        "Format": 1,
        "Version": version,
        "Patch": "pcl.bsdiff",
        "OfficialSha256": sha256(original),
        "OutputSha256": sha256(output),
        "PatchSha256": sha256(delta),
    }, indent=2) + "\n", encoding="utf-8")
    print(f"Verified binary patch: {len(delta)} bytes; official {sha256(original)}; patched {sha256(output)}")


if __name__ == "__main__":
    package(sys.argv[1], sys.argv[2], sys.argv[3], os.environ["UPSTREAM_TAG"])
