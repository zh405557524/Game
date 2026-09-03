#!/usr/bin/env python3
"""Build Project Realm's deterministic county mineral/economy baseline v0.2.

The model deliberately separates three layers:

* geological evidence and inferred deposits;
* county-level resource potential and accessibility;
* 1628 industrial/commercial development state.

It never treats MRDS record quality as ore grade and does not invent tonnage.
All generated indices are deterministic integers in the 0--100 range.  The
CHGIS-derived county-seat coordinates in the v0.1 input remain non-commercial.
"""

from __future__ import annotations

import argparse
import bisect
import csv
import hashlib
import json
import math
import sqlite3
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path
from statistics import median
from typing import Any, Iterable, Sequence

from pyogrio.raw import read as read_vector
from shapely import Point, from_wkb
from shapely.ops import nearest_points
from shapely.strtree import STRtree


SNAPSHOT_YEAR = 1628
POPULATION_BASELINE = 209_249_000
DEFAULT_DATA_ROOT = Path("docs/90_资料与归档/01_崇祯元年历史资料/data/1628")
DEFAULT_GEOGRAPHY_DIR = DEFAULT_DATA_ROOT / "4.地理地貌资源与天气"
DEFAULT_DATA_DIR = DEFAULT_DATA_ROOT / "5.县级人口矿产与产业商业"
DEFAULT_USGS_DIR = Path("tmp/research/usgs_china_minerals")
USGS_CHINA_GIS_URL = (
    "https://www.usgs.gov/data/compilation-geospatial-data-gis-mineral-"
    "industries-and-related-infrastructure-peoples-republic"
)
USGS_MRDS_URL = "https://mrdata.usgs.gov/mrds/"
MING_MINING_STUDY_URL = (
    "https://www.researchgate.net/publication/350620302_A_quantitative_analysis_of_records_"
    "on_silver_copper_iron_and_leadzinc_mines_in_the_Ming_and_Qing_Veritable_Records_"
    "Shilu_shilu"
)
USGS_PUBLIC_DOMAIN = "USGS public domain / CC0 data release"
NONCOMMERCIAL_MARK = "no - replace or license CHGIS-derived county coordinates"


@dataclass(frozen=True)
class Resource:
    code: str
    name: str


RESOURCES = [
    Resource("coal", "煤"),
    Resource("iron", "铁"),
    Resource("copper", "铜"),
    Resource("lead", "铅"),
    Resource("tin", "锡"),
    Resource("zinc", "锌"),
    Resource("silver", "银"),
    Resource("gold", "金"),
    Resource("mercury", "汞/朱砂"),
    Resource("sulfur", "硫磺"),
    Resource("saltpeter", "硝源"),
    Resource("sea_salt", "海盐"),
    Resource("well_salt", "井盐"),
    Resource("lake_salt", "池盐"),
    Resource("rock_brine_salt", "岩盐/卤水"),
    Resource("kaolin", "瓷土"),
    Resource("common_clay", "普通陶土"),
    Resource("limestone", "石灰石"),
    Resource("gypsum", "石膏"),
    Resource("building_stone", "建筑石材"),
    Resource("alum", "明矾"),
    Resource("quartz_sand", "石英砂"),
]
RESOURCE_BY_CODE = {item.code: item for item in RESOURCES}
RESOURCE_ORDER = {item.code: index for index, item in enumerate(RESOURCES)}

QUALITY_WEIGHTS = {"A": 100, "B": 90, "C": 75, "D": 55, "E": 35}
STATUS_WEIGHTS = {
    "Producer": 100,
    "Past Producer": 100,
    "Prospect": 85,
    "exploration": 85,
    "Occurrence": 70,
    "mineralized_occurrence": 70,
    "Unknown": 55,
    "synthetic_proxy": 55,
    "Plant": 0,
}

# MRDS commodity codes that existed in the pre-industrial resource system.
MRDS_CODE_MAP = {
    "COA": "coal",
    "FE": "iron",
    "CU": "copper",
    "PB": "lead",
    "SN": "tin",
    "ZN": "zinc",
    "AG": "silver",
    "AU": "gold",
    "HG": "mercury",
    "S": "sulfur",
    "S_P": "sulfur",
    "S_A": "sulfur",
    "CLY_K": "kaolin",
    "CLY3": "kaolin",
    "CLY": "common_clay",
    "LST": "limestone",
    "GYP": "gypsum",
    "STN": "building_stone",
    "STN2": "building_stone",
    "AL3": "alum",
    "SIL": "quartz_sand",
    "QTZ": "quartz_sand",
}

GDB_COMMODITY_MAP = {
    "coal": "coal",
    "iron": "iron",
    "copper": "copper",
    "lead": "lead",
    "tin": "tin",
    "zinc": "zinc",
    "silver": "silver",
    "gold": "gold",
    "mercury": "mercury",
    "sulfur": "sulfur",
    "halite": "rock_brine_salt",
    "clay (kaolin)": "kaolin",
    "clay": "common_clay",
    "silica sand": "quartz_sand",
}

DEPOSIT_COLUMNS = [
    "deposit_id",
    "parent_deposit_id",
    "county_id",
    "snapshot_year",
    "resource_code",
    "resource_name",
    "deposit_name",
    "longitude",
    "latitude",
    "evidence_distance_km",
    "match_radius_km",
    "source_type",
    "source_record_id",
    "source_reference",
    "source_license",
    "evidence_grade",
    "evidence_weight_0_100",
    "status",
    "status_weight_0_100",
    "distance_weight_0_100",
    "evidence_contribution_0_100",
    "size_index_0_100",
    "size_band",
    "ore_grade_index_0_100",
    "ore_grade_band",
    "burial_difficulty_0_100",
    "surface_accessibility_0_100",
    "base_extraction_capacity_index_0_100",
    "inference_method",
    "is_synthetic_proxy",
    "commercial_release_ready",
]

POTENTIAL_COLUMNS = [
    "county_id",
    "snapshot_year",
    "region",
    "upper_unit",
    "intermediate_unit",
    "county",
    "resource_code",
    "resource_name",
    "deposit_count",
    "physical_deposit_count",
    "synthetic_proxy_count",
    "potential_score_0_100",
    "surface_accessibility_0_100",
    "effective_industrial_value_0_100",
    "nearest_evidence_distance_km",
    "nearest_deposit_name",
    "evidence_source_types",
    "best_evidence_grade",
    "calculation_method",
    "traceability",
    "source_license",
    "commercial_release_ready",
]

SECTORS = [
    "mining_smelting",
    "textile",
    "ceramics",
    "salt_food",
    "forestry_paper",
    "shipbuilding",
    "arms",
    "building_materials",
]

ECONOMY_COLUMNS = [
    "county_id",
    "snapshot_year",
    "region",
    "upper_unit",
    "intermediate_unit",
    "county",
    "longitude",
    "latitude",
    "area_km2_est",
    "population_est_1628",
    "population_density_per_km2",
    "household_count_est",
    "labor_force_est",
    "urban_population_est",
    "urbanization_rate_0_100",
    "population_pressure_0_100",
    "population_estimation_method",
    "agriculture_resource_0_100",
    "forest_resource_0_100",
    "pasture_resource_0_100",
    "fishery_resource_0_100",
    "salt_resource_0_100",
    "fuel_resource_0_100",
    "metal_resource_0_100",
    "building_material_resource_0_100",
    "resource_diversity_0_100",
    "water_access_0_100",
    "transport_access_0_100",
    "labor_availability_0_100",
    "market_population_0_100",
    "administrative_centrality_0_100",
    "mining_smelting_potential_0_100",
    "mining_smelting_initial_1628_0_100",
    "textile_potential_0_100",
    "textile_initial_1628_0_100",
    "ceramics_potential_0_100",
    "ceramics_initial_1628_0_100",
    "salt_food_potential_0_100",
    "salt_food_initial_1628_0_100",
    "forestry_paper_potential_0_100",
    "forestry_paper_initial_1628_0_100",
    "shipbuilding_potential_0_100",
    "shipbuilding_initial_1628_0_100",
    "arms_potential_0_100",
    "arms_initial_1628_0_100",
    "building_materials_potential_0_100",
    "building_materials_initial_1628_0_100",
    "industrial_composite_potential_0_100",
    "industrial_initial_1628_0_100",
    "local_market_0_100",
    "long_distance_trade_0_100",
    "waterborne_trade_0_100",
    "commercial_potential_0_100",
    "commercial_prosperity_1628_0_100",
    "confirmed_disruption_penalty_0_100",
    "tax_base_potential_0_100",
    "grain_surplus_potential_0_100",
    "economic_resilience_0_100",
    "historical_anchor_count",
    "economy_method",
    "commercial_release_ready",
]

OVERVIEW_COLUMNS = [
    "county_id",
    "region",
    "upper_unit",
    "intermediate_unit",
    "county",
    "population_est_1628",
    "household_count_est",
    "labor_force_est",
    "population_density_per_km2",
    "urban_population_est",
    "urbanization_rate_0_100",
    "population_pressure_0_100",
    "top_minerals",
    "coal_potential_0_100",
    "iron_potential_0_100",
    "metal_resource_0_100",
    "agriculture_resource_0_100",
    "fuel_resource_0_100",
    "building_material_resource_0_100",
    "industrial_composite_potential_0_100",
    "industrial_initial_1628_0_100",
    "mining_smelting_initial_1628_0_100",
    "textile_initial_1628_0_100",
    "ceramics_initial_1628_0_100",
    "salt_food_initial_1628_0_100",
    "forestry_paper_initial_1628_0_100",
    "shipbuilding_initial_1628_0_100",
    "arms_initial_1628_0_100",
    "building_materials_initial_1628_0_100",
    "commercial_potential_0_100",
    "commercial_prosperity_1628_0_100",
    "local_market_0_100",
    "long_distance_trade_0_100",
    "waterborne_trade_0_100",
    "tax_base_potential_0_100",
    "grain_surplus_potential_0_100",
    "economic_resilience_0_100",
    "commercial_release_ready",
]

