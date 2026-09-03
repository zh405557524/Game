#!/usr/bin/env python3
"""Download and verify the pinned research inputs for culture build v0.4.

The files downloaded by this script are research/build inputs only.  They stay
under ``tmp/`` (which is ignored by Git) and are not game runtime assets.
CBDB and the Chinese Academies dataset carry research/commercial-use
conditions; the derived v0.4 data therefore remains non-commercial by default.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path
from typing import Any


PINNED_CBDB_FILENAME = "cbdb_20260822.sqlite3"
PINNED_CBDB_SHA256 = "25861a3506ace7163348557f1ba0f59ef24cbe49f408f8cdde3041bd0083dffb"
PINNED_CBDB_ZIP_URL = (
  "https://huggingface.co/datasets/cbdb/cbdb-sqlite/resolve/main/"
  "history/cbdb_202608/cbdb_20260822.zip"
)
CBDB_LATEST_MANIFEST_URL = (
  "https://raw.githubusercontent.com/cbdb-project/cbdb_sqlite/master/latest.json"
)

ACADEMIES_DOI = "doi:10.7910/DVN/J6XRIV"
ACADEMIES_METADATA_URL = (
  "https://dataverse.harvard.edu/api/datasets/:persistentId/"
  "?persistentId=doi%3A10.7910%2FDVN%2FJ6XRIV"
)
ACADEMIES_DATAVERSE_FILENAME = "ACADEMY_Data.tab"
ACADEMIES_EXPECTED_FILENAME = "ACADEMY_Data.csv"
ACADEMIES_EXPECTED_MD5 = "c60772d89e7bd52d8140417ec0051ceb"

DEFAULT_OUTPUT_DIR = Path("tmp/research/ming_culture_v0.4")


def digest(path: Path, algorithm: str) -> str:
  hasher = hashlib.new(algorithm)
  with path.open("rb") as stream:
    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
      hasher.update(chunk)
  return hasher.hexdigest()


def download(url: str, output: Path, *, resume: bool = True) -> None:
  """Download using strict TLS, IPv4, retries, and resumable transfers."""
  curl = shutil.which("curl")
  if not curl:
    raise RuntimeError("curl is required for the culture input downloader")
  output.parent.mkdir(parents=True, exist_ok=True)
  destination = output if resume else output.with_suffix(output.suffix + ".download")
  if not resume and destination.exists():
    destination.unlink()
  command = [
    curl,
    "-4",
    "-fL",
    "--retry",
    "5",
    "--retry-all-errors",
    "--retry-delay",
    "2",
    "--output",
    str(destination),
    url,
  ]
  if resume:
    command[1:1] = ["--continue-at", "-"]
  subprocess.run(command, check=True)
  if not resume:
    destination.replace(output)


def load_json(path: Path) -> dict[str, Any]:
  with path.open(encoding="utf-8") as stream:
    value = json.load(stream)
  if not isinstance(value, dict):
    raise RuntimeError(f"Expected a JSON object in {path}")
  return value


def verify_cbdb_release(output_dir: Path) -> tuple[Path, dict[str, Any]]:
  latest_path = output_dir / "cbdb_latest.json"
  download(CBDB_LATEST_MANIFEST_URL, latest_path, resume=False)
  latest = load_json(latest_path)
  observed = {
    "sqlite_filename": latest.get("sqlite_filename"),
    "sha256": latest.get("sha256"),
  }
  expected = {
    "sqlite_filename": PINNED_CBDB_FILENAME,
    "sha256": PINNED_CBDB_SHA256,
  }
  if observed != expected:
    raise RuntimeError(
      "CBDB latest.json no longer matches the pinned v0.4 release. "
      f"Expected {expected}, observed {observed}. Review before upgrading."
    )

  zip_path = output_dir / "cbdb_20260822.zip"
  sqlite_path = output_dir / PINNED_CBDB_FILENAME
  if not sqlite_path.exists() or digest(sqlite_path, "sha256") != PINNED_CBDB_SHA256:
    download(PINNED_CBDB_ZIP_URL, zip_path)
    with zipfile.ZipFile(zip_path) as archive:
      members = {Path(name).name: name for name in archive.namelist()}
      if PINNED_CBDB_FILENAME not in members:
        raise RuntimeError(f"{PINNED_CBDB_FILENAME} is missing from {zip_path}")
      archive.extract(members[PINNED_CBDB_FILENAME], output_dir)
      extracted = output_dir / members[PINNED_CBDB_FILENAME]
      if extracted != sqlite_path:
        extracted.replace(sqlite_path)
  observed_hash = digest(sqlite_path, "sha256")
  if observed_hash != PINNED_CBDB_SHA256:
    raise RuntimeError(
      f"CBDB SHA-256 mismatch: expected {PINNED_CBDB_SHA256}, observed {observed_hash}"
    )
  return sqlite_path, latest


def verify_academies(output_dir: Path) -> tuple[Path, dict[str, Any]]:
  metadata_path = output_dir / "academies_dataverse.json"
  download(ACADEMIES_METADATA_URL, metadata_path, resume=False)
  metadata = load_json(metadata_path)
  files = metadata.get("data", {}).get("latestVersion", {}).get("files", [])
  candidate: dict[str, Any] | None = None
  for entry in files:
    data_file = entry.get("dataFile", {})
    if data_file.get("filename") == ACADEMIES_DATAVERSE_FILENAME:
      candidate = data_file
      break
  if not candidate:
    raise RuntimeError(
      f"Harvard Dataverse metadata does not contain {ACADEMIES_DATAVERSE_FILENAME}"
    )
  checksum = candidate.get("checksum", {})
  if checksum.get("type") != "MD5" or checksum.get("value") != ACADEMIES_EXPECTED_MD5:
    raise RuntimeError(
      "Chinese Academies dataset checksum changed. Review the Dataverse release "
      f"before updating: {checksum}"
    )
  file_id = candidate.get("id")
  if not file_id:
    raise RuntimeError("Chinese Academies data file has no Dataverse file id")
  data_path = output_dir / ACADEMIES_EXPECTED_FILENAME
  if not data_path.exists() or digest(data_path, "md5") != ACADEMIES_EXPECTED_MD5:
    download(
      f"https://dataverse.harvard.edu/api/access/datafile/{file_id}?format=original",
      data_path,
    )
  observed_md5 = digest(data_path, "md5")
  if observed_md5 != ACADEMIES_EXPECTED_MD5:
    raise RuntimeError(
      f"Academies MD5 mismatch: expected {ACADEMIES_EXPECTED_MD5}, observed {observed_md5}"
    )
  return data_path, metadata


def main() -> None:
  parser = argparse.ArgumentParser()
  parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT_DIR)
  parser.add_argument(
    "--skip-academies",
    action="store_true",
    help="Download only the pinned CBDB release.",
  )
  args = parser.parse_args()
  args.output_dir.mkdir(parents=True, exist_ok=True)

  cbdb_path, latest = verify_cbdb_release(args.output_dir)
  academies_path: Path | None = None
  academies_metadata: dict[str, Any] | None = None
  if not args.skip_academies:
    academies_path, academies_metadata = verify_academies(args.output_dir)

  manifest = {
    "status": "verified",
    "usage_boundary": "research_build_inputs_only",
    "commercial_release_ready": "no",
    "cbdb": {
      "path": str(cbdb_path),
      "filename": PINNED_CBDB_FILENAME,
      "sha256": digest(cbdb_path, "sha256"),
      "release_manifest_url": CBDB_LATEST_MANIFEST_URL,
      "download_url": PINNED_CBDB_ZIP_URL,
      "generated_at_utc": latest.get("generated_at_utc", ""),
    },
    "chinese_academies": None,
  }
  if academies_path and academies_metadata:
    manifest["chinese_academies"] = {
      "path": str(academies_path),
      "filename": academies_path.name,
      "md5": digest(academies_path, "md5"),
      "doi": ACADEMIES_DOI,
      "metadata_url": ACADEMIES_METADATA_URL,
      "dataset_version": academies_metadata.get("data", {})
      .get("latestVersion", {})
      .get("versionNumber"),
    }
  manifest_path = args.output_dir / "download_manifest_v0.4.json"
  with manifest_path.open("w", encoding="utf-8", newline="\n") as stream:
    json.dump(manifest, stream, ensure_ascii=False, indent=2, sort_keys=True)
    stream.write("\n")
  print(json.dumps(manifest, ensure_ascii=False, indent=2))


if __name__ == "__main__":
  try:
    main()
  except (OSError, RuntimeError, subprocess.CalledProcessError, zipfile.BadZipFile) as exc:
    print(f"culture input download failed: {exc}", file=sys.stderr)
    raise SystemExit(1) from exc
