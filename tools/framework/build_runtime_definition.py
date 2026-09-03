#!/usr/bin/env python3
"""Build the immutable Project Realm development Definition database.

The historical source is always opened read-only and is never modified. The output is a
rebuildable Unity development asset and is explicitly blocked from commercial releases.
"""

from __future__ import annotations

import argparse
import hashlib
import os
import re
import sqlite3
import sys
import tempfile
from pathlib import Path
from typing import Iterable, Sequence


REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SOURCE = REPO_ROOT / (
    "docs/90_资料与归档/01_崇祯元年历史资料/data/1628/"
    "13.模拟基础规则/game_world_1628_v1.0.sqlite"
)
DEFAULT_OUTPUT = REPO_ROOT / (
    "game/Assets/ProjectRealm/Content/Definitions/Development/Resources/"
    "realm_definition_ming1628_dev_v1.sqlite"
)
FRAMEWORK_DOCS = REPO_ROOT / "docs/01_游戏底层架构/01_世界模拟框架与规则"
RUNTIME_DOCS = REPO_ROOT / "docs/01_游戏底层架构/02_世界运行设计"
EXPECTED_SOURCE_SHA256 = "4ce8e6e076b5736e690fb2cc82df4e35e69ae295b20baff84b58aa551efbcb42"
EXPECTED_SOURCE_USER_VERSION = 10
OUTPUT_USER_VERSION = 1
OUTPUT_SIZE_LIMIT = 16 * 1024 * 1024
WORLD_ID = "MING1628"
SAMPLE_COUNTY_ID = "MING1628-0205"
SAMPLE_DIVISION_ID = "MING1628-0205-LD033"
SAMPLE_SETTLEMENT_ID = "MING1628-0205-V2080"

COPY_TABLE_COUNTS = {
    "county_economy_baseline": 1168,
    "county_geography_resources": 1168,
    "county_social_structure_baseline": 1168,
    "county_education_profile": 1168,
    "county_culture_education_baseline": 1168,
    "region_summary": 15,
    "occupation_definition": 150,
    "education_definition": 33,
    "social_status_definition": 35,
    "terrain_resource_rules": 47,
    "weather_event_rules": 12,
    "administrative_fiscal_rule": 18,
    "agriculture_rule": 25,
    "industry_commerce_transport_rule": 58,
    "military_rule": 20,
    "simulation_rule": 26,
}

FORBIDDEN_TABLES = {
    "settlement_node",
    "local_division_definition",
    "county_subregion_definition",
    "village_extension",
    "historical_person_catalog",
    "historical_family_lineage",
    "historical_person_relationship",
    "person_county_association",
    "person_family_membership",
    "person_group_membership",
    "settlement_poi_core",
    "settlement_multi_zone_core",
    "mineral_deposit_definition",
}