ANCHOR_COLUMNS = [
    "anchor_id",
    "anchor_name",
    "county_id",
    "anchor_type",
    "industry_sector",
    "sector_score_0_100",
    "commerce_score_0_100",
    "military_industry_score_0_100",
    "source_reference",
    "evidence_grade",
    "notes",
    "commercial_release_ready",
]


def clamp(value: float, low: float = 0, high: float = 100) -> float:
    return max(low, min(high, value))


def score(value: float) -> int:
    return int(round(clamp(value)))


def number(row: dict[str, str], key: str, default: float = 0.0) -> float:
    value = row.get(key, "")
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def deterministic_int(key: str, low: int, high: int) -> int:
    if low > high:
        low, high = high, low
    raw = int.from_bytes(hashlib.sha256(key.encode("utf-8")).digest()[:8], "big")
    return low + raw % (high - low + 1)


def haversine_km(lon1: float, lat1: float, lon2: float, lat2: float) -> float:
    radius = 6371.0088
    p1, p2 = math.radians(lat1), math.radians(lat2)
    dp = p2 - p1
    dl = math.radians(lon2 - lon1)
    a = math.sin(dp / 2) ** 2 + math.cos(p1) * math.cos(p2) * math.sin(dl / 2) ** 2
    return 2 * radius * math.asin(math.sqrt(a))


def matching_radius(county: dict[str, str]) -> float:
    return max(25.0, math.sqrt(max(1.0, number(county, "area_km2_est")) / math.pi))


def distance_weight(distance_km: float, radius_km: float) -> int:
    if distance_km <= radius_km:
        return 100
    if distance_km <= 2 * radius_km:
        return 75
    if distance_km <= 3 * radius_km:
        return 45
    return 0


def percentile_scores(values: Sequence[float]) -> list[int]:
    ordered = sorted(values)
    denominator = max(1, len(ordered) - 1)
    return [score(100 * (bisect.bisect_right(ordered, value) - 1) / denominator) for value in values]


def idx_1_5(value: float) -> int:
    return score((value - 1) * 25)


def weighted(parts: Iterable[tuple[float, float]]) -> int:
    return score(sum(value * weight for value, weight in parts))


def aggregate_probabilistic(values: Iterable[float]) -> int:
    remaining = 1.0
    for value in values:
        remaining *= 1.0 - clamp(value) / 100.0
    return score(100 * (1.0 - remaining))


def parse_codes(code_list: str) -> list[str]:
    result: list[str] = []
    for raw in code_list.replace(",", " ").replace(";", " ").split():
        mapped = MRDS_CODE_MAP.get(raw.strip().upper())
        if mapped and mapped not in result:
            result.append(mapped)
    return result


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open(encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream))


