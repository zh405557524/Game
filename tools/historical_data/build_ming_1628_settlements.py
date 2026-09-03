#!/usr/bin/env python3
"""Build Project Realm's deterministic county subregion/village catalog v0.3.

The county remains the authoritative simulation unit.  Subregions and villages
are a deterministic, lazily materialized projection used when a player enters a
county.  Relative positions are rendering coordinates, not claimed historical
latitude/longitude.  Generated village names are explicitly distinguished from
names documented in historical sources.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import shutil
import sqlite3
import time
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable, Sequence


SNAPSHOT_YEAR = 1628
RULESET_VERSION = "v0.3"
EXPECTED_COUNTIES = 1_168
EXPECTED_SUBREGIONS = 6_315
EXPECTED_VILLAGES = 505_684
WEIGHT_TOTAL = 1_000_000

DEFAULT_DATA_ROOT = Path("docs/90_资料与归档/01_崇祯元年历史资料/data/1628")
DEFAULT_GEOGRAPHY_DIR = DEFAULT_DATA_ROOT / "4.地理地貌资源与天气"
DEFAULT_ECONOMY_DIR = DEFAULT_DATA_ROOT / "5.县级人口矿产与产业商业"
DEFAULT_OUTPUT_DIR = DEFAULT_DATA_ROOT / "6.县内区域与村庄"

NONCOMMERCIAL_MARK = "no"
POSITION_METHOD = "synthetic_relative"
GENERATION_METHOD = "deterministic county-detail projection v0.3"

REGION_ORDER = [
  "北直隶（京师）",
  "南直隶（南京）",
  "山东",
  "山西",
  "河南",
  "陕西",
  "四川",
  "江西",
  "湖广",
  "浙江",
  "福建",
  "广东",
  "广西",
  "云南",
  "贵州",
]
REGION_INDEX = {region: index for index, region in enumerate(REGION_ORDER, 1)}

DIRECTION_ORDER = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"]
DIRECTION_NAME = {
  "C": "中部",
  "N": "北部",
  "NE": "东北部",
  "E": "东部",
  "SE": "东南部",
  "S": "南部",
  "SW": "西南部",
  "W": "西部",
  "NW": "西北部",
}
DIRECTION_CENTER = {
  "C": (5_000, 5_000),
  "N": (5_000, 2_000),
  "NE": (7_350, 2_650),
  "E": (8_000, 5_000),
  "SE": (7_350, 7_350),
  "S": (5_000, 8_000),
  "SW": (2_650, 7_350),
  "W": (2_000, 5_000),
  "NW": (2_650, 2_650),
}

TERRAIN_SPECS = {
  "plain_pct": {
    "code": "plain_agriculture",
    "name": "平原农耕",
    "terrain": "平原",
    "biome": "temperate_plain_farmland",
    "water": "inland",
    "population_factor": 1.35,
    "farmland_factor": 1.70,
  },
  "hill_pct": {
    "code": "hill_forest",
    "name": "丘陵林田",
    "terrain": "丘陵",
    "biome": "wooded_hills",
    "water": "stream",
    "population_factor": 0.90,
    "farmland_factor": 0.75,
  },
  "mountain_pct": {
    "code": "mountain_forest",
    "name": "山地林矿",
    "terrain": "山地",
    "biome": "mountain_forest",
    "water": "mountain_stream",
    "population_factor": 0.62,
    "farmland_factor": 0.35,
  },
  "plateau_pct": {
    "code": "plateau_pasture",
    "name": "高原农牧",
    "terrain": "高原",
    "biome": "plateau_steppe",
    "water": "seasonal_stream",
    "population_factor": 0.72,
    "farmland_factor": 0.52,
  },
  "basin_valley_pct": {
    "code": "basin_valley",
    "name": "盆地河谷",
    "terrain": "盆地/河谷",
    "biome": "river_valley_farmland",
    "water": "river_valley",
    "population_factor": 1.30,
    "farmland_factor": 1.45,
  },
  "grassland_pct": {
    "code": "grassland_pasture",
    "name": "草原牧业",
    "terrain": "草原",
    "biome": "temperate_grassland",
    "water": "seasonal_stream",
    "population_factor": 0.48,
    "farmland_factor": 0.18,
  },
  "desert_pct": {
    "code": "desert_oasis",
    "name": "荒漠绿洲",
    "terrain": "荒漠",
    "biome": "arid_oasis",
    "water": "oasis",
    "population_factor": 0.36,
    "farmland_factor": 0.22,
  },
  "wetland_lake_pct": {
    "code": "wetland_fishery",
    "name": "湖泊湿地",
    "terrain": "湖泊/湿地",
    "biome": "lake_wetland",
    "water": "lake_wetland",
    "population_factor": 1.05,
    "farmland_factor": 0.78,
  },
  "coast_island_pct": {
    "code": "coast_fishery",
    "name": "滨海渔盐",
    "terrain": "海岸/岛屿",
    "biome": "coastal_estuary",
    "water": "coast",
    "population_factor": 0.88,
    "farmland_factor": 0.40,
  },
}

RESOURCE_COLUMNS = {
  "agriculture": ("agriculture_resource_0_100", "农业"),
  "forest": ("forest_resource_0_100", "林业"),
  "pasture": ("pasture_resource_0_100", "牧业"),
  "fishery": ("fishery_resource_0_100", "渔业"),
  "salt": ("salt_resource_0_100", "盐业"),
  "fuel": ("fuel_resource_0_100", "燃料"),
  "metal": ("metal_resource_0_100", "金属"),
  "building": ("building_material_resource_0_100", "建材"),
}

ZONE_RESOURCE_MODIFIERS = {
  "county_core": {
    "agriculture": -8, "forest": -10, "pasture": -12, "fishery": -4,
    "salt": -4, "fuel": 2, "metal": 0, "building": 10,
  },
  "plain_agriculture": {
    "agriculture": 18, "forest": -12, "pasture": -4, "fishery": 0,
    "salt": -2, "fuel": -4, "metal": -5, "building": 0,
  },
  "hill_forest": {
    "agriculture": -2, "forest": 14, "pasture": 2, "fishery": -5,
    "salt": -5, "fuel": 3, "metal": 4, "building": 7,
  },
  "mountain_forest": {
    "agriculture": -14, "forest": 22, "pasture": 5, "fishery": -8,
    "salt": -8, "fuel": 8, "metal": 13, "building": 15,
  },
  "plateau_pasture": {
    "agriculture": -8, "forest": -2, "pasture": 20, "fishery": -8,
    "salt": 2, "fuel": 4, "metal": 5, "building": 4,
  },
  "basin_valley": {
    "agriculture": 14, "forest": -3, "pasture": -1, "fishery": 6,
    "salt": 0, "fuel": -2, "metal": -4, "building": 1,
  },
  "grassland_pasture": {
    "agriculture": -16, "forest": -12, "pasture": 26, "fishery": -10,
    "salt": 1, "fuel": 0, "metal": 2, "building": -2,
  },
  "desert_oasis": {
    "agriculture": -18, "forest": -20, "pasture": 4, "fishery": -12,
    "salt": 14, "fuel": 2, "metal": 4, "building": 2,
  },
  "wetland_fishery": {
    "agriculture": 5, "forest": -8, "pasture": -10, "fishery": 26,
    "salt": 1, "fuel": -8, "metal": -10, "building": -4,
  },
  "coast_fishery": {
    "agriculture": -6, "forest": -7, "pasture": -10, "fishery": 25,
    "salt": 28, "fuel": -6, "metal": -8, "building": -2,
  },
  "river_transport": {
    "agriculture": 7, "forest": -4, "pasture": -5, "fishery": 17,
    "salt": 0, "fuel": -3, "metal": -5, "building": 1,
  },
  "mining_zone": {
    "agriculture": -12, "forest": 3, "pasture": -2, "fishery": -8,
    "salt": 0, "fuel": 22, "metal": 26, "building": 14,
  },
  "mixed_interior": {
    "agriculture": 2, "forest": 1, "pasture": 0, "fishery": 0,
    "salt": 0, "fuel": 0, "metal": 0, "building": 1,
  },
}

SUMMARY_COLUMNS = [
  "county_id", "snapshot_year", "region", "upper_unit", "intermediate_unit", "county",
  "population_est_1628", "urban_population_est", "rural_population_est",
  "area_km2_est", "population_density_per_km2", "target_village_population",
  "subregion_count", "village_count", "mean_rural_population_per_village",
  "documented_village_name_count", "generated_village_name_count",
  "calculation_method", "commercial_release_ready",
]

SUBREGION_COLUMNS = [
  "subregion_id", "snapshot_year", "county_id", "region", "upper_unit",
  "intermediate_unit", "county", "subregion_name", "direction_code", "direction_name",
  "zone_type", "primary_landform", "secondary_landform", "water_context",
  "major_water_feature", "climate_zone", "primary_resource_tags",
  "area_share_ppm", "population_share_ppm", "farmland_share_ppm", "village_count",
  "center_rel_x_0_10000", "center_rel_y_0_10000", "render_biome_code", "render_seed",
  *[column for column, _ in RESOURCE_COLUMNS.values()],
  "generation_method", "data_quality", "commercial_release_ready",
]

VILLAGE_CSV_COLUMNS = [
  "village_id", "snapshot_year", "region", "county_id", "county", "subregion_id",
  "subregion_name", "village_name", "settlement_form", "name_source_type",
  "historical_name_claim", "anchor_id", "relative_x_0_10000", "relative_y_0_10000",
  "population_weight_ppm", "farmland_weight_ppm", "render_seed", "position_method",
  "commercial_release_ready",
]

VILLAGE_DB_COLUMNS = [
  "village_id", "snapshot_year", "county_id", "subregion_id", "village_name",
  "settlement_form", "name_source_type", "historical_name_claim", "anchor_id",
  "relative_x_0_10000", "relative_y_0_10000", "population_weight_ppm",
  "farmland_weight_ppm", "render_seed", "position_method", "commercial_release_ready",
]

ANCHOR_COLUMNS = [
  "anchor_id", "county_id", "village_name", "settlement_form", "name_source_type",
  "evidence_period", "direction_hint", "preferred_zone_type", "source_title",
  "source_reference", "source_url", "evidence_grade", "notes",
]

RULE_COLUMNS = [
  "rule_id", "region", "settlement_forms", "water_terms", "landform_terms",
  "local_terms", "notes", "ruleset_version",
]

MANIFEST_COLUMNS = [
  "part_index", "region", "file", "row_count", "county_count", "size_bytes", "sha256",
]

COMMON_SURNAMES = list(
  "赵钱孙李周吴郑王冯陈褚卫蒋沈韩杨朱秦尤许何吕施张孔曹严华金魏陶姜"
  "戚谢邹喻柏水窦章云苏潘葛奚范彭郎鲁韦昌马苗凤花方俞任袁柳鲍史唐"
  "费廉岑薛雷贺倪汤滕殷罗毕郝邬安常乐于傅皮卞齐康伍余元卜顾孟平黄"
  "和穆萧尹姚邵湛汪祁毛禹狄米贝明臧计伏成戴宋茅庞熊纪舒屈项祝董梁"
  "杜阮蓝闵席季麻强贾路娄危江童颜郭梅盛林刁钟徐邱骆高夏蔡田樊胡凌"
  "霍虞万支柯管卢莫经房裘缪解应宗丁宣邓郁单杭洪包诸左石崔吉钮龚程"
  "嵇邢裴陆荣翁荀羊甄曲封芮储靳汲邴糜松井段富巫乌焦巴弓牧隗山谷车"
  "侯宓蓬全班仰秋仲伊宫宁仇栾暴甘厉戎祖武符刘景詹束龙叶幸司韶郜黎"
  "蓟薄印宿白怀蒲邰从鄂索咸籍赖卓蔺屠蒙池乔阴胥能苍双闻莘党翟谭贡"
  "劳逄姬申扶堵冉宰郦雍郤璩桑桂濮牛寿通边燕冀郏浦尚农温别庄晏柴瞿"
  "阎充慕连茹习宦艾鱼容向古易慎戈廖庾终暨居衡步都耿满弘匡国文寇广"
  "禄阙东欧利师巩聂晁勾敖融冷辛阚那简饶空曾毋沙乜养鞠须丰巢关蒯相"
  "查后荆红游竺权盖益桓公"
)

NAME_PREFIXES = ["东", "西", "南", "北", "上", "下", "前", "后", "中", "新", "老", "大", "小"]
NAME_ADJECTIVES = ["青", "白", "红", "黄", "长", "清", "平", "安", "永", "兴", "福", "瑞"]
CHINESE_NUMERALS = ["二", "三", "四", "五", "六", "七", "八", "九", "十", "十二", "十八", "二十四"]


@dataclass(frozen=True)
class ZoneCandidate:
  code: str
  label: str
  terrain: str
  biome: str
  water_context: str
  area_weight: float
  population_factor: float
  farmland_factor: float
  priority_bonus: float = 0.0


def clamp(value: float, low: float, high: float) -> float:
  return max(low, min(high, value))


def number(row: dict[str, str], key: str, default: float = 0.0) -> float:
  value = row.get(key, "")
  if value in {"", None}:
    return default
  return float(value)


def stable_unit(key: str) -> float:
  digest = hashlib.sha256(key.encode("utf-8")).digest()
  return int.from_bytes(digest[:8], "big") / (2**64 - 1)


def stable_factor(key: str, low: float, high: float) -> float:
  return low + (high - low) * stable_unit(key)


def stable_int(key: str, low: int, high: int) -> int:
  if high < low:
    raise ValueError(f"Invalid deterministic range: {low}..{high}")
  return low + int(stable_unit(key) * (high - low + 1)) % (high - low + 1)


def stable_choice(key: str, values: Sequence[str]) -> str:
  if not values:
    raise ValueError("Cannot choose from an empty sequence")
  return values[stable_int(key, 0, len(values) - 1)]


def read_csv(path: Path) -> list[dict[str, str]]:
  with path.open(encoding="utf-8-sig", newline="") as stream:
    return list(csv.DictReader(stream))


def write_csv_atomic(path: Path, columns: Sequence[str], rows: Iterable[dict[str, Any]]) -> None:
  path.parent.mkdir(parents=True, exist_ok=True)
  temporary = path.with_suffix(path.suffix + ".tmp")
  with temporary.open("w", encoding="utf-8", newline="") as stream:
    writer = csv.DictWriter(stream, fieldnames=columns, lineterminator="\n", extrasaction="ignore")
    writer.writeheader()
    writer.writerows(rows)
  temporary.replace(path)


def file_sha256(path: Path) -> str:
  digest = hashlib.sha256()
  with path.open("rb") as stream:
    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
      digest.update(chunk)
  return digest.hexdigest()


def allocate_exact(total: int, weights: Sequence[float], minimum: int = 0) -> list[int]:
  if not weights:
    return []
  if minimum * len(weights) > total:
    minimum = 0
  positive = [max(0.0, float(value)) for value in weights]
  weight_sum = sum(positive)
  if weight_sum <= 0:
    positive = [1.0] * len(weights)
    weight_sum = float(len(weights))
  remaining = total - minimum * len(weights)
  exact = [remaining * value / weight_sum for value in positive]
  result = [minimum + math.floor(value) for value in exact]
  remainder = total - sum(result)
  order = sorted(range(len(weights)), key=lambda index: (-(exact[index] - math.floor(exact[index])), index))
  for index in order[:remainder]:
    result[index] += 1
  if sum(result) != total:
    raise RuntimeError(f"Allocation mismatch: {sum(result)} != {total}")
  return result


def direction_from_points(county: dict[str, str], longitude: float, latitude: float) -> str | None:
  county_lon = number(county, "longitude")
  county_lat = number(county, "latitude")
  dx = (longitude - county_lon) * math.cos(math.radians(county_lat))
  dy = latitude - county_lat
  if math.hypot(dx, dy) < 0.01:
    return None
  angle = (math.degrees(math.atan2(dx, dy)) + 360) % 360
  index = int((angle + 22.5) // 45) % 8
  return DIRECTION_ORDER[index]


def shuffled_directions(county_id: str) -> list[str]:
  return sorted(DIRECTION_ORDER, key=lambda direction: stable_unit(f"{county_id}|direction|{direction}"))


def target_village_population(economy: dict[str, str], geography: dict[str, str]) -> int:
  region = economy["region"]
  plain = number(geography, "plain_pct")
  hill = number(geography, "hill_pct")
  mountain = number(geography, "mountain_pct")
  plateau = number(geography, "plateau_pct")
  basin = number(geography, "basin_valley_pct")
  grassland = number(geography, "grassland_pct")
  desert = number(geography, "desert_pct")
  wetland = number(geography, "wetland_lake_pct")
  if region in {"南直隶（南京）", "浙江", "江西"} and plain + basin + wetland >= 55:
    baseline = 350
  elif mountain + hill >= 65:
    baseline = 320
  elif plateau + grassland + desert >= 55:
    baseline = 300
  elif basin >= 25:
    baseline = 400
  elif plain >= 60:
    baseline = 500
  else:
    baseline = 410
  density = max(10.0, number(economy, "population_density_per_km2"))
  density_factor = clamp((density / 100) ** 0.08, 0.86, 1.16)
  county_factor = stable_factor(economy["county_id"], 0.92, 1.08)
  return round(baseline * density_factor * county_factor)


def subregion_count(economy: dict[str, str], geography: dict[str, str]) -> int:
  area = number(economy, "area_km2_est")
  terrain_diversity = sum(
    number(geography, field) >= 12
    for field in TERRAIN_SPECS
  )
  count = (
    4
    + int(area >= 1_200)
    + int(area >= 3_000)
    + int(terrain_diversity >= 3)
    + int(number(economy, "resource_diversity_0_100") >= 55)
  )
  return int(clamp(count, 4, 8))


def village_count(economy: dict[str, str], geography: dict[str, str]) -> tuple[int, int, int]:
  rural = int(economy["population_est_1628"]) - int(economy["urban_population_est"])
  target = target_village_population(economy, geography)
  return max(20, round(rural / target)), rural, target


def mineral_direction(
  economy: dict[str, str],
  deposits: Sequence[dict[str, str]],
) -> str | None:
  physical = [
    row for row in deposits
    if row.get("is_synthetic_proxy") == "no"
    and row.get("longitude") not in {"", None}
    and row.get("latitude") not in {"", None}
  ]
  physical.sort(
    key=lambda row: (
      -number(row, "evidence_contribution_0_100"),
      -number(row, "size_index_0_100"),
      row.get("deposit_id", ""),
    )
  )
  for row in physical:
    direction = direction_from_points(economy, float(row["longitude"]), float(row["latitude"]))
    if direction:
      return direction
  return None


def make_zone_candidates(
  economy: dict[str, str],
  geography: dict[str, str],
  deposits: Sequence[dict[str, str]],
) -> list[ZoneCandidate]:
  urbanization = number(economy, "urbanization_rate_0_100")
  candidates = [
    ZoneCandidate(
      code="county_core",
      label="县城近郊",
      terrain=geography.get("primary_landform", "混合地貌"),
      biome="county_seat_hinterland",
      water_context="market_and_roads",
      area_weight=6 + urbanization * 0.25,
      population_factor=1.75,
      farmland_factor=0.72,
      priority_bonus=100,
    )
  ]
  for field, spec in TERRAIN_SPECS.items():
    share = number(geography, field)
    if share <= 0:
      continue
    candidates.append(
      ZoneCandidate(
        code=str(spec["code"]),
        label=str(spec["name"]),
        terrain=str(spec["terrain"]),
        biome=str(spec["biome"]),
        water_context=str(spec["water"]),
        area_weight=max(1.0, share),
        population_factor=float(spec["population_factor"]),
        farmland_factor=float(spec["farmland_factor"]),
        priority_bonus=8 if share >= 20 else 3 if share >= 10 else 0,
      )
    )

  freshwater = number(geography, "freshwater_index_1_5")
  has_river = bool(
    geography.get("major_river_systems")
    or geography.get("nearest_mapped_major_river")
    or freshwater >= 3
  )
  if has_river:
    candidates.append(
      ZoneCandidate(
        code="river_transport",
        label="河谷水运",
        terrain="河流/河谷",
        biome="river_transport_corridor",
        water_context="major_river_or_canal",
        area_weight=max(5.0, 3 + freshwater * 2 + number(geography, "wetland_lake_pct") * 0.20),
        population_factor=1.25,
        farmland_factor=1.10,
        priority_bonus=8,
      )
    )

  physical_count = sum(row.get("is_synthetic_proxy") == "no" for row in deposits)
  extractive_score = max(
    number(economy, "metal_resource_0_100"),
    number(economy, "fuel_resource_0_100"),
    number(economy, "building_material_resource_0_100"),
    number(economy, "salt_resource_0_100"),
  )
  if physical_count or extractive_score >= 25:
    candidates.append(
      ZoneCandidate(
        code="mining_zone",
        label="矿产采掘",
        terrain=geography.get("secondary_landform") or geography.get("primary_landform", "混合地貌"),
        biome="extractive_hinterland",
        water_context="mine_stream_or_well",
        area_weight=max(5.0, 4 + extractive_score * 0.12),
        population_factor=0.58,
        farmland_factor=0.28,
        priority_bonus=12 if physical_count else 5,
      )
    )

  candidates.append(
    ZoneCandidate(
      code="mixed_interior",
      label="内陆村田",
      terrain=geography.get("primary_landform", "混合地貌"),
      biome="mixed_rural_hinterland",
      water_context="local_streams_and_wells",
      area_weight=12,
      population_factor=0.95,
      farmland_factor=0.90,
      priority_bonus=1,
    )
  )
  return candidates


def select_zone_candidates(
  economy: dict[str, str],
  candidates: Sequence[ZoneCandidate],
  count: int,
) -> list[ZoneCandidate]:
  core = next(candidate for candidate in candidates if candidate.code == "county_core")
  others = [candidate for candidate in candidates if candidate.code != "county_core"]
  others.sort(
    key=lambda candidate: (
      -(candidate.area_weight + candidate.priority_bonus),
      stable_unit(f"{economy['county_id']}|zone-priority|{candidate.code}"),
      candidate.code,
    )
  )
  selected = [core, *others[: count - 1]]
  fallback_index = 1
  while len(selected) < count:
    selected.append(
      ZoneCandidate(
        code=f"mixed_interior_{fallback_index}",
        label=f"村田片区{fallback_index}",
        terrain="混合地貌",
        biome="mixed_rural_hinterland",
        water_context="local_streams_and_wells",
        area_weight=8,
        population_factor=0.90,
        farmland_factor=0.85,
      )
    )
    fallback_index += 1
  return selected


def zone_profile(code: str) -> str:
  if code.startswith("mixed_interior"):
    return "mixed_interior"
  return code


def assign_zone_directions(
  economy: dict[str, str],
  selected: Sequence[ZoneCandidate],
  deposits: Sequence[dict[str, str]],
) -> list[str]:
  result = ["C"]
  unused = shuffled_directions(economy["county_id"])
  desired_mining = mineral_direction(economy, deposits)
  for candidate in selected[1:]:
    desired = desired_mining if candidate.code == "mining_zone" else None
    if desired and desired in unused:
      direction = desired
      unused.remove(desired)
    else:
      direction = unused.pop(0)
    result.append(direction)
  return result


def calibrated_resource_scores(
  economy: dict[str, str],
  selected: Sequence[ZoneCandidate],
  area_shares: Sequence[int],
) -> dict[str, list[int]]:
  output: dict[str, list[int]] = {}
  for resource, (column, _) in RESOURCE_COLUMNS.items():
    baseline = int(number(economy, column))
    raw: list[float] = []
    for candidate in selected:
      modifier = ZONE_RESOURCE_MODIFIERS[zone_profile(candidate.code)][resource]
      jitter = stable_int(
        f"{economy['county_id']}|{candidate.code}|resource|{resource}",
        -4,
        4,
      )
      raw.append(clamp(baseline + modifier + jitter, 0, 100))
    raw_average = sum(value * share for value, share in zip(raw, area_shares)) / WEIGHT_TOTAL
    shift = baseline - raw_average
    scores = [round(clamp(value + shift, 0, 100)) for value in raw]
    for _ in range(200):
      average = sum(value * share for value, share in zip(scores, area_shares)) / WEIGHT_TOTAL
      if abs(average - baseline) <= 0.50:
        break
      change = 1 if average < baseline else -1
      adjustable = [
        index for index, value in enumerate(scores)
        if 0 <= value + change <= 100
      ]
      if not adjustable:
        break
      best = max(adjustable, key=lambda index: (area_shares[index], -index))
      scores[best] += change
    output[resource] = scores
  return output


def zone_major_water_feature(candidate: ZoneCandidate, geography: dict[str, str]) -> str:
  profile = zone_profile(candidate.code)
  if profile == "river_transport":
    return (
      geography.get("major_river_systems")
      or geography.get("nearest_mapped_major_river")
      or geography.get("river_basin")
      or "地方河渠"
    )
  if profile == "wetland_fishery":
    return geography.get("major_lakes_wetlands") or geography.get("nearest_mapped_major_lake") or "地方湖荡"
  if profile == "coast_fishery":
    return "海岸/河口"
  return candidate.water_context


def build_subregions(
  economy: dict[str, str],
  geography: dict[str, str],
  deposits: Sequence[dict[str, str]],
  count: int,
  total_villages: int,
) -> list[dict[str, Any]]:
  candidates = make_zone_candidates(economy, geography, deposits)
  selected = select_zone_candidates(economy, candidates, count)
  directions = assign_zone_directions(economy, selected, deposits)

  area_weights = [candidate.area_weight for candidate in selected]
  population_weights = [
    candidate.area_weight
    * candidate.population_factor
    * stable_factor(f"{economy['county_id']}|{candidate.code}|population", 0.92, 1.08)
    for candidate in selected
  ]
  farmland_weights = [
    candidate.area_weight
    * candidate.farmland_factor
    * stable_factor(f"{economy['county_id']}|{candidate.code}|farmland", 0.92, 1.08)
    for candidate in selected
  ]
  area_shares = allocate_exact(WEIGHT_TOTAL, area_weights, minimum=1)
  population_shares = allocate_exact(WEIGHT_TOTAL, population_weights, minimum=1)
  farmland_shares = allocate_exact(WEIGHT_TOTAL, farmland_weights, minimum=1)
  village_counts = allocate_exact(total_villages, population_weights, minimum=1)
  resource_scores = calibrated_resource_scores(economy, selected, area_shares)

  zones: list[dict[str, Any]] = []
  for index, (candidate, direction) in enumerate(zip(selected, directions), 1):
    center_x, center_y = DIRECTION_CENTER[direction]
    if direction != "C":
      center_x += stable_int(f"{economy['county_id']}|{candidate.code}|x", -420, 420)
      center_y += stable_int(f"{economy['county_id']}|{candidate.code}|y", -420, 420)
    center_x = round(clamp(center_x, 500, 9_500))
    center_y = round(clamp(center_y, 500, 9_500))
    scores = {resource: resource_scores[resource][index - 1] for resource in RESOURCE_COLUMNS}
    ranked_resources = sorted(
      RESOURCE_COLUMNS,
      key=lambda resource: (-scores[resource], resource),
    )
    resource_tags = ";".join(
      RESOURCE_COLUMNS[resource][1]
      for resource in ranked_resources[:3]
      if scores[resource] > 0
    ) or "无显著资源"
    subregion_id = f"{economy['county_id']}-Z{index:02d}"
    row: dict[str, Any] = {
      "subregion_id": subregion_id,
      "snapshot_year": SNAPSHOT_YEAR,
      "county_id": economy["county_id"],
      "region": economy["region"],
      "upper_unit": economy["upper_unit"],
      "intermediate_unit": economy.get("intermediate_unit", ""),
      "county": economy["county"],
      "subregion_name": "县城近郊区" if candidate.code == "county_core" else f"{DIRECTION_NAME[direction]}{candidate.label}区",
      "direction_code": direction,
      "direction_name": DIRECTION_NAME[direction],
      "zone_type": zone_profile(candidate.code),
      "primary_landform": candidate.terrain,
      "secondary_landform": geography.get("secondary_landform", ""),
      "water_context": candidate.water_context,
      "major_water_feature": zone_major_water_feature(candidate, geography),
      "climate_zone": geography.get("climate_zone", ""),
      "primary_resource_tags": resource_tags,
      "area_share_ppm": area_shares[index - 1],
      "population_share_ppm": population_shares[index - 1],
      "farmland_share_ppm": farmland_shares[index - 1],
      "village_count": village_counts[index - 1],
      "center_rel_x_0_10000": center_x,
      "center_rel_y_0_10000": center_y,
      "render_biome_code": candidate.biome,
      "render_seed": hashlib.sha256(f"{subregion_id}|render|{RULESET_VERSION}".encode()).hexdigest()[:16],
      "generation_method": GENERATION_METHOD,
      "data_quality": "generated_game_baseline_v0.3",
      "commercial_release_ready": NONCOMMERCIAL_MARK,
    }
    for resource, (column, _) in RESOURCE_COLUMNS.items():
      row[column] = scores[resource]
    zones.append(row)
  return zones


def parse_rule_rows(rows: Sequence[dict[str, str]]) -> dict[str, dict[str, Any]]:
  output: dict[str, dict[str, Any]] = {}
  for row in rows:
    region = row["region"]
    parsed: dict[str, Any] = dict(row)
    for column in ["settlement_forms", "water_terms", "landform_terms", "local_terms"]:
      parsed[f"_{column}"] = [item for item in row[column].split(";") if item]
    output[region] = parsed
  missing = [region for region in REGION_ORDER if region not in output]
  if missing:
    raise RuntimeError(f"Missing settlement naming rules: {missing}")
  return output


def validate_anchor_rows(
  rows: Sequence[dict[str, str]],
  counties: dict[str, dict[str, str]],
) -> dict[str, list[dict[str, str]]]:
  by_county: dict[str, list[dict[str, str]]] = defaultdict(list)
  seen_ids: set[str] = set()
  seen_names: set[tuple[str, str]] = set()
  for row in rows:
    anchor_id = row["anchor_id"]
    county_id = row["county_id"]
    key = (county_id, row["village_name"])
    if anchor_id in seen_ids:
      raise RuntimeError(f"Duplicate historical village anchor id: {anchor_id}")
    if key in seen_names:
      raise RuntimeError(f"Duplicate historical village name in county: {key}")
    if county_id not in counties:
      raise RuntimeError(f"Historical village anchor references unknown county: {county_id}")
    if row["name_source_type"] not in {"documented_pre_1628", "documented_continuity"}:
      raise RuntimeError(f"Invalid documented name source type: {anchor_id}")
    if not row.get("source_title") or not row.get("source_reference") or not row.get("source_url"):
      raise RuntimeError(f"Historical village anchor lacks source fields: {anchor_id}")
    if row.get("direction_hint") and row["direction_hint"] not in DIRECTION_NAME:
      raise RuntimeError(f"Invalid direction hint for {anchor_id}: {row['direction_hint']}")
    seen_ids.add(anchor_id)
    seen_names.add(key)
    by_county[county_id].append(dict(row))
  for county_rows in by_county.values():
    county_rows.sort(key=lambda row: row["anchor_id"])
  return by_county


def append_form(root: str, form: str) -> str:
  if not root:
    return form
  if root.endswith(form):
    return root
  return f"{root}{form}"


def generated_village_name(
  village_id: str,
  rule: dict[str, Any],
  used_names: set[str],
) -> tuple[str, str]:
  forms: list[str] = rule["_settlement_forms"]
  waters: list[str] = rule["_water_terms"]
  landforms: list[str] = rule["_landform_terms"]
  local_terms: list[str] = rule["_local_terms"]
  for attempt in range(256):
    key = f"{village_id}|name|{RULESET_VERSION}|{attempt}"
    form = stable_choice(f"{key}|form", forms)
    surname = stable_choice(f"{key}|surname", COMMON_SURNAMES)
    second_surname = stable_choice(f"{key}|surname2", COMMON_SURNAMES)
    water = stable_choice(f"{key}|water", waters)
    landform = stable_choice(f"{key}|landform", landforms)
    local = stable_choice(f"{key}|local", local_terms)
    prefix = stable_choice(f"{key}|prefix", NAME_PREFIXES)
    adjective = stable_choice(f"{key}|adjective", NAME_ADJECTIVES)
    numeral = stable_choice(f"{key}|numeral", CHINESE_NUMERALS)
    pattern = stable_int(f"{key}|pattern", 0, 11)
    if pattern == 0:
      root = f"{surname}家"
      name = append_form(root, form)
    elif pattern == 1:
      root = f"{prefix}{surname}"
      name = append_form(root, form)
    elif pattern == 2:
      root = local
      name = append_form(root, form)
    elif pattern == 3:
      form = water
      name = f"{surname}家{water}"
    elif pattern == 4:
      root = f"{adjective}{landform}"
      name = append_form(root, form)
    elif pattern == 5:
      root = f"{prefix}{local}"
      name = append_form(root, form)
    elif pattern == 6:
      form = landform
      name = f"{surname}家{landform}"
    elif pattern == 7:
      root = f"{numeral}里"
      name = append_form(root, form)
    elif pattern == 8:
      root = f"{surname}{second_surname}"
      name = append_form(root, form)
    elif pattern == 9:
      root = f"{prefix}{water}"
      name = append_form(root, form)
    elif pattern == 10:
      root = f"{adjective}{water}"
      name = append_form(root, form)
    else:
      root = f"{prefix}{landform}"
      name = append_form(root, form)
    if name not in used_names:
      return name, form
  ordinal = int(village_id.rsplit("V", 1)[-1])
  form = forms[ordinal % len(forms)]
  name = f"{COMMON_SURNAMES[ordinal % len(COMMON_SURNAMES)]}{ordinal}里{form}"
  if name in used_names:
    raise RuntimeError(f"Unable to generate a unique village name for {village_id}")
  return name, form


def anchor_zone(
  anchor: dict[str, str],
  zones: Sequence[dict[str, Any]],
) -> dict[str, Any]:
  direction = anchor.get("direction_hint", "")
  preferred = anchor.get("preferred_zone_type", "")
  return max(
    zones,
    key=lambda zone: (
      100 if preferred and zone["zone_type"] == preferred else 0,
      80 if direction and zone["direction_code"] == direction else 0,
      10 if zone["zone_type"] != "county_core" else 0,
      zone["population_share_ppm"],
      -int(zone["subregion_id"].rsplit("Z", 1)[-1]),
    ),
  )


def village_position(village_id: str, zone: dict[str, Any]) -> tuple[int, int]:
  center_x = int(zone["center_rel_x_0_10000"])
  center_y = int(zone["center_rel_y_0_10000"])
  angle = stable_unit(f"{village_id}|position-angle") * math.tau
  radius_limit = 550 + 1_450 * math.sqrt(int(zone["area_share_ppm"]) / WEIGHT_TOTAL)
  radius = math.sqrt(stable_unit(f"{village_id}|position-radius")) * radius_limit
  x = round(clamp(center_x + math.cos(angle) * radius, 100, 9_900))
  y = round(clamp(center_y + math.sin(angle) * radius, 100, 9_900))
  return x, y


def build_county_villages(
  economy: dict[str, str],
  zones: Sequence[dict[str, Any]],
  anchors: Sequence[dict[str, str]],
  rule: dict[str, Any],
) -> list[dict[str, Any]]:
  anchors_by_zone: dict[str, list[dict[str, str]]] = defaultdict(list)
  for anchor in anchors:
    zone = anchor_zone(anchor, zones)
    anchors_by_zone[zone["subregion_id"]].append(anchor)
  for rows in anchors_by_zone.values():
    rows.sort(key=lambda row: row["anchor_id"])

  rows: list[dict[str, Any]] = []
  used_names: set[str] = set()
  village_ordinal = 0
  for zone in zones:
    count = int(zone["village_count"])
    zone_anchors = anchors_by_zone.get(zone["subregion_id"], [])
    if len(zone_anchors) > count:
      raise RuntimeError(f"More anchors than village slots in {zone['subregion_id']}")
    local_rows: list[dict[str, Any]] = []
    for local_ordinal in range(1, count + 1):
      village_ordinal += 1
      village_id = f"{economy['county_id']}-V{village_ordinal:04d}"
      anchor = zone_anchors[local_ordinal - 1] if local_ordinal <= len(zone_anchors) else None
      if anchor:
        village_name = anchor["village_name"]
        settlement_form = anchor["settlement_form"]
        name_source_type = anchor["name_source_type"]
        historical_name_claim = "yes"
        anchor_id = anchor["anchor_id"]
        if village_name in used_names:
          raise RuntimeError(f"Duplicate documented village name in {economy['county_id']}: {village_name}")
      else:
        village_name, settlement_form = generated_village_name(village_id, rule, used_names)
        name_source_type = "generated_period_style"
        historical_name_claim = "no"
        anchor_id = ""
      used_names.add(village_name)
      relative_x, relative_y = village_position(village_id, zone)
      local_rows.append(
        {
          "village_id": village_id,
          "snapshot_year": SNAPSHOT_YEAR,
          "region": economy["region"],
          "county_id": economy["county_id"],
          "county": economy["county"],
          "subregion_id": zone["subregion_id"],
          "subregion_name": zone["subregion_name"],
          "village_name": village_name,
          "settlement_form": settlement_form,
          "name_source_type": name_source_type,
          "historical_name_claim": historical_name_claim,
          "anchor_id": anchor_id,
          "relative_x_0_10000": relative_x,
          "relative_y_0_10000": relative_y,
          "render_seed": hashlib.sha256(f"{village_id}|render|{RULESET_VERSION}".encode()).hexdigest()[:16],
          "position_method": POSITION_METHOD,
          "commercial_release_ready": NONCOMMERCIAL_MARK,
        }
      )
    population_weights = [
      stable_factor(f"{row['village_id']}|population-weight", 0.72, 1.28)
      for row in local_rows
    ]
    farmland_weights = [
      stable_factor(f"{row['village_id']}|farmland-weight", 0.62, 1.38)
      for row in local_rows
    ]
    population_allocations = allocate_exact(
      int(zone["population_share_ppm"]),
      population_weights,
      minimum=1,
    )
    farmland_allocations = allocate_exact(
      int(zone["farmland_share_ppm"]),
      farmland_weights,
      minimum=1,
    )
    for row, population_weight, farmland_weight in zip(
      local_rows,
      population_allocations,
      farmland_allocations,
    ):
      row["population_weight_ppm"] = population_weight
      row["farmland_weight_ppm"] = farmland_weight
    rows.extend(local_rows)

  if len(rows) != sum(int(zone["village_count"]) for zone in zones):
    raise RuntimeError(f"Village row mismatch for {economy['county_id']}")
  if sum(int(row["population_weight_ppm"]) for row in rows) != WEIGHT_TOTAL:
    raise RuntimeError(f"Village population weights do not sum for {economy['county_id']}")
  if sum(int(row["farmland_weight_ppm"]) for row in rows) != WEIGHT_TOTAL:
    raise RuntimeError(f"Village farmland weights do not sum for {economy['county_id']}")
  return rows


def sqlite_type(column: str) -> str:
  if column in {
    "area_km2_est",
    "population_density_per_km2",
    "mean_rural_population_per_village",
  }:
    return "REAL"
  if (
    column == "snapshot_year"
    or column.endswith("_ppm")
    or column.endswith("_count")
    or column.endswith("_est")
    or column.endswith("_population")
    or column.endswith("_0_100")
    or column.endswith("_0_10000")
    or column in {"target_village_population", "village_count", "subregion_count"}
  ):
    return "INTEGER"
  return "TEXT"


def create_table(
  connection: sqlite3.Connection,
  table: str,
  columns: Sequence[str],
  primary_key: Sequence[str],
  foreign_keys: Sequence[tuple[str, str, str]] = (),
) -> None:
  connection.execute(f'DROP TABLE IF EXISTS "{table}"')
  definitions: list[str] = []
  for column in columns:
    definition = f'"{column}" {sqlite_type(column)} NOT NULL'
    if column.endswith("_0_100"):
      definition += f' CHECK ("{column}" BETWEEN 0 AND 100)'
    if column.endswith("_ppm"):
      definition += f' CHECK ("{column}" BETWEEN 0 AND {WEIGHT_TOTAL})'
    if column.endswith("_0_10000"):
      definition += f' CHECK ("{column}" BETWEEN 0 AND 10000)'
    definitions.append(definition)
  definitions.append("PRIMARY KEY (" + ",".join(f'"{column}"' for column in primary_key) + ")")
  for local, foreign_table, foreign_column in foreign_keys:
    definitions.append(
      f'FOREIGN KEY ("{local}") REFERENCES "{foreign_table}"("{foreign_column}")'
    )
  connection.execute(f'CREATE TABLE "{table}" ({",".join(definitions)})')


def insert_rows(
  connection: sqlite3.Connection,
  table: str,
  columns: Sequence[str],
  rows: Sequence[dict[str, Any]],
) -> None:
  if not rows:
    return
  placeholders = ",".join("?" for _ in columns)
  column_sql = ",".join(f'"{column}"' for column in columns)
  connection.executemany(
    f'INSERT INTO "{table}" ({column_sql}) VALUES ({placeholders})',
    [[row.get(column, "") for column in columns] for row in rows],
  )


def prepare_database(
  source_database: Path,
  temporary_database: Path,
  summaries: Sequence[dict[str, Any]],
  subregions: Sequence[dict[str, Any]],
  anchors: Sequence[dict[str, str]],
  rules: Sequence[dict[str, str]],
) -> sqlite3.Connection:
  if temporary_database.exists():
    temporary_database.unlink()
  source = sqlite3.connect(source_database)
  target = sqlite3.connect(temporary_database)
  source.backup(target)
  source.close()
  target.execute("PRAGMA foreign_keys=ON")
  target.execute("PRAGMA journal_mode=DELETE")
  target.execute("PRAGMA synchronous=OFF")
  for view in [
    "v_county_entry_villages",
    "v_county_entry_subregions",
    "v_county_settlement_summary",
  ]:
    target.execute(f'DROP VIEW IF EXISTS "{view}"')
  create_table(
    target,
    "county_settlement_summary",
    SUMMARY_COLUMNS,
    ["county_id"],
    [("county_id", "county_economy_baseline", "county_id")],
  )
  create_table(
    target,
    "county_subregion_definition",
    SUBREGION_COLUMNS,
    ["subregion_id"],
    [("county_id", "county_economy_baseline", "county_id")],
  )
  create_table(
    target,
    "historical_village_name_anchors",
    ANCHOR_COLUMNS,
    ["anchor_id"],
    [("county_id", "county_economy_baseline", "county_id")],
  )
  create_table(target, "settlement_naming_rules", RULE_COLUMNS, ["rule_id"])
  create_table(
    target,
    "village_catalog",
    VILLAGE_DB_COLUMNS,
    ["village_id"],
    [
      ("county_id", "county_economy_baseline", "county_id"),
      ("subregion_id", "county_subregion_definition", "subregion_id"),
    ],
  )
  insert_rows(target, "county_settlement_summary", SUMMARY_COLUMNS, summaries)
  insert_rows(target, "county_subregion_definition", SUBREGION_COLUMNS, subregions)
  insert_rows(target, "historical_village_name_anchors", ANCHOR_COLUMNS, anchors)
  insert_rows(target, "settlement_naming_rules", RULE_COLUMNS, rules)
  target.commit()
  return target


def finalize_database(connection: sqlite3.Connection) -> None:
  connection.execute("CREATE INDEX idx_subregion_county ON county_subregion_definition(county_id)")
  connection.execute("CREATE INDEX idx_village_county ON village_catalog(county_id, village_id)")
  connection.execute("CREATE INDEX idx_village_subregion ON village_catalog(subregion_id, village_id)")
  connection.execute("CREATE UNIQUE INDEX idx_village_county_name ON village_catalog(county_id, village_name)")
  connection.execute("CREATE INDEX idx_village_source_type ON village_catalog(name_source_type)")
  connection.execute(
    "CREATE VIEW v_county_settlement_summary AS "
    "SELECT * FROM county_settlement_summary"
  )
  connection.execute(
    "CREATE VIEW v_county_entry_subregions AS SELECT "
    "z.*, s.rural_population_est, "
    "CAST(ROUND(s.rural_population_est * z.population_share_ppm / 1000000.0) AS INTEGER) "
    "AS projected_rural_population "
    "FROM county_subregion_definition AS z "
    "JOIN county_settlement_summary AS s USING (county_id)"
  )
  connection.execute(
    "CREATE VIEW v_county_entry_villages AS SELECT "
    "v.village_id, v.snapshot_year, c.region, c.upper_unit, c.intermediate_unit, c.county, "
    "v.county_id, v.subregion_id, z.subregion_name, z.direction_code, z.direction_name, "
    "z.zone_type, z.primary_landform, z.water_context, z.primary_resource_tags, "
    "z.render_biome_code, v.village_name, v.settlement_form, v.name_source_type, "
    "v.historical_name_claim, v.anchor_id, v.relative_x_0_10000, v.relative_y_0_10000, "
    "v.population_weight_ppm, v.farmland_weight_ppm, "
    "CAST(ROUND(s.rural_population_est * v.population_weight_ppm / 1000000.0) AS INTEGER) "
    "AS projected_rural_population, "
    "v.render_seed, v.position_method, v.commercial_release_ready "
    "FROM village_catalog AS v "
    "JOIN county_subregion_definition AS z USING (subregion_id) "
    "JOIN county_settlement_summary AS s ON s.county_id=v.county_id "
    "JOIN county_economy_baseline AS c ON c.county_id=v.county_id"
  )
  connection.execute("PRAGMA user_version=3")
  connection.execute("ANALYZE")
  connection.commit()


def safe_region_name(region: str) -> str:
  return region.replace("/", "_").replace("\\", "_")


def canonical_row_bytes(row: dict[str, Any], columns: Sequence[str]) -> bytes:
  values = [row.get(column, "") for column in columns]
  return (json.dumps(values, ensure_ascii=False, separators=(",", ":")) + "\n").encode("utf-8")


def build_specs(
  economies: Sequence[dict[str, str]],
  geographies: dict[str, dict[str, str]],
  deposits_by_county: dict[str, list[dict[str, str]]],
  anchors_by_county: dict[str, list[dict[str, str]]],
) -> tuple[list[dict[str, Any]], list[dict[str, Any]], dict[str, list[dict[str, Any]]]]:
  summaries: list[dict[str, Any]] = []
  all_subregions: list[dict[str, Any]] = []
  subregions_by_county: dict[str, list[dict[str, Any]]] = {}
  for economy in economies:
    county_id = economy["county_id"]
    geography = geographies[county_id]
    villages, rural_population, target = village_count(economy, geography)
    zone_total = subregion_count(economy, geography)
    zones = build_subregions(
      economy,
      geography,
      deposits_by_county.get(county_id, []),
      zone_total,
      villages,
    )
    subregions_by_county[county_id] = zones
    all_subregions.extend(zones)
    documented = len(anchors_by_county.get(county_id, []))
    summaries.append(
      {
        "county_id": county_id,
        "snapshot_year": SNAPSHOT_YEAR,
        "region": economy["region"],
        "upper_unit": economy["upper_unit"],
        "intermediate_unit": economy.get("intermediate_unit", ""),
        "county": economy["county"],
        "population_est_1628": int(economy["population_est_1628"]),
        "urban_population_est": int(economy["urban_population_est"]),
        "rural_population_est": rural_population,
        "area_km2_est": economy["area_km2_est"],
        "population_density_per_km2": economy["population_density_per_km2"],
        "target_village_population": target,
        "subregion_count": zone_total,
        "village_count": villages,
        "mean_rural_population_per_village": f"{rural_population / villages:.2f}",
        "documented_village_name_count": documented,
        "generated_village_name_count": villages - documented,
        "calculation_method": GENERATION_METHOD,
        "commercial_release_ready": NONCOMMERCIAL_MARK,
      }
    )
  return summaries, all_subregions, subregions_by_county


def write_village_catalog_and_database(
  output_dir: Path,
  database: sqlite3.Connection,
  economies: Sequence[dict[str, str]],
  subregions_by_county: dict[str, list[dict[str, Any]]],
  anchors_by_county: dict[str, list[dict[str, str]]],
  rules_by_region: dict[str, dict[str, Any]],
) -> tuple[list[dict[str, Any]], str]:
  temporary_generated = output_dir / ".generated_v0.3.tmp"
  if temporary_generated.exists():
    shutil.rmtree(temporary_generated)
  catalog_dir = temporary_generated / "village_catalog"
  catalog_dir.mkdir(parents=True)

  streams: dict[str, Any] = {}
  writers: dict[str, csv.DictWriter] = {}
  paths: dict[str, Path] = {}
  row_counts: Counter[str] = Counter()
  county_counts: Counter[str] = Counter()
  fingerprint = hashlib.sha256()
  try:
    for region in REGION_ORDER:
      index = REGION_INDEX[region]
      filename = f"{index:02d}_{safe_region_name(region)}_village_catalog_v0.3.csv"
      path = catalog_dir / filename
      stream = path.open("w", encoding="utf-8", newline="")
      writer = csv.DictWriter(stream, fieldnames=VILLAGE_CSV_COLUMNS, lineterminator="\n", extrasaction="ignore")
      writer.writeheader()
      streams[region] = stream
      writers[region] = writer
      paths[region] = path

    for county_index, economy in enumerate(economies, 1):
      county_id = economy["county_id"]
      rows = build_county_villages(
        economy,
        subregions_by_county[county_id],
        anchors_by_county.get(county_id, []),
        rules_by_region[economy["region"]],
      )
      writers[economy["region"]].writerows(rows)
      insert_rows(database, "village_catalog", VILLAGE_DB_COLUMNS, rows)
      row_counts[economy["region"]] += len(rows)
      county_counts[economy["region"]] += 1
      for row in rows:
        fingerprint.update(canonical_row_bytes(row, VILLAGE_CSV_COLUMNS))
      if county_index % 50 == 0:
        database.commit()
    database.commit()
  finally:
    for stream in streams.values():
      stream.close()

  manifest: list[dict[str, Any]] = []
  for region in REGION_ORDER:
    path = paths[region]
    manifest.append(
      {
        "part_index": REGION_INDEX[region],
        "region": region,
        "file": f"generated/village_catalog/{path.name}",
        "row_count": row_counts[region],
        "county_count": county_counts[region],
        "size_bytes": path.stat().st_size,
        "sha256": file_sha256(path),
      }
    )

  final_generated = output_dir / "generated"
  if final_generated.exists():
    shutil.rmtree(final_generated)
  temporary_generated.replace(final_generated)
  return manifest, fingerprint.hexdigest()


def validate_database(
  database_path: Path,
  manifest: Sequence[dict[str, Any]],
  fingerprint: str,
  input_paths: Sequence[Path],
) -> dict[str, Any]:
  connection = sqlite3.connect(database_path)
  connection.row_factory = sqlite3.Row
  try:
    counts = {
      "counties": connection.execute("SELECT COUNT(*) FROM county_settlement_summary").fetchone()[0],
      "subregions": connection.execute("SELECT COUNT(*) FROM county_subregion_definition").fetchone()[0],
      "villages": connection.execute("SELECT COUNT(*) FROM village_catalog").fetchone()[0],
      "documented_names": connection.execute(
        "SELECT COUNT(*) FROM village_catalog WHERE historical_name_claim='yes'"
      ).fetchone()[0],
      "generated_names": connection.execute(
        "SELECT COUNT(*) FROM village_catalog WHERE name_source_type='generated_period_style'"
      ).fetchone()[0],
    }
    expected = {
      "counties": EXPECTED_COUNTIES,
      "subregions": EXPECTED_SUBREGIONS,
      "villages": EXPECTED_VILLAGES,
    }
    for key, value in expected.items():
      if counts[key] != value:
        raise RuntimeError(f"Validation failed for {key}: {counts[key]} != {value}")

    bad_zone_counts = connection.execute(
      "SELECT COUNT(*) FROM county_settlement_summary WHERE subregion_count NOT BETWEEN 4 AND 8"
    ).fetchone()[0]
    bad_minimum_villages = connection.execute(
      "SELECT COUNT(*) FROM county_settlement_summary WHERE village_count < 20"
    ).fetchone()[0]
    duplicate_names = connection.execute(
      "SELECT COUNT(*) FROM (SELECT county_id,village_name,COUNT(*) n FROM village_catalog "
      "GROUP BY county_id,village_name HAVING n>1)"
    ).fetchone()[0]
    bad_zone_weights = connection.execute(
      "SELECT COUNT(*) FROM (SELECT county_id FROM county_subregion_definition GROUP BY county_id "
      "HAVING SUM(area_share_ppm)<>1000000 OR SUM(population_share_ppm)<>1000000 "
      "OR SUM(farmland_share_ppm)<>1000000)"
    ).fetchone()[0]
    bad_village_weights = connection.execute(
      "SELECT COUNT(*) FROM (SELECT county_id FROM village_catalog GROUP BY county_id "
      "HAVING SUM(population_weight_ppm)<>1000000 OR SUM(farmland_weight_ppm)<>1000000)"
    ).fetchone()[0]
    bad_source_claims = connection.execute(
      "SELECT COUNT(*) FROM village_catalog WHERE "
      "(name_source_type='generated_period_style' AND historical_name_claim<>'no') "
      "OR (name_source_type<>'generated_period_style' AND historical_name_claim<>'yes')"
    ).fetchone()[0]
    foreign_key_errors = len(connection.execute("PRAGMA foreign_key_check").fetchall())
    user_version = connection.execute("PRAGMA user_version").fetchone()[0]
    for label, value in {
      "bad_zone_counts": bad_zone_counts,
      "bad_minimum_villages": bad_minimum_villages,
      "duplicate_names": duplicate_names,
      "bad_zone_weights": bad_zone_weights,
      "bad_village_weights": bad_village_weights,
      "bad_source_claims": bad_source_claims,
      "foreign_key_errors": foreign_key_errors,
    }.items():
      if value:
        raise RuntimeError(f"Validation failed: {label}={value}")
    if user_version != 3:
      raise RuntimeError(f"Unexpected SQLite user_version: {user_version}")

    maximum_resource_error = 0.0
    for resource, (column, _) in RESOURCE_COLUMNS.items():
      row = connection.execute(
        f'SELECT MAX(ABS(weighted-baseline)) FROM ('
        f'SELECT z.county_id, SUM(z."{column}"*z.area_share_ppm)/1000000.0 weighted, '
        f'c."{column}" baseline FROM county_subregion_definition z '
        f'JOIN county_economy_baseline c USING(county_id) GROUP BY z.county_id)'
      ).fetchone()
      error = float(row[0] or 0)
      maximum_resource_error = max(maximum_resource_error, error)
      if error > 1.0:
        raise RuntimeError(f"Resource projection error for {resource}: {error:.4f}")

    largest = connection.execute(
      "SELECT county_id,county,village_count FROM county_settlement_summary "
      "ORDER BY village_count DESC,county_id LIMIT 1"
    ).fetchone()
    started = time.perf_counter()
    largest_rows = connection.execute(
      "SELECT * FROM v_county_entry_villages WHERE county_id=? ORDER BY subregion_id,village_id",
      (largest["county_id"],),
    ).fetchall()
    query_ms = (time.perf_counter() - started) * 1000
    if len(largest_rows) != largest["village_count"]:
      raise RuntimeError("Largest-county query returned the wrong row count")
    if query_ms > 250:
      raise RuntimeError(f"Largest-county query exceeded 250 ms: {query_ms:.2f} ms")

    manifest_total = sum(int(row["row_count"]) for row in manifest)
    if manifest_total != EXPECTED_VILLAGES:
      raise RuntimeError(f"Catalog manifest row mismatch: {manifest_total}")
    max_part_size = max(int(row["size_bytes"]) for row in manifest)
    if max_part_size >= 90 * 1024 * 1024:
      raise RuntimeError(f"A village catalog part exceeds 90 MiB: {max_part_size}")

    return {
      "status": "pass",
      "snapshot_year": SNAPSHOT_YEAR,
      "ruleset_version": RULESET_VERSION,
      "counts": counts,
      "rural_population_total": connection.execute(
        "SELECT SUM(rural_population_est) FROM county_settlement_summary"
      ).fetchone()[0],
      "mean_villages_per_county": round(counts["villages"] / counts["counties"], 2),
      "maximum_resource_projection_error": round(maximum_resource_error, 6),
      "weight_validation": {
        "zone_area_population_farmland": "all counties sum to 1000000",
        "village_population_farmland": "all counties sum to 1000000",
      },
      "sqlite": {
        "user_version": user_version,
        "foreign_key_errors": foreign_key_errors,
        "database_size_bytes": database_path.stat().st_size,
      },
      "performance": {
        "largest_county_id": largest["county_id"],
        "largest_county": largest["county"],
        "largest_county_villages": largest["village_count"],
        "entry_query_ms": round(query_ms, 3),
      },
      "catalog_manifest": list(manifest),
      "deterministic_build_fingerprint": fingerprint,
      "input_sha256": {str(path): file_sha256(path) for path in input_paths},
    }
  finally:
    connection.close()


def main() -> None:
  parser = argparse.ArgumentParser()
  parser.add_argument("--geography-dir", type=Path, default=DEFAULT_GEOGRAPHY_DIR)
  parser.add_argument("--economy-dir", type=Path, default=DEFAULT_ECONOMY_DIR)
  parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT_DIR)
  parser.add_argument(
    "--rules-csv",
    type=Path,
    default=DEFAULT_OUTPUT_DIR / "settlement_naming_rules_v0.3.csv",
  )
  parser.add_argument(
    "--anchors-csv",
    type=Path,
    default=DEFAULT_OUTPUT_DIR / "historical_village_name_anchors_v0.3.csv",
  )
  args = parser.parse_args()

  geography_csv = args.geography_dir / "county_geography_resources_v0.1.csv"
  economy_csv = args.economy_dir / "county_economy_baseline_v0.2.csv"
  deposit_csv = args.economy_dir / "mineral_deposit_definition_v0.2.csv"
  source_database = args.economy_dir / "game_world_1628_v0.2.sqlite"
  required = [
    geography_csv,
    economy_csv,
    deposit_csv,
    source_database,
    args.rules_csv,
    args.anchors_csv,
  ]
  missing = [str(path) for path in required if not path.exists()]
  if missing:
    raise SystemExit("Missing input files:\n- " + "\n- ".join(missing))

  args.output_dir.mkdir(parents=True, exist_ok=True)
  economies = sorted(read_csv(economy_csv), key=lambda row: row["county_id"])
  geographies = {row["county_id"]: row for row in read_csv(geography_csv)}
  deposits_by_county: dict[str, list[dict[str, str]]] = defaultdict(list)
  for row in read_csv(deposit_csv):
    deposits_by_county[row["county_id"]].append(row)
  rule_rows = read_csv(args.rules_csv)
  anchor_rows = read_csv(args.anchors_csv)
  county_map = {row["county_id"]: row for row in economies}
  if len(economies) != EXPECTED_COUNTIES or len(geographies) != EXPECTED_COUNTIES:
    raise RuntimeError("v0.3 requires exactly 1,168 county economy and geography rows")
  rules_by_region = parse_rule_rows(rule_rows)
  anchors_by_county = validate_anchor_rows(anchor_rows, county_map)

  summaries, subregions, subregions_by_county = build_specs(
    economies,
    geographies,
    deposits_by_county,
    anchors_by_county,
  )
  if len(summaries) != EXPECTED_COUNTIES:
    raise RuntimeError(f"Expected {EXPECTED_COUNTIES} county summaries, found {len(summaries)}")
  if len(subregions) != EXPECTED_SUBREGIONS:
    raise RuntimeError(f"Expected {EXPECTED_SUBREGIONS} subregions, found {len(subregions)}")
  total_villages = sum(int(row["village_count"]) for row in summaries)
  if total_villages != EXPECTED_VILLAGES:
    raise RuntimeError(f"Expected {EXPECTED_VILLAGES} villages, found {total_villages}")

  summary_path = args.output_dir / "county_settlement_summary_v0.3.csv"
  subregion_path = args.output_dir / "county_subregion_definition_v0.3.csv"
  manifest_path = args.output_dir / "village_catalog_manifest_v0.3.csv"
  database_path = args.output_dir / "game_world_1628_v0.3.sqlite"
  report_path = args.output_dir / "settlement_v0.3_validation_report.json"
  temporary_database = database_path.with_suffix(database_path.suffix + ".tmp")

  connection = prepare_database(
    source_database,
    temporary_database,
    summaries,
    subregions,
    anchor_rows,
    rule_rows,
  )
  try:
    manifest, fingerprint = write_village_catalog_and_database(
      args.output_dir,
      connection,
      economies,
      subregions_by_county,
      anchors_by_county,
      rules_by_region,
    )
    finalize_database(connection)
  finally:
    connection.close()

  write_csv_atomic(summary_path, SUMMARY_COLUMNS, summaries)
  write_csv_atomic(subregion_path, SUBREGION_COLUMNS, subregions)
  write_csv_atomic(manifest_path, MANIFEST_COLUMNS, manifest)
  temporary_database.replace(database_path)

  report = validate_database(
    database_path,
    manifest,
    fingerprint,
    required,
  )
  temporary_report = report_path.with_suffix(report_path.suffix + ".tmp")
  with temporary_report.open("w", encoding="utf-8") as stream:
    json.dump(report, stream, ensure_ascii=False, indent=2)
    stream.write("\n")
  temporary_report.replace(report_path)
  print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
  main()
