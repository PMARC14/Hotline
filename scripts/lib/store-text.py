#!/usr/bin/env python3
"""Prints the Store art jobs for one language: one tab-separated line per image, `NAME  WIDTH  HEIGHT  QUERY`.

Used by scripts/make-store-localized.sh. The captions come from the language's listing file (the same text as on the
images, `## Screenshot captions` in store/listing.md or the fifth `## ` section of store/listings/<lang>.md); the poster
and box text come from store/art/taglines.json.

    store-text.py ROOT LANG        (LANG is "en" or a file name in store/listings, without .md)
"""
import json
import os
import re
import sys
import urllib.parse

root, lang = sys.argv[1], sys.argv[2]


def sections(path):
    text = open(path, encoding="utf-8").read().replace("\r\n", "\n")
    parts = re.split(r"^## ", text, flags=re.M)
    return parts[1:]  # drop what comes before the first heading


def captions():
    if lang == "en":
        body = next(s for s in sections(os.path.join(root, "store", "listing.md")) if s.startswith("Screenshot captions"))
    else:
        body = sections(os.path.join(root, "store", "listings", lang + ".md"))[4]  # short, description, what's new, features, captions
    found = re.findall(r"^\d+\.\s+(.+)$", body, flags=re.M)
    if len(found) < 4:
        sys.exit(f"{lang}: expected 4 screenshot captions, found {len(found)}")
    return found[:4]


def q(value):
    return urllib.parse.quote(value, safe="")


panels = ["math", "summary", "slash", "settings"]
for i, (panel, caption) in enumerate(zip(panels, captions()), start=1):
    print(f"screenshot-{i}\t1920\t1080\tkind=shot&img=../../artifacts/store/panel-{panel}.png&caption={q(caption)}")

tag = json.load(open(os.path.join(root, "store", "art", "taglines.json"), encoding="utf-8"))[lang]
art = f"title=Hotline%20AI&sub={q(tag['sub'])}&key={q(tag['key'])}"
print(f"poster-1440x2160\t1440\t2160\tkind=poster&{art}")
print(f"boxart-2160x2160\t2160\t2160\tkind=box&{art}")