def write_csv(path: Path, columns: Sequence[str], rows: Sequence[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    # Match the existing v0.1 files and keep the first identifier free of a
    # UTF-8 BOM so game engines and generic CSV readers see the exact header.
    with path.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=columns, extrasaction="ignore")
        writer.writeheader()
        for row in rows:
            writer.writerow({column: row.get(column, "") for column in columns})


def find_gdb(usgs_dir: Path) -> Path:
    candidates = list((usgs_dir / "CHN_GIS_gdb").rglob("*.gdb"))
    if len(candidates) != 1:
        raise RuntimeError(f"Expected one USGS .gdb under {usgs_dir}, found {len(candidates)}")
    return candidates[0]


def nearest_county(
    counties: Sequence[dict[str, str]], lon: float, lat: float
) -> tuple[dict[str, str], float, float] | None:
    best: tuple[float, float, dict[str, str]] | None = None
    for county in counties:
        distance = haversine_km(lon, lat, number(county, "longitude"), number(county, "latitude"))
        radius = matching_radius(county)
        if distance > 3 * radius:
            continue
        candidate = (distance / radius, distance, county)
        if best is None or candidate[:2] < best[:2]:
            best = candidate
    if best is None:
        return None
    return best[2], best[1], matching_radius(best[2])


def surface_accessibility(county: dict[str, str], resource_code: str) -> int:
    transport = idx_1_5(number(county, "transport_index_1_5"))
    water = idx_1_5(number(county, "freshwater_index_1_5"))
    mountain = number(county, "mountain_pct")
    hill = number(county, "hill_pct")
    landslide = idx_1_5(number(county, "landslide_risk_1_5"))
    value = 78 + 0.20 * (transport - 50) + 0.10 * (water - 50)
    value -= 0.30 * mountain + 0.12 * hill + 0.10 * (landslide - 50)
    if resource_code in {"sea_salt", "lake_salt", "common_clay", "quartz_sand"}:
        value += 8
    return score(clamp(value, 5, 95))


def size_range(status: str, synthetic_strength: int | None = None) -> tuple[int, int]:
    if synthetic_strength is not None:
        return max(8, synthetic_strength - 12), min(88, synthetic_strength + 10)
    if status in {"Producer", "Past Producer"}:
        return 60, 92
    if status in {"Prospect", "exploration"}:
        return 43, 76
    if status in {"Occurrence", "mineralized_occurrence"}:
        return 25, 58
    return 18, 46


GRADE_RANGES = {
    "coal": (42, 84),
    "iron": (32, 78),
    "copper": (18, 67),
    "lead": (20, 70),
    "tin": (22, 72),
    "zinc": (20, 70),
    "silver": (15, 60),
    "gold": (12, 56),
    "mercury": (20, 68),
    "sulfur": (30, 76),
    "saltpeter": (28, 70),
    "sea_salt": (45, 82),
    "well_salt": (42, 82),
    "lake_salt": (48, 86),
    "rock_brine_salt": (40, 80),
    "kaolin": (32, 85),
    "common_clay": (38, 76),
    "limestone": (40, 84),
    "gypsum": (34, 76),
    "building_stone": (42, 86),
    "alum": (30, 74),
    "quartz_sand": (40, 84),
}


def band(value: int) -> str:
    if value >= 80:
        return "very_high"
    if value >= 65:
        return "high"
    if value >= 45:
        return "medium"
    if value >= 25:
        return "low"
    return "very_low"


def make_deposit(
    *,
    deposit_id: str,
    parent_deposit_id: str,
    county: dict[str, str],
    resource_code: str,
    name: str,
    longitude: float,
    latitude: float,
    evidence_distance_km: float,
    source_type: str,
    source_record_id: str,
    source_reference: str,
    evidence_grade: str,
    status: str,
    inference_method: str,
    synthetic: bool,
    synthetic_strength: int | None = None,
    forced_size: int | None = None,
    forced_grade: int | None = None,
) -> dict[str, Any]:
    radius = matching_radius(county)
    quality_weight = QUALITY_WEIGHTS[evidence_grade]
    status_weight = STATUS_WEIGHTS[status]
    dist_weight = distance_weight(evidence_distance_km, radius)
    contribution = score(quality_weight * status_weight * dist_weight / 10_000)
    low, high = size_range(status, synthetic_strength)
    size_value = forced_size or deterministic_int(deposit_id + ":size", low, high)
    grade_low, grade_high = GRADE_RANGES[resource_code]
    if synthetic_strength is not None:
        grade_low = max(grade_low, synthetic_strength - 18)
        grade_high = min(grade_high, synthetic_strength + 16)
        if grade_low > grade_high:
            grade_low, grade_high = GRADE_RANGES[resource_code]
    grade_value = forced_grade or deterministic_int(deposit_id + ":grade", grade_low, grade_high)
    terrain_depth = 0.20 * number(county, "mountain_pct") + 0.08 * number(county, "hill_pct")
    burial = score(
        deterministic_int(deposit_id + ":depth", 12, 68)
        + terrain_depth
        + (8 if resource_code in {"well_salt", "rock_brine_salt"} else 0)
    )
    access = surface_accessibility(county, resource_code)
    base_capacity = weighted([(size_value, 0.45), (grade_value, 0.25), (access, 0.30)])
    return {
        "deposit_id": deposit_id,
        "parent_deposit_id": parent_deposit_id,
        "county_id": county["county_id"],
        "snapshot_year": SNAPSHOT_YEAR,
        "resource_code": resource_code,
        "resource_name": RESOURCE_BY_CODE[resource_code].name,
        "deposit_name": name,
        "longitude": f"{longitude:.5f}",
        "latitude": f"{latitude:.5f}",
        "evidence_distance_km": f"{evidence_distance_km:.2f}",
        "match_radius_km": f"{radius:.2f}",
        "source_type": source_type,
        "source_record_id": source_record_id,
        "source_reference": source_reference,
        "source_license": USGS_PUBLIC_DOMAIN if source_type.startswith("usgs") else "historical/geographic model proxy",
        "evidence_grade": evidence_grade,
        "evidence_weight_0_100": quality_weight,
        "status": status,
        "status_weight_0_100": status_weight,
        "distance_weight_0_100": dist_weight,
        "evidence_contribution_0_100": contribution,
        "size_index_0_100": size_value,
        "size_band": band(size_value),
        "ore_grade_index_0_100": grade_value,
        "ore_grade_band": band(grade_value),
        "burial_difficulty_0_100": burial,
        "surface_accessibility_0_100": access,
        "base_extraction_capacity_index_0_100": base_capacity,
        "inference_method": inference_method,
        "is_synthetic_proxy": "yes" if synthetic else "no",
        "commercial_release_ready": NONCOMMERCIAL_MARK,
    }


def load_mrds_deposits(
    counties: Sequence[dict[str, str]], mrds_path: Path
) -> list[dict[str, Any]]:
    with mrds_path.open(encoding="utf-8") as stream:
        feature_collection = json.load(stream)
    deposits: list[dict[str, Any]] = []
    for feature in feature_collection.get("features", []):
        properties = feature.get("properties") or {}
        geometry = feature.get("geometry") or {}
        if properties.get("dev_stat") == "Plant" or geometry.get("type") != "Point":
            continue
        coordinates = geometry.get("coordinates") or []
        if len(coordinates) < 2:
            continue
        lon, lat = float(coordinates[0]), float(coordinates[1])
        matched = nearest_county(counties, lon, lat)
        if matched is None:
            continue
        county, distance, _ = matched
        resources = parse_codes(str(properties.get("code_list") or ""))
        evidence_grade = str(properties.get("grade") or "E").upper()
        if evidence_grade not in QUALITY_WEIGHTS:
            evidence_grade = "E"
        status = str(properties.get("dev_stat") or "Unknown")
        if status not in STATUS_WEIGHTS:
            status = "Unknown"
        parent_id = f"MRDS-{properties.get('dep_id')}"
        for resource_code in resources:
            deposits.append(
                make_deposit(
                    deposit_id=f"{parent_id}-{resource_code}",
                    parent_deposit_id=parent_id,
                    county=county,
                    resource_code=resource_code,
                    name=str(properties.get("site_name") or parent_id),
                    longitude=lon,
                    latitude=lat,
                    evidence_distance_km=distance,
                    source_type="usgs_mrds_point",
                    source_record_id=str(properties.get("dep_id") or ""),
                    source_reference=str(properties.get("url") or USGS_MRDS_URL),
                    evidence_grade=evidence_grade,
                    status=status,
                    inference_method=(
                        "MRDS point assigned to nearest county seat within three county service radii; "
                        "record quality is evidence weight, never ore grade"
                    ),
                    synthetic=False,
                )
            )
    return deposits


def vector_records(gdb: Path, layer: str, geometry: bool = False) -> tuple[dict[str, Any], list[dict[str, Any]], Any]:
    meta, _, geometries, arrays = read_vector(gdb, layer=layer, read_geometry=geometry)
    fields = [str(item) for item in meta["fields"]]
    rows = [dict(zip(fields, values)) for values in zip(*arrays)]
    return meta, rows, geometries


def load_gdb_point_deposits(
    counties: Sequence[dict[str, str]], gdb: Path, existing: Sequence[dict[str, Any]]
) -> list[dict[str, Any]]:
    _, rows, _ = vector_records(gdb, "CHN_Mineral_Deposits", geometry=False)
    existing_locations: dict[tuple[str, str], list[tuple[float, float]]] = defaultdict(list)
    for deposit in existing:
        existing_locations[(str(deposit["county_id"]), str(deposit["resource_code"]))].append(
            (float(deposit["longitude"]), float(deposit["latitude"]))
        )

    deposits: list[dict[str, Any]] = []
    for row in rows:
        lon, lat = float(row["LONGITUDE"]), float(row["LATITUDE"])
        matched = nearest_county(counties, lon, lat)
        if matched is None:
            continue
        county, distance, _ = matched
        raw_commodities = [row.get(f"DsgAttr0{index}") for index in range(1, 5)]
        resource_codes: list[str] = []
        for raw in raw_commodities:
            mapped = GDB_COMMODITY_MAP.get(str(raw or "").strip().lower())
            if mapped and mapped not in resource_codes:
                resource_codes.append(mapped)
        parent_id = f"USGS-CHN-{row['FeatureUID']}"
        for resource_code in resource_codes:
            duplicate = any(
                haversine_km(lon, lat, other_lon, other_lat) <= 5.0
                for other_lon, other_lat in existing_locations[(county["county_id"], resource_code)]
            )
            if duplicate:
                continue
            deposits.append(
                make_deposit(
                    deposit_id=f"{parent_id}-{resource_code}",
                    parent_deposit_id=parent_id,
                    county=county,
                    resource_code=resource_code,
                    name=str(row.get("FeatureNam") or parent_id),
                    longitude=lon,
                    latitude=lat,
                    evidence_distance_km=distance,
                    source_type="usgs_china_gis_major_deposit",
                    source_record_id=str(row["FeatureUID"]),
                    source_reference=str(row.get("InfSource1") or USGS_CHINA_GIS_URL),
                    evidence_grade="B",
                    status="exploration",
                    inference_method=(
                        "USGS China GIS major-deposit point assigned to nearest county seat; "
                        "size, grade and depth indices are deterministic game ranges"
                    ),
                    synthetic=False,
                )
            )
    return deposits


def load_coal_polygon_deposits(
    counties: Sequence[dict[str, str]], gdb: Path
) -> list[dict[str, Any]]:
    _, rows, raw_geometries = vector_records(gdb, "CHN_Mineral_Resources_Coal", geometry=True)
    geometries = [from_wkb(item) for item in raw_geometries]
    tree = STRtree(geometries)
    deposits: list[dict[str, Any]] = []
    for county in counties:
        lon, lat = number(county, "longitude"), number(county, "latitude")
        point = Point(lon, lat)
        radius = matching_radius(county)
        # EPSG:4214 is geographic.  A generous degree envelope is narrowed by
        # an exact haversine measurement to the nearest polygon point.
        candidates = tree.query(point.buffer(3 * radius / 85.0))
        nearest: tuple[float, int, float, float] | None = None
        for raw_index in candidates:
            index = int(raw_index)
            geometry = geometries[index]
            on_point, on_polygon = nearest_points(point, geometry)
            distance = haversine_km(on_point.x, on_point.y, on_polygon.x, on_polygon.y)
            if distance > 3 * radius:
                continue
            candidate = (distance, index, on_polygon.x, on_polygon.y)
            if nearest is None or candidate[:2] < nearest[:2]:
                nearest = candidate
        if nearest is None:
            continue
        distance, index, deposit_lon, deposit_lat = nearest
        row = rows[index]
        area_hint = max(0.0, float(row.get("Shape_Area") or 0))
        size_value = score(43 + 11 * math.log10(1 + area_hint * 100))
        rank_text = str(row.get("DsgAttr03") or "")
        if "High" in rank_text:
            grade_value = 78
        elif "Medium" in rank_text:
            grade_value = 62
        elif "Low" in rank_text:
            grade_value = 46
        else:
            grade_value = deterministic_int(f"COAL-{index}:grade", 45, 72)
        descriptor = "、".join(
            text for text in [str(row.get("ADM1") or ""), str(row.get("DsgAttr02") or ""), rank_text] if text
        )
        source_id = f"COAL-{index + 1:04d}"
        deposits.append(
            make_deposit(
                deposit_id=f"USGS-{source_id}-{county['county_id']}",
                parent_deposit_id=f"USGS-{source_id}",
                county=county,
                resource_code="coal",
                name=str(row.get("FeatureNam") or f"USGS煤田区（{descriptor}）"),
                longitude=deposit_lon,
                latitude=deposit_lat,
                evidence_distance_km=distance,
                source_type="usgs_china_gis_coal_polygon",
                source_record_id=source_id,
                source_reference=str(row.get("InfSource1") or USGS_CHINA_GIS_URL),
                # The source geometry is authoritative, but its national-scale
                # polygon does not prove a county-scale workable seam.
                evidence_grade="C",
                status="mineralized_occurrence",
                inference_method=(
                    "nearest USGS coal-resource polygon within three county service radii; "
                    "polygon area/rank only set broad deterministic game indices"
                ),
                synthetic=False,
                forced_size=size_value,
                forced_grade=grade_value,
            )
        )
    return deposits


def proxy_definition(county: dict[str, str], resource_code: str) -> tuple[int, str, str] | None:
    region = county["region"]
    upper = county["upper_unit"]
    intermediate = county.get("intermediate_unit", "")
    name = county["county"]
    resources = county.get("primary_resources", "")
    coast = number(county, "coast_island_pct")
    wetland = number(county, "wetland_lake_pct")
    plain = number(county, "plain_pct")
    hill = number(county, "hill_pct")
    mountain = number(county, "mountain_pct")
    plateau = number(county, "plateau_pct")
    desert = number(county, "desert_pct")
    precipitation = number(county, "annual_precip_mm_est")
    water = idx_1_5(number(county, "freshwater_index_1_5"))
    transport = idx_1_5(number(county, "transport_index_1_5"))
    soil = idx_1_5(number(county, "soil_fertility_index_1_5"))

    if resource_code == "sea_salt" and (coast >= 5 or "海盐" in resources):
        return score(48 + 0.40 * coast + 0.10 * transport), "coastal salt-pan proxy", "C"
    if resource_code == "well_salt" and region == "四川" and upper in {
        "成都府", "嘉定州", "叙州府", "潼川州", "重庆府", "夔州府"
    }:
        return score(50 + deterministic_int(county["county_id"] + ":well", 0, 24)), "Sichuan historical well-salt belt proxy", "C"
    if resource_code == "lake_salt" and region == "山西" and (
        upper == "平阳府" or intermediate == "解州" or "解州" in upper
    ):
        return score(62 + deterministic_int(county["county_id"] + ":lake-salt", 0, 22)), "Hedong salt-lake historical belt proxy", "B"
    if resource_code == "rock_brine_salt" and (
        region == "云南" or (region == "四川" and upper in {"叙州府", "重庆府", "夔州府"})
    ):
        return score(38 + 0.20 * plateau + deterministic_int(county["county_id"] + ":brine", 0, 18)), "southwest sedimentary brine proxy", "D"
    if resource_code == "saltpeter":
        dry = max(0.0, (900 - precipitation) / 12)
        cave = 0.18 * (mountain + hill) if region in {"广西", "贵州", "云南", "四川"} else 0
        strength = score(20 + dry + 0.18 * (plateau + desert) + cave)
        if strength >= 34:
            return strength, "aridity/loess/limestone-cave nitrate proxy", "C" if strength >= 62 else "D"
    if resource_code == "kaolin":
        if name == "浮梁县":
            return 92, "Jingdezhen/Fuliang historical porcelain-clay anchor", "B"
        if region in {"江西", "福建", "浙江", "南直隶（南京）"} and hill + mountain >= 38:
            strength = score(28 + 0.30 * hill + 0.24 * mountain)
            return strength, "southeast weathered-granite clay proxy", "D"
    if resource_code == "common_clay":
        strength = score(24 + 0.34 * plain + 0.14 * hill + 0.14 * soil)
        return strength, "alluvial/plain and weathered-soil clay proxy", "C" if strength >= 62 else "D"
    if resource_code == "limestone" and mountain + hill >= 25:
        karst_bonus = 20 if region in {"广西", "贵州", "云南"} else 0
        strength = score(22 + 0.32 * mountain + 0.18 * hill + karst_bonus)
        return strength, "mountain/karst limestone proxy", "C" if strength >= 62 else "D"
    if resource_code == "gypsum" and region in {"山西", "陕西", "四川", "云南"}:
        strength = score(28 + max(0.0, 850 - precipitation) / 20 + 0.12 * plateau)
        if strength >= 34:
            return strength, "sedimentary basin/aridity gypsum proxy", "D"
    if resource_code == "building_stone":
        strength = score(22 + 0.44 * mountain + 0.25 * hill + 0.05 * transport)
        return strength, "exposed bedrock and terrain construction-stone proxy", "C" if strength >= 62 else "D"
    if resource_code == "alum" and region in {"浙江", "福建", "江西", "南直隶（南京）"} and mountain + hill >= 35:
        strength = score(24 + 0.20 * hill + 0.18 * mountain)
        return strength, "southeast volcanic/weathering alum proxy", "D"
    if resource_code == "quartz_sand" and (coast + wetland + plain >= 25 or water >= 50):
        strength = score(26 + 0.22 * plain + 0.25 * coast + 0.12 * wetland + 0.08 * water)
        return strength, "river/lake/coastal quartz-sand proxy", "C" if strength >= 62 else "D"
    if resource_code == "coal" and name == "阳城县":
        return 88, "Zezhou/Yangcheng historical coal-mining anchor", "B"
    if resource_code == "iron" and name == "阳城县":
        return 86, "Zezhou/Yangcheng historical iron-smelting anchor", "B"
    return None


def synthetic_proxy_deposits(counties: Sequence[dict[str, str]]) -> list[dict[str, Any]]:
    deposits: list[dict[str, Any]] = []
    for county in counties:
        lon, lat = number(county, "longitude"), number(county, "latitude")
        for resource in RESOURCES:
            definition = proxy_definition(county, resource.code)
            if definition is None:
                continue
            strength, method, evidence_grade = definition
            is_historical = "historical" in method or "Jingdezhen" in method
            status = "Past Producer" if is_historical else "synthetic_proxy"
            source_type = "historical_mining_anchor" if is_historical else "synthetic_proxy"
            reference = (
                "《明史·食货志》与明代区域矿业史锚点"
                if is_historical
                else "county geography v0.1 deterministic inference rule"
            )
            deposits.append(
                make_deposit(
                    deposit_id=f"PROXY-{county['county_id']}-{resource.code}",
                    parent_deposit_id=f"PROXY-{county['county_id']}-{resource.code}",
                    county=county,
                    resource_code=resource.code,
                    name=f"{county['county']}·{resource.name}{'历史锚点' if is_historical else '地理推定点'}",
                    longitude=lon,
                    latitude=lat,
                    evidence_distance_km=0.0,
                    source_type=source_type,
                    source_record_id=f"{county['county_id']}:{resource.code}",
                    source_reference=reference,
                    evidence_grade=evidence_grade,
                    status=status,
                    inference_method=method,
                    synthetic=True,
                    synthetic_strength=strength,
                    forced_size=strength if is_historical else None,
                    forced_grade=score(strength - 4) if is_historical else None,
                )
            )
    return deposits


ANCHOR_SPECS: list[dict[str, Any]] = [
    {"county": "大兴县", "name": "北京京师", "sectors": {"arms": 95}, "commerce": 98, "military": 100, "type": "capital"},
    {"county": "宛平县", "name": "北京京师", "sectors": {"arms": 95}, "commerce": 96, "military": 100, "type": "capital"},
    {"county": "上元县", "name": "南京", "sectors": {"textile": 86, "arms": 88, "shipbuilding": 82}, "commerce": 96, "military": 90, "type": "capital"},
    {"county": "江宁县", "name": "南京", "sectors": {"textile": 84, "arms": 86, "shipbuilding": 80}, "commerce": 94, "military": 88, "type": "capital"},
    {"county": "吴县", "name": "苏州丝棉织造与商业", "sectors": {"textile": 100}, "commerce": 100, "military": 18, "type": "craft_trade"},
    {"county": "长洲县", "name": "苏州丝棉织造与商业", "sectors": {"textile": 96}, "commerce": 98, "military": 18, "type": "craft_trade"},
    {"county": "华亭县", "upper": "松江府", "name": "松江棉纺织", "sectors": {"textile": 98}, "commerce": 92, "military": 15, "type": "craft_trade"},
    {"county": "上海县", "name": "松江港市", "sectors": {"textile": 92, "shipbuilding": 75}, "commerce": 96, "military": 20, "type": "port_trade"},
    {"county": "钱塘县", "name": "杭州丝织与市场", "sectors": {"textile": 94}, "commerce": 94, "military": 18, "type": "craft_trade"},
    {"county": "仁和县", "name": "杭州丝织与市场", "sectors": {"textile": 92}, "commerce": 92, "military": 18, "type": "craft_trade"},
    {"county": "秀水县", "name": "嘉兴丝织", "sectors": {"textile": 88}, "commerce": 86, "military": 12, "type": "craft_trade"},
    {"county": "乌程县", "name": "湖州蚕桑丝织", "sectors": {"textile": 90}, "commerce": 86, "military": 12, "type": "craft_trade"},
    {"county": "浮梁县", "name": "景德镇陶瓷", "sectors": {"ceramics": 100}, "commerce": 88, "military": 10, "type": "craft_industry"},
    {"county": "南海县", "name": "佛山冶铁与广州商贸", "sectors": {"mining_smelting": 98, "arms": 92, "shipbuilding": 82}, "commerce": 100, "military": 90, "type": "metallurgy_trade"},
    {"county": "番禺县", "name": "广州港市", "sectors": {"shipbuilding": 90}, "commerce": 100, "military": 75, "type": "port_trade"},
    {"county": "海澄县", "name": "月港海贸", "sectors": {"shipbuilding": 88}, "commerce": 98, "military": 40, "type": "port_trade"},
    {"county": "龙溪县", "name": "漳州月港商贸", "sectors": {"shipbuilding": 80}, "commerce": 92, "military": 35, "type": "port_trade"},
    {"county": "晋江县", "name": "泉州海贸", "sectors": {"shipbuilding": 82}, "commerce": 92, "military": 35, "type": "port_trade"},
    {"county": "鄞县", "name": "宁波港贸", "sectors": {"shipbuilding": 82}, "commerce": 90, "military": 35, "type": "port_trade"},
    {"county": "阳城县", "name": "泽州煤铁矿冶", "sectors": {"mining_smelting": 96, "arms": 76}, "commerce": 52, "military": 72, "type": "mining_metallurgy"},
    {"county": "遵化县", "name": "遵化铁冶与军需", "sectors": {"mining_smelting": 90, "arms": 90}, "commerce": 55, "military": 90, "type": "mining_military"},
    {"county": "丘县", "upper": "东昌府", "name": "临清州辖区运河商市", "sectors": {"textile": 72}, "commerce": 96, "military": 35, "type": "canal_trade"},
    {"county": "山阳县", "upper": "淮安府", "name": "淮安漕运", "sectors": {"salt_food": 78}, "commerce": 94, "military": 40, "type": "canal_trade"},
    {"county": "江都县", "name": "扬州盐运商市", "sectors": {"salt_food": 96}, "commerce": 98, "military": 32, "type": "salt_trade"},
    {"county": "仪真县", "name": "长江运河港", "sectors": {"shipbuilding": 72}, "commerce": 88, "military": 32, "type": "canal_trade"},
]


def build_anchors(counties: Sequence[dict[str, str]]) -> tuple[list[dict[str, Any]], list[str]]:
    anchors: list[dict[str, Any]] = []
    missing: list[str] = []
    for spec in ANCHOR_SPECS:
        matches = [
            county
            for county in counties
            if county["county"] == spec["county"]
            and (not spec.get("upper") or county["upper_unit"] == spec["upper"])
        ]
        if len(matches) != 1:
            missing.append(f"{spec['county']}@{spec.get('upper', '*')}")
            continue
        county = matches[0]
        for sector, sector_score in spec["sectors"].items():
            anchor_id = f"ANCHOR-{len(anchors) + 1:03d}"
            if spec["type"] in {"mining_metallurgy", "metallurgy_trade", "mining_military"}:
                source_reference = f"《明史·食货志》；明清实录矿业记录量化研究：{MING_MINING_STUDY_URL}"
            else:
                source_reference = "《明史·食货志》及明代区域经济史锚点（v0.2初步整理）"
            anchors.append(
                {
                    "anchor_id": anchor_id,
                    "anchor_name": spec["name"],
                    "county_id": county["county_id"],
                    "anchor_type": spec["type"],
                    "industry_sector": sector,
                    "sector_score_0_100": sector_score,
                    "commerce_score_0_100": spec["commerce"],
                    "military_industry_score_0_100": spec["military"],
                    "source_reference": source_reference,
                    "evidence_grade": "historical_anchor",
                    "notes": "用于1628初始开发状态，不反向修改地质潜力；后续由地方志专题校订",
                    "commercial_release_ready": NONCOMMERCIAL_MARK,
                }
            )
    return anchors, missing


def build_county_potentials(
    counties: Sequence[dict[str, str]], deposits: Sequence[dict[str, Any]]
) -> list[dict[str, Any]]:
    grouped: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    for deposit in deposits:
        grouped[(str(deposit["county_id"]), str(deposit["resource_code"]))].append(deposit)

    rows: list[dict[str, Any]] = []
    for county in counties:
        for resource in RESOURCES:
            matches = sorted(
                grouped.get((county["county_id"], resource.code), []),
                key=lambda item: (float(item["evidence_distance_km"]), str(item["deposit_id"])),
            )
            contributions = [int(item["evidence_contribution_0_100"]) for item in matches]
            potential = aggregate_probabilistic(contributions)
            if matches and sum(contributions):
                accessibility = score(
                    sum(
                        int(item["surface_accessibility_0_100"]) * contribution
                        for item, contribution in zip(matches, contributions)
                    )
                    / sum(contributions)
                )
            else:
                accessibility = surface_accessibility(county, resource.code)
            effective = score(potential * accessibility / 100)
            physical = [item for item in matches if item["is_synthetic_proxy"] == "no"]
            synthetic = [item for item in matches if item["is_synthetic_proxy"] == "yes"]
            source_types = sorted({str(item["source_type"]) for item in matches})
            best_grade = (
                max((str(item["evidence_grade"]) for item in matches), key=lambda item: QUALITY_WEIGHTS[item])
                if matches
                else "none"
            )
            traceability = ";".join(str(item["deposit_id"]) for item in matches) or "none"
            rows.append(
                {
                    "county_id": county["county_id"],
                    "snapshot_year": SNAPSHOT_YEAR,
                    "region": county["region"],
                    "upper_unit": county["upper_unit"],
                    "intermediate_unit": county.get("intermediate_unit", ""),
                    "county": county["county"],
                    "resource_code": resource.code,
                    "resource_name": resource.name,
                    "deposit_count": len(matches),
                    "physical_deposit_count": len(physical),
                    "synthetic_proxy_count": len(synthetic),
                    "potential_score_0_100": potential,
                    "surface_accessibility_0_100": accessibility,
                    "effective_industrial_value_0_100": effective,
                    "nearest_evidence_distance_km": (
                        f"{float(matches[0]['evidence_distance_km']):.2f}" if matches else "-1.00"
                    ),
                    "nearest_deposit_name": matches[0]["deposit_name"] if matches else "none",
                    "evidence_source_types": ";".join(source_types) or "none",
                    "best_evidence_grade": best_grade,
                    "calculation_method": (
                        "100*(1-product(1-evidence_contribution/100)); "
                        "effective=potential*weighted_surface_accessibility/100"
                    ),
                    "traceability": traceability,
                    "source_license": (
                        ";".join(sorted({str(item["source_license"]) for item in matches}))
                        if matches
                        else "no evidence in v0.2"
                    ),
                    "commercial_release_ready": NONCOMMERCIAL_MARK,
                }
            )
    return rows


def administrative_scores(counties: Sequence[dict[str, str]]) -> dict[str, int]:
    first_upper: set[str] = set()
    first_region: set[str] = set()
    seen_upper: set[tuple[str, str]] = set()
    seen_region: set[str] = set()
    for county in counties:
        upper_key = (county["region"], county["upper_unit"])
        if upper_key not in seen_upper:
            first_upper.add(county["county_id"])
            seen_upper.add(upper_key)
        if county["region"] not in seen_region:
            first_region.add(county["county_id"])
            seen_region.add(county["region"])
    result: dict[str, int] = {}
    for county in counties:
        value = 28
        if county["county_id"] in first_upper:
            value = 62
        if county["county_id"] in first_region:
            value = 80
        if county["county"] in {"大兴县", "宛平县"}:
            value = 100
        if county["county"] in {"上元县", "江宁县"}:
            value = 95
        result[county["county_id"]] = value
    return result


def composite_top(values: Iterable[int]) -> int:
    ordered = sorted(values, reverse=True)
    if not ordered:
        return 0
    top = ordered[:3]
    return score(0.60 * top[0] + 0.40 * sum(top) / len(top))


def build_economy(
    counties: Sequence[dict[str, str]],
    potentials: Sequence[dict[str, Any]],
    anchors: Sequence[dict[str, Any]],
) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    mineral: dict[str, dict[str, dict[str, Any]]] = defaultdict(dict)
    for row in potentials:
        mineral[str(row["county_id"])][str(row["resource_code"])] = row

    sector_anchor: dict[str, dict[str, int]] = defaultdict(dict)
    commerce_anchor: dict[str, int] = defaultdict(int)
    military_anchor: dict[str, int] = defaultdict(int)
    port_anchor: dict[str, int] = defaultdict(int)
    anchor_count: Counter[str] = Counter()
    for anchor in anchors:
        county_id = str(anchor["county_id"])
        sector = str(anchor["industry_sector"])
        sector_anchor[county_id][sector] = max(
            sector_anchor[county_id].get(sector, 0), int(anchor["sector_score_0_100"])
        )
        commerce_anchor[county_id] = max(commerce_anchor[county_id], int(anchor["commerce_score_0_100"]))
        military_anchor[county_id] = max(
            military_anchor[county_id], int(anchor["military_industry_score_0_100"])
        )
        if anchor["anchor_type"] == "port_trade":
            port_anchor[county_id] = 100
        elif anchor["anchor_type"] == "canal_trade":
            port_anchor[county_id] = max(port_anchor[county_id], 65)
        anchor_count[county_id] += 1

    population_values = [number(county, "population_1630_est_allocated") for county in counties]
    density_values = [
        population / max(1.0, number(county, "area_km2_est"))
        for county, population in zip(counties, population_values)
    ]
    population_rank = percentile_scores(population_values)
    density_rank = percentile_scores(density_values)
    admin = administrative_scores(counties)
    economy_rows: list[dict[str, Any]] = []

    for index, county in enumerate(counties):
        county_id = county["county_id"]
        values = mineral[county_id]

        def p(code: str) -> int:
            return int(values[code]["potential_score_0_100"])

        def effective(code: str) -> int:
            return int(values[code]["effective_industrial_value_0_100"])

        agriculture = weighted(
            [
                (idx_1_5(number(county, "agriculture_potential_1_5")), 0.55),
                (idx_1_5(number(county, "soil_fertility_index_1_5")), 0.20),
                (number(county, "arable_land_pct_est"), 0.25),
            ]
        )
        forest = weighted(
            [
                (idx_1_5(number(county, "forest_potential_1_5")), 0.70),
                (number(county, "forest_land_pct_est"), 0.30),
            ]
        )
        pasture = weighted(
            [
                (idx_1_5(number(county, "pasture_potential_1_5")), 0.70),
                (number(county, "pasture_land_pct_est"), 0.30),
            ]
        )
        water = idx_1_5(number(county, "freshwater_index_1_5"))
        coast = score(number(county, "coast_island_pct") * 1.5)
        wetland = score(number(county, "wetland_lake_pct") * 2)
        river_distance = number(county, "major_river_distance_km_est", 999)
        river_bonus = 20 if river_distance <= 15 else 10 if river_distance <= 40 else 0
        water_access = score(max(water, coast, wetland) + river_bonus)
        fishery = weighted(
            [
                (idx_1_5(number(county, "fishery_potential_1_5")), 0.55),
                (water_access, 0.25),
                (max(coast, wetland), 0.20),
            ]
        )
        salt_resource = composite_top(
            [p("sea_salt"), p("well_salt"), p("lake_salt"), p("rock_brine_salt")]
        )
        metal_resource = composite_top(
            [
                effective(code)
                for code in ["iron", "copper", "lead", "tin", "zinc", "silver", "gold", "mercury"]
            ]
        )
        building_resource = composite_top(
            [
                effective(code)
                for code in ["kaolin", "common_clay", "limestone", "gypsum", "building_stone", "alum", "quartz_sand"]
            ]
        )
        fuel = max(effective("coal"), score(forest * 0.68))
        positive_count = sum(p(resource.code) >= 20 for resource in RESOURCES)
        renewable_mean = (agriculture + forest + pasture + fishery) / 4
        diversity = score(0.65 * positive_count / len(RESOURCES) * 100 + 0.35 * renewable_mean)
        transport = score(
            0.75 * idx_1_5(number(county, "transport_index_1_5")) + 0.25 * water_access
        )
        labor = population_rank[index]
        market_population = weighted([(population_rank[index], 0.60), (density_rank[index], 0.40)])
        local_market = weighted([(market_population, 0.60), (admin[county_id], 0.20), (transport, 0.20)])
        resource_text = county.get("primary_resources", "")
        fiber = score(
            0.45 * agriculture
            + (35 if "棉麻" in resource_text else 0)
            + (32 if "蚕桑" in resource_text else 0)
        )
        wood = score(0.80 * forest + (15 if "竹材" in resource_text else 0))
        clay = composite_top([effective("kaolin"), effective("common_clay")])
        coast_river = max(water_access, coast)
        arms_materials = composite_top(
            [effective(code) for code in ["coal", "iron", "copper", "lead", "tin", "sulfur", "saltpeter"]]
        )

        potentials_by_sector = {
            "mining_smelting": weighted(
                [(metal_resource, 0.50), (fuel, 0.20), (water_access, 0.05), (labor, 0.10), (transport, 0.15)]
            ),
            "textile": weighted(
                [(fiber, 0.35), (water_access, 0.10), (labor, 0.20), (local_market, 0.20), (transport, 0.15)]
            ),
            "ceramics": weighted(
                [(clay, 0.35), (fuel, 0.25), (water_access, 0.10), (labor, 0.10), (transport, 0.20)]
            ),
            "salt_food": weighted(
                [
                    ((agriculture + fishery + salt_resource) / 3, 0.40),
                    (fuel, 0.10),
                    (water_access, 0.10),
                    (labor, 0.15),
                    (transport, 0.25),
                ]
            ),
            "forestry_paper": weighted(
                [(wood, 0.40), (water_access, 0.20), (labor, 0.10), (transport, 0.20), (local_market, 0.10)]
            ),
            "shipbuilding": weighted(
                [
                    (coast_river, 0.25),
                    (wood, 0.25),
                    (effective("iron"), 0.10),
                    (labor, 0.10),
                    (transport, 0.20),
                    (port_anchor[county_id], 0.10),
                ]
            ),
            "arms": weighted(
                [
                    (arms_materials, 0.40),
                    (fuel, 0.15),
                    (labor, 0.15),
                    (transport, 0.15),
                    (military_anchor[county_id], 0.15),
                ]
            ),
            "building_materials": weighted(
                [
                    ((building_resource * 0.75 + wood * 0.25), 0.50),
                    (labor, 0.20),
                    (transport, 0.15),
                    (local_market, 0.15),
                ]
            ),
        }
        initial_by_sector: dict[str, int] = {}
        for sector in SECTORS:
            market_labor = (local_market + labor) / 2
            initial_by_sector[sector] = weighted(
                [
                    (potentials_by_sector[sector], 0.35),
                    (market_labor, 0.15),
                    (transport, 0.10),
                    (admin[county_id], 0.10),
                    (sector_anchor[county_id].get(sector, 0), 0.30),
                ]
            )

        sector_weights = {
            "mining_smelting": 0.20,
            "textile": 0.20,
            "ceramics": 0.10,
            "salt_food": 0.15,
            "forestry_paper": 0.10,
            "shipbuilding": 0.10,
            "arms": 0.10,
            "building_materials": 0.05,
        }
        industrial_potential = weighted(
            (potentials_by_sector[key], sector_weights[key]) for key in SECTORS
        )
        industrial_initial = weighted(
            (initial_by_sector[key], sector_weights[key]) for key in SECTORS
        )
        hazard = sum(
            idx_1_5(number(county, key))
            for key in [
                "drought_risk_1_5",
                "flood_risk_1_5",
                "cold_risk_1_5",
                "heat_risk_1_5",
                "typhoon_risk_1_5",
                "landslide_risk_1_5",
            ]
        ) / 6
        resilience = weighted(
            [(diversity, 0.35), (agriculture, 0.25), (water_access, 0.20), (100 - hazard, 0.20)]
        )
        long_distance = weighted([(transport, 0.50), (admin[county_id], 0.25), (diversity, 0.25)])
        waterborne = weighted([(coast_river, 0.55), (transport, 0.25), (port_anchor[county_id], 0.20)])
        commercial_potential = weighted(
            [
                (transport, 0.30),
                (market_population, 0.25),
                (diversity, 0.15),
                (admin[county_id], 0.10),
                (waterborne, 0.10),
                (resilience, 0.10),
            ]
        )
        disruption_penalty = 0  # no county-level 1628 disaster correction is asserted in v0.2
        commercial_initial = score(
            0.45 * commercial_potential
            + 0.20 * industrial_initial
            + 0.15 * density_rank[index]
            + 0.20 * commerce_anchor[county_id]
            - disruption_penalty
        )
        population = int(population_values[index])
        urbanization = score(
            clamp(
                2.5
                + 0.10 * commercial_initial
                + 0.06 * industrial_initial
                + (6 if admin[county_id] >= 95 else 3 if admin[county_id] >= 80 else 1 if admin[county_id] >= 60 else 0),
                3,
                28,
            )
        )
        population_pressure = score(number(county, "population_pressure_pct") / 1.20)
        grain_surplus = score(
            50 + 0.65 * (agriculture - 50) - 0.45 * (population_pressure - 72)
        )
        tax_base = weighted(
            [(commercial_initial, 0.35), (industrial_initial, 0.25), (agriculture, 0.25), (local_market, 0.15)]
        )

        row: dict[str, Any] = {
            "county_id": county_id,
            "snapshot_year": SNAPSHOT_YEAR,
            "region": county["region"],
            "upper_unit": county["upper_unit"],
            "intermediate_unit": county.get("intermediate_unit", ""),
            "county": county["county"],
            "longitude": county["longitude"],
            "latitude": county["latitude"],
            "area_km2_est": county["area_km2_est"],
            "population_est_1628": population,
            "population_density_per_km2": f"{density_values[index]:.2f}",
            "household_count_est": round(population / 5.5),
            "labor_force_est": round(population * 0.44),
            "urban_population_est": round(population * urbanization / 100),
            "urbanization_rate_0_100": urbanization,
            "population_pressure_0_100": population_pressure,
            "population_estimation_method": (
                "1628 gameplay baseline allocated from the existing Cao Shuji 1630 regional total; "
                "households=population/5.5; labor=population*44%"
            ),
            "agriculture_resource_0_100": agriculture,
            "forest_resource_0_100": forest,
            "pasture_resource_0_100": pasture,
            "fishery_resource_0_100": fishery,
            "salt_resource_0_100": salt_resource,
            "fuel_resource_0_100": fuel,
            "metal_resource_0_100": metal_resource,
            "building_material_resource_0_100": building_resource,
            "resource_diversity_0_100": diversity,
            "water_access_0_100": water_access,
            "transport_access_0_100": transport,
            "labor_availability_0_100": labor,
            "market_population_0_100": market_population,
            "administrative_centrality_0_100": admin[county_id],
            "industrial_composite_potential_0_100": industrial_potential,
            "industrial_initial_1628_0_100": industrial_initial,
            "local_market_0_100": local_market,
            "long_distance_trade_0_100": long_distance,
            "waterborne_trade_0_100": waterborne,
            "commercial_potential_0_100": commercial_potential,
            "commercial_prosperity_1628_0_100": commercial_initial,
            "confirmed_disruption_penalty_0_100": disruption_penalty,
            "tax_base_potential_0_100": tax_base,
            "grain_surplus_potential_0_100": grain_surplus,
            "economic_resilience_0_100": resilience,
            "historical_anchor_count": anchor_count[county_id],
            "economy_method": "deterministic v0.2 weights; geological potential separated from 1628 development anchors",
            "commercial_release_ready": NONCOMMERCIAL_MARK,
        }
        for sector in SECTORS:
            row[f"{sector}_potential_0_100"] = potentials_by_sector[sector]
            row[f"{sector}_initial_1628_0_100"] = initial_by_sector[sector]
        economy_rows.append(row)

    overview_rows: list[dict[str, Any]] = []
    for row in economy_rows:
        county_id = str(row["county_id"])
        minerals = sorted(
            mineral[county_id].values(),
            key=lambda item: (-int(item["potential_score_0_100"]), RESOURCE_ORDER[str(item["resource_code"])]),
        )
        top = [
            f"{item['resource_name']}:{item['potential_score_0_100']}"
            for item in minerals
            if int(item["potential_score_0_100"]) > 0
        ][:5]
        overview = {column: row.get(column, "") for column in OVERVIEW_COLUMNS}
        overview["top_minerals"] = ";".join(top) or "none"
        overview["coal_potential_0_100"] = mineral[county_id]["coal"]["potential_score_0_100"]
        overview["iron_potential_0_100"] = mineral[county_id]["iron"]["potential_score_0_100"]
        overview_rows.append(overview)
    return economy_rows, overview_rows


def sqlite_type(column: str) -> str:
    if column.endswith("_0_100") or column in {
        "snapshot_year",
        "population_est_1628",
        "household_count_est",
        "labor_force_est",
        "urban_population_est",
        "historical_anchor_count",
        "deposit_count",
        "physical_deposit_count",
        "synthetic_proxy_count",
    }:
        return "INTEGER"
    if column in {
        "longitude",
        "latitude",
        "area_km2_est",
        "population_density_per_km2",
        "evidence_distance_km",
        "match_radius_km",
        "nearest_evidence_distance_km",
    }:
        return "REAL"
    return "TEXT"


def create_and_insert(
    connection: sqlite3.Connection,
    table: str,
    columns: Sequence[str],
    rows: Sequence[dict[str, Any]],
    primary_key: Sequence[str],
) -> None:
    connection.execute(f'DROP TABLE IF EXISTS "{table}"')
    definitions: list[str] = []
    for column in columns:
        definition = f'"{column}" {sqlite_type(column)} NOT NULL'
        if column.endswith("_0_100"):
            definition += f' CHECK ("{column}" BETWEEN 0 AND 100)'
        definitions.append(definition)
    definitions.append("PRIMARY KEY (" + ",".join(f'"{item}"' for item in primary_key) + ")")
    connection.execute(f'CREATE TABLE "{table}" ({",".join(definitions)})')
    placeholders = ",".join("?" for _ in columns)
    column_sql = ",".join(f'"{item}"' for item in columns)
    connection.executemany(
        f'INSERT INTO "{table}" ({column_sql}) VALUES ({placeholders})',
        [[row.get(column, "") for column in columns] for row in rows],
    )


def build_sqlite(
    old_database: Path,
    output_database: Path,
    deposits: Sequence[dict[str, Any]],
    potentials: Sequence[dict[str, Any]],
    economy: Sequence[dict[str, Any]],
    overview: Sequence[dict[str, Any]],
    anchors: Sequence[dict[str, Any]],
) -> None:
    temporary = output_database.with_suffix(output_database.suffix + ".tmp")
    if temporary.exists():
        temporary.unlink()
    source = sqlite3.connect(old_database)
    target = sqlite3.connect(temporary)
    try:
        source.backup(target)
        target.execute("PRAGMA foreign_keys=ON")
        target.execute("DROP VIEW IF EXISTS v_county_gameplay_overview")
        create_and_insert(target, "mineral_deposit_definition", DEPOSIT_COLUMNS, deposits, ["deposit_id"])
        create_and_insert(
            target,
            "county_mineral_potential",
            POTENTIAL_COLUMNS,
            potentials,
            ["county_id", "resource_code"],
        )
        create_and_insert(
            target,
            "county_economy_baseline",
            ECONOMY_COLUMNS,
            economy,
            ["county_id"],
        )
        create_and_insert(
            target,
            "county_gameplay_overview",
            OVERVIEW_COLUMNS,
            overview,
            ["county_id"],
        )
        create_and_insert(
            target,
            "historical_economic_anchors",
            ANCHOR_COLUMNS,
            anchors,
            ["anchor_id"],
        )
        target.execute(
            "CREATE INDEX idx_deposit_county_resource "
            "ON mineral_deposit_definition(county_id, resource_code)"
        )
        target.execute(
            "CREATE INDEX idx_mineral_resource_score "
            "ON county_mineral_potential(resource_code, potential_score_0_100 DESC)"
        )
        target.execute(
            "CREATE INDEX idx_economy_industry "
            "ON county_economy_baseline(industrial_initial_1628_0_100 DESC)"
        )
        view_columns: list[str] = []
        for column in OVERVIEW_COLUMNS:
            if column == "top_minerals":
                view_columns.append('o."top_minerals" AS "top_minerals"')
            elif column == "coal_potential_0_100":
                view_columns.append(
                    'coal."potential_score_0_100" AS "coal_potential_0_100"'
                )
            elif column == "iron_potential_0_100":
                view_columns.append(
                    'iron."potential_score_0_100" AS "iron_potential_0_100"'
                )
            else:
                view_columns.append(f'e."{column}" AS "{column}"')
        target.execute(
            "CREATE VIEW v_county_gameplay_overview AS SELECT "
            + ",".join(view_columns)
            + " FROM county_economy_baseline AS e"
            + " JOIN county_gameplay_overview AS o USING (county_id)"
            + " JOIN county_mineral_potential AS coal"
            + "   ON coal.county_id=e.county_id AND coal.resource_code='coal'"
            + " JOIN county_mineral_potential AS iron"
            + "   ON iron.county_id=e.county_id AND iron.resource_code='iron'"
        )
        target.execute("PRAGMA user_version=2")
        target.commit()
    finally:
        source.close()
        target.close()
    temporary.replace(output_database)


def percentile_threshold(values: Sequence[int], percentile: float) -> int:
    ordered = sorted(values)
    index = min(len(ordered) - 1, max(0, math.ceil(percentile * len(ordered)) - 1))
    return ordered[index]


def theoretical_mine_output(deposit: dict[str, Any]) -> dict[str, float]:
    # Same workers, technology, equipment and management for every compared
    # deposit.  Capacity, grade and geological conditions remain deposit-level.
    workers = standard_workers = 100.0
    technology = equipment = management = 1.0
    access = int(deposit["surface_accessibility_0_100"]) / 100
    depth_condition = 1.0 - 0.55 * int(deposit["burial_difficulty_0_100"]) / 100
    geological_condition = max(0.20, access * depth_condition)
    ore = (
        int(deposit["base_extraction_capacity_index_0_100"])
        * (workers / standard_workers) ** 0.65
        * technology
        * equipment
        * management
        * geological_condition
    )
    grade_factor = 0.05 + 0.0065 * int(deposit["ore_grade_index_0_100"])
    usable = ore * grade_factor * 0.65 * 0.75
    return {"ore_index": round(ore, 4), "usable_metal_index": round(usable, 4)}


def assert_scores(rows: Sequence[dict[str, Any]], columns: Sequence[str], label: str) -> None:
    score_columns = [column for column in columns if column.endswith("_0_100")]
    for row_index, row in enumerate(rows, start=1):
        for column in score_columns:
            value = row.get(column)
            if isinstance(value, bool) or not isinstance(value, int) or not 0 <= value <= 100:
                raise AssertionError(f"{label} row {row_index} invalid {column}={value!r}")


def validate(
    counties: Sequence[dict[str, str]],
    deposits: Sequence[dict[str, Any]],
    potentials: Sequence[dict[str, Any]],
    economy: Sequence[dict[str, Any]],
    overview: Sequence[dict[str, Any]],
    anchors: Sequence[dict[str, Any]],
    database: Path,
) -> dict[str, Any]:
    if len(counties) != 1168 or len({item["county_id"] for item in counties}) != 1168:
        raise AssertionError("v0.1 county input is not 1,168 unique counties")
    if len(potentials) != 25_696:
        raise AssertionError(f"mineral slot count {len(potentials)} != 25,696")
    if len(economy) != 1168 or len({item["county_id"] for item in economy}) != 1168:
        raise AssertionError("economy table must contain 1,168 unique counties")
    if len(overview) != 1168 or len({item["county_id"] for item in overview}) != 1168:
        raise AssertionError("overview table must contain 1,168 unique counties")
    if len({item["deposit_id"] for item in deposits}) != len(deposits):
        raise AssertionError("deposit_id is not unique")
    for county_id, group in _group_rows(potentials, "county_id").items():
        if len(group) != 22 or {item["resource_code"] for item in group} != set(RESOURCE_BY_CODE):
            raise AssertionError(f"{county_id} does not have exactly 22 resource slots")
    assert_scores(deposits, DEPOSIT_COLUMNS, "deposit")
    assert_scores(potentials, POTENTIAL_COLUMNS, "potential")
    assert_scores(economy, ECONOMY_COLUMNS, "economy")
    assert_scores(overview, OVERVIEW_COLUMNS, "overview")
    assert_scores(anchors, ANCHOR_COLUMNS, "historical anchor")
    if sum(int(item["population_est_1628"]) for item in economy) != POPULATION_BASELINE:
        raise AssertionError("county population no longer sums to 209,249,000")
    for item in potentials:
        if int(item["potential_score_0_100"]) > 0 and (
            int(item["deposit_count"]) <= 0 or item["traceability"] == "none"
        ):
            raise AssertionError(f"positive mineral lacks traceability: {item['county_id']} {item['resource_code']}")
        if item["commercial_release_ready"] != NONCOMMERCIAL_MARK:
            raise AssertionError("commercial release marker was lost")
    if any(item["commercial_release_ready"] != NONCOMMERCIAL_MARK for item in anchors):
        raise AssertionError("historical anchor commercial release marker was lost")

    potential_lookup = {
        (str(item["county_id"]), str(item["resource_code"])): int(item["potential_score_0_100"])
        for item in potentials
    }
    economy_by_name = {str(item["county"]): item for item in economy}
    county_id_by_name = {item["county"]: item["county_id"] for item in counties}
    all_by_resource: dict[str, list[int]] = {
        code: [int(item["potential_score_0_100"]) for item in potentials if item["resource_code"] == code]
        for code in RESOURCE_BY_CODE
    }

    yangcheng_id = county_id_by_name["阳城县"]
    yangcheng = {
        "coal": potential_lookup[(yangcheng_id, "coal")],
        "iron": potential_lookup[(yangcheng_id, "iron")],
    }
    if yangcheng["coal"] <= median(all_by_resource["coal"]) or yangcheng["iron"] <= median(all_by_resource["iron"]):
        raise AssertionError("Yangcheng coal/iron must exceed national medians")

    shanxi_ids = {item["county_id"] for item in counties if item["region"] == "山西"}
    shanxi_variation = {
        code: len({potential_lookup[(county_id, code)] for county_id in shanxi_ids})
        for code in ["coal", "iron"]
    }
    if min(shanxi_variation.values()) < 2:
        raise AssertionError("Shanxi coal/iron lack county-level spatial variation")

    ceramics_values = [int(item["ceramics_initial_1628_0_100"]) for item in economy]
    fuliang = int(economy_by_name["浮梁县"]["ceramics_initial_1628_0_100"])
    if fuliang < percentile_threshold(ceramics_values, 0.90):
        raise AssertionError("Fuliang ceramics initial index is not in the top decile")

    nanhai = economy_by_name["南海县"]
    nanhai_metrics = {
        "mining_smelting": int(nanhai["mining_smelting_initial_1628_0_100"]),
        "arms": int(nanhai["arms_initial_1628_0_100"]),
        "commerce": int(nanhai["commercial_prosperity_1628_0_100"]),
    }
    nanhai_top_decile = any(
        nanhai_metrics[key] >= percentile_threshold(
            [
                int(item[
                    "mining_smelting_initial_1628_0_100"
                    if key == "mining_smelting"
                    else "arms_initial_1628_0_100"
                    if key == "arms"
                    else "commercial_prosperity_1628_0_100"
                ])
                for item in economy
            ],
            0.90,
        )
        for key in nanhai_metrics
    )
    if not nanhai_top_decile:
        raise AssertionError("Nanhai metallurgy, arms or commerce must enter the top decile")

    wu = economy_by_name["吴县"]
    commercial_values = [int(item["commercial_prosperity_1628_0_100"]) for item in economy]
    mining_values = [int(item["mining_smelting_potential_0_100"]) for item in economy]
    metal_values = [int(item["metal_resource_0_100"]) for item in economy]
    if int(wu["commercial_prosperity_1628_0_100"]) < percentile_threshold(commercial_values, 0.75):
        raise AssertionError("Wu county commerce is not high")
    if int(wu["metal_resource_0_100"]) > median(metal_values) or int(
        wu["mining_smelting_potential_0_100"]
    ) > 50:
        raise AssertionError("Wu county mineral endowment/mining potential is not low")

    industry_median = median(int(item["industrial_initial_1628_0_100"]) for item in economy)
    remote_candidates: list[dict[str, Any]] = []
    for row in economy:
        county_id = str(row["county_id"])
        max_mineral = max(potential_lookup[(county_id, code)] for code in RESOURCE_BY_CODE)
        if (
            max_mineral >= 75
            and int(row["industrial_initial_1628_0_100"]) < industry_median
            and int(row["historical_anchor_count"]) == 0
        ):
            remote_candidates.append(
                {
                    "county_id": county_id,
                    "county": row["county"],
                    "max_mineral_potential": max_mineral,
                    "industrial_initial_1628": int(row["industrial_initial_1628_0_100"]),
                }
            )
    if not remote_candidates:
        raise AssertionError("no high-mineral/low-initial-industry remote county found")

    iron_deposits = [
        item
        for item in deposits
        if item["resource_code"] == "iron" and int(item["evidence_contribution_0_100"]) > 0
    ]
    mine_outputs = [(item, theoretical_mine_output(item)) for item in iron_deposits]
    low_deposit, low_output = min(mine_outputs, key=lambda pair: pair[1]["usable_metal_index"])
    high_deposit, high_output = max(mine_outputs, key=lambda pair: pair[1]["usable_metal_index"])
    ratio = high_output["usable_metal_index"] / max(0.0001, low_output["usable_metal_index"])
    if ratio < 1.20:
        raise AssertionError("deposit-level production comparison is not meaningfully different")

    with sqlite3.connect(database) as connection:
        view_count = connection.execute("SELECT COUNT(*) FROM v_county_gameplay_overview").fetchone()[0]
        view_unique = connection.execute(
            "SELECT COUNT(DISTINCT county_id) FROM v_county_gameplay_overview"
        ).fetchone()[0]
    if view_count != 1168 or view_unique != 1168:
        raise AssertionError("SQLite overview view must contain 1,168 unique counties")

    return {
        "status": "pass",
        "county_count": len(counties),
        "mineral_slot_count": len(potentials),
        "deposit_definition_count": len(deposits),
        "population_total": sum(int(item["population_est_1628"]) for item in economy),
        "sqlite_overview_count": view_count,
        "shanxi_unique_values": shanxi_variation,
        "yangcheng_vs_national_median": {
            "yangcheng": yangcheng,
            "national_median": {
                "coal": median(all_by_resource["coal"]),
                "iron": median(all_by_resource["iron"]),
            },
        },
        "fuliang_ceramics": {
            "value": fuliang,
            "top_decile_threshold": percentile_threshold(ceramics_values, 0.90),
        },
        "nanhai": nanhai_metrics,
        "wu_county": {
            "commerce": int(wu["commercial_prosperity_1628_0_100"]),
            "commerce_upper_quartile_threshold": percentile_threshold(commercial_values, 0.75),
            "mining_potential": int(wu["mining_smelting_potential_0_100"]),
            "mining_median": median(mining_values),
            "metal_resource": int(wu["metal_resource_0_100"]),
            "metal_resource_median": median(metal_values),
        },
        "high_mineral_low_industry_example": remote_candidates[0],
        "same_inputs_iron_deposit_comparison": {
            "formula": (
                "ore=base_capacity*(workers/standard_workers)^0.65*technology*equipment*management*"
                "surface_and_underground_conditions; usable=ore*grade_factor*0.65*0.75"
            ),
            "shared_inputs": {
                "workers": 100,
                "standard_workers": 100,
                "technology": 1.0,
                "equipment": 1.0,
                "management": 1.0,
            },
            "low": {"deposit_id": low_deposit["deposit_id"], **low_output},
            "high": {"deposit_id": high_deposit["deposit_id"], **high_output},
            "usable_output_ratio": round(ratio, 3),
        },
        "excluded_modern_resource_codes": ["rare_earth", "uranium", "petroleum"],
        "commercial_release_ready": NONCOMMERCIAL_MARK,
    }


def _group_rows(rows: Sequence[dict[str, Any]], key: str) -> dict[str, list[dict[str, Any]]]:
    result: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for row in rows:
        result[str(row[key])].append(row)
    return result


def verify_usgs_archive(usgs_dir: Path) -> str:
    expected = "5813ba2a93e024b273f6fd9080e3d1c5"
    archive = usgs_dir / "CHN_GIS.gdb.zip"
    if not archive.exists():
        raise RuntimeError(f"Missing {archive}; run download_usgs_mineral_inputs.py first")
    digest = hashlib.md5()  # noqa: S324 - publisher checksum is MD5
    with archive.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    actual = digest.hexdigest()
    if actual != expected:
        raise RuntimeError(f"USGS archive MD5 mismatch: {actual} != {expected}")
    return actual


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data-dir", type=Path, default=DEFAULT_DATA_DIR)
    parser.add_argument("--geography-dir", type=Path, default=DEFAULT_GEOGRAPHY_DIR)
    parser.add_argument("--usgs-dir", type=Path, default=DEFAULT_USGS_DIR)
    args = parser.parse_args()

    source_csv = args.geography_dir / "county_geography_resources_v0.1.csv"
    source_database = args.geography_dir / "game_world_1628_geography_v0.1.sqlite"
    counties = read_csv(source_csv)
    if len(counties) != 1168:
        raise RuntimeError(f"Expected 1,168 v0.1 counties, found {len(counties)}")
    checksum = verify_usgs_archive(args.usgs_dir)
    gdb = find_gdb(args.usgs_dir)

    mrds = load_mrds_deposits(counties, args.usgs_dir / "mrds_core_china.geojson")
    gdb_points = load_gdb_point_deposits(counties, gdb, mrds)
    coal_polygons = load_coal_polygon_deposits(counties, gdb)
    proxies = synthetic_proxy_deposits(counties)
    deposits = sorted(
        [*mrds, *gdb_points, *coal_polygons, *proxies],
        key=lambda item: (
            str(item["county_id"]),
            RESOURCE_ORDER[str(item["resource_code"])],
            str(item["deposit_id"]),
        ),
    )
    potentials = build_county_potentials(counties, deposits)
    anchors, missing_anchors = build_anchors(counties)
    economy, overview = build_economy(counties, potentials, anchors)

    deposit_csv = args.data_dir / "mineral_deposit_definition_v0.2.csv"
    potential_csv = args.data_dir / "county_mineral_potential_v0.2.csv"
    economy_csv = args.data_dir / "county_economy_baseline_v0.2.csv"
    overview_csv = args.data_dir / "county_gameplay_overview_v0.2.csv"
    database = args.data_dir / "game_world_1628_v0.2.sqlite"
    report_path = args.data_dir / "economy_v0.2_validation_report.json"

    write_csv(deposit_csv, DEPOSIT_COLUMNS, deposits)
    write_csv(potential_csv, POTENTIAL_COLUMNS, potentials)
    write_csv(economy_csv, ECONOMY_COLUMNS, economy)
    write_csv(overview_csv, OVERVIEW_COLUMNS, overview)
    build_sqlite(source_database, database, deposits, potentials, economy, overview, anchors)
    report = validate(counties, deposits, potentials, economy, overview, anchors, database)
    report["source_counts"] = {
        "mrds_deposit_resource_rows": len(mrds),
        "gdb_major_deposit_resource_rows": len(gdb_points),
        "gdb_coal_county_rows": len(coal_polygons),
        "synthetic_proxy_rows": len(proxies),
        "historical_anchor_rows": len(anchors),
        "missing_anchor_specs": missing_anchors,
    }
    report["usgs_china_gis_archive_md5"] = checksum
    report["deterministic_build_fingerprint"] = hashlib.sha256(
        "\n".join(str(item["deposit_id"]) for item in deposits).encode("utf-8")
    ).hexdigest()
    with report_path.open("w", encoding="utf-8") as stream:
        json.dump(report, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
