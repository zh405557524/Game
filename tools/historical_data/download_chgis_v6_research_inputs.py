#!/usr/bin/env python3
"""Download the CHGIS V6 research inputs used by the geography draft.

CHGIS V6 permits academic/non-commercial research use and restricts
commercial use and redistribution.  Run this script only after reading and
accepting the CHGIS license.  Raw files stay under ``tmp/`` and are ignored by
Git; the generated game dataset is also marked as not commercial-release
ready until its coordinates are replaced or separately licensed.
"""

from __future__ import annotations

import argparse
import time
import urllib.request
import zipfile
from pathlib import Path


DATAVERSE_FILE_IDS = {
    "chgis_counties/v6_time_cnty_pts_utf_wgs84.zip": 3048165,
    "chgis_1820/v6_1820_cnty_pts_utf.zip": 2966719,
    "chgis_1820/v6_1820_coded_rvr_lin_utf.zip": 2966716,
    "chgis_1820/v6_1820_lks_pgn_utf.zip": 2966718,
    "chgis_1820/v6_1820_pref_pgn_utf.zip": 2966717,
}

LICENSE_URL = "https://chgis.fas.harvard.edu/data/chgis/v6/"


def download(file_id: int, target: Path) -> None:
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists() and target.stat().st_size > 1000:
        print(f"exists: {target}")
        return
    url = f"https://dataverse.harvard.edu/api/access/datafile/{file_id}"
    for attempt in range(1, 9):
        try:
            print(f"download attempt {attempt}: {target.name}")
            request = urllib.request.Request(
                url,
                headers={
                    "User-Agent": "ProjectRealmResearch/0.2",
                    "Connection": "close",
                },
            )
            with urllib.request.urlopen(request, timeout=180) as source:
                with target.open("wb") as destination:
                    while chunk := source.read(262_144):
                        destination.write(chunk)
            print(f"downloaded: {target} ({target.stat().st_size} bytes)")
            return
        except Exception as error:  # network endpoints occasionally reset TLS
            print(f"retryable download error: {type(error).__name__}: {error}")
            target.unlink(missing_ok=True)
            time.sleep(2)
    raise RuntimeError(f"Unable to download {target.name}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path("tmp/research"))
    parser.add_argument(
        "--accept-chgis-noncommercial-license",
        action="store_true",
        help="Confirm that you read and accept the CHGIS V6 research license.",
    )
    args = parser.parse_args()
    if not args.accept_chgis_noncommercial_license:
        raise SystemExit(
            "Read the CHGIS V6 license first, then rerun with "
            "--accept-chgis-noncommercial-license.\n"
            f"License page: {LICENSE_URL}"
        )

    for relative, file_id in DATAVERSE_FILE_IDS.items():
        archive = args.root / relative
        download(file_id, archive)
        extraction_dir = archive.parent / "extracted"
        extraction_dir.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(archive) as bundle:
            bundle.extractall(extraction_dir)
        print(f"extracted: {extraction_dir}")


if __name__ == "__main__":
    main()
