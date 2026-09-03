#!/usr/bin/env python3
"""Build the five-block static simulation foundation snapshot v1.0.

The accepted v0.9 database is immutable input.  This builder adds exactly five
WITHOUT ROWID rule tables and no views, indexes, runtime state, save data or
event instances.  Rule provenance remains in an external, versioned manifest
so the runtime database stays small.
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
  DATA_ROOT / "12.运行数据库索引优化/game_world_1628_v0.9.sqlite"
)
DEFAULT_OUTPUT_DIR = DATA_ROOT / "13.模拟基础规则"
DEFAULT_RULES_PATH = DEFAULT_OUTPUT_DIR / "simulation_foundation_rules_v1.0.json"
DEFAULT_SOURCE_MANIFEST = (
  DEFAULT_OUTPUT_DIR / "simulation_foundation_source_manifest_v1.0.csv"
)
OUTPUT_DATABASE_NAME = "game_world_1628_v1.0.sqlite"
REPORT_NAME = "simulation_foundation_v1.0_validation_report.json"
SIZE_REPORT_NAME = "simulation_foundation_v1.0_object_sizes.csv"
RULESET_VERSION = "v1.0"
SOURCE_USER_VERSION = 9
OUTPUT_USER_VERSION = 10
MAX_DATABASE_GROWTH_BYTES = 512 * 1024


TABLE_PREFIXES = {
  "administrative_fiscal_rule": "AF-",
  "military_rule": "MI-",
  "agriculture_rule": "AG-",
  "industry_commerce_transport_rule": "IC-",
  "simulation_rule": "SR-",
}
TABLE_NAMES = tuple(TABLE_PREFIXES)
EXPECTED_RULE_COUNTS = {
  "administrative_fiscal_rule": 18,
  "military_rule": 20,
  "agriculture_rule": 25,
  "industry_commerce_transport_rule": 58,
  "simulation_rule": 26,
}
EXPECTED_TOTAL_RULES = sum(EXPECTED_RULE_COUNTS.values())
ALLOWED_EVIDENCE_CLASSES = {
  "documented_structure",
  "historical_parameter",
  "historical_range",
  "model_calibration",
}
FORBIDDEN_RULE_TYPES = {
  "runtime_state",
  "save_state",
  "save_slot",
  "event_instance",
  "historical_event_instance",
  "inventory_snapshot",
  "price_snapshot",
  "fiscal_ledger_snapshot",
  "military_unit_instance",
}
FORBIDDEN_PARAMETER_KEYS = {
  "current_population",
  "current_households",
  "current_stock",
  "current_inventory",
  "current_price",
  "current_balance",
  "event_instance_id",
  "occurred_at",
  "save_slot_id",
  "player_id",
}
EXACT_SOURCE_OBJECTS = (
  "county_economy_baseline",
  "county_geography_resources",
  "county_social_structure_baseline",
  "county_occupation_quota_core",
  "settlement_node",
  "local_division_definition",
  "historical_person_catalog",
)


def file_sha256(path: Path) -> str:
  digest = hashlib.sha256()
  with path.open("rb") as stream:
    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
      digest.update(chunk)
  return digest.hexdigest()


def quoted(identifier: str) -> str:
  return '"' + identifier.replace('"', '""') + '"'


def canonical_json(value: Any) -> str:
  return json.dumps(
    value, ensure_ascii=False, sort_keys=True, separators=(",", ":"),
  )


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


def read_source_manifest(path: Path) -> dict[str, dict[str, str]]:
  with path.open("r", encoding="utf-8", newline="") as stream:
    rows = list(csv.DictReader(stream))
  required_columns = {
    "source_key", "source_title", "source_type", "date_scope", "url",
    "local_path", "used_for", "evidence_boundary", "access_note",
  }
  if not rows:
    raise RuntimeError("source manifest is empty")
  if set(rows[0]) != required_columns:
    raise RuntimeError(
      "source manifest columns differ from the fixed v1.0 contract"
    )
  result: dict[str, dict[str, str]] = {}
  for row in rows:
    source_key = row["source_key"].strip()
    if not source_key or source_key in result:
      raise RuntimeError(f"duplicate or empty source key: {source_key!r}")
    if not row["url"].strip() and not row["local_path"].strip():
      raise RuntimeError(f"source {source_key} has neither URL nor local path")
    result[source_key] = row
  return result


def parameter_keys(value: Any) -> set[str]:
  keys: set[str] = set()
  if isinstance(value, dict):
    for key, nested in value.items():
      keys.add(str(key))
      keys.update(parameter_keys(nested))
  elif isinstance(value, list):
    for nested in value:
      keys.update(parameter_keys(nested))
  return keys


def semantic_contract_summary(
  rules: dict[str, list[dict[str, Any]]],
) -> dict[str, Any]:
  industry_rows = rules["industry_commerce_transport_rule"]
  agriculture_rows = rules["agriculture_rule"]
  simulation_rows = rules["simulation_rule"]
  commodity_rows = [
    row for row in industry_rows if row["rule_type"] == "commodity"
  ]
  recipe_rows = [
    row for row in industry_rows if row["rule_type"] == "production_recipe"
  ]
  crop_rows = [
    row for row in agriculture_rows
    if row["rule_type"] in {"crop", "cash_crop", "limited_new_world_crop"}
  ]
  transport_rows = [
    row for row in industry_rows if row["rule_type"] == "transport_mode"
  ]
  event_rows = [
    row for row in simulation_rows if row["rule_type"] == "event_trigger"
  ]

  commodity_codes = [
    str(row["parameters"].get("commodity_code", ""))
    for row in commodity_rows
  ]
  crop_codes = [
    str(row["parameters"].get("crop_code", "")) for row in crop_rows
  ]
  transport_codes = [
    str(row["parameters"].get("mode_code", "")) for row in transport_rows
  ]
  event_codes = [
    str(row["parameters"].get("event_code", "")) for row in event_rows
  ]
  commodity_code_set = set(commodity_codes)
  crop_code_set = set(crop_codes)
  unresolved_crop_outputs = sorted({
    str(row["parameters"].get("commodity_output_code", ""))
    for row in crop_rows
    if row["parameters"].get("commodity_output_code") not in commodity_code_set
  })
  unresolved_recipe_outputs: set[str] = set()
  unresolved_recipe_inputs: set[str] = set()
  unresolved_recipe_crop_codes: set[str] = set()
  recipes_with_legacy_inputs: list[str] = []
  for row in recipe_rows:
    parameters = row["parameters"]
    if "inputs" in parameters:
      recipes_with_legacy_inputs.append(row["rule_id"])
    for code in parameters.get("output", {}):
      if code not in commodity_code_set:
        unresolved_recipe_outputs.add(str(code))
    for code in parameters.get("commodity_inputs", {}):
      if code not in commodity_code_set:
        unresolved_recipe_inputs.add(str(code))
    for code in parameters.get("eligible_crop_codes", []):
      if code not in crop_code_set:
        unresolved_recipe_crop_codes.add(str(code))

  crop_yield_order_violations: list[str] = []
  for row in crop_rows:
    parameters = row["parameters"]
    low = parameters.get("yield_kg_per_mu_low")
    mid = parameters.get("yield_kg_per_mu_mid")
    high = parameters.get("yield_kg_per_mu_high")
    if not all(isinstance(value, (int, float)) for value in (low, mid, high)):
      crop_yield_order_violations.append(row["rule_id"])
    elif not low <= mid <= high:
      crop_yield_order_violations.append(row["rule_id"])

  event_probability_violations: list[str] = []
  for row in event_rows:
    arrays = [
      value for key, value in row["parameters"].items()
      if key.startswith("annual_probability_by_")
    ]
    for values in arrays:
      if (
        not isinstance(values, list)
        or len(values) != 5
        or any(not isinstance(value, (int, float)) for value in values)
        or values != sorted(values)
        or any(value < 0 or value > 1 for value in values)
      ):
        event_probability_violations.append(row["rule_id"])

  expenditure_row = next(
    row for row in rules["administrative_fiscal_rule"]
    if row["rule_id"] == "AF-FISCAL-009"
  )
  expenditure_share_sum = round(sum(
    float(value)
    for key, value in expenditure_row["parameters"].items()
    if key.endswith("_share")
  ), 10)

  return {
    "commodity_code_count": len(commodity_codes),
    "commodity_code_duplicate_count": len(commodity_codes) - len(commodity_code_set),
    "crop_code_count": len(crop_codes),
    "crop_code_duplicate_count": len(crop_codes) - len(crop_code_set),
    "production_recipe_count": len(recipe_rows),
    "transport_mode_count": len(transport_codes),
    "transport_mode_duplicate_count": len(transport_codes) - len(set(transport_codes)),
    "event_code_count": len(event_codes),
    "event_code_duplicate_count": len(event_codes) - len(set(event_codes)),
    "unresolved_crop_output_codes": unresolved_crop_outputs,
    "unresolved_recipe_output_codes": sorted(unresolved_recipe_outputs),
    "unresolved_recipe_input_codes": sorted(unresolved_recipe_inputs),
    "unresolved_recipe_crop_codes": sorted(unresolved_recipe_crop_codes),
    "recipes_with_legacy_inputs": sorted(recipes_with_legacy_inputs),
    "crop_yield_order_violations": sorted(crop_yield_order_violations),
    "event_probability_violations": sorted(set(event_probability_violations)),
    "county_expenditure_share_sum": expenditure_share_sum,
  }


def load_and_validate_rules(
  path: Path,
  source_manifest: dict[str, dict[str, str]],
) -> dict[str, list[dict[str, Any]]]:
  document = json.loads(path.read_text(encoding="utf-8"))
  if set(document) != {
    "ruleset_version", "snapshot_year", "scope_statement", "tables",
  }:
    raise RuntimeError("rules JSON top-level keys differ from the v1.0 contract")
  if document["ruleset_version"] != RULESET_VERSION:
    raise RuntimeError("rules JSON version is not v1.0")
  if document["snapshot_year"] != 1628:
    raise RuntimeError("rules JSON snapshot year is not 1628")
  tables = document["tables"]
  if set(tables) != set(TABLE_NAMES):
    raise RuntimeError(
      "rules JSON must contain exactly the five approved table groups"
    )

  required_fields = {
    "rule_id", "rule_type", "display_name_zh_hans", "applies_to",
    "parameters", "evidence_class", "historical_claim", "source_keys",
    "evidence_note",
  }
  used_sources: set[str] = set()
  all_rule_ids: set[str] = set()
  normalized: dict[str, list[dict[str, Any]]] = {}
  for table_name in TABLE_NAMES:
    rows = tables[table_name]
    if len(rows) != EXPECTED_RULE_COUNTS[table_name]:
      raise RuntimeError(
        f"{table_name}: expected {EXPECTED_RULE_COUNTS[table_name]} rules, "
        f"got {len(rows)}"
      )
    normalized_rows: list[dict[str, Any]] = []
    for row in rows:
      if set(row) != required_fields:
        raise RuntimeError(
          f"{table_name}/{row.get('rule_id', '?')}: field contract mismatch"
        )
      rule_id = str(row["rule_id"])
      if not rule_id.startswith(TABLE_PREFIXES[table_name]):
        raise RuntimeError(f"{rule_id}: wrong prefix for {table_name}")
      if rule_id in all_rule_ids:
        raise RuntimeError(f"duplicate rule ID across groups: {rule_id}")
      all_rule_ids.add(rule_id)
      if row["rule_type"] in FORBIDDEN_RULE_TYPES:
        raise RuntimeError(f"{rule_id}: forbidden runtime/save rule type")
      if row["evidence_class"] not in ALLOWED_EVIDENCE_CLASSES:
        raise RuntimeError(f"{rule_id}: invalid evidence class")
      if row["historical_claim"] not in {"yes", "no"}:
        raise RuntimeError(f"{rule_id}: invalid historical_claim")
      if (
        row["evidence_class"] == "model_calibration"
        and row["historical_claim"] != "no"
      ):
        raise RuntimeError(f"{rule_id}: model calibration claims history")
      if not isinstance(row["parameters"], dict):
        raise RuntimeError(f"{rule_id}: parameters must be a JSON object")
      forbidden_keys = parameter_keys(row["parameters"]) & FORBIDDEN_PARAMETER_KEYS
      if forbidden_keys:
        raise RuntimeError(
          f"{rule_id}: forbidden runtime keys: {sorted(forbidden_keys)}"
        )
      if not isinstance(row["source_keys"], list):
        raise RuntimeError(f"{rule_id}: source_keys must be an array")
      unknown_sources = set(row["source_keys"]) - set(source_manifest)
      if unknown_sources:
        raise RuntimeError(
          f"{rule_id}: unknown source keys: {sorted(unknown_sources)}"
        )
      used_sources.update(row["source_keys"])
      normalized_rows.append(row)
    normalized[table_name] = sorted(
      normalized_rows, key=lambda item: item["rule_id"],
    )

  unused_sources = set(source_manifest) - used_sources
  if unused_sources:
    raise RuntimeError(f"unused source manifest rows: {sorted(unused_sources)}")
  if len(all_rule_ids) != EXPECTED_TOTAL_RULES:
    raise RuntimeError("unexpected total rule count")
  semantic = semantic_contract_summary(normalized)
  semantic_expected = {
    "commodity_code_count": 29,
    "commodity_code_duplicate_count": 0,
    "crop_code_count": 15,
    "crop_code_duplicate_count": 0,
    "production_recipe_count": 18,
    "transport_mode_count": 6,
    "transport_mode_duplicate_count": 0,
    "event_code_count": 12,
    "event_code_duplicate_count": 0,
    "unresolved_crop_output_codes": [],
    "unresolved_recipe_output_codes": [],
    "unresolved_recipe_input_codes": [],
    "unresolved_recipe_crop_codes": [],
    "recipes_with_legacy_inputs": [],
    "crop_yield_order_violations": [],
    "event_probability_violations": [],
    "county_expenditure_share_sum": 1.0,
  }
  if semantic != semantic_expected:
    raise RuntimeError(
      "simulation rule semantic contract mismatch: "
      + canonical_json({
        "expected": semantic_expected,
        "actual": semantic,
      })
    )
  return normalized


def create_rule_table_sql(table_name: str) -> str:
  if table_name not in TABLE_NAMES:
    raise ValueError(f"unapproved table name: {table_name}")
  return f"""
    CREATE TABLE {quoted(table_name)} (
      rule_id TEXT PRIMARY KEY,
      rule_type TEXT NOT NULL,
      display_name_zh_hans TEXT NOT NULL,
      applies_to TEXT NOT NULL,
      parameters_json TEXT NOT NULL
        CHECK(json_valid(parameters_json) AND json_type(parameters_json)='object'),
      evidence_class TEXT NOT NULL
        CHECK(evidence_class IN (
          'documented_structure','historical_parameter',
          'historical_range','model_calibration'
        )),
      historical_claim TEXT NOT NULL CHECK(historical_claim IN ('yes','no')),
      source_keys_json TEXT NOT NULL
        CHECK(json_valid(source_keys_json) AND json_type(source_keys_json)='array'),
      evidence_note TEXT NOT NULL
    ) WITHOUT ROWID
  """


def transform_database(
  connection: sqlite3.Connection,
  rules: dict[str, list[dict[str, Any]]],
) -> None:
  connection.execute("PRAGMA foreign_keys=ON")
  connection.execute("BEGIN IMMEDIATE")
  try:
    for table_name in TABLE_NAMES:
      connection.execute(create_rule_table_sql(table_name))
      rows = [
        (
          row["rule_id"],
          row["rule_type"],
          row["display_name_zh_hans"],
          row["applies_to"],
          canonical_json(row["parameters"]),
          row["evidence_class"],
          row["historical_claim"],
          canonical_json(sorted(row["source_keys"])),
          row["evidence_note"],
        )
        for row in rules[table_name]
      ]
      connection.executemany(
        f"INSERT INTO {quoted(table_name)} VALUES (?,?,?,?,?,?,?,?,?)",
        rows,
      )
    connection.execute(f"PRAGMA user_version={OUTPUT_USER_VERSION}")
    connection.commit()
  except Exception:
    connection.rollback()
    raise


def build_database(
  source_database: Path,
  target_database: Path,
  rules: dict[str, list[dict[str, Any]]],
) -> None:
  if target_database.exists():
    target_database.unlink()
  shutil.copyfile(source_database, target_database)
  connection = sqlite3.connect(target_database)
  try:
    transform_database(connection, rules)
  finally:
    connection.close()


def object_schema_map(connection: sqlite3.Connection) -> dict[str, tuple[str, str]]:
  return {
    str(row[1]): (str(row[0]), str(row[2] or ""))
    for row in connection.execute(
      "SELECT type,name,sql FROM sqlite_schema "
      "WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name"
    ).fetchall()
  }


def physical_table_counts(connection: sqlite3.Connection) -> dict[str, int]:
  names = [
    str(row[0])
    for row in connection.execute(
      "SELECT name FROM sqlite_schema WHERE type='table' "
      "AND name NOT LIKE 'sqlite_%' ORDER BY name"
    ).fetchall()
  ]
  return {
    name: int(connection.execute(
      f"SELECT COUNT(*) FROM {quoted(name)}"
    ).fetchone()[0])
    for name in names
  }


def bidirectional_difference(
  connection: sqlite3.Connection,
  object_name: str,
) -> int:
  object_sql = quoted(object_name)
  row = connection.execute(
    "SELECT "
    f"(SELECT COUNT(*) FROM (SELECT * FROM main.{object_sql} "
    f"EXCEPT SELECT * FROM source_v09.{object_sql})) + "
    f"(SELECT COUNT(*) FROM (SELECT * FROM source_v09.{object_sql} "
    f"EXCEPT SELECT * FROM main.{object_sql}))"
  ).fetchone()
  return int(row[0])


def validate_database(
  database: Path,
  source_database: Path,
  source_sha256: str,
  rules: dict[str, list[dict[str, Any]]],
) -> dict[str, Any]:
  source_connection = sqlite3.connect(
    f"file:{source_database}?mode=ro", uri=True,
  )
  output_connection = sqlite3.connect(database)
  output_connection.row_factory = sqlite3.Row
  try:
    source_schema = object_schema_map(source_connection)
    output_schema = object_schema_map(output_connection)
    added_names = sorted(set(output_schema) - set(source_schema))
    removed_names = sorted(set(source_schema) - set(output_schema))
    changed_source_definitions = sorted(
      name for name in source_schema
      if name in output_schema and source_schema[name] != output_schema[name]
    )
    added_types = {
      name: output_schema[name][0] for name in added_names
    }
    source_counts = physical_table_counts(source_connection)
    output_counts = physical_table_counts(output_connection)
    old_count_differences = {
      name: {
        "source": count,
        "output": output_counts.get(name),
      }
      for name, count in source_counts.items()
      if output_counts.get(name) != count
    }

    source_uri = f"file:{source_database}?mode=ro"
    output_connection.execute("ATTACH DATABASE ? AS source_v09", (source_uri,))
    exact_differences = {
      name: bidirectional_difference(output_connection, name)
      for name in EXACT_SOURCE_OBJECTS
    }
    output_connection.execute("DETACH DATABASE source_v09")

    rule_counts = {
      name: int(output_connection.execute(
        f"SELECT COUNT(*) FROM {quoted(name)}"
      ).fetchone()[0])
      for name in TABLE_NAMES
    }
    noncanonical_json_rows = 0
    model_claim_violation_count = 0
    forbidden_rule_type_count = 0
    for table_name in TABLE_NAMES:
      for row in output_connection.execute(
        f"SELECT rule_type,parameters_json,evidence_class,historical_claim,"
        f"source_keys_json FROM {quoted(table_name)} ORDER BY rule_id"
      ).fetchall():
        if row[0] in FORBIDDEN_RULE_TYPES:
          forbidden_rule_type_count += 1
        if row[2] == "model_calibration" and row[3] != "no":
          model_claim_violation_count += 1
        if canonical_json(json.loads(row[1])) != row[1]:
          noncanonical_json_rows += 1
        if canonical_json(json.loads(row[4])) != row[4]:
          noncanonical_json_rows += 1

    checks: dict[str, Any] = {
      "source_database_sha256_before": source_sha256,
      "source_database_sha256_after": file_sha256(source_database),
      "source_database_unchanged": (
        source_sha256 == file_sha256(source_database)
      ),
      "source_user_version": source_connection.execute(
        "PRAGMA user_version"
      ).fetchone()[0],
      "output_user_version": output_connection.execute(
        "PRAGMA user_version"
      ).fetchone()[0],
      "added_object_names": added_names,
      "added_object_types": added_types,
      "removed_source_object_names": removed_names,
      "changed_source_object_definitions": changed_source_definitions,
      "old_table_row_count_differences": old_count_differences,
      "exact_source_data_differences": exact_differences,
      "new_rule_row_counts": rule_counts,
      "new_rule_total": sum(rule_counts.values()),
      "new_view_count": sum(
        output_schema[name][0] == "view" for name in added_names
      ),
      "new_index_count": sum(
        output_schema[name][0] == "index" for name in added_names
      ),
      "new_trigger_count": sum(
        output_schema[name][0] == "trigger" for name in added_names
      ),
      "noncanonical_json_row_count": noncanonical_json_rows,
      "model_historical_claim_violation_count": model_claim_violation_count,
      "forbidden_rule_type_count": forbidden_rule_type_count,
      "foreign_key_check_count": len(
        output_connection.execute("PRAGMA foreign_key_check").fetchall()
      ),
      "integrity_check": output_connection.execute(
        "PRAGMA integrity_check"
      ).fetchone()[0],
    }
  finally:
    output_connection.close()
    source_connection.close()

  expected_added = sorted(TABLE_NAMES)
  errors: list[str] = []
  expected_scalars = {
    "source_database_unchanged": True,
    "source_user_version": SOURCE_USER_VERSION,
    "output_user_version": OUTPUT_USER_VERSION,
    "added_object_names": expected_added,
    "removed_source_object_names": [],
    "changed_source_object_definitions": [],
    "old_table_row_count_differences": {},
    "new_rule_total": EXPECTED_TOTAL_RULES,
    "new_view_count": 0,
    "new_index_count": 0,
    "new_trigger_count": 0,
    "noncanonical_json_row_count": 0,
    "model_historical_claim_violation_count": 0,
    "forbidden_rule_type_count": 0,
    "foreign_key_check_count": 0,
    "integrity_check": "ok",
  }
  for key, expected in expected_scalars.items():
    if checks.get(key) != expected:
      errors.append(f"{key}: expected {expected!r}, got {checks.get(key)!r}")
  for table_name, expected in EXPECTED_RULE_COUNTS.items():
    if checks["new_rule_row_counts"].get(table_name) != expected:
      errors.append(
        f"{table_name}: expected {expected} rules, "
        f"got {checks['new_rule_row_counts'].get(table_name)}"
      )
  for name, object_type in checks["added_object_types"].items():
    if object_type != "table":
      errors.append(f"unapproved added object type: {object_type} {name}")
  for name, differences in checks["exact_source_data_differences"].items():
    if differences:
      errors.append(f"{name}: {differences} differences from v0.9")

  source_size = source_database.stat().st_size
  output_size = database.stat().st_size
  growth = output_size - source_size
  size = {
    "source_bytes": source_size,
    "output_bytes": output_size,
    "growth_bytes": growth,
    "source_mib": round(source_size / 1024 / 1024, 3),
    "output_mib": round(output_size / 1024 / 1024, 3),
    "growth_kib": round(growth / 1024, 3),
    "maximum_growth_kib": MAX_DATABASE_GROWTH_BYTES / 1024,
  }
  if growth < 0 or growth > MAX_DATABASE_GROWTH_BYTES:
    errors.append(
      f"database growth out of range: {growth} bytes "
      f"(maximum {MAX_DATABASE_GROWTH_BYTES})"
    )

  return {
    "status": "pass" if not errors else "fail",
    "ruleset_version": RULESET_VERSION,
    "errors": errors,
    "checks": checks,
    "size": size,
  }


def query_elapsed_ms(
  connection: sqlite3.Connection,
  sql: str,
  parameters: Sequence[Any],
) -> tuple[float, int]:
  connection.execute(sql, parameters).fetchall()
  samples: list[float] = []
  row_count = 0
  for _ in range(5):
    started = time.perf_counter()
    rows = connection.execute(sql, parameters).fetchall()
    samples.append((time.perf_counter() - started) * 1000)
    row_count = len(rows)
  return round(max(samples), 3), row_count


def benchmark_database(database: Path) -> dict[str, Any]:
  connection = sqlite3.connect(f"file:{database}?mode=ro", uri=True)
  try:
    tests: dict[str, tuple[str, tuple[Any, ...]]] = {
      "administrative_rule_by_id": (
        "SELECT * FROM administrative_fiscal_rule WHERE rule_id=?",
        ("AF-FISCAL-010",),
      ),
      "military_supply_rules": (
        "SELECT * FROM military_rule WHERE rule_type=? ORDER BY rule_id",
        ("monthly_grain_ration",),
      ),
      "agriculture_crop_rules": (
        "SELECT * FROM agriculture_rule WHERE rule_type IN "
        "('crop','cash_crop','limited_new_world_crop') ORDER BY rule_id",
        (),
      ),
      "industry_recipes": (
        "SELECT * FROM industry_commerce_transport_rule "
        "WHERE rule_type='production_recipe' ORDER BY rule_id",
        (),
      ),
      "simulation_event_rules": (
        "SELECT * FROM simulation_rule WHERE rule_type='event_trigger' "
        "ORDER BY rule_id",
        (),
      ),
    }
    results: dict[str, Any] = {}
    for name, (sql, parameters) in tests.items():
      elapsed_ms, row_count = query_elapsed_ms(connection, sql, parameters)
      results[name] = {"elapsed_ms": elapsed_ms, "row_count": row_count}
    results["maximum_elapsed_ms"] = max(
      item["elapsed_ms"] for item in results.values()
      if isinstance(item, dict)
    )
    results["all_under_250ms"] = results["maximum_elapsed_ms"] < 250
    return results
  finally:
    connection.close()


def object_size_rows(database: Path) -> list[dict[str, Any]]:
  connection = sqlite3.connect(f"file:{database}?mode=ro", uri=True)
  try:
    byte_map = {
      str(row[0]): int(row[1])
      for row in connection.execute(
        "SELECT name,SUM(pgsize) FROM dbstat GROUP BY name"
      ).fetchall()
    }
    rows = [{
      "database_version": RULESET_VERSION,
      "object_name": "__database_total__",
      "object_type": "database",
      "row_count": "",
      "bytes": database.stat().st_size,
      "kib": f"{database.stat().st_size / 1024:.3f}",
    }]
    for table_name in TABLE_NAMES:
      row_count = int(connection.execute(
        f"SELECT COUNT(*) FROM {quoted(table_name)}"
      ).fetchone()[0])
      size = byte_map.get(table_name, 0)
      rows.append({
        "database_version": RULESET_VERSION,
        "object_name": table_name,
        "object_type": "table",
        "row_count": row_count,
        "bytes": size,
        "kib": f"{size / 1024:.3f}",
      })
    return rows
  finally:
    connection.close()


def main() -> None:
  parser = argparse.ArgumentParser(
    description="Build the five-block Ming 1628 simulation foundation v1.0",
  )
  parser.add_argument(
    "--source-database", type=Path, default=DEFAULT_SOURCE_DATABASE,
  )
  parser.add_argument("--rules", type=Path, default=DEFAULT_RULES_PATH)
  parser.add_argument(
    "--source-manifest", type=Path, default=DEFAULT_SOURCE_MANIFEST,
  )
  parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT_DIR)
  args = parser.parse_args()

  source_database = args.source_database.resolve()
  rules_path = args.rules.resolve()
  source_manifest_path = args.source_manifest.resolve()
  output_dir = args.output_dir.resolve()
  for path, label in (
    (source_database, "v0.9 source database"),
    (rules_path, "rules JSON"),
    (source_manifest_path, "source manifest"),
  ):
    if not path.exists():
      raise SystemExit(f"Missing {label}: {path}")

  source_connection = sqlite3.connect(
    f"file:{source_database}?mode=ro", uri=True,
  )
  try:
    source_version = source_connection.execute(
      "PRAGMA user_version"
    ).fetchone()[0]
    collisions = sorted(
      set(TABLE_NAMES) & {
        str(row[0]) for row in source_connection.execute(
          "SELECT name FROM sqlite_schema"
        ).fetchall()
      }
    )
  finally:
    source_connection.close()
  if source_version != SOURCE_USER_VERSION:
    raise RuntimeError(
      f"v1.0 requires source user_version={SOURCE_USER_VERSION}, "
      f"got {source_version}"
    )
  if collisions:
    raise RuntimeError(f"v0.9 already contains target objects: {collisions}")

  source_manifest = read_source_manifest(source_manifest_path)
  rules = load_and_validate_rules(rules_path, source_manifest)
  output_dir.mkdir(parents=True, exist_ok=True)
  output_database = output_dir / OUTPUT_DATABASE_NAME
  first_temporary = output_database.with_suffix(".sqlite.tmp.first")
  repeat_temporary = output_database.with_suffix(".sqlite.tmp.repeat")
  report_path = output_dir / REPORT_NAME
  size_report_path = output_dir / SIZE_REPORT_NAME
  source_hash = file_sha256(source_database)

  print("[v1.0] building first deterministic snapshot", flush=True)
  build_database(source_database, first_temporary, rules)
  print("[v1.0] validating exact five-table scope and v0.9 preservation", flush=True)
  validation = validate_database(
    first_temporary, source_database, source_hash, rules,
  )

  print("[v1.0] building repeat snapshot for SHA-256 check", flush=True)
  build_database(source_database, repeat_temporary, rules)
  first_hash = file_sha256(first_temporary)
  repeat_hash = file_sha256(repeat_temporary)
  validation["repeat_build"] = {
    "first_sha256": first_hash,
    "repeat_sha256": repeat_hash,
    "sha256_match": first_hash == repeat_hash,
  }
  if first_hash != repeat_hash:
    validation["errors"].append("repeat build database SHA-256 mismatch")

  print("[v1.0] benchmarking five rule entry paths", flush=True)
  validation["performance"] = benchmark_database(first_temporary)
  if not validation["performance"]["all_under_250ms"]:
    validation["errors"].append("one or more rule queries exceed 250 ms")
  validation["database_sha256"] = first_hash
  validation["input_hashes"] = {
    "source_database": source_hash,
    "rules_json": file_sha256(rules_path),
    "source_manifest": file_sha256(source_manifest_path),
    "builder": file_sha256(Path(__file__).resolve()),
  }
  validation["scope_contract"] = {
    "approved_new_tables": list(TABLE_NAMES),
    "approved_new_table_count": len(TABLE_NAMES),
    "runtime_state_rows": 0,
    "save_rows": 0,
    "event_instance_rows": 0,
    "inventory_snapshot_rows": 0,
    "price_snapshot_rows": 0,
    "new_person_rows": 0,
    "new_settlement_rows": 0,
    "source_manifest_stored_in_database": False,
  }
  validation["semantic_contract"] = semantic_contract_summary(rules)

  size_rows = object_size_rows(first_temporary)
  write_csv_atomic(
    size_report_path,
    [
      "database_version", "object_name", "object_type", "row_count",
      "bytes", "kib",
    ],
    size_rows,
  )
  validation["generated_file_hashes"] = {
    size_report_path.name: file_sha256(size_report_path),
  }
  validation["status"] = "pass" if not validation["errors"] else "fail"
  write_json_atomic(report_path, validation)

  if repeat_temporary.exists():
    repeat_temporary.unlink()
  if validation["status"] != "pass":
    raise RuntimeError(
      "v1.0 validation failed: " + "; ".join(validation["errors"])
    )
  first_temporary.replace(output_database)
  print(json.dumps({
    "status": "pass",
    "database": str(output_database),
    "database_sha256": first_hash,
    "new_tables": len(TABLE_NAMES),
    "new_rules": EXPECTED_TOTAL_RULES,
    "growth_kib": validation["size"]["growth_kib"],
    "validation": str(report_path),
  }, ensure_ascii=False, indent=2), flush=True)


if __name__ == "__main__":
  main()
