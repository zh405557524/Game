#!/usr/bin/env python3
"""Build the compact, read-only Project Realm world snapshot v0.8.

The v0.7 database is treated as immutable input.  Repeated wide-table columns
are moved into narrow WITHOUT ROWID tables and the old read interfaces are
recreated as compatibility views.  Public identifiers remain stable.  The
only intentional value change is that virtual single-leaf zones reuse their
parent settlement coordinates and render seed.
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
  DATA_ROOT / "10.乡镇基层区划与计算分区/game_world_1628_v0.7.sqlite"
)
DEFAULT_OUTPUT_DIR = DATA_ROOT / "11.运行数据库瘦身"
OUTPUT_DATABASE_NAME = "game_world_1628_v0.8.sqlite"
RULESET_VERSION = "v0.8"
SNAPSHOT_YEAR = 1628
EXPECTED_SETTLEMENTS = 508_729
EXPECTED_VILLAGES = 505_684
EXPECTED_VIRTUAL_ZONES = 533_105
EXPECTED_SINGLE_ZONES = 506_459
EXPECTED_MULTI_ZONE_SETTLEMENTS = 2_270
EXPECTED_STORED_MULTI_ZONES = 26_646
EXPECTED_POIS = 193_328
EXPECTED_DIVISIONS = 18_279
EXPECTED_OCCUPATION_QUOTAS = 175_200
MAX_DATABASE_BYTES = 470 * 1024 * 1024
MIN_SAVING_PERCENT = 45.0


SECTOR_COUNT_COLUMNS = [
  "agriculture_count", "forestry_hunting_count", "pastoral_count",
  "fishery_water_count", "mining_salt_count", "food_processing_count",
  "textile_clothing_count", "ceramics_building_count",
  "metal_wood_paper_count", "transport_post_port_count",
  "commerce_finance_count", "domestic_service_count",
  "medicine_health_count", "religion_ritual_count",
  "education_culture_count", "government_admin_count",
  "military_security_count", "marginal_unfixed_count",
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
  "v_local_division_entry_settlements",
  "v_settlement_occupation_profile",
]

EXACT_COMPATIBILITY_OBJECTS = [
  "village_catalog",
  "settlement_local_division",
  "county_occupation_quota",
  "settlement_sector_quota",
  "settlement_poi",
]

ZONE_NON_RENDER_COLUMNS = [
  "zone_id", "snapshot_year", "settlement_id", "county_id", "zone_name",
  "zone_type", "resident_population", "labor_force_est",
  "historical_claim", "commercial_release_ready",
]


def file_sha256(path: Path) -> str:
  digest = hashlib.sha256()
  with path.open("rb") as stream:
    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
      digest.update(chunk)
  return digest.hexdigest()


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


def quoted(identifier: str) -> str:
  return '"' + identifier.replace('"', '""') + '"'


def transform_database(connection: sqlite3.Connection) -> None:
  """Replace duplicated v0.7 storage with compact v0.8 cores and views."""
  connection.execute("PRAGMA foreign_keys=OFF")
  connection.executescript(
    """
    BEGIN IMMEDIATE;

    DROP VIEW IF EXISTS v_county_entry_villages;
    DROP VIEW IF EXISTS v_settlement_entry_zones;
    DROP VIEW IF EXISTS v_zone_entry_pois;
    DROP VIEW IF EXISTS v_local_division_entry_settlements;
    DROP VIEW IF EXISTS v_county_entry_settlements;
    DROP VIEW IF EXISTS v_settlement_occupation_profile;

    CREATE TABLE village_extension (
      village_id TEXT PRIMARY KEY,
      settlement_form TEXT NOT NULL,
      anchor_id TEXT NOT NULL,
      population_weight_ppm INTEGER NOT NULL
        CHECK(population_weight_ppm BETWEEN 0 AND 1000000),
      farmland_weight_ppm INTEGER NOT NULL
        CHECK(farmland_weight_ppm BETWEEN 0 AND 1000000),
      position_method TEXT NOT NULL,
      FOREIGN KEY(village_id) REFERENCES settlement_node(settlement_id)
    ) WITHOUT ROWID;

    INSERT INTO village_extension
    SELECT village_id,settlement_form,anchor_id,population_weight_ppm,
           farmland_weight_ppm,position_method
    FROM village_catalog
    ORDER BY village_id;

    CREATE TABLE settlement_multi_zone_core (
      settlement_id TEXT NOT NULL,
      zone_ordinal INTEGER NOT NULL CHECK(zone_ordinal > 0),
      resident_population INTEGER NOT NULL,
      labor_force_est INTEGER NOT NULL,
      relative_x_0_10000 INTEGER NOT NULL
        CHECK(relative_x_0_10000 BETWEEN 0 AND 10000),
      relative_y_0_10000 INTEGER NOT NULL
        CHECK(relative_y_0_10000 BETWEEN 0 AND 10000),
      render_seed TEXT NOT NULL,
      PRIMARY KEY(settlement_id,zone_ordinal),
      FOREIGN KEY(settlement_id) REFERENCES settlement_node(settlement_id)
    ) WITHOUT ROWID;

    INSERT INTO settlement_multi_zone_core
    SELECT z.settlement_id,CAST(SUBSTR(z.zone_id,-3) AS INTEGER),
           z.resident_population,z.labor_force_est,
           z.relative_x_0_10000,z.relative_y_0_10000,z.render_seed
    FROM settlement_zone z
    JOIN (
      SELECT settlement_id
      FROM settlement_zone
      GROUP BY settlement_id
      HAVING COUNT(*) > 1
    ) multi USING(settlement_id)
    ORDER BY z.settlement_id,z.zone_id;

    CREATE TABLE settlement_local_division_core (
      settlement_id TEXT PRIMARY KEY,
      division_id TEXT NOT NULL,
      membership_method TEXT NOT NULL,
      historical_membership_claim TEXT NOT NULL,
      source_unit_anchor_id TEXT NOT NULL,
      distance_score_0_10000 INTEGER NOT NULL
        CHECK(distance_score_0_10000 BETWEEN 0 AND 10000),
      FOREIGN KEY(settlement_id) REFERENCES settlement_node(settlement_id),
      FOREIGN KEY(division_id) REFERENCES local_division_definition(division_id)
    ) WITHOUT ROWID;

    INSERT INTO settlement_local_division_core
    SELECT settlement_id,division_id,membership_method,
           historical_membership_claim,source_unit_anchor_id,
           distance_score_0_10000
    FROM settlement_local_division
    ORDER BY settlement_id;

    CREATE TABLE county_occupation_quota_core (
      county_id TEXT NOT NULL,
      occupation_code TEXT NOT NULL,
      worker_count_est INTEGER NOT NULL,
      worker_share_ppm INTEGER NOT NULL
        CHECK(worker_share_ppm BETWEEN 0 AND 1000000),
      raw_weight REAL NOT NULL,
      PRIMARY KEY(county_id,occupation_code),
      FOREIGN KEY(county_id) REFERENCES county_economy_baseline(county_id),
      FOREIGN KEY(occupation_code) REFERENCES occupation_definition(occupation_code)
    ) WITHOUT ROWID;

    INSERT INTO county_occupation_quota_core
    SELECT county_id,occupation_code,worker_count_est,worker_share_ppm,raw_weight
    FROM county_occupation_quota
    ORDER BY county_id,occupation_code;

    CREATE TABLE settlement_sector_quota_core (
      settlement_id TEXT PRIMARY KEY,
      agriculture_count INTEGER NOT NULL,
      forestry_hunting_count INTEGER NOT NULL,
      pastoral_count INTEGER NOT NULL,
      fishery_water_count INTEGER NOT NULL,
      mining_salt_count INTEGER NOT NULL,
      food_processing_count INTEGER NOT NULL,
      textile_clothing_count INTEGER NOT NULL,
      ceramics_building_count INTEGER NOT NULL,
      metal_wood_paper_count INTEGER NOT NULL,
      transport_post_port_count INTEGER NOT NULL,
      commerce_finance_count INTEGER NOT NULL,
      domestic_service_count INTEGER NOT NULL,
      medicine_health_count INTEGER NOT NULL,
      religion_ritual_count INTEGER NOT NULL,
      education_culture_count INTEGER NOT NULL,
      government_admin_count INTEGER NOT NULL,
      military_security_count INTEGER NOT NULL,
      marginal_unfixed_count INTEGER NOT NULL,
      FOREIGN KEY(settlement_id) REFERENCES settlement_node(settlement_id)
    ) WITHOUT ROWID;

    INSERT INTO settlement_sector_quota_core
    SELECT settlement_id,agriculture_count,forestry_hunting_count,pastoral_count,
           fishery_water_count,mining_salt_count,food_processing_count,
           textile_clothing_count,ceramics_building_count,metal_wood_paper_count,
           transport_post_port_count,commerce_finance_count,domestic_service_count,
           medicine_health_count,religion_ritual_count,education_culture_count,
           government_admin_count,military_security_count,marginal_unfixed_count
    FROM settlement_sector_quota
    ORDER BY settlement_id;

    CREATE TABLE settlement_poi_core (
      settlement_id TEXT NOT NULL,
      poi_ordinal INTEGER NOT NULL CHECK(poi_ordinal > 0),
      zone_ordinal INTEGER NOT NULL CHECK(zone_ordinal > 0),
      poi_type_code TEXT NOT NULL,
      workforce_slots_est INTEGER NOT NULL,
      render_seed TEXT NOT NULL,
      PRIMARY KEY(settlement_id,poi_ordinal),
      FOREIGN KEY(settlement_id) REFERENCES settlement_node(settlement_id),
      FOREIGN KEY(poi_type_code) REFERENCES institution_poi_definition(poi_type_code)
    ) WITHOUT ROWID;

    INSERT INTO settlement_poi_core
    SELECT settlement_id,CAST(SUBSTR(poi_id,-3) AS INTEGER),
           CAST(SUBSTR(zone_id,-3) AS INTEGER),poi_type_code,
           workforce_slots_est,render_seed
    FROM settlement_poi
    ORDER BY settlement_id,poi_id;

    DROP TABLE settlement_poi;
    DROP TABLE settlement_zone;
    DROP TABLE settlement_local_division;
    DROP TABLE settlement_sector_quota;
    DROP TABLE county_occupation_quota;
    DROP TABLE village_catalog;

    CREATE VIEW village_catalog AS
    SELECT s.settlement_id AS village_id,s.snapshot_year,s.county_id,
           s.subregion_id,s.settlement_name AS village_name,e.settlement_form,
           s.name_source_type,s.historical_name_claim,e.anchor_id,
           s.relative_x_0_10000,s.relative_y_0_10000,e.population_weight_ppm,
           e.farmland_weight_ppm,s.render_seed,e.position_method,
           s.commercial_release_ready
    FROM village_extension e
    JOIN settlement_node s ON s.settlement_id=e.village_id;

    CREATE VIEW settlement_zone AS
    SELECT printf('%s-B%03d',m.settlement_id,m.zone_ordinal) AS zone_id,
           s.snapshot_year,m.settlement_id,s.county_id,
           CASE ((m.zone_ordinal-1)%9)
             WHEN 0 THEN '东坊' WHEN 1 THEN '南坊' WHEN 2 THEN '西坊'
             WHEN 3 THEN '北坊' WHEN 4 THEN '中坊' WHEN 5 THEN '东南坊'
             WHEN 6 THEN '西南坊' WHEN 7 THEN '东北坊' ELSE '西北坊'
           END || CAST(((m.zone_ordinal-1)/9)+1 AS INTEGER) || '片' AS zone_name,
           'urban_leaf_block' AS zone_type,m.resident_population,
           m.labor_force_est,m.relative_x_0_10000,m.relative_y_0_10000,
           m.render_seed,'no' AS historical_claim,s.commercial_release_ready
    FROM settlement_multi_zone_core m
    JOIN settlement_node s USING(settlement_id)
    UNION ALL
    SELECT s.settlement_id||'-B001',s.snapshot_year,s.settlement_id,s.county_id,
           CASE s.settlement_type_code
             WHEN 'village' THEN '村中人口块'
             WHEN 'military_settlement' THEN '营堡人口块'
             WHEN 'resource_industrial' THEN '产业场人口块'
             WHEN 'transport_port_station' THEN '港驿人口块'
             ELSE '聚落人口块'
           END,
           'single_population_block',s.resident_population,s.labor_force_est,
           s.relative_x_0_10000,s.relative_y_0_10000,s.render_seed,
           'no',s.commercial_release_ready
    FROM settlement_node s
    WHERE NOT EXISTS (
      SELECT 1 FROM settlement_multi_zone_core m
      WHERE m.settlement_id=s.settlement_id
    );

    CREATE VIEW settlement_local_division AS
    SELECT m.settlement_id,m.division_id,s.county_id,s.snapshot_year,
           m.membership_method,m.historical_membership_claim,
           m.source_unit_anchor_id,m.distance_score_0_10000,
           s.commercial_release_ready
    FROM settlement_local_division_core m
    JOIN settlement_node s USING(settlement_id);

    CREATE VIEW county_occupation_quota AS
    SELECT q.county_id,e.snapshot_year,e.region,e.upper_unit,e.intermediate_unit,
           e.county,q.occupation_code,d.sector_code,d.occupation_name_zh_hans,
           q.worker_count_est,q.worker_share_ppm,q.raw_weight,d.primary_driver,
           'structural_projection_with_manual_anchors' AS evidence_type,
           'normalized county drivers + largest remainder' AS estimation_method,
           'no' AS commercial_release_ready
    FROM county_occupation_quota_core q
    JOIN county_economy_baseline e USING(county_id)
    JOIN occupation_definition d USING(occupation_code);

    CREATE VIEW settlement_sector_quota AS
    SELECT q.settlement_id,s.county_id,s.labor_force_est,
           q.agriculture_count,q.forestry_hunting_count,q.pastoral_count,
           q.fishery_water_count,q.mining_salt_count,q.food_processing_count,
           q.textile_clothing_count,q.ceramics_building_count,
           q.metal_wood_paper_count,q.transport_post_port_count,
           q.commerce_finance_count,q.domestic_service_count,
           q.medicine_health_count,q.religion_ritual_count,
           q.education_culture_count,q.government_admin_count,
           q.military_security_count,q.marginal_unfixed_count
    FROM settlement_sector_quota_core q
    JOIN settlement_node s USING(settlement_id);

    CREATE VIEW settlement_poi AS
    SELECT printf('%s-P%03d',p.settlement_id,p.poi_ordinal) AS poi_id,
           s.snapshot_year,p.settlement_id,
           printf('%s-B%03d',p.settlement_id,p.zone_ordinal) AS zone_id,
           s.county_id,p.poi_type_code,
           s.settlement_name||'·'||d.display_name_zh_hans AS poi_name,
           d.default_capacity AS capacity_est,p.workforce_slots_est,
           'generated_functional_name' AS name_source_type,
           'no' AS historical_claim,
           'generated_gameplay_placement' AS location_precision,
           p.render_seed,'no' AS commercial_release_ready
    FROM settlement_poi_core p
    JOIN settlement_node s USING(settlement_id)
    JOIN institution_poi_definition d USING(poi_type_code);

    CREATE INDEX idx_local_membership_division
      ON settlement_local_division_core(division_id,settlement_id);
    CREATE INDEX idx_social_occupation_county
      ON county_occupation_quota_core(county_id,worker_count_est DESC);

    CREATE VIEW v_county_entry_villages AS
    SELECT v.village_id,v.snapshot_year,c.region,c.upper_unit,
           c.intermediate_unit,c.county,v.county_id,v.subregion_id,
           z.subregion_name,z.direction_code,z.direction_name,z.zone_type,
           z.primary_landform,z.water_context,z.primary_resource_tags,
           z.render_biome_code,v.village_name,v.settlement_form,
           v.name_source_type,v.historical_name_claim,v.anchor_id,
           v.relative_x_0_10000,v.relative_y_0_10000,
           v.population_weight_ppm,v.farmland_weight_ppm,
           CAST(ROUND(cs.rural_population_est*v.population_weight_ppm/1000000.0)
             AS INTEGER) AS projected_rural_population,
           v.render_seed,v.position_method,v.commercial_release_ready
    FROM village_catalog v
    JOIN county_subregion_definition z USING(subregion_id)
    JOIN county_settlement_summary cs ON cs.county_id=v.county_id
    JOIN county_economy_baseline c ON c.county_id=v.county_id;

    CREATE VIEW v_settlement_entry_zones AS
    SELECT z.*,s.settlement_name,s.settlement_type_code,s.urban_rural
    FROM settlement_zone z
    JOIN settlement_node s USING(settlement_id);

    CREATE VIEW v_zone_entry_pois AS
    SELECT p.*,s.settlement_name,z.zone_name,
           d.display_name_zh_hans AS poi_type_name,d.sector_code
    FROM settlement_poi p
    JOIN settlement_node s USING(settlement_id)
    JOIN settlement_zone z
      ON z.settlement_id=p.settlement_id AND z.zone_id=p.zone_id
    JOIN institution_poi_definition d USING(poi_type_code);

    CREATE VIEW v_local_division_entry_settlements AS
    SELECT m.division_id,d.division_name,d.division_type_code,d.is_county_core,
           m.membership_method,m.historical_membership_claim,
           m.source_unit_anchor_id AS membership_source_unit_anchor_id,
           m.distance_score_0_10000,s.*,z.subregion_name,z.direction_name,
           z.zone_type
    FROM settlement_local_division m
    JOIN local_division_definition d USING(division_id)
    JOIN settlement_node s USING(settlement_id)
    JOIN county_subregion_definition z ON z.subregion_id=s.subregion_id;

    CREATE VIEW v_county_entry_settlements AS
    SELECT s.*,e.region,e.upper_unit,e.intermediate_unit,e.county,
           z.subregion_name,z.direction_name,z.zone_type,z.primary_landform,
           z.primary_resource_tags,m.division_id,d.division_name
    FROM settlement_node s
    JOIN county_economy_baseline e USING(county_id)
    LEFT JOIN county_subregion_definition z USING(subregion_id)
    JOIN settlement_local_division m USING(settlement_id)
    JOIN local_division_definition d USING(division_id);

    CREATE VIEW v_settlement_occupation_profile AS
    SELECT s.settlement_id,s.settlement_name,s.settlement_type_code,
           s.resident_population,q.*
    FROM settlement_node s
    JOIN settlement_sector_quota q USING(settlement_id);

    PRAGMA user_version=8;
    COMMIT;
    """
  )
  connection.execute("PRAGMA foreign_keys=ON")


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


def bidirectional_difference(
  connection: sqlite3.Connection,
  object_name: str,
  columns: Sequence[str] | None = None,
  where: str = "",
) -> int:
  column_sql = "*" if columns is None else ",".join(quoted(value) for value in columns)
  where_sql = f" WHERE {where}" if where else ""
  object_sql = quoted(object_name)
  row = connection.execute(
    "SELECT "
    f"(SELECT COUNT(*) FROM (SELECT {column_sql} FROM main.{object_sql}{where_sql} "
    f"EXCEPT SELECT {column_sql} FROM source_v07.{object_sql}{where_sql})) + "
    f"(SELECT COUNT(*) FROM (SELECT {column_sql} FROM source_v07.{object_sql}{where_sql} "
    f"EXCEPT SELECT {column_sql} FROM main.{object_sql}{where_sql}))"
  ).fetchone()
  return int(row[0])


def query_elapsed_ms(
  connection: sqlite3.Connection,
  sql: str,
  parameters: Sequence[Any],
) -> float:
  connection.execute(sql, parameters).fetchall()
  samples = []
  for _ in range(4):
    started = time.perf_counter()
    connection.execute(sql, parameters).fetchall()
    samples.append((time.perf_counter() - started) * 1000)
  return round(max(samples), 3)


def validate_database(
  connection: sqlite3.Connection,
  source_database: Path,
  source_hash_before: str,
) -> dict[str, Any]:
  checks: dict[str, Any] = {}
  source_uri = f"file:{source_database}?mode=ro"
  connection.execute("ATTACH DATABASE ? AS source_v07", (source_uri,))

  checks["source_database_sha256_before"] = source_hash_before
  checks["source_database_sha256_after"] = file_sha256(source_database)
  checks["source_database_unchanged"] = (
    checks["source_database_sha256_before"]
    == checks["source_database_sha256_after"]
  )
  checks["user_version"] = connection.execute("PRAGMA user_version").fetchone()[0]
  checks["settlement_rows"] = connection.execute(
    "SELECT COUNT(*) FROM settlement_node"
  ).fetchone()[0]
  checks["village_extension_rows"] = connection.execute(
    "SELECT COUNT(*) FROM village_extension"
  ).fetchone()[0]
  checks["multi_zone_rows_stored"] = connection.execute(
    "SELECT COUNT(*) FROM settlement_multi_zone_core"
  ).fetchone()[0]
  checks["multi_zone_settlements_stored"] = connection.execute(
    "SELECT COUNT(DISTINCT settlement_id) FROM settlement_multi_zone_core"
  ).fetchone()[0]
  checks["virtual_zone_rows"] = connection.execute(
    "SELECT COUNT(*) FROM settlement_zone"
  ).fetchone()[0]
  checks["virtual_single_zone_rows"] = connection.execute(
    "SELECT COUNT(*) FROM settlement_zone WHERE zone_type='single_population_block'"
  ).fetchone()[0]
  checks["poi_rows"] = connection.execute(
    "SELECT COUNT(*) FROM settlement_poi"
  ).fetchone()[0]
  checks["division_rows"] = connection.execute(
    "SELECT COUNT(*) FROM local_division_definition"
  ).fetchone()[0]
  checks["local_membership_rows"] = connection.execute(
    "SELECT COUNT(*) FROM settlement_local_division"
  ).fetchone()[0]
  checks["occupation_quota_rows"] = connection.execute(
    "SELECT COUNT(*) FROM county_occupation_quota"
  ).fetchone()[0]
  checks["settlement_sector_quota_rows"] = connection.execute(
    "SELECT COUNT(*) FROM settlement_sector_quota"
  ).fetchone()[0]
  checks["object_row_counts"] = {
    name: connection.execute(f"SELECT COUNT(*) FROM {quoted(name)}").fetchone()[0]
    for name in (
      "county_economy_baseline", "settlement_node", "village_catalog",
      "settlement_zone", "settlement_poi", "local_division_definition",
      "settlement_local_division", "county_occupation_quota",
      "settlement_sector_quota", "historical_person_catalog",
    )
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
      ("membership_settlement_id", "settlement_local_division", "settlement_id"),
      ("historical_person_id", "historical_person_catalog", "person_id"),
    )
  }

  checks["compatibility_column_mismatch_count"] = sum(
    object_columns(connection, "main", name)
    != object_columns(connection, "source_v07", name)
    for name in COMPATIBILITY_OBJECTS
  )
  checks["compatibility_object_types"] = {
    name: connection.execute(
      "SELECT type FROM sqlite_schema WHERE name=?", (name,)
    ).fetchone()[0]
    for name in (
      "village_catalog", "settlement_zone", "settlement_local_division",
      "county_occupation_quota", "settlement_sector_quota", "settlement_poi",
    )
  }

  checks["exact_compatibility_differences"] = {
    name: bidirectional_difference(connection, name)
    for name in EXACT_COMPATIBILITY_OBJECTS
  }
  checks["zone_non_render_difference_count"] = bidirectional_difference(
    connection, "settlement_zone", ZONE_NON_RENDER_COLUMNS,
  )
  checks["multi_zone_full_difference_count"] = bidirectional_difference(
    connection,
    "settlement_zone",
    where="zone_type='urban_leaf_block'",
  )
  checks["single_zone_position_or_seed_difference_count"] = connection.execute(
    "SELECT COUNT(*) FROM settlement_zone t "
    "JOIN source_v07.settlement_zone s USING(zone_id) "
    "WHERE t.zone_type='single_population_block' AND "
    "(t.relative_x_0_10000<>s.relative_x_0_10000 "
    "OR t.relative_y_0_10000<>s.relative_y_0_10000 "
    "OR t.render_seed<>s.render_seed)"
  ).fetchone()[0]
  checks["settlement_node_difference_count"] = bidirectional_difference(
    connection, "settlement_node",
  )
  checks["local_division_definition_difference_count"] = bidirectional_difference(
    connection, "local_division_definition",
  )
  checks["historical_person_difference_count"] = bidirectional_difference(
    connection, "historical_person_catalog",
  )

  checks["zone_population_or_labor_mismatch_count"] = connection.execute(
    "SELECT COUNT(*) FROM ("
    "SELECT s.settlement_id,s.resident_population,s.labor_force_est,"
    "SUM(z.resident_population) zone_population,SUM(z.labor_force_est) zone_labor "
    "FROM settlement_node s JOIN settlement_zone z USING(settlement_id) "
    "GROUP BY s.settlement_id "
    "HAVING zone_population<>s.resident_population OR zone_labor<>s.labor_force_est)"
  ).fetchone()[0]
  checks["poi_virtual_zone_orphan_count"] = connection.execute(
    "SELECT COUNT(*) FROM settlement_poi_core p WHERE NOT ("
    "EXISTS (SELECT 1 FROM settlement_multi_zone_core z "
    "WHERE z.settlement_id=p.settlement_id AND z.zone_ordinal=p.zone_ordinal) "
    "OR (p.zone_ordinal=1 AND NOT EXISTS ("
    "SELECT 1 FROM settlement_multi_zone_core z "
    "WHERE z.settlement_id=p.settlement_id)))"
  ).fetchone()[0]
  checks["foreign_key_check_count"] = len(
    connection.execute("PRAGMA foreign_key_check").fetchall()
  )
  checks["integrity_check"] = connection.execute(
    "PRAGMA integrity_check"
  ).fetchone()[0]

  largest_village_county = connection.execute(
    "SELECT county_id FROM settlement_node WHERE settlement_type_code='village' "
    "GROUP BY county_id ORDER BY COUNT(*) DESC,county_id LIMIT 1"
  ).fetchone()[0]
  largest_zone_settlement = connection.execute(
    "SELECT settlement_id FROM settlement_multi_zone_core "
    "GROUP BY settlement_id ORDER BY COUNT(*) DESC,settlement_id LIMIT 1"
  ).fetchone()[0]
  explicit_zone = connection.execute(
    "SELECT settlement_id,zone_id FROM settlement_zone "
    "WHERE settlement_id=? ORDER BY zone_id LIMIT 1",
    (largest_zone_settlement,),
  ).fetchone()
  largest_division = connection.execute(
    "SELECT division_id FROM settlement_local_division_core "
    "GROUP BY division_id ORDER BY COUNT(*) DESC,division_id LIMIT 1"
  ).fetchone()[0]
  busiest_poi_zone = connection.execute(
    "SELECT settlement_id,printf('%s-B%03d',settlement_id,zone_ordinal) zone_id "
    "FROM settlement_poi_core GROUP BY settlement_id,zone_ordinal "
    "ORDER BY COUNT(*) DESC,settlement_id,zone_ordinal LIMIT 1"
  ).fetchone()
  checks["query_performance_ms"] = {
    "largest_county_villages": query_elapsed_ms(
      connection,
      "SELECT * FROM v_county_entry_villages WHERE county_id=? ORDER BY village_id",
      (largest_village_county,),
    ),
    "largest_settlement_zones": query_elapsed_ms(
      connection,
      "SELECT * FROM v_settlement_entry_zones WHERE settlement_id=? ORDER BY zone_id",
      (largest_zone_settlement,),
    ),
    "explicit_zone_with_parent": query_elapsed_ms(
      connection,
      "SELECT * FROM v_settlement_entry_zones WHERE settlement_id=? AND zone_id=?",
      (explicit_zone["settlement_id"], explicit_zone["zone_id"]),
    ),
    "largest_division_settlements": query_elapsed_ms(
      connection,
      "SELECT * FROM v_local_division_entry_settlements "
      "WHERE division_id=? ORDER BY settlement_id",
      (largest_division,),
    ),
    "county_occupations": query_elapsed_ms(
      connection,
      "SELECT * FROM county_occupation_quota "
      "WHERE county_id=? ORDER BY occupation_code",
      (largest_village_county,),
    ),
    "explicit_zone_pois_with_parent": query_elapsed_ms(
      connection,
      "SELECT * FROM v_zone_entry_pois "
      "WHERE settlement_id=? AND zone_id=? ORDER BY poi_id",
      (busiest_poi_zone["settlement_id"], busiest_poi_zone["zone_id"]),
    ),
  }
  checks["query_performance_under_250ms"] = all(
    value < 250 for value in checks["query_performance_ms"].values()
  )
  connection.execute("DETACH DATABASE source_v07")

  expected = {
    "source_database_unchanged": True,
    "user_version": 8,
    "settlement_rows": EXPECTED_SETTLEMENTS,
    "village_extension_rows": EXPECTED_VILLAGES,
    "multi_zone_rows_stored": EXPECTED_STORED_MULTI_ZONES,
    "multi_zone_settlements_stored": EXPECTED_MULTI_ZONE_SETTLEMENTS,
    "virtual_zone_rows": EXPECTED_VIRTUAL_ZONES,
    "virtual_single_zone_rows": EXPECTED_SINGLE_ZONES,
    "poi_rows": EXPECTED_POIS,
    "division_rows": EXPECTED_DIVISIONS,
    "local_membership_rows": EXPECTED_SETTLEMENTS,
    "occupation_quota_rows": EXPECTED_OCCUPATION_QUOTAS,
    "settlement_sector_quota_rows": EXPECTED_SETTLEMENTS,
    "compatibility_column_mismatch_count": 0,
    "zone_non_render_difference_count": 0,
    "multi_zone_full_difference_count": 0,
    "settlement_node_difference_count": 0,
    "local_division_definition_difference_count": 0,
    "historical_person_difference_count": 0,
    "zone_population_or_labor_mismatch_count": 0,
    "poi_virtual_zone_orphan_count": 0,
    "foreign_key_check_count": 0,
    "integrity_check": "ok",
    "query_performance_under_250ms": True,
  }
  errors = [
    f"{key}: expected {value}, got {checks.get(key)}"
    for key, value in expected.items()
    if checks.get(key) != value
  ]
  for name, differences in checks["exact_compatibility_differences"].items():
    if differences:
      errors.append(f"{name}: {differences} compatibility row differences")
  for name, duplicates in checks["key_id_duplicate_counts"].items():
    if duplicates:
      errors.append(f"{name}: {duplicates} duplicate stable IDs")
  if any(value != "view" for value in checks["compatibility_object_types"].values()):
    errors.append("one or more compact public objects are not compatibility views")
  return {
    "status": "pass" if not errors else "fail",
    "ruleset_version": RULESET_VERSION,
    "errors": errors,
    "checks": checks,
  }


def object_size_rows(
  database: Path,
  version: str,
) -> list[dict[str, Any]]:
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


def main() -> None:
  parser = argparse.ArgumentParser(
    description="Build compact Ming 1628 runtime world database v0.8",
  )
  parser.add_argument(
    "--source-database", type=Path, default=DEFAULT_SOURCE_DATABASE,
  )
  parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT_DIR)
  args = parser.parse_args()
  source_database = args.source_database.resolve()
  output_dir = args.output_dir.resolve()
  if not source_database.exists():
    raise SystemExit(f"Missing v0.7 SQLite database: {source_database}")
  output_dir.mkdir(parents=True, exist_ok=True)
  output_database = output_dir / OUTPUT_DATABASE_NAME
  temporary_database = output_database.with_suffix(output_database.suffix + ".tmp")
  report_path = output_dir / "world_compaction_v0.8_validation_report.json"
  size_path = output_dir / "world_compaction_v0.8_object_sizes.csv"
  previous_report: dict[str, Any] = {}
  if report_path.exists():
    try:
      previous_report = json.loads(report_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
      previous_report = {}

  source_hash_before = file_sha256(source_database)
  source = sqlite3.connect(f"file:{source_database}?mode=ro", uri=True)
  try:
    if source.execute("PRAGMA user_version").fetchone()[0] != 7:
      raise RuntimeError("v0.8 compaction requires source SQLite user_version=7")
    for name in EXACT_COMPATIBILITY_OBJECTS + ["settlement_zone"]:
      row = source.execute(
        "SELECT type FROM sqlite_schema WHERE name=?", (name,),
      ).fetchone()
      if not row or row[0] != "table":
        raise RuntimeError(f"v0.7 source object is not a table: {name}")
  finally:
    source.close()

  if temporary_database.exists():
    temporary_database.unlink()
  print("[v0.8] copying immutable v0.7 source", flush=True)
  shutil.copy2(source_database, temporary_database)
  connection = sqlite3.connect(temporary_database)
  connection.row_factory = sqlite3.Row
  try:
    print("[v0.8] normalizing duplicated runtime storage", flush=True)
    transform_database(connection)
    validation = validate_database(
      connection, source_database, source_hash_before,
    )
    if validation["status"] != "pass":
      raise RuntimeError("v0.8 validation failed: " + "; ".join(validation["errors"]))
    print("[v0.8] vacuuming compact database", flush=True)
    connection.execute("VACUUM")
  finally:
    connection.close()

  compact_bytes = temporary_database.stat().st_size
  source_bytes = source_database.stat().st_size
  saved_bytes = source_bytes - compact_bytes
  saved_percent = saved_bytes * 100.0 / source_bytes
  validation["size"] = {
    "source_bytes": source_bytes,
    "compact_bytes": compact_bytes,
    "saved_bytes": saved_bytes,
    "source_mib": round(source_bytes / 1024 / 1024, 3),
    "compact_mib": round(compact_bytes / 1024 / 1024, 3),
    "saved_mib": round(saved_bytes / 1024 / 1024, 3),
    "saved_percent": round(saved_percent, 3),
    "maximum_compact_mib": MAX_DATABASE_BYTES / 1024 / 1024,
    "minimum_saved_percent": MIN_SAVING_PERCENT,
  }
  if compact_bytes > MAX_DATABASE_BYTES:
    validation["errors"].append(
      f"compact database exceeds 470 MiB: {compact_bytes / 1024 / 1024:.3f} MiB"
    )
  if saved_percent < MIN_SAVING_PERCENT:
    validation["errors"].append(
      f"database saving below {MIN_SAVING_PERCENT:.1f}%: {saved_percent:.3f}%"
    )

  final_check = sqlite3.connect(
    f"file:{temporary_database}?mode=ro", uri=True,
  )
  try:
    validation["checks"]["post_vacuum_integrity_check"] = final_check.execute(
      "PRAGMA integrity_check"
    ).fetchone()[0]
    validation["checks"]["post_vacuum_user_version"] = final_check.execute(
      "PRAGMA user_version"
    ).fetchone()[0]
  finally:
    final_check.close()
  if validation["checks"]["post_vacuum_integrity_check"] != "ok":
    validation["errors"].append("post-VACUUM integrity check failed")
  if validation["checks"]["post_vacuum_user_version"] != 8:
    validation["errors"].append("post-VACUUM user_version is not 8")
  if file_sha256(source_database) != source_hash_before:
    validation["errors"].append("v0.7 source database changed during compaction")

  size_rows = [
    *object_size_rows(source_database, "v0.7"),
    *object_size_rows(temporary_database, "v0.8"),
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
    validation["errors"].append("repeat build output SHA-256 mismatch")
  validation["status"] = "pass" if not validation["errors"] else "fail"
  write_json_atomic(report_path, validation)
  if validation["status"] != "pass":
    raise RuntimeError("v0.8 final validation failed: " + "; ".join(validation["errors"]))
  temporary_database.replace(output_database)
  print(json.dumps({
    "status": "pass",
    "source_mib": validation["size"]["source_mib"],
    "compact_mib": validation["size"]["compact_mib"],
    "saved_percent": validation["size"]["saved_percent"],
    "database": str(output_database),
    "validation": str(report_path),
  }, ensure_ascii=False, indent=2), flush=True)


if __name__ == "__main__":
  main()
