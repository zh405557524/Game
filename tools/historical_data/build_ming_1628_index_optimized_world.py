#!/usr/bin/env python3
"""Build the workload-tested index-optimized Project Realm snapshot v0.9.

The accepted v0.8 database is immutable input.  This builder removes only
indexes that are duplicated by another prefix index or whose bounded result
set is faster to sort/scan than to keep as a large persistent B-tree.  Tables,
views, rows, column order and public identifiers remain unchanged.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
from pathlib import Path
import shutil
import sqlite3
import time
from typing import Any, Iterable, Sequence


REPO_ROOT = Path(__file__).resolve().parents[2]
DATA_ROOT = REPO_ROOT / "docs/90_资料与归档/01_崇祯元年历史资料/data/1628"
DEFAULT_SOURCE_DATABASE = (
  DATA_ROOT / "11.运行数据库瘦身/game_world_1628_v0.8.sqlite"
)
DEFAULT_OUTPUT_DIR = DATA_ROOT / "12.运行数据库索引优化"
OUTPUT_DATABASE_NAME = "game_world_1628_v0.9.sqlite"
RULESET_VERSION = "v0.9"
MAX_DATABASE_BYTES = 410 * 1024 * 1024
MIN_SAVED_BYTES = 30 * 1024 * 1024


REMOVED_INDEXES = {
  "idx_settlement_county": (
    "county prefix is already covered by idx_settlement_county_name; "
    "the largest county remains a bounded 2,827-row scan"
  ),
  "idx_social_occupation_county": (
    "each county has exactly 150 occupation rows, so an in-memory sort is cheaper"
  ),
  "idx_culture_county": (
    "duplicates the county_id primary-key autoindex exactly"
  ),
  "idx_local_division_county": (
    "a county has at most 40 divisions and idx_local_division_county_name "
    "already covers the county prefix"
  ),
  "idx_local_division_subregion": (
    "subregion division result sets are small; measured scan remains below 1 ms"
  ),
}

REQUIRED_INDEXES = [
  "sqlite_autoindex_settlement_node_1",
  "idx_settlement_county_name",
  "idx_local_membership_division",
  "idx_person_primary_county",
  "idx_person_assoc_county",
  "idx_person_assoc_person",
  "idx_relationship_from",
  "idx_relationship_to",
  "idx_family_member_family",
]

COMPATIBILITY_OBJECTS = [
  "village_catalog",
  "settlement_zone",
  "settlement_local_division",
  "county_occupation_quota",
  "settlement_sector_quota",
  "settlement_poi",
  "v_county_entry_villages",
  "v_county_entry_settlements",
  "v_settlement_entry_zones",
  "v_zone_entry_pois",
  "v_county_entry_local_divisions",
  "v_local_division_entry_settlements",
  "v_settlement_occupation_profile",
  "v_county_people",
  "v_county_families",
  "v_person_network_edges",
]

EXACT_DATA_OBJECTS = [
  "settlement_node",
  "village_catalog",
  "settlement_zone",
  "settlement_local_division",
  "local_division_definition",
  "county_occupation_quota",
  "settlement_sector_quota",
  "settlement_poi",
  "historical_person_catalog",
  "historical_person_relationship",
  "person_county_association",
  "person_family_membership",
  "person_group_membership",
]

EXPECTED_ROW_COUNTS = {
  "county_economy_baseline": 1_168,
  "settlement_node": 508_729,
  "village_catalog": 505_684,
  "settlement_zone": 533_105,
  "settlement_poi": 193_328,
  "local_division_definition": 18_279,
  "settlement_local_division": 508_729,
  "county_occupation_quota": 175_200,
  "settlement_sector_quota": 508_729,
  "historical_person_catalog": 82_869,
  "historical_person_relationship": 148_447,
  "person_county_association": 99_307,
  "person_family_membership": 62_475,
  "person_group_membership": 21_979,
}


def file_sha256(path: Path) -> str:
  digest = hashlib.sha256()
  with path.open("rb") as stream:
    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
      digest.update(chunk)
  return digest.hexdigest()


def quoted(identifier: str) -> str:
  return '"' + identifier.replace('"', '""') + '"'


def write_json_atomic(path: Path, value: dict[str, Any]) -> None:
  temporary = path.with_suffix(path.suffix + ".tmp")
  temporary.write_text(
    json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
    encoding="utf-8",
  )
  temporary.replace(path)


def write_csv_atomic(
  path: Path,
  columns: Sequence[str],
  rows: Iterable[dict[str, Any]],
) -> None:
  temporary = path.with_suffix(path.suffix + ".tmp")
  with temporary.open("w", encoding="utf-8", newline="") as stream:
    writer = csv.DictWriter(
      stream, fieldnames=columns, extrasaction="ignore", lineterminator="\n",
    )
    writer.writeheader()
    for row in rows:
      writer.writerow({column: row.get(column, "") for column in columns})
  temporary.replace(path)


def object_columns(
  connection: sqlite3.Connection,
  schema: str,
  object_name: str,
) -> list[str]:
  return [
    str(row[1])
    for row in connection.execute(
      f"PRAGMA {quoted(schema)}.table_info({quoted(object_name)})"
    ).fetchall()
  ]


def object_size_rows(database: Path, version: str) -> list[dict[str, Any]]:
  connection = sqlite3.connect(f"file:{database}?mode=ro", uri=True)
  try:
    rows = [
      {
        "database_version": version,
        "object_name": row[0],
        "object_type": row[1] or "internal",
        "bytes": int(row[2]),
        "mib": f"{int(row[2]) / 1024 / 1024:.3f}",
      }
      for row in connection.execute(
        "SELECT d.name,s.type,SUM(d.pgsize) bytes "
        "FROM dbstat d LEFT JOIN sqlite_schema s ON s.name=d.name "
        "GROUP BY d.name,s.type ORDER BY bytes DESC,d.name"
      ).fetchall()
    ]
  finally:
    connection.close()
  rows.insert(0, {
    "database_version": version,
    "object_name": "__database_total__",
    "object_type": "database",
    "bytes": database.stat().st_size,
    "mib": f"{database.stat().st_size / 1024 / 1024:.3f}",
  })
  return rows


def index_size_map(database: Path) -> dict[str, int]:
  connection = sqlite3.connect(f"file:{database}?mode=ro", uri=True)
  try:
    return {
      str(row[0]): int(row[1])
      for row in connection.execute(
        "SELECT d.name,SUM(d.pgsize) bytes FROM dbstat d "
        "JOIN sqlite_schema s ON s.name=d.name AND s.type='index' "
        "GROUP BY d.name ORDER BY d.name"
      ).fetchall()
    }
  finally:
    connection.close()


def bidirectional_difference(
  connection: sqlite3.Connection,
  object_name: str,
) -> int:
  object_sql = quoted(object_name)
  row = connection.execute(
    "SELECT "
    f"(SELECT COUNT(*) FROM (SELECT * FROM main.{object_sql} "
    f"EXCEPT SELECT * FROM source_v08.{object_sql})) + "
    f"(SELECT COUNT(*) FROM (SELECT * FROM source_v08.{object_sql} "
    f"EXCEPT SELECT * FROM main.{object_sql}))"
  ).fetchone()
  return int(row[0])


def transform_database(connection: sqlite3.Connection) -> None:
  connection.executescript(
    """
    BEGIN IMMEDIATE;
    DROP INDEX idx_settlement_county;
    DROP INDEX idx_social_occupation_county;
    DROP INDEX idx_culture_county;
    DROP INDEX idx_local_division_county;
    DROP INDEX idx_local_division_subregion;
    PRAGMA user_version=9;
    COMMIT;
    ANALYZE;
    VACUUM;
    """
  )


def query_elapsed_ms(
  connection: sqlite3.Connection,
  sql: str,
  parameters: Sequence[Any],
) -> tuple[float, int]:
  connection.execute(sql, parameters).fetchall()
  samples: list[float] = []
  row_count = 0
  for _ in range(4):
    started = time.perf_counter()
    rows = connection.execute(sql, parameters).fetchall()
    samples.append((time.perf_counter() - started) * 1000)
    row_count = len(rows)
  return round(max(samples), 3), row_count


def benchmark_database(database: Path) -> dict[str, Any]:
  connection = sqlite3.connect(f"file:{database}?mode=ro", uri=True)
  connection.row_factory = sqlite3.Row
  try:
    county_id = connection.execute(
      "SELECT county_id FROM settlement_node GROUP BY county_id "
      "ORDER BY COUNT(*) DESC,county_id LIMIT 1"
    ).fetchone()[0]
    division_id = connection.execute(
      "SELECT division_id FROM settlement_local_division_core GROUP BY division_id "
      "ORDER BY COUNT(*) DESC,division_id LIMIT 1"
    ).fetchone()[0]
    subregion_id = connection.execute(
      "SELECT primary_subregion_id FROM local_division_definition "
      "GROUP BY primary_subregion_id "
      "ORDER BY COUNT(*) DESC,primary_subregion_id LIMIT 1"
    ).fetchone()[0]
    person_id = connection.execute(
      "SELECT from_person_id FROM historical_person_relationship "
      "GROUP BY from_person_id ORDER BY COUNT(*) DESC,from_person_id LIMIT 1"
    ).fetchone()[0]
    repeated_name = connection.execute(
      "SELECT settlement_name FROM settlement_node GROUP BY settlement_name "
      "ORDER BY COUNT(*) DESC,settlement_name LIMIT 1"
    ).fetchone()[0]
    tests: dict[str, tuple[str, tuple[Any, ...]]] = {
      "largest_county_villages": (
        "SELECT * FROM v_county_entry_villages "
        "WHERE county_id=? ORDER BY village_id", (county_id,),
      ),
      "largest_county_settlements": (
        "SELECT * FROM v_county_entry_settlements "
        "WHERE county_id=? ORDER BY settlement_id", (county_id,),
      ),
      "county_villages_by_type": (
        "SELECT * FROM settlement_node WHERE county_id=? "
        "AND settlement_type_code='village' ORDER BY settlement_id", (county_id,),
      ),
      "settlement_name_in_county": (
        "SELECT * FROM v_county_entry_settlements "
        "WHERE county_id=? AND settlement_name=?", (county_id, repeated_name),
      ),
      "settlement_name_global": (
        "SELECT * FROM v_county_entry_settlements WHERE settlement_name=? "
        "ORDER BY county_id,settlement_id", (repeated_name,),
      ),
      "largest_division_settlements": (
        "SELECT * FROM v_local_division_entry_settlements "
        "WHERE division_id=? ORDER BY settlement_id", (division_id,),
      ),
      "largest_county_divisions": (
        "SELECT * FROM v_county_entry_local_divisions "
        "WHERE county_id=? ORDER BY division_id", (county_id,),
      ),
      "county_towns": (
        "SELECT * FROM local_division_definition WHERE county_id=? "
        "AND division_type_code='town' ORDER BY division_id", (county_id,),
      ),
      "subregion_divisions": (
        "SELECT * FROM local_division_definition "
        "WHERE primary_subregion_id=? ORDER BY division_id", (subregion_id,),
      ),
      "county_occupations_by_code": (
        "SELECT * FROM county_occupation_quota "
        "WHERE county_id=? ORDER BY occupation_code", (county_id,),
      ),
      "county_occupations_by_workers": (
        "SELECT * FROM county_occupation_quota "
        "WHERE county_id=? ORDER BY worker_count_est DESC", (county_id,),
      ),
      "county_people": (
        "SELECT * FROM v_county_people WHERE county_id=? ORDER BY person_id",
        (county_id,),
      ),
      "person_network_edges": (
        "SELECT * FROM v_person_network_edges "
        "WHERE from_person_id=? ORDER BY relationship_id", (person_id,),
      ),
    }
    measured: dict[str, Any] = {}
    for name, (sql, parameters) in tests.items():
      elapsed_ms, row_count = query_elapsed_ms(connection, sql, parameters)
      measured[name] = {"elapsed_ms": elapsed_ms, "row_count": row_count}
    measured["maximum_elapsed_ms"] = max(
      value["elapsed_ms"] for value in measured.values()
      if isinstance(value, dict)
    )
    measured["all_under_250ms"] = measured["maximum_elapsed_ms"] < 250
    return measured
  finally:
    connection.close()


def explain_plans(database: Path) -> dict[str, list[str]]:
  connection = sqlite3.connect(f"file:{database}?mode=ro", uri=True)
  try:
    plans = {
      "county_settlements": (
        "EXPLAIN QUERY PLAN SELECT * FROM v_county_entry_settlements "
        "WHERE county_id='MING1628-0001' ORDER BY settlement_id"
      ),
      "county_occupations_by_workers": (
        "EXPLAIN QUERY PLAN SELECT * FROM county_occupation_quota "
        "WHERE county_id='MING1628-0001' ORDER BY worker_count_est DESC"
      ),
      "county_towns": (
        "EXPLAIN QUERY PLAN SELECT * FROM local_division_definition "
        "WHERE county_id='MING1628-0001' "
        "AND division_type_code='town' ORDER BY division_id"
      ),
    }
    return {
      name: [str(row[3]) for row in connection.execute(sql).fetchall()]
      for name, sql in plans.items()
    }
  finally:
    connection.close()


def validate_database(
  connection: sqlite3.Connection,
  source_database: Path,
  source_hash_before: str,
) -> dict[str, Any]:
  checks: dict[str, Any] = {}
  source_uri = f"file:{source_database}?mode=ro"
  connection.execute("ATTACH DATABASE ? AS source_v08", (source_uri,))

  checks["source_database_sha256_before"] = source_hash_before
  checks["source_database_sha256_after"] = file_sha256(source_database)
  checks["source_database_unchanged"] = (
    checks["source_database_sha256_before"]
    == checks["source_database_sha256_after"]
  )
  checks["user_version"] = connection.execute(
    "PRAGMA user_version"
  ).fetchone()[0]
  checks["object_row_counts"] = {
    name: connection.execute(
      f"SELECT COUNT(*) FROM {quoted(name)}"
    ).fetchone()[0]
    for name in EXPECTED_ROW_COUNTS
  }
  checks["key_id_duplicate_counts"] = {
    label: connection.execute(
      f"SELECT COUNT(*)-COUNT(DISTINCT {quoted(column)}) FROM {quoted(name)}"
    ).fetchone()[0]
    for label, name, column in (
      ("county_id", "county_economy_baseline", "county_id"),
      ("settlement_id", "settlement_node", "settlement_id"),
      ("village_id", "village_catalog", "village_id"),
      ("zone_id", "settlement_zone", "zone_id"),
      ("poi_id", "settlement_poi", "poi_id"),
      ("division_id", "local_division_definition", "division_id"),
      ("historical_person_id", "historical_person_catalog", "person_id"),
      ("relationship_id", "historical_person_relationship", "relationship_id"),
    )
  }
  checks["compatibility_column_mismatch_count"] = sum(
    object_columns(connection, "main", name)
    != object_columns(connection, "source_v08", name)
    for name in COMPATIBILITY_OBJECTS
  )
  checks["table_view_schema_difference_count"] = connection.execute(
    "SELECT "
    "(SELECT COUNT(*) FROM ("
    "SELECT type,name,sql FROM main.sqlite_schema WHERE type IN ('table','view') "
    "EXCEPT SELECT type,name,sql FROM source_v08.sqlite_schema "
    "WHERE type IN ('table','view'))) + "
    "(SELECT COUNT(*) FROM ("
    "SELECT type,name,sql FROM source_v08.sqlite_schema WHERE type IN ('table','view') "
    "EXCEPT SELECT type,name,sql FROM main.sqlite_schema "
    "WHERE type IN ('table','view')))"
  ).fetchone()[0]
  checks["exact_data_differences"] = {
    name: bidirectional_difference(connection, name)
    for name in EXACT_DATA_OBJECTS
  }
  current_indexes = {
    str(row[0]) for row in connection.execute(
      "SELECT name FROM sqlite_schema WHERE type='index'"
    ).fetchall()
  }
  checks["removed_indexes_still_present"] = sorted(
    set(REMOVED_INDEXES) & current_indexes
  )
  checks["required_indexes_missing"] = sorted(
    set(REQUIRED_INDEXES) - current_indexes
  )
  checks["index_count"] = len(current_indexes)
  checks["source_index_count"] = connection.execute(
    "SELECT COUNT(*) FROM source_v08.sqlite_schema WHERE type='index'"
  ).fetchone()[0]
  checks["foreign_key_check_count"] = len(
    connection.execute("PRAGMA main.foreign_key_check").fetchall()
  )
  checks["integrity_check"] = connection.execute(
    "PRAGMA main.integrity_check"
  ).fetchone()[0]
  connection.execute("DETACH DATABASE source_v08")

  errors: list[str] = []
  expected_scalars = {
    "source_database_unchanged": True,
    "user_version": 9,
    "compatibility_column_mismatch_count": 0,
    "table_view_schema_difference_count": 0,
    "foreign_key_check_count": 0,
    "integrity_check": "ok",
  }
  for key, expected in expected_scalars.items():
    if checks.get(key) != expected:
      errors.append(f"{key}: expected {expected}, got {checks.get(key)}")
  for name, expected in EXPECTED_ROW_COUNTS.items():
    actual = checks["object_row_counts"].get(name)
    if actual != expected:
      errors.append(f"{name}: expected {expected} rows, got {actual}")
  for name, duplicates in checks["key_id_duplicate_counts"].items():
    if duplicates:
      errors.append(f"{name}: {duplicates} duplicate stable IDs")
  for name, differences in checks["exact_data_differences"].items():
    if differences:
      errors.append(f"{name}: {differences} data differences from v0.8")
  if checks["removed_indexes_still_present"]:
    errors.append(
      "removed indexes still present: "
      + ", ".join(checks["removed_indexes_still_present"])
    )
  if checks["required_indexes_missing"]:
    errors.append(
      "required indexes missing: "
      + ", ".join(checks["required_indexes_missing"])
    )
  return {
    "status": "pass" if not errors else "fail",
    "ruleset_version": RULESET_VERSION,
    "errors": errors,
    "checks": checks,
  }


def main() -> None:
  parser = argparse.ArgumentParser(
    description="Build index-optimized Ming 1628 runtime database v0.9",
  )
  parser.add_argument(
    "--source-database", type=Path, default=DEFAULT_SOURCE_DATABASE,
  )
  parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT_DIR)
  args = parser.parse_args()

  source_database = args.source_database.resolve()
  output_dir = args.output_dir.resolve()
  if not source_database.exists():
    raise SystemExit(f"Missing v0.8 SQLite database: {source_database}")
  output_dir.mkdir(parents=True, exist_ok=True)
  output_database = output_dir / OUTPUT_DATABASE_NAME
  temporary_database = output_database.with_suffix(output_database.suffix + ".tmp")
  report_path = output_dir / "index_optimization_v0.9_validation_report.json"
  size_path = output_dir / "index_optimization_v0.9_object_sizes.csv"

  previous_report: dict[str, Any] = {}
  if report_path.exists():
    try:
      previous_report = json.loads(report_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
      previous_report = {}

  source_hash_before = file_sha256(source_database)
  source_indexes = index_size_map(source_database)
  source_connection = sqlite3.connect(
    f"file:{source_database}?mode=ro", uri=True,
  )
  try:
    if source_connection.execute("PRAGMA user_version").fetchone()[0] != 8:
      raise RuntimeError("v0.9 index optimization requires source user_version=8")
    missing = sorted(set(REMOVED_INDEXES) - set(source_indexes))
    if missing:
      raise RuntimeError("source indexes missing: " + ", ".join(missing))
  finally:
    source_connection.close()

  if temporary_database.exists():
    temporary_database.unlink()
  print("[v0.9] copying immutable v0.8 source", flush=True)
  shutil.copy2(source_database, temporary_database)
  connection = sqlite3.connect(temporary_database)
  connection.row_factory = sqlite3.Row
  try:
    print("[v0.9] removing redundant and low-value indexes", flush=True)
    transform_database(connection)
    print("[v0.9] validating unchanged data and public interfaces", flush=True)
    validation = validate_database(
      connection, source_database, source_hash_before,
    )
  finally:
    connection.close()

  source_bytes = source_database.stat().st_size
  optimized_bytes = temporary_database.stat().st_size
  saved_bytes = source_bytes - optimized_bytes
  saved_percent = saved_bytes * 100.0 / source_bytes
  output_indexes = index_size_map(temporary_database)
  source_index_bytes = sum(source_indexes.values())
  output_index_bytes = sum(output_indexes.values())
  removed_index_bytes = {
    name: source_indexes[name] for name in REMOVED_INDEXES
  }
  validation["size"] = {
    "source_bytes": source_bytes,
    "optimized_bytes": optimized_bytes,
    "saved_bytes": saved_bytes,
    "source_mib": round(source_bytes / 1024 / 1024, 3),
    "optimized_mib": round(optimized_bytes / 1024 / 1024, 3),
    "saved_mib": round(saved_bytes / 1024 / 1024, 3),
    "saved_percent": round(saved_percent, 3),
    "maximum_optimized_mib": MAX_DATABASE_BYTES / 1024 / 1024,
    "minimum_saved_mib": MIN_SAVED_BYTES / 1024 / 1024,
    "source_index_mib": round(source_index_bytes / 1024 / 1024, 3),
    "optimized_index_mib": round(output_index_bytes / 1024 / 1024, 3),
    "index_saved_mib": round(
      (source_index_bytes - output_index_bytes) / 1024 / 1024, 3,
    ),
  }
  validation["removed_indexes"] = {
    name: {
      "reason": REMOVED_INDEXES[name],
      "source_bytes": removed_index_bytes[name],
      "source_mib": round(removed_index_bytes[name] / 1024 / 1024, 3),
    }
    for name in REMOVED_INDEXES
  }
  if optimized_bytes > MAX_DATABASE_BYTES:
    validation["errors"].append(
      f"optimized database exceeds 410 MiB: {optimized_bytes / 1024 / 1024:.3f} MiB"
    )
  if saved_bytes < MIN_SAVED_BYTES:
    validation["errors"].append(
      f"database saving below 30 MiB: {saved_bytes / 1024 / 1024:.3f} MiB"
    )

  print("[v0.9] benchmarking retained workload", flush=True)
  validation["performance"] = {
    "source_v0.8": benchmark_database(source_database),
    "optimized_v0.9": benchmark_database(temporary_database),
  }
  if not validation["performance"]["optimized_v0.9"]["all_under_250ms"]:
    validation["errors"].append("one or more v0.9 workload queries exceed 250 ms")
  validation["query_plans"] = explain_plans(temporary_database)
  if file_sha256(source_database) != source_hash_before:
    validation["errors"].append("v0.8 source database changed during optimization")

  size_rows = [
    *object_size_rows(source_database, "v0.8"),
    *object_size_rows(temporary_database, "v0.9"),
  ]
  write_csv_atomic(
    size_path,
    ["database_version", "object_name", "object_type", "bytes", "mib"],
    size_rows,
  )
  validation["database_sha256"] = file_sha256(temporary_database)
  validation["generated_file_hashes"] = {
    size_path.name: file_sha256(size_path),
  }
  validation["input_hashes"] = {
    "source_database": source_hash_before,
    "builder": file_sha256(Path(__file__).resolve()),
  }
  comparable_repeat = (
    previous_report.get("status") == "pass"
    and previous_report.get("ruleset_version") == RULESET_VERSION
    and previous_report.get("input_hashes") == validation["input_hashes"]
  )
  database_hash_match = (
    comparable_repeat
    and previous_report.get("database_sha256") == validation["database_sha256"]
  )
  generated_hashes_match = (
    comparable_repeat
    and previous_report.get("generated_file_hashes")
    == validation["generated_file_hashes"]
  )
  object_row_counts_match = (
    comparable_repeat
    and previous_report.get("checks", {}).get("object_row_counts")
    == validation["checks"]["object_row_counts"]
  )
  validation["repeat_build"] = {
    "previous_comparable_run_found": comparable_repeat,
    "database_sha256_match": database_hash_match if comparable_repeat else None,
    "generated_file_hashes_match": generated_hashes_match if comparable_repeat else None,
    "object_row_counts_match": object_row_counts_match if comparable_repeat else None,
    "deterministic_outputs_match": (
      database_hash_match and generated_hashes_match and object_row_counts_match
      if comparable_repeat else None
    ),
  }
  if comparable_repeat and not (
    database_hash_match and generated_hashes_match and object_row_counts_match
  ):
    validation["errors"].append("repeat build output mismatch")
  validation["status"] = "pass" if not validation["errors"] else "fail"
  write_json_atomic(report_path, validation)
  if validation["status"] != "pass":
    raise RuntimeError(
      "v0.9 final validation failed: " + "; ".join(validation["errors"])
    )
  temporary_database.replace(output_database)
  print(json.dumps({
    "status": "pass",
    "source_mib": validation["size"]["source_mib"],
    "optimized_mib": validation["size"]["optimized_mib"],
    "saved_mib": validation["size"]["saved_mib"],
    "database": str(output_database),
    "validation": str(report_path),
  }, ensure_ascii=False, indent=2), flush=True)


if __name__ == "__main__":
  main()
