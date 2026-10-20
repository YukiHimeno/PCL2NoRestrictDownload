#!/usr/bin/env python3
"""Select an unprocessed upstream release for the CI patch builder."""

import argparse
import json
import os
import re
import sys
from urllib.error import HTTPError
from urllib.parse import quote
from urllib.request import Request, urlopen

UPSTREAM = "Meloong-Git/PCL"
DOWNSTREAM = "YukiHimeno/PCL2NoRestrictDownload"
# The first run may publish this release; earlier releases lack the same patch point.
FIRST_RELEASE_ID = 418095202  # 2.99.0.0

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
    candidates = sorted(
        (release for release in releases if not release["draft"]),
        key=lambda item: item["published_at"],
    )
    # Process in publication order so multiple releases between scheduled runs are not lost.
    for index, release in enumerate(candidates):
        if release["id"] < FIRST_RELEASE_ID:
            continue
        tag = release["tag_name"]
        base = candidates[index - 1]["tag_name"] if index else ""
        for value in (tag, base):
            if value and not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,100}", value):
                raise ValueError(f"Unsafe upstream release tag: {value!r}")
        output_tag = "patch-" + tag
        if api(f"repos/{DOWNSTREAM}/releases/tags/{quote(output_tag, safe='')}", missing_ok=True) is None:
            return tag, base
    return None


def main():
    parser = argparse.ArgumentParser()
    subcommands = parser.add_subparsers(dest="command", required=True)
    subcommands.add_parser("select")
    args = parser.parse_args()
    selection = choose_release()
    tag, base = selection if selection else ("", "")
    print(f"Upstream release to build: {tag or '(none)'}; previous release: {base or '(none)'}")
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        output.write(f"tag={tag}\nbase={base}\n")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, HTTPError) as error:
        print(f"Release automation stopped: {error}", file=sys.stderr)
        sys.exit(1)
