#!/usr/bin/env python3
"""Validate OpenCode's candidate against a pristine upstream checkout."""

from pathlib import Path
import re
import subprocess
import sys

SOURCE = Path("Plain Craft Launcher 2/Modules/ModSecret.vb")
DECLARATION = re.compile(
    rb"(?m)^[ \t]*Public[ \t]+Const[ \t]+RestrictMcDownload[ \t]+As[ \t]+Boolean[ \t]*=[ \t]*(True|False)[ \t]*(?:'[^\r\n]*)?\r?$"
)


def validate(original, modified):
    if original == modified:
        raise ValueError("OpenCode did not change the source file")
    matches = DECLARATION.findall(modified)
    if matches != [b"False"]:
        raise ValueError("Candidate must contain exactly one explicit RestrictMcDownload = False declaration")
    before = original.decode("utf-8-sig").splitlines()
    after = modified.decode("utf-8-sig").splitlines()
    if before == after:
        raise ValueError("Candidate only changed line endings")
    anchors = [index for index, line in enumerate(before) if re.match(r"^\s*Public\s+Const\s+RestrictMcDownload\s+As\s+Boolean\s*=", line)]
    if len(anchors) != 1 or len(before) != len(after):
        raise ValueError("Cannot locate a unique existing restriction declaration")
    if any(left != right for index, (left, right) in enumerate(zip(before, after)) if index != anchors[0]):
        raise ValueError("Candidate changed code outside the restriction declaration")


def validate_checkout(root, candidate):
    original = subprocess.check_output(["git", "-C", str(root), "show", f"HEAD:{SOURCE.as_posix()}"])
    validate(original, candidate.read_bytes())


if __name__ == "__main__":
    try:
        validate_checkout(Path(sys.argv[1]), Path(sys.argv[2]))
    except (ValueError, OSError, UnicodeError, subprocess.CalledProcessError) as error:
        sys.exit(f"OpenCode candidate rejected: {error}")
