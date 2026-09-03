#!/usr/bin/env python3
"""Download reproducible USGS mineral inputs used by the 1628 game model.

The raw files live under ``tmp/research`` and are intentionally not committed.
USGS MRDS supplies point occurrences; the 2023 China GIS release supplies coal
resource polygons and a second set of major mineral deposits.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import urllib.parse
import urllib.request
import zipfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


SCIENCEBASE_ITEM_ID = "64348094d34ee8d4add91365"
SCIENCEBASE_ITEM_URL = (
    f"https://www.sciencebase.gov/catalog/item/{SCIENCEBASE_ITEM_ID}?format=json"
)
GDB_FILE_NAME = "CHN_GIS.gdb.zip"
GDB_MD5 = "5813ba2a93e024b273f6fd9080e3d1c5"
GDB_EXPECTED_SIZE = 77_811_868
MRDS_QUERY_URL = (
    "https://energy.usgs.gov/arcgis/rest/services/MRData/"
    "Mineral_Resource_Data_System/FeatureServer/3/query"
)
MRDS_BBOX = "93,15,126,45"
USER_AGENT = "Project-Realm-Historical-Research/0.2"


def request_json(url: str, timeout: int = 90) -> dict[str, Any]:
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return json.load(response)


def md5sum(path: Path) -> str:
    digest = hashlib.md5()  # noqa: S324 - required to verify the publisher checksum
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def download_file(url: str, destination: Path, force: bool) -> None:
    if destination.exists() and not force:
        return
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix(destination.suffix + ".part")
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(request, timeout=180) as response, temporary.open("wb") as out:
        shutil.copyfileobj(response, out, length=1024 * 1024)
    temporary.replace(destination)


def download_mrds(destination: Path, force: bool) -> dict[str, Any]:
    if destination.exists() and not force:
        with destination.open(encoding="utf-8") as stream:
            existing = json.load(stream)
        return {
            "record_count": len(existing.get("features", [])),
            "source": MRDS_QUERY_URL,
            "bbox": MRDS_BBOX,
            "reused": True,
        }

    features: list[dict[str, Any]] = []
    offset = 0
    page_size = 2000
    while True:
        params = {
            "f": "geojson",
            "where": "1=1",
            "geometry": MRDS_BBOX,
            "geometryType": "esriGeometryEnvelope",
            "inSR": "4326",
            "spatialRel": "esriSpatialRelIntersects",
            "outFields": "dep_id,site_name,dev_stat,code_list,grade,url",
            "resultOffset": str(offset),
            "resultRecordCount": str(page_size),
            "returnGeometry": "true",
        }
        payload = request_json(MRDS_QUERY_URL + "?" + urllib.parse.urlencode(params))
        batch = payload.get("features", [])
        features.extend(batch)
        if len(batch) < page_size:
            break
        offset += len(batch)

    unique: dict[str, dict[str, Any]] = {}
    for feature in features:
        properties = feature.get("properties") or {}
        identifier = str(properties.get("dep_id") or "")
        if identifier:
            unique[identifier] = feature
    output = {
        "type": "FeatureCollection",
        "name": "USGS_MRDS_core_China_for_Project_Realm",
        "source": MRDS_QUERY_URL,
        "bbox_query": [93, 15, 126, 45],
        "downloaded_at_utc": datetime.now(timezone.utc).isoformat(),
        "features": [unique[key] for key in sorted(unique)],
    }
    destination.parent.mkdir(parents=True, exist_ok=True)
    with destination.open("w", encoding="utf-8") as stream:
        json.dump(output, stream, ensure_ascii=False, separators=(",", ":"))
    return {
        "record_count": len(output["features"]),
        "source": MRDS_QUERY_URL,
        "bbox": MRDS_BBOX,
        "reused": False,
    }


def download_gdb(output_dir: Path, force: bool) -> dict[str, Any]:
    item = request_json(SCIENCEBASE_ITEM_URL)
    matches = [entry for entry in item.get("files", []) if entry.get("name") == GDB_FILE_NAME]
    if len(matches) != 1:
        raise RuntimeError(f"Expected one {GDB_FILE_NAME} entry, found {len(matches)}")
    metadata = matches[0]
    archive = output_dir / GDB_FILE_NAME
    download_file(str(metadata["downloadUri"]), archive, force)
    if archive.stat().st_size != GDB_EXPECTED_SIZE:
        raise RuntimeError(
            f"Unexpected {archive.name} size: {archive.stat().st_size} != {GDB_EXPECTED_SIZE}"
        )
    actual_md5 = md5sum(archive)
    if actual_md5 != GDB_MD5:
        raise RuntimeError(f"USGS archive checksum mismatch: {actual_md5} != {GDB_MD5}")

    extract_dir = output_dir / "CHN_GIS_gdb"
    marker = extract_dir / ".extracted-md5"
    if force or not marker.exists() or marker.read_text(encoding="utf-8").strip() != GDB_MD5:
        if extract_dir.exists():
            shutil.rmtree(extract_dir)
        extract_dir.mkdir(parents=True)
        with zipfile.ZipFile(archive) as source:
            source.extractall(extract_dir)
        marker.write_text(GDB_MD5 + "\n", encoding="utf-8")
    gdb_paths = list(extract_dir.rglob("*.gdb"))
    if len(gdb_paths) != 1:
        raise RuntimeError(f"Expected one extracted .gdb directory, found {len(gdb_paths)}")
    return {
        "sciencebase_item": SCIENCEBASE_ITEM_ID,
        "download_url": metadata["downloadUri"],
        "archive": str(archive),
        "gdb": str(gdb_paths[0]),
        "bytes": archive.stat().st_size,
        "md5": actual_md5,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--output-dir", type=Path, default=Path("tmp/research/usgs_china_minerals")
    )
    parser.add_argument("--skip-gdb", action="store_true")
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args()

    args.output_dir.mkdir(parents=True, exist_ok=True)
    manifest: dict[str, Any] = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "license_note": "USGS public data; read source metadata and limitations",
        "mrds": download_mrds(args.output_dir / "mrds_core_china.geojson", args.force),
    }
    if not args.skip_gdb:
        manifest["china_gis"] = download_gdb(args.output_dir, args.force)
    with (args.output_dir / "manifest.json").open("w", encoding="utf-8") as stream:
        json.dump(manifest, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    print(json.dumps(manifest, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