GENERIC_OR_LEGACY_MODULES = {"DomainModule", "SolverModule", "PersonModule", "TaxModule"}
AGGREGATE_COUNTY_MODULES = {
    "AdministrationModule",
    "AgricultureModule",
    "EnvironmentalRiskModule",
    "FiscalModule",
    "MarketModule",
    "MilitaryOrganizationModule",
    "ObservationModule",
    "PopulationModule",
    "PublicOrderModule",
    "SocietyAggregationModule",
    "TransportModule",
    "TreasuryModule",
}
PERCEPTION_MODULES = {
    "CommunicationModule",
    "DeceptionModule",
    "IntelligenceModule",
    "KnowledgeViewModule",
    "MarketInformationModule",
    "ObservationModule",
    "ReportModule",
    "VisibilityPolicyModule",
}
DECISION_MODULES = {
    "CohortBehaviorDecisionModule",
    "CollectiveActionFormationModule",
    "DecisionExplanationModule",
    "GovernmentDecisionModule",
    "GovernmentPlanningModule",
    "HouseholdDecisionModule",
    "OrganizationDecisionModule",
    "PersonDecisionModule",
    "SocialInfluenceModule",
}
AGGREGATION_MODULES = {
    "CohortSocietyProfileModule",
    "FiscalModule",
    "SocietyAggregationModule",
    "TaxAnalyticsModule",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--validate-only", action="store_true")
    return parser.parse_args()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def open_readonly(path: Path) -> sqlite3.Connection:
    connection = sqlite3.connect(f"file:{path.resolve()}?mode=ro&immutable=1", uri=True)
    connection.row_factory = sqlite3.Row
    connection.execute("PRAGMA query_only=ON")
    connection.execute("PRAGMA trusted_schema=OFF")
    connection.execute("PRAGMA foreign_keys=ON")
    return connection


def validate_source(path: Path) -> sqlite3.Connection:
    if not path.is_file():
        raise SystemExit(f"Definition source database is missing: {path}")
    source_hash = sha256_file(path)
    if source_hash != EXPECTED_SOURCE_SHA256:
        raise SystemExit(
            f"Definition source SHA-256 mismatch: expected {EXPECTED_SOURCE_SHA256}, got {source_hash}"
        )
    connection = open_readonly(path)
    if connection.execute("PRAGMA user_version").fetchone()[0] != EXPECTED_SOURCE_USER_VERSION:
        connection.close()
        raise SystemExit(f"Definition source user_version must be {EXPECTED_SOURCE_USER_VERSION}")
    if connection.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
        connection.close()
        raise SystemExit("Definition source integrity_check failed")
    for table, expected_count in COPY_TABLE_COUNTS.items():
        actual = connection.execute(f'SELECT COUNT(*) FROM "{table}"').fetchone()[0]
        if actual != expected_count:
            connection.close()
            raise SystemExit(f"Source table {table} expected {expected_count} rows, found {actual}")
    return connection


def authoritative_module_names() -> list[str]:
    names: set[str] = set()
    for root in (FRAMEWORK_DOCS, RUNTIME_DOCS):
        for path in root.rglob("*.md"):
            if "参考资料" in path.parts or "归档" in path.parts:
                continue
            names.update(re.findall(r"`([A-Z][A-Za-z0-9]*Module)`", path.read_text(encoding="utf-8")))
    canonical = sorted(names - GENERIC_OR_LEGACY_MODULES)
    if len(canonical) != 101:
        raise SystemExit(f"Expected 101 canonical modules from authoritative docs, found {len(canonical)}")
    return canonical


def kebab_module_name(source_name: str) -> str:
    stem = source_name[: -len("Module")]
    return re.sub(r"(?<!^)(?=[A-Z])", "-", stem).lower()


def module_id(source_name: str) -> str:
    return f"module.{kebab_module_name(source_name)}.v1"


def module_stage(source_name: str) -> int:
    if source_name in PERCEPTION_MODULES:
        return 60
    if source_name in DECISION_MODULES:
        return 70
    if source_name in AGGREGATION_MODULES:
        return 40
    if source_name == "WorldEventModule":
        return 110
    return 30


def stable_label_id(prefix: str, *labels: str) -> str:
    material = "\x1f".join(labels).encode("utf-8")
    return f"{prefix}.{hashlib.sha256(material).hexdigest()[:16]}"


def quote_identifier(value: str) -> str:
    return '"' + value.replace('"', '""') + '"'


def table_order_columns(source: sqlite3.Connection, table: str) -> list[str]:
    columns = list(source.execute(f"PRAGMA table_info({quote_identifier(table)})"))
    primary = [row[1] for row in sorted((row for row in columns if row[5]), key=lambda row: row[5])]
    return primary or [row[1] for row in columns]


def copy_source_table(source: sqlite3.Connection, output: sqlite3.Connection, table: str) -> None:
    schema_row = source.execute(
        "SELECT sql FROM sqlite_master WHERE type='table' AND name=?", (table,)
    ).fetchone()
    if schema_row is None or not schema_row[0]:
        raise SystemExit(f"Source table schema is missing: {table}")
    output.execute(schema_row[0])
    columns = [row[1] for row in source.execute(f"PRAGMA table_info({quote_identifier(table)})")]
    placeholders = ",".join("?" for _ in columns)
    column_sql = ",".join(quote_identifier(column) for column in columns)
    order_sql = ",".join(quote_identifier(column) for column in table_order_columns(source, table))
    insert_sql = f"INSERT INTO {quote_identifier(table)} ({column_sql}) VALUES ({placeholders})"
    cursor = source.execute(f"SELECT {column_sql} FROM {quote_identifier(table)} ORDER BY {order_sql}")
    while True:
        rows = cursor.fetchmany(512)
        if not rows:
            break
        output.executemany(insert_sql, (tuple(row) for row in rows))


def create_runtime_schema(output: sqlite3.Connection) -> None:
    output.executescript(
        """
        CREATE TABLE definition_manifest (
            world_id TEXT PRIMARY KEY,
            ruleset_version TEXT NOT NULL,
            module_catalog_version TEXT NOT NULL,
            state_schema_version TEXT NOT NULL,
            source_database_sha256 TEXT NOT NULL,
            source_user_version INTEGER NOT NULL,
            content_hash TEXT NOT NULL,
            initialization_algorithm_version TEXT NOT NULL,
            random_algorithm_version TEXT NOT NULL,
            calendar_definition_id TEXT NOT NULL,
            validation_profile TEXT NOT NULL,
            commercial_release_ready TEXT NOT NULL CHECK(commercial_release_ready='no')
        ) WITHOUT ROWID;
        CREATE TABLE calendar_definition (
            calendar_definition_id TEXT PRIMARY KEY,
            month_count INTEGER NOT NULL,
            days_per_month INTEGER NOT NULL,
            historical_calendar_claim TEXT NOT NULL
        ) WITHOUT ROWID;
        CREATE TABLE simulation_node (
            node_id TEXT PRIMARY KEY,
            node_kind TEXT NOT NULL,
            display_name TEXT NOT NULL,
            geographic_parent_id TEXT NOT NULL,
            historical_claim TEXT NOT NULL CHECK(historical_claim IN ('yes','no'))
        ) WITHOUT ROWID;
        CREATE TABLE faction_node (
            faction_id TEXT PRIMARY KEY,
            display_name TEXT NOT NULL,
            historical_claim TEXT NOT NULL CHECK(historical_claim IN ('yes','no'))
        ) WITHOUT ROWID;
        CREATE TABLE jurisdiction_relation (
            jurisdiction_id TEXT PRIMARY KEY,
            faction_id TEXT NOT NULL,
            region_id TEXT NOT NULL,
            authority_kind TEXT NOT NULL,
            historical_claim TEXT NOT NULL CHECK(historical_claim IN ('yes','no')),
            FOREIGN KEY(faction_id) REFERENCES faction_node(faction_id),
            FOREIGN KEY(region_id) REFERENCES simulation_node(node_id)
        ) WITHOUT ROWID;
        CREATE TABLE settlement_owner (
            settlement_id TEXT PRIMARY KEY,
            owner_id TEXT NOT NULL,
            ownership_kind TEXT NOT NULL,
            FOREIGN KEY(settlement_id) REFERENCES simulation_node(node_id)
        ) WITHOUT ROWID;
        CREATE TABLE module_definition (
            definition_id TEXT PRIMARY KEY,
            source_name TEXT NOT NULL UNIQUE,
            implementation_version TEXT NOT NULL,
            implementation_tier TEXT NOT NULL,
            source_group TEXT NOT NULL
        ) WITHOUT ROWID;
        CREATE TABLE module_capability (
            definition_id TEXT NOT NULL,
            capability_id TEXT NOT NULL,
            authority_key TEXT NOT NULL,
            authority_mode TEXT NOT NULL,
            required INTEGER NOT NULL CHECK(required IN (0,1)),
            PRIMARY KEY(definition_id, capability_id),
            FOREIGN KEY(definition_id) REFERENCES module_definition(definition_id)
        ) WITHOUT ROWID;
        CREATE TABLE module_stage (
            definition_id TEXT NOT NULL,
            stage INTEGER NOT NULL,
            PRIMARY KEY(definition_id, stage),
            FOREIGN KEY(definition_id) REFERENCES module_definition(definition_id)
        ) WITHOUT ROWID;
        CREATE TABLE module_compatibility_alias (
            alias_name TEXT PRIMARY KEY,
            canonical_definition_id TEXT NOT NULL,
            authoritative_provider INTEGER NOT NULL CHECK(authoritative_provider=0),
            FOREIGN KEY(canonical_definition_id) REFERENCES module_definition(definition_id)
        ) WITHOUT ROWID;
        CREATE TABLE node_module_composition (
            node_id TEXT NOT NULL,
            definition_id TEXT NOT NULL,
            composition_profile TEXT NOT NULL,
            PRIMARY KEY(node_id, definition_id),
            FOREIGN KEY(node_id) REFERENCES simulation_node(node_id),
            FOREIGN KEY(definition_id) REFERENCES module_definition(definition_id)
        ) WITHOUT ROWID;
        CREATE TABLE development_source_reference (
            reference_id TEXT PRIMARY KEY,
            source_table TEXT NOT NULL,
            source_row_id TEXT NOT NULL,
            display_name TEXT NOT NULL,
            historical_claim TEXT NOT NULL,
            commercial_release_ready TEXT NOT NULL
        ) WITHOUT ROWID;
        """
    )


def logical_content_hash(source: sqlite3.Connection, module_names: Sequence[str]) -> str:
    digest = hashlib.sha256()
    digest.update(EXPECTED_SOURCE_SHA256.encode("ascii"))
    digest.update(b"framework-definition-v1\0")
    for table in sorted(COPY_TABLE_COUNTS):
        digest.update(table.encode("utf-8") + b"\0")
        columns = [row[1] for row in source.execute(f"PRAGMA table_info({quote_identifier(table)})")]
        column_sql = ",".join(quote_identifier(column) for column in columns)
        order_sql = ",".join(quote_identifier(column) for column in table_order_columns(source, table))
        for row in source.execute(f"SELECT {column_sql} FROM {quote_identifier(table)} ORDER BY {order_sql}"):
            for value in row:
                encoded = ("<null>" if value is None else str(value)).encode("utf-8")
                digest.update(len(encoded).to_bytes(4, "little"))
                digest.update(encoded)
    for name in module_names:
        digest.update(name.encode("ascii") + b"\0")
    return digest.hexdigest()


def install_topology(source: sqlite3.Connection, output: sqlite3.Connection) -> list[str]:
    county_rows = list(
        source.execute(
            "SELECT county_id, region, upper_unit, intermediate_unit, county "
            "FROM county_economy_baseline ORDER BY county_id"
        )
    )
    output.execute(
        "INSERT INTO simulation_node VALUES (?,?,?,?,?)",
        (WORLD_ID, "World", "崇祯元年开发世界", "", "no"),
    )
    inserted = {WORLD_ID}
    county_ids: list[str] = []
    for row in county_rows:
        region_id = stable_label_id("region", row[1])
        if region_id not in inserted:
            output.execute(
                "INSERT INTO simulation_node VALUES (?,?,?,?,?)",
                (region_id, "Region", row[1], WORLD_ID, "no"),
            )
            inserted.add(region_id)
        parent_id = region_id
        if row[2]:
            upper_id = stable_label_id("admin-upper", row[1], row[2])
            if upper_id not in inserted:
                output.execute(
                    "INSERT INTO simulation_node VALUES (?,?,?,?,?)",
                    (upper_id, "Region", row[2], region_id, "no"),
                )
                inserted.add(upper_id)
            parent_id = upper_id
        if row[3]:
            intermediate_id = stable_label_id("admin-intermediate", row[1], row[2], row[3])
            if intermediate_id not in inserted:
                output.execute(
                    "INSERT INTO simulation_node VALUES (?,?,?,?,?)",
                    (intermediate_id, "Region", row[3], parent_id, "no"),
                )
                inserted.add(intermediate_id)
            parent_id = intermediate_id
        output.execute(
            "INSERT INTO simulation_node VALUES (?,?,?,?,?)",
            (row[0], "County", row[4], parent_id, "no"),
        )
        inserted.add(row[0])
        county_ids.append(row[0])

    division = source.execute(
        "SELECT division_id, division_name, historical_name_claim, commercial_release_ready "
        "FROM local_division_definition WHERE division_id=?",
        (SAMPLE_DIVISION_ID,),
    ).fetchone()
    settlement = source.execute(
        "SELECT settlement_id, settlement_name, historical_name_claim, commercial_release_ready "
        "FROM settlement_node WHERE settlement_id=?",
        (SAMPLE_SETTLEMENT_ID,),
    ).fetchone()
    if division is None or settlement is None:
        raise SystemExit("The fixed sample division or village is missing from the source database")
    output.execute(
        "INSERT INTO simulation_node VALUES (?,?,?,?,?)",
        (division[0], "LocalDivision", division[1], SAMPLE_COUNTY_ID, "yes" if division[2] == "yes" else "no"),
    )
    output.execute(
        "INSERT INTO simulation_node VALUES (?,?,?,?,?)",
        (settlement[0], "Settlement", settlement[1], SAMPLE_DIVISION_ID, "yes" if settlement[2] == "yes" else "no"),
    )
    output.executemany(
        "INSERT INTO development_source_reference VALUES (?,?,?,?,?,?)",
        [
            ("sample-division", "local_division_definition", division[0], division[1], division[2], division[3]),
            ("sample-settlement", "settlement_node", settlement[0], settlement[1], settlement[2], settlement[3]),
        ],
    )

    faction_id = "faction.ming.dev"
    output.execute("INSERT INTO faction_node VALUES (?,?,?)", (faction_id, "大明开发占位势力", "no"))
    output.executemany(
        "INSERT INTO jurisdiction_relation VALUES (?,?,?,?,?)",
        [
            (f"jurisdiction.ming.dev.{county_id}", faction_id, county_id, "development-only", "no")
            for county_id in county_ids
        ],
    )
    output.execute(
        "INSERT INTO settlement_owner VALUES (?,?,?)",
        (SAMPLE_SETTLEMENT_ID, "owner.development.unassigned", "development-placeholder"),
    )
    return county_ids


def install_modules(output: sqlite3.Connection, module_names: Sequence[str], county_ids: Sequence[str]) -> None:
    for source_name in module_names:
        stem = kebab_module_name(source_name)
        definition_id = module_id(source_name)
        output.execute(
            "INSERT INTO module_definition VALUES (?,?,?,?,?)",
            (definition_id, source_name, "scaffold-v1", "Scaffold", "authoritative-docs"),
        )
        output.execute(
            "INSERT INTO module_capability VALUES (?,?,?,?,?)",
            (
                definition_id,
                f"capability.{stem}.scaffold.v1",
                f"authority.{stem}",
                "Authoritative",
                0,
            ),
        )
        output.execute("INSERT INTO module_stage VALUES (?,?)", (definition_id, module_stage(source_name)))
    output.executemany(
        "INSERT INTO module_compatibility_alias VALUES (?,?,0)",
        [("PersonModule", module_id("PersonIdentityModule")), ("TaxModule", module_id("TaxPolicyModule"))],
    )

    full_nodes = {SAMPLE_COUNTY_ID, SAMPLE_DIVISION_ID, SAMPLE_SETTLEMENT_ID}
    compositions: list[tuple[str, str, str]] = []
    for county_id in county_ids:
        selected = module_names if county_id in full_nodes else sorted(AGGREGATE_COUNTY_MODULES)
        profile = "sample-full-v1" if county_id in full_nodes else "county-aggregate-v1"
        compositions.extend((county_id, module_id(name), profile) for name in selected)
    for node_id in (SAMPLE_DIVISION_ID, SAMPLE_SETTLEMENT_ID):
        compositions.extend((node_id, module_id(name), "sample-full-v1") for name in module_names)
    output.executemany("INSERT INTO node_module_composition VALUES (?,?,?)", sorted(compositions))


def validate_output(path: Path) -> None:
    if not path.is_file():
        raise SystemExit(f"Generated Definition database is missing: {path}")
    if path.stat().st_size > OUTPUT_SIZE_LIMIT:
        raise SystemExit(
            f"Generated Definition database exceeds 16 MiB: {path.stat().st_size} bytes"
        )
    connection = open_readonly(path)
    try:
        if connection.execute("PRAGMA user_version").fetchone()[0] != OUTPUT_USER_VERSION:
            raise SystemExit("Generated Definition user_version mismatch")
        if connection.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
            raise SystemExit("Generated Definition integrity_check failed")
        foreign_key_errors = list(connection.execute("PRAGMA foreign_key_check"))
        if foreign_key_errors:
            raise SystemExit(f"Generated Definition foreign_key_check failed: {foreign_key_errors[:3]}")
        actual_tables = {
            row[0]
            for row in connection.execute(
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"
            )
        }
        forbidden = sorted(actual_tables & FORBIDDEN_TABLES)
        if forbidden:
            raise SystemExit(f"Forbidden high-resolution tables leaked into Definition output: {forbidden}")
        for table, expected_count in COPY_TABLE_COUNTS.items():
            actual = connection.execute(f"SELECT COUNT(*) FROM {quote_identifier(table)}").fetchone()[0]
            if actual != expected_count:
                raise SystemExit(f"Generated table {table} expected {expected_count} rows, found {actual}")
        if connection.execute("SELECT COUNT(*) FROM module_definition").fetchone()[0] != 101:
            raise SystemExit("Generated module catalog is incomplete")
        if connection.execute(
            "SELECT COUNT(*) FROM node_module_composition WHERE node_id=?", (SAMPLE_SETTLEMENT_ID,)
        ).fetchone()[0] != 101:
            raise SystemExit("The sample village does not instantiate every canonical module")
        for table in COPY_TABLE_COUNTS:
            columns = {row[1] for row in connection.execute(f"PRAGMA table_info({quote_identifier(table)})")}
            if "commercial_release_ready" in columns:
                ready = connection.execute(
                    f"SELECT COUNT(*) FROM {quote_identifier(table)} "
                    "WHERE lower(commercial_release_ready)='yes'"
                ).fetchone()[0]
                if ready:
                    raise SystemExit(f"Commercial-ready rows unexpectedly present in development table {table}")
        manifest = connection.execute(
            "SELECT commercial_release_ready FROM definition_manifest WHERE world_id=?", (WORLD_ID,)
        ).fetchone()
        if manifest is None or manifest[0] != "no":
            raise SystemExit("Development Definition manifest must block commercial release")
    finally:
        connection.close()


def build(source_path: Path, output_path: Path) -> None:
    source = validate_source(source_path)
    module_names = authoritative_module_names()
    content_hash = logical_content_hash(source, module_names)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        prefix=output_path.name + ".", suffix=".tmp", dir=output_path.parent
    )
    os.close(descriptor)
    temporary = Path(temporary_name)
    temporary.unlink()
    try:
        output = sqlite3.connect(temporary)
        try:
            output.execute("PRAGMA page_size=4096")
            output.execute("PRAGMA journal_mode=DELETE")
            output.execute("PRAGMA synchronous=FULL")
            output.execute("PRAGMA foreign_keys=OFF")
            for table in COPY_TABLE_COUNTS:
                copy_source_table(source, output, table)
            create_runtime_schema(output)
            county_ids = install_topology(source, output)
            install_modules(output, module_names, county_ids)
            output.execute(
                "INSERT INTO calendar_definition VALUES (?,?,?,?)",
                ("calendar.economic-12x30.v1", 12, 30, "no"),
            )
            output.execute(
                "INSERT INTO definition_manifest VALUES (?,?,?,?,?,?,?,?,?,?,?,?)",
                (
                    WORLD_ID,
                    "framework-ruleset-v1",
                    "framework-module-catalog-v1",
                    "save-schema-v1",
                    EXPECTED_SOURCE_SHA256,
                    EXPECTED_SOURCE_USER_VERSION,
                    content_hash,
                    "framework-empty-v1",
                    "pcg32-v1",
                    "calendar.economic-12x30.v1",
                    "development-scaffold",
                    "no",
                ),
            )
            output.execute(f"PRAGMA user_version={OUTPUT_USER_VERSION}")
            output.commit()
            output.execute("PRAGMA foreign_keys=ON")
            foreign_key_errors = list(output.execute("PRAGMA foreign_key_check"))
            if foreign_key_errors:
                raise SystemExit(f"Definition foreign_key_check failed before VACUUM: {foreign_key_errors[:3]}")
            output.execute("VACUUM")
        finally:
            output.close()
            source.close()
        os.replace(temporary, output_path)
        validate_output(output_path)
        print(f"Built {output_path}")
        print(f"bytes={output_path.stat().st_size}")
        print(f"logical_content_sha256={content_hash}")
        print(f"file_sha256={sha256_file(output_path)}")
    finally:
        if temporary.exists():
            temporary.unlink()


def main() -> int:
    args = parse_args()
    if args.validate_only:
        validate_output(args.output)
        print(f"Validated {args.output}")
        print(f"bytes={args.output.stat().st_size}")
        print(f"file_sha256={sha256_file(args.output)}")
        return 0
    build(args.source, args.output)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except sqlite3.DatabaseError as error:
        print(f"SQLite error: {error}", file=sys.stderr)
        raise SystemExit(1)
