#!/usr/bin/env python3
"""Select an unprocessed upstream release or patch an ephemeral checkout."""

import argparse
import json
import os
from pathlib import Path
import re
import sys
from urllib.error import HTTPError
from urllib.parse import quote
from urllib.request import Request, urlopen

UPSTREAM = "Meloong-Git/PCL"
DOWNSTREAM = "YukiHimeno/PCL2NoRestrictDownload"
# The first run may publish this release; earlier releases lack the same patch point.
FIRST_RELEASE_ID = 418095202  # 2.99.0.0
SOURCE = Path("Plain Craft Launcher 2/Modules/ModSecret.vb")
OLD = b"Public Const RestrictMcDownload As Boolean = True"
NEW = b"Public Const RestrictMcDownload As Boolean = False"


def api(path, missing_ok=False):
    request = Request(
        "https://api.github.com/" + path,
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": "Bearer " + os.environ["GH_TOKEN"],
            "X-GitHub-Api-Version": "2022-11-28",
        },
    )
    try:
        with urlopen(request, timeout=30) as response:
            return json.load(response)
    except HTTPError as error:
        if missing_ok and error.code == 404:
            return None
        raise


def choose_release():
    releases = api(f"repos/{UPSTREAM}/releases?per_page=100")
    candidates = [
        release for release in releases
        if not release["draft"] and release["id"] >= FIRST_RELEASE_ID
    ]
    # Process in publication order so multiple releases between scheduled runs are not lost.
    for release in sorted(candidates, key=lambda item: item["published_at"]):
        tag = release["tag_name"]
        if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,100}", tag):
            raise ValueError(f"Unsafe upstream release tag: {tag!r}")
        output_tag = "no-restrict-" + tag
        if api(f"repos/{DOWNSTREAM}/releases/tags/{quote(output_tag, safe='')}", missing_ok=True) is None:
            return tag
    return None


def patch(root):
    path = root / SOURCE
    original = path.read_bytes()
    declarations = re.findall(rb"Public\s+Const\s+RestrictMcDownload\s+As\s+Boolean\s*=\s*[^\r\n]+", original)
    if len(declarations) != 1 or declarations[0] != OLD or original.count(OLD) != 1:
        raise ValueError(f"Unexpected restriction declaration in {path}; review upstream changes before publishing")
    path.write_bytes(original.replace(OLD, NEW, 1))


def main():
    parser = argparse.ArgumentParser()
    subcommands = parser.add_subparsers(dest="command", required=True)
    subcommands.add_parser("select")
    subcommands.add_parser("patch").add_argument("checkout", type=Path)
    args = parser.parse_args()
    if args.command == "select":
        tag = choose_release()
        print(f"Upstream release to build: {tag or '(none)'}")
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
            output.write(f"tag={tag or ''}\n")
    else:
        patch(args.checkout)
        print(f"Patched {args.checkout / SOURCE}")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, HTTPError) as error:
        print(f"Release automation stopped: {error}", file=sys.stderr)
        sys.exit(1)
