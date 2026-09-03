#!/usr/bin/env python3
"""Build Project Realm's county-local township/town computation layer v0.7.

The v0.6 database remains an immutable source.  This builder copies it, adds a
deterministic county -> local division -> settlement membership layer, and
keeps the county as the authoritative economic ledger.  Historical local-unit
records are stored separately from normalized gameplay divisions so generated
boundaries are never presented as documented Ming boundaries.
"""

from __future__ import annotations

import argparse
from collections import defaultdict
import csv
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import sqlite3
import time
from typing import Any, Iterable, Sequence


REPO_ROOT = Path(__file__).resolve().parents[2]
DATA_ROOT = REPO_ROOT / "docs/90_资料与归档/01_崇祯元年历史资料/data/1628"
DEFAULT_SOURCE_DIR = DATA_ROOT / "9.教育职业身份与社会阶层"
DEFAULT_OUTPUT_DIR = DATA_ROOT / "10.乡镇基层区划与计算分区"
DEFAULT_SOURCE_DATABASE = DEFAULT_SOURCE_DIR / "game_world_1628_v0.6.sqlite"
RULESET_VERSION = "v0.7"
SNAPSHOT_YEAR = 1628
WEIGHT_TOTAL = 1_000_000
EXPECTED_COUNTIES = 1_168
EXPECTED_VILLAGES = 505_684
EXPECTED_SETTLEMENTS = 508_729
COMMERCIAL_RELEASE_READY = "no"


RESOURCE_COLUMNS = [
  "agriculture_resource_0_100",
  "forest_resource_0_100",
  "pasture_resource_0_100",
  "fishery_resource_0_100",
  "salt_resource_0_100",
  "fuel_resource_0_100",
  "metal_resource_0_100",
  "building_material_resource_0_100",
]

ANCHOR_COLUMNS = [
  "anchor_id", "snapshot_year", "county_id", "source_unit_name",
  "source_unit_type", "parent_anchor_id", "unit_count",
  "matched_settlement_id", "source_year", "source_title",
  "source_reference", "source_url", "evidence_grade", "evidence_scope",
  "historical_claim", "notes", "commercial_release_ready",
]

DIVISION_COLUMNS = [
  "division_id", "snapshot_year", "county_id", "primary_subregion_id",
  "division_type_code", "division_name", "is_county_core",
  "source_unit_anchor_id", "source_unit_name", "source_unit_type",
  "center_settlement_id", "center_settlement_name",
  "historical_name_claim", "boundary_historical_claim", "evidence_grade",
  "assignment_method", "resident_population_est", "household_count_est",
  "labor_force_est", "area_km2_est", "population_share_ppm",
  "household_share_ppm", "labor_share_ppm", "area_share_ppm",
  "farmland_share_ppm", "village_count", "settlement_count",
  "center_rel_x_0_10000", "center_rel_y_0_10000", *RESOURCE_COLUMNS,
  "render_seed", "commercial_release_ready",
]

MEMBERSHIP_COLUMNS = [
  "settlement_id", "division_id", "county_id", "snapshot_year",
  "membership_method", "historical_membership_claim",
  "source_unit_anchor_id", "distance_score_0_10000",
  "commercial_release_ready",
]

SUMMARY_COLUMNS = [
  "county_id", "snapshot_year", "region", "upper_unit", "intermediate_unit",
  "county", "division_count", "town_count", "township_count",
  "source_backed_division_count", "count_constrained_division_count",
  "settlement_count", "village_count", "population_est_1628",
  "household_count_est", "labor_force_est", "mean_population_per_division",
  "mean_villages_per_division", "division_count_method",
  "commercial_release_ready",
]

SOURCE_COLUMNS = [
  "source_id", "source_title", "pinned_version", "content_hash", "source_url",
  "usage", "evidence_boundary", "commercial_release_ready",
]


def clamp(value: float, low: float, high: float) -> float:
  return max(low, min(high, value))


def stable_digest(*parts: Any) -> bytes:
  text = "|".join([RULESET_VERSION, *(str(part) for part in parts)])
  return hashlib.sha256(text.encode("utf-8")).digest()


def file_sha256(path: Path) -> str:
  digest = hashlib.sha256()
  with path.open("rb") as stream:
    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
      digest.update(chunk)
  return digest.hexdigest()


def rows_as_dicts(cursor: sqlite3.Cursor) -> list[dict[str, Any]]:
  return [dict(row) for row in cursor.fetchall()]


def read_csv(path: Path) -> list[dict[str, str]]:
  with path.open(encoding="utf-8-sig", newline="") as stream:
    rows = []
    for row in csv.DictReader(stream):
      rows.append({key: (value or "").strip() for key, value in row.items()})
    return rows


def write_csv_atomic(path: Path, columns: Sequence[str], rows: Iterable[dict[str, Any]]) -> None:
  path.parent.mkdir(parents=True, exist_ok=True)
  temporary = path.with_suffix(path.suffix + ".tmp")
  with temporary.open("w", encoding="utf-8", newline="") as stream:
    writer = csv.DictWriter(stream, fieldnames=columns, extrasaction="ignore", lineterminator="\n")
    writer.writeheader()
    for row in rows:
      writer.writerow({column: row.get(column, "") for column in columns})
  temporary.replace(path)


def write_json_atomic(path: Path, value: dict[str, Any]) -> None:
  path.parent.mkdir(parents=True, exist_ok=True)
  temporary = path.with_suffix(path.suffix + ".tmp")
  temporary.write_text(
    json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
    encoding="utf-8",
  )
  temporary.replace(path)


def allocate_exact(total: int, weights: Sequence[float], minimum: int = 0) -> list[int]:
  if not weights:
    if total:
      raise ValueError("cannot allocate a nonzero total without weights")
    return []
  if total < minimum * len(weights):
    raise ValueError(f"total {total} is below minimum allocation {minimum}x{len(weights)}")
  remaining = total - minimum * len(weights)
  clean = [max(0.0, float(weight)) for weight in weights]
  if sum(clean) <= 0:
    clean = [1.0] * len(weights)
  exact = [remaining * weight / sum(clean) for weight in clean]
  result = [minimum + math.floor(value) for value in exact]
  remainder = total - sum(result)
  order = sorted(
    range(len(exact)),
    key=lambda index: (-(exact[index] - math.floor(exact[index])), index),
  )
  for index in order[:remainder]:
    result[index] += 1
  if sum(result) != total:
    raise RuntimeError("exact allocation failed")
  return result


def squared_distance(first: dict[str, Any], second: dict[str, Any]) -> int:
  return (
    int(first["relative_x_0_10000"]) - int(second["relative_x_0_10000"])
  ) ** 2 + (
    int(first["relative_y_0_10000"]) - int(second["relative_y_0_10000"])
  ) ** 2


def distance_score(first: dict[str, Any], second: dict[str, Any]) -> int:
  return round(clamp(math.sqrt(squared_distance(first, second)) / math.sqrt(2), 0, 10_000))


def choose_farthest_seed(
  candidates: Sequence[dict[str, Any]],
  seeds: Sequence[dict[str, Any]],
) -> dict[str, Any]:
  best: dict[str, Any] | None = None
  best_key: tuple[int, int] | None = None
  for candidate in candidates:
    if any(candidate["settlement_id"] == seed["settlement_id"] for seed in seeds):
      continue
    minimum_distance = min(
      (squared_distance(candidate, seed) for seed in seeds),
      default=2 * 10_000**2,
    )
    key = (minimum_distance, int(candidate["resident_population"]))
    if best is None or key > best_key or (
      key == best_key and candidate["settlement_id"] < best["settlement_id"]
    ):
      best = candidate
      best_key = key
  if best is None:
    raise RuntimeError("unable to select a distinct division seed")
  return best


def cluster_subregion(
  subregion: dict[str, Any],
  settlements: Sequence[dict[str, Any]],
  cluster_count: int,
) -> list[dict[str, Any]]:
  if cluster_count < 1 or cluster_count > len(settlements):
    raise RuntimeError(
      f"invalid cluster count {cluster_count} for {subregion['subregion_id']} with {len(settlements)} settlements"
    )
  mandatory = sorted(
    [
      row for row in settlements
      if row["settlement_type_code"] in {"county_seat", "market_town"}
    ],
    key=lambda row: (
      0 if row["settlement_type_code"] == "county_seat" else 1,
      row["settlement_id"],
    ),
  )
  if len(mandatory) > cluster_count:
    raise RuntimeError(f"too many mandatory centers in {subregion['subregion_id']}")
  seeds = list(mandatory)
  if not seeds:
    seeds.append(min(
      settlements,
      key=lambda row: (
        (int(row["relative_x_0_10000"]) - int(subregion["center_rel_x_0_10000"])) ** 2
        + (int(row["relative_y_0_10000"]) - int(subregion["center_rel_y_0_10000"])) ** 2,
        -int(row["resident_population"]),
        row["settlement_id"],
      ),
    ))
  while len(seeds) < cluster_count:
    seeds.append(choose_farthest_seed(settlements, seeds))

  clusters = [
    {
      "subregion": subregion,
      "seed": seed,
      "members": [seed],
      "population_load": int(seed["resident_population"]),
    }
    for seed in seeds
  ]
  seed_ids = {seed["settlement_id"] for seed in seeds}
  total_population = sum(int(row["resident_population"]) for row in settlements)
  target_population = max(1.0, total_population / cluster_count)
  remaining = sorted(
    [row for row in settlements if row["settlement_id"] not in seed_ids],
    key=lambda row: (-int(row["resident_population"]), row["settlement_id"]),
  )
  for settlement in remaining:
    selected = min(
      clusters,
      key=lambda cluster: (
        squared_distance(settlement, cluster["seed"]) / 100_000_000
        + 0.75 * (cluster["population_load"] / target_population) ** 2,
        cluster["seed"]["settlement_id"],
      ),
    )
    selected["members"].append(settlement)
    selected["population_load"] += int(settlement["resident_population"])
  for cluster in clusters:
    cluster["members"].sort(key=lambda row: row["settlement_id"])
  return clusters


def generated_division_count(
  economy: dict[str, Any],
  geography: dict[str, Any],
  subregion_count: int,
  minimum_center_count: int,
) -> int:
  rough_terrain = int(geography["hill_pct"]) + int(geography["mountain_pct"])
  terrain_factor = 1.10 if rough_terrain >= 65 else 1.0
  spatial_factor = clamp(
    (float(economy["area_km2_est"]) / 1_200) ** 0.12 * terrain_factor,
    0.85,
    1.25,
  )
  estimated = round(int(economy["household_count_est"]) / 2_200 * spatial_factor)
  return max(subregion_count, minimum_center_count, min(40, estimated))


def source_count_constraint(anchors: Sequence[dict[str, str]]) -> tuple[int | None, str]:
  territorial_units = [row for row in anchors if row["source_unit_type"] == "du"]
  if territorial_units:
    return len(territorial_units), "documented_territorial_units"
  count_rows = [
    row for row in anchors
    if row["source_unit_type"] == "li_count" and row["unit_count"]
  ]
  if count_rows:
    return max(int(row["unit_count"]) for row in count_rows), "documented_count_only"
  return None, "household_terrain_projection"


def distribute_cluster_counts(
  division_count: int,
  subregions: Sequence[dict[str, Any]],
  settlements_by_subregion: dict[str, list[dict[str, Any]]],
) -> list[int]:
  minimums = []
  for subregion in subregions:
    settlements = settlements_by_subregion[subregion["subregion_id"]]
    center_count = sum(
      row["settlement_type_code"] in {"county_seat", "market_town"}
      for row in settlements
    )
    minimums.append(max(1, center_count))
  if division_count < sum(minimums):
    division_count = sum(minimums)
  remaining = division_count - sum(minimums)
  extras = allocate_exact(
    remaining,
    [int(row["population_share_ppm"]) for row in subregions],
  )
  counts = [minimum + extra for minimum, extra in zip(minimums, extras)]
  for index, subregion in enumerate(subregions):
    available = len(settlements_by_subregion[subregion["subregion_id"]])
    if counts[index] > available:
      overflow = counts[index] - available
      counts[index] = available
      recipients = [
        other for other in range(len(counts))
        if other != index
        and counts[other] < len(settlements_by_subregion[subregions[other]["subregion_id"]])
      ]
      for _ in range(overflow):
        if not recipients:
          raise RuntimeError("not enough settlements to host all local divisions")
        recipient = max(
          recipients,
          key=lambda other: (
            int(subregions[other]["population_share_ppm"]) / counts[other],
            -other,
          ),
        )
        counts[recipient] += 1
        if counts[recipient] >= len(settlements_by_subregion[subregions[recipient]["subregion_id"]]):
          recipients.remove(recipient)
  if sum(counts) != division_count:
    raise RuntimeError("subregion division allocation changed the county total")
  return counts


def settlement_root(name: str) -> str:
  suffixes = (
    "产业场聚落", "港驿聚落", "营堡聚落", "资源聚落", "村", "庄", "寨", "堡",
    "屯", "营", "店", "铺", "集", "沟", "峪", "湾", "塘", "圩", "场", "镇",
  )
  for suffix in suffixes:
    if name.endswith(suffix) and len(name) > len(suffix) + 1:
      return name[:-len(suffix)]
  return name


def county_root(name: str) -> str:
  return re.sub(r"(?:县|州|卫)$", "", name) or name


def unique_division_name(base: str, used: set[str], direction_name: str, ordinal: int) -> str:
  if base not in used:
    used.add(base)
    return base
  directional = f"{direction_name}{base}"
  if directional not in used:
    used.add(directional)
    return directional
  candidate_ordinal = ordinal
  while True:
    candidate = f"{base}{candidate_ordinal}"
    if candidate not in used:
      used.add(candidate)
      return candidate
    candidate_ordinal += 1


def assign_historical_units(
  clusters: list[dict[str, Any]],
  anchors: Sequence[dict[str, str]],
) -> None:
  units = sorted(
    [row for row in anchors if row["source_unit_type"] == "du"],
    key=lambda row: row["anchor_id"],
  )
  if not units:
    for cluster in clusters:
      cluster["source_anchor"] = None
    return
  if len(units) != len(clusters):
    raise RuntimeError(
      f"documented unit count {len(units)} does not match division count {len(clusters)}"
    )
  cluster_by_member = {
    member["settlement_id"]: cluster
    for cluster in clusters
    for member in cluster["members"]
  }
  unit_by_id = {row["anchor_id"]: row for row in units}
  assignment: dict[str, dict[str, Any]] = {}
  used_clusters: set[int] = set()
  membership_anchors = [
    row for row in anchors
    if row["evidence_scope"] == "documented_membership"
    and row["matched_settlement_id"]
    and row["parent_anchor_id"] in unit_by_id
  ]
  for anchor in membership_anchors:
    cluster = cluster_by_member.get(anchor["matched_settlement_id"])
    if cluster is None:
      raise RuntimeError(f"historical membership references unknown settlement {anchor['matched_settlement_id']}")
    unit_id = anchor["parent_anchor_id"]
    if unit_id in assignment and assignment[unit_id] is not cluster:
      raise RuntimeError(f"historical unit {unit_id} resolves to multiple clusters")
    assignment[unit_id] = cluster
    used_clusters.add(id(cluster))
  remaining_clusters = sorted(
    [cluster for cluster in clusters if id(cluster) not in used_clusters],
    key=lambda cluster: (
      cluster["subregion"]["subregion_id"],
      int(cluster["seed"]["relative_y_0_10000"]),
      int(cluster["seed"]["relative_x_0_10000"]),
      cluster["seed"]["settlement_id"],
    ),
  )
  remaining_units = [row for row in units if row["anchor_id"] not in assignment]
  for unit, cluster in zip(remaining_units, remaining_clusters):
    assignment[unit["anchor_id"]] = cluster
  for unit in units:
    assignment[unit["anchor_id"]]["source_anchor"] = unit


def build_county(
  economy: dict[str, Any],
  geography: dict[str, Any],
  subregions: list[dict[str, Any]],
  settlements: list[dict[str, Any]],
  anchors: list[dict[str, str]],
) -> tuple[list[dict[str, Any]], list[dict[str, Any]], dict[str, Any]]:
  county_id = economy["county_id"]
  settlements_by_subregion: dict[str, list[dict[str, Any]]] = defaultdict(list)
  for settlement in settlements:
    settlements_by_subregion[settlement["subregion_id"]].append(settlement)
  if set(settlements_by_subregion) != {row["subregion_id"] for row in subregions}:
    raise RuntimeError(f"settlement/subregion coverage mismatch for {county_id}")
  minimum_center_count = sum(
    max(1, sum(
      settlement["settlement_type_code"] in {"county_seat", "market_town"}
      for settlement in settlements_by_subregion[subregion["subregion_id"]]
    ))
    for subregion in subregions
  )
  count_constraint, count_method = source_count_constraint(anchors)
  if count_constraint is None:
    division_count = generated_division_count(
      economy, geography, len(subregions), minimum_center_count,
    )
  else:
    division_count = max(count_constraint, len(subregions), minimum_center_count)
  cluster_counts = distribute_cluster_counts(
    division_count, subregions, settlements_by_subregion,
  )
  clusters: list[dict[str, Any]] = []
  for subregion, cluster_count in zip(subregions, cluster_counts):
    clusters.extend(cluster_subregion(
      subregion,
      settlements_by_subregion[subregion["subregion_id"]],
      cluster_count,
    ))
  clusters.sort(key=lambda cluster: (
    0 if cluster["seed"]["settlement_type_code"] == "county_seat" else
    1 if cluster["seed"]["settlement_type_code"] == "market_town" else 2,
    cluster["subregion"]["subregion_id"],
    int(cluster["seed"]["relative_y_0_10000"]),
    int(cluster["seed"]["relative_x_0_10000"]),
    cluster["seed"]["settlement_id"],
  ))
  assign_historical_units(clusters, anchors)
  exact_membership = {
    row["matched_settlement_id"]: row for row in anchors
    if row["evidence_scope"] == "documented_membership" and row["matched_settlement_id"]
  }

  for ordinal, cluster in enumerate(clusters, 1):
    seed = cluster["seed"]
    cluster["division_id"] = f"{county_id}-LD{ordinal:03d}"
    cluster["is_county_core"] = int(seed["settlement_type_code"] == "county_seat")
    cluster["division_type_code"] = (
      "town" if seed["settlement_type_code"] in {"county_seat", "market_town"}
      else "township"
    )

  used_names: set[str] = set()
  for ordinal, cluster in enumerate(clusters, 1):
    seed = cluster["seed"]
    source_anchor = cluster.get("source_anchor")
    if cluster["is_county_core"]:
      base_name = f"{county_root(economy['county'])}城关镇"
    elif cluster["division_type_code"] == "town":
      base_name = seed["settlement_name"] if seed["settlement_name"].endswith("镇") else f"{seed['settlement_name']}镇"
    elif source_anchor:
      base_name = source_anchor["source_unit_name"]
      if not base_name.endswith("乡"):
        base_name += "乡"
    else:
      base_name = f"{settlement_root(seed['settlement_name'])}乡"
    cluster["division_name"] = unique_division_name(
      base_name, used_names, cluster["subregion"]["direction_name"], ordinal,
    )

  households = allocate_exact(
    int(economy["household_count_est"]),
    [sum(int(row["resident_population"]) for row in cluster["members"]) for cluster in clusters],
  )
  population_shares = allocate_exact(
    WEIGHT_TOTAL,
    [sum(int(row["resident_population"]) for row in cluster["members"]) for cluster in clusters],
  )
  household_shares = allocate_exact(WEIGHT_TOTAL, households)
  labor_shares = allocate_exact(
    WEIGHT_TOTAL,
    [sum(int(row["labor_force_est"]) for row in cluster["members"]) for cluster in clusters],
  )
  area_shares: dict[int, int] = {}
  farmland_shares: dict[int, int] = {}
  clusters_by_subregion: dict[str, list[dict[str, Any]]] = defaultdict(list)
  for cluster in clusters:
    clusters_by_subregion[cluster["subregion"]["subregion_id"]].append(cluster)
  for subregion in subregions:
    local_clusters = clusters_by_subregion[subregion["subregion_id"]]
    local_area = allocate_exact(
      int(subregion["area_share_ppm"]),
      [len(cluster["members"]) for cluster in local_clusters],
    )
    local_farmland = allocate_exact(
      int(subregion["farmland_share_ppm"]),
      [
        sum(
          int(row["resident_population"])
          for row in cluster["members"]
          if row["settlement_type_code"] == "village"
        ) or 1
        for cluster in local_clusters
      ],
    )
    for cluster, area_share, farmland_share in zip(local_clusters, local_area, local_farmland):
      area_shares[id(cluster)] = area_share
      farmland_shares[id(cluster)] = farmland_share

  division_rows: list[dict[str, Any]] = []
  membership_rows: list[dict[str, Any]] = []
  for index, cluster in enumerate(clusters):
    seed = cluster["seed"]
    source_anchor = cluster.get("source_anchor")
    population = sum(int(row["resident_population"]) for row in cluster["members"])
    labor = sum(int(row["labor_force_est"]) for row in cluster["members"])
    village_count = sum(row["settlement_type_code"] == "village" for row in cluster["members"])
    if source_anchor and not cluster["is_county_core"] and cluster["division_type_code"] == "township":
      historical_name_claim = "normalized_from_documented_unit"
    else:
      historical_name_claim = "no"
    if source_anchor:
      assignment_method = "documented_unit_spatial_projection"
      evidence_grade = source_anchor["evidence_grade"]
    elif count_method == "documented_count_only":
      assignment_method = "documented_count_spatial_projection"
      evidence_grade = "C"
    else:
      assignment_method = "deterministic_household_terrain_projection"
      evidence_grade = "D"
    division = {
      "division_id": cluster["division_id"],
      "snapshot_year": SNAPSHOT_YEAR,
      "county_id": county_id,
      "primary_subregion_id": cluster["subregion"]["subregion_id"],
      "division_type_code": cluster["division_type_code"],
      "division_name": cluster["division_name"],
      "is_county_core": cluster["is_county_core"],
      "source_unit_anchor_id": source_anchor["anchor_id"] if source_anchor else "",
      "source_unit_name": source_anchor["source_unit_name"] if source_anchor else "",
      "source_unit_type": source_anchor["source_unit_type"] if source_anchor else "",
      "center_settlement_id": seed["settlement_id"],
      "center_settlement_name": seed["settlement_name"],
      "historical_name_claim": historical_name_claim,
      "boundary_historical_claim": "no",
      "evidence_grade": evidence_grade,
      "assignment_method": assignment_method,
      "resident_population_est": population,
      "household_count_est": households[index],
      "labor_force_est": labor,
      "area_km2_est": f"{float(economy['area_km2_est']) * area_shares[id(cluster)] / WEIGHT_TOTAL:.3f}",
      "population_share_ppm": population_shares[index],
      "household_share_ppm": household_shares[index],
      "labor_share_ppm": labor_shares[index],
      "area_share_ppm": area_shares[id(cluster)],
      "farmland_share_ppm": farmland_shares[id(cluster)],
      "village_count": village_count,
      "settlement_count": len(cluster["members"]),
      "center_rel_x_0_10000": seed["relative_x_0_10000"],
      "center_rel_y_0_10000": seed["relative_y_0_10000"],
      "render_seed": stable_digest(cluster["division_id"], "render").hex()[:16],
      "commercial_release_ready": COMMERCIAL_RELEASE_READY,
    }
    for resource in RESOURCE_COLUMNS:
      division[resource] = cluster["subregion"][resource]
    division_rows.append(division)

    for member in cluster["members"]:
      exact_anchor = exact_membership.get(member["settlement_id"])
      if exact_anchor:
        membership_method = "documented_membership"
        historical_membership_claim = "yes"
        membership_source_id = exact_anchor["anchor_id"]
      elif member["settlement_id"] == seed["settlement_id"] and cluster["is_county_core"]:
        membership_method = "county_seat_center_locked"
        historical_membership_claim = "no"
        membership_source_id = ""
      elif member["settlement_id"] == seed["settlement_id"] and cluster["division_type_code"] == "town":
        membership_method = "market_town_center_locked"
        historical_membership_claim = "no"
        membership_source_id = ""
      elif source_anchor:
        membership_method = "spatial_projection_with_documented_unit"
        historical_membership_claim = "no"
        membership_source_id = ""
      elif count_method == "documented_count_only":
        membership_method = "spatial_projection_with_documented_count"
        historical_membership_claim = "no"
        membership_source_id = ""
      else:
        membership_method = "deterministic_spatial_projection"
        historical_membership_claim = "no"
        membership_source_id = ""
      membership_rows.append({
        "settlement_id": member["settlement_id"],
        "division_id": cluster["division_id"],
        "county_id": county_id,
        "snapshot_year": SNAPSHOT_YEAR,
        "membership_method": membership_method,
        "historical_membership_claim": historical_membership_claim,
        "source_unit_anchor_id": membership_source_id,
        "distance_score_0_10000": distance_score(member, seed),
        "commercial_release_ready": COMMERCIAL_RELEASE_READY,
      })

  summary = {
    "county_id": county_id,
    "snapshot_year": SNAPSHOT_YEAR,
    "region": economy["region"],
    "upper_unit": economy["upper_unit"],
    "intermediate_unit": economy["intermediate_unit"],
    "county": economy["county"],
    "division_count": len(division_rows),
    "town_count": sum(row["division_type_code"] == "town" for row in division_rows),
    "township_count": sum(row["division_type_code"] == "township" for row in division_rows),
    "source_backed_division_count": sum(bool(row["source_unit_anchor_id"]) for row in division_rows),
    "count_constrained_division_count": len(division_rows) if count_method == "documented_count_only" else 0,
    "settlement_count": len(settlements),
    "village_count": sum(row["settlement_type_code"] == "village" for row in settlements),
    "population_est_1628": economy["population_est_1628"],
    "household_count_est": economy["household_count_est"],
    "labor_force_est": economy["labor_force_est"],
    "mean_population_per_division": f"{int(economy['population_est_1628']) / len(division_rows):.2f}",
    "mean_villages_per_division": f"{sum(row['settlement_type_code'] == 'village' for row in settlements) / len(division_rows):.2f}",
    "division_count_method": count_method,
    "commercial_release_ready": COMMERCIAL_RELEASE_READY,
  }
  return division_rows, membership_rows, summary


def build_all(
  connection: sqlite3.Connection,
  anchors: list[dict[str, str]],
) -> tuple[list[dict[str, Any]], list[dict[str, Any]], list[dict[str, Any]]]:
  anchors_by_county: dict[str, list[dict[str, str]]] = defaultdict(list)
  for anchor in anchors:
    anchors_by_county[anchor["county_id"]].append(anchor)
  economies = rows_as_dicts(connection.execute(
    "SELECT * FROM county_economy_baseline ORDER BY county_id"
  ))
  geography = {
    row["county_id"]: row for row in rows_as_dicts(connection.execute(
      "SELECT * FROM county_geography_resources ORDER BY county_id"
    ))
  }
  if len(economies) != EXPECTED_COUNTIES or len(geography) != EXPECTED_COUNTIES:
    raise RuntimeError("v0.7 requires 1,168 county economy and geography rows")
  all_divisions: list[dict[str, Any]] = []
  all_memberships: list[dict[str, Any]] = []
  summaries: list[dict[str, Any]] = []
  for county_index, economy in enumerate(economies, 1):
    county_id = economy["county_id"]
    subregions = rows_as_dicts(connection.execute(
      "SELECT * FROM county_subregion_definition WHERE county_id=? ORDER BY subregion_id",
      (county_id,),
    ))
    settlements = rows_as_dicts(connection.execute(
      "SELECT * FROM settlement_node WHERE county_id=? ORDER BY settlement_id",
      (county_id,),
    ))
    divisions, memberships, summary = build_county(
      economy,
      geography[county_id],
      subregions,
      settlements,
      anchors_by_county.get(county_id, []),
    )
    all_divisions.extend(divisions)
    all_memberships.extend(memberships)
    summaries.append(summary)
    if county_index % 100 == 0:
      print(f"[v0.7] divisions {county_index}/{len(economies)} counties", flush=True)
  return all_divisions, all_memberships, summaries


def build_source_manifest(source_database: Path, anchors_path: Path) -> list[dict[str, Any]]:
  return [
    {
      "source_id": "v0.6_sqlite",
      "source_title": "Project Realm game_world_1628_v0.6.sqlite",
      "pinned_version": "user_version=6",
      "content_hash": file_sha256(source_database),
      "source_url": "local:game_world_1628_v0.6.sqlite",
      "usage": "immutable county, subregion and settlement source",
      "evidence_boundary": "inherits all upstream estimation and licensing boundaries",
      "commercial_release_ready": "no",
    },
    {
      "source_id": "chgis_database_scope",
      "source_title": "China Historical GIS database design",
      "pinned_version": "accessed 2026-08-30",
      "content_hash": "",
      "source_url": "https://chgis.fas.harvard.edu/pages/database/",
      "usage": "county-level coverage boundary",
      "evidence_boundary": "does not supply nationwide 1628 township boundaries",
      "commercial_release_ready": "no",
    },
    {
      "source_id": "wanli_shuntian_gazetteer",
      "source_title": "万历顺天府志",
      "pinned_version": "digital transcription accessed 2026-08-30",
      "content_hash": "",
      "source_url": "https://www.shidianguji.com/book/NGJ892411999021244140614/chapter/1lolymce2g6r8",
      "usage": "Daxing documented count of 36 registered li",
      "evidence_boundary": "count only; no names, membership or boundaries claimed",
      "commercial_release_ready": "no",
    },
    {
      "source_id": "jiajing_wujiang_gazetteer",
      "source_title": "嘉靖吴江县志",
      "pinned_version": "1561 edition digital transcription accessed 2026-08-30",
      "content_hash": "",
      "source_url": "https://www.shidianguji.com/book/HY4681/chapter/1ksj0s7n6yubt",
      "usage": "Wujiang six xiang, 29 du, four towns and documented village membership",
      "evidence_boundary": "names and hierarchy are documented; generated gameplay boundaries are not",
      "commercial_release_ready": "no",
    },
    {
      "source_id": "yangcheng_gazetteer_history",
      "source_title": "阳城县志历次编修纪略",
      "pinned_version": "accessed 2026-08-30",
      "content_hash": "",
      "source_url": "https://www.cssn.cn/zjzg/fzwy/ctwh/202312/t20231220_5719998.shtml",
      "usage": "documents existence of the 1625 Yangcheng gazetteer edition",
      "evidence_boundary": "does not provide verified 1625 local-unit text; no division facts imported",
      "commercial_release_ready": "no",
    },
    {
      "source_id": "manual_local_unit_anchors",
      "source_title": "Historical local unit anchors v0.7",
      "pinned_version": RULESET_VERSION,
      "content_hash": file_sha256(anchors_path),
      "source_url": "local:historical_local_unit_anchors_v0.7.csv",
      "usage": "audited pilot local-unit facts and evidence boundaries",
      "evidence_boundary": "only listed fields and relationships",
      "commercial_release_ready": "no",
    },
  ]


def create_v07_tables(connection: sqlite3.Connection) -> None:
  connection.execute("DROP TABLE IF EXISTS settlement_local_division")
  connection.execute("DROP TABLE IF EXISTS local_division_definition")
  connection.execute("DROP TABLE IF EXISTS historical_local_unit_anchor")
  connection.execute(
    "CREATE TABLE historical_local_unit_anchor ("
    "anchor_id TEXT PRIMARY KEY,snapshot_year INTEGER NOT NULL,county_id TEXT NOT NULL,"
    "source_unit_name TEXT NOT NULL,source_unit_type TEXT NOT NULL,parent_anchor_id TEXT NOT NULL,"
    "unit_count INTEGER NOT NULL,matched_settlement_id TEXT NOT NULL,source_year INTEGER NOT NULL,"
    "source_title TEXT NOT NULL,source_reference TEXT NOT NULL,source_url TEXT NOT NULL,"
    "evidence_grade TEXT NOT NULL,evidence_scope TEXT NOT NULL,historical_claim TEXT NOT NULL,"
    "notes TEXT NOT NULL,commercial_release_ready TEXT NOT NULL,"
    "FOREIGN KEY(county_id) REFERENCES county_economy_baseline(county_id))"
  )
  resource_definitions = ",".join(
    f'{column} INTEGER NOT NULL CHECK ({column} BETWEEN 0 AND 100)'
    for column in RESOURCE_COLUMNS
  )
  connection.execute(
    "CREATE TABLE local_division_definition ("
    "division_id TEXT PRIMARY KEY,snapshot_year INTEGER NOT NULL,county_id TEXT NOT NULL,"
    "primary_subregion_id TEXT NOT NULL,division_type_code TEXT NOT NULL,division_name TEXT NOT NULL,"
    "is_county_core INTEGER NOT NULL CHECK(is_county_core IN (0,1)),source_unit_anchor_id TEXT NOT NULL,"
    "source_unit_name TEXT NOT NULL,source_unit_type TEXT NOT NULL,center_settlement_id TEXT NOT NULL,"
    "center_settlement_name TEXT NOT NULL,historical_name_claim TEXT NOT NULL,"
    "boundary_historical_claim TEXT NOT NULL,evidence_grade TEXT NOT NULL,assignment_method TEXT NOT NULL,"
    "resident_population_est INTEGER NOT NULL,household_count_est INTEGER NOT NULL,labor_force_est INTEGER NOT NULL,"
    "area_km2_est REAL NOT NULL,population_share_ppm INTEGER NOT NULL CHECK(population_share_ppm BETWEEN 0 AND 1000000),"
    "household_share_ppm INTEGER NOT NULL CHECK(household_share_ppm BETWEEN 0 AND 1000000),"
    "labor_share_ppm INTEGER NOT NULL CHECK(labor_share_ppm BETWEEN 0 AND 1000000),"
    "area_share_ppm INTEGER NOT NULL CHECK(area_share_ppm BETWEEN 0 AND 1000000),"
    "farmland_share_ppm INTEGER NOT NULL CHECK(farmland_share_ppm BETWEEN 0 AND 1000000),"
    "village_count INTEGER NOT NULL,settlement_count INTEGER NOT NULL,"
    "center_rel_x_0_10000 INTEGER NOT NULL CHECK(center_rel_x_0_10000 BETWEEN 0 AND 10000),"
    "center_rel_y_0_10000 INTEGER NOT NULL CHECK(center_rel_y_0_10000 BETWEEN 0 AND 10000),"
    + resource_definitions + ",render_seed TEXT NOT NULL,commercial_release_ready TEXT NOT NULL,"
    "FOREIGN KEY(county_id) REFERENCES county_economy_baseline(county_id),"
    "FOREIGN KEY(primary_subregion_id) REFERENCES county_subregion_definition(subregion_id),"
    "FOREIGN KEY(center_settlement_id) REFERENCES settlement_node(settlement_id))"
  )
  connection.execute(
    "CREATE TABLE settlement_local_division ("
    "settlement_id TEXT PRIMARY KEY,division_id TEXT NOT NULL,county_id TEXT NOT NULL,"
    "snapshot_year INTEGER NOT NULL,membership_method TEXT NOT NULL,"
    "historical_membership_claim TEXT NOT NULL,source_unit_anchor_id TEXT NOT NULL,"
    "distance_score_0_10000 INTEGER NOT NULL CHECK(distance_score_0_10000 BETWEEN 0 AND 10000),"
    "commercial_release_ready TEXT NOT NULL,"
    "FOREIGN KEY(settlement_id) REFERENCES settlement_node(settlement_id),"
    "FOREIGN KEY(division_id) REFERENCES local_division_definition(division_id),"
    "FOREIGN KEY(county_id) REFERENCES county_economy_baseline(county_id))"
  )


def insert_rows(
  connection: sqlite3.Connection,
  table: str,
  columns: Sequence[str],
  rows: Iterable[dict[str, Any]],
) -> None:
  names = ",".join(f'"{column}"' for column in columns)
  placeholders = ",".join("?" for _ in columns)
  connection.executemany(
    f'INSERT INTO "{table}" ({names}) VALUES ({placeholders})',
    ([row.get(column, "") for column in columns] for row in rows),
  )


def install_database(
  source_database: Path,
  output_database: Path,
  anchors: list[dict[str, str]],
  divisions: list[dict[str, Any]],
  memberships: list[dict[str, Any]],
) -> sqlite3.Connection:
  output_database.parent.mkdir(parents=True, exist_ok=True)
  temporary = output_database.with_suffix(output_database.suffix + ".tmp")
  if temporary.exists():
    temporary.unlink()
  shutil.copy2(source_database, temporary)
  connection = sqlite3.connect(temporary)
  connection.row_factory = sqlite3.Row
  connection.execute("PRAGMA foreign_keys=OFF")
  create_v07_tables(connection)
  insert_rows(connection, "historical_local_unit_anchor", ANCHOR_COLUMNS, anchors)
  insert_rows(connection, "local_division_definition", DIVISION_COLUMNS, divisions)
  insert_rows(connection, "settlement_local_division", MEMBERSHIP_COLUMNS, memberships)
  connection.execute("CREATE INDEX idx_local_division_county ON local_division_definition(county_id,division_type_code,division_id)")
  connection.execute("CREATE UNIQUE INDEX idx_local_division_county_name ON local_division_definition(county_id,division_name)")
  connection.execute("CREATE INDEX idx_local_division_subregion ON local_division_definition(primary_subregion_id,division_id)")
  connection.execute("CREATE INDEX idx_local_membership_division ON settlement_local_division(division_id,settlement_id)")
  connection.execute("CREATE INDEX idx_local_membership_county ON settlement_local_division(county_id,division_id)")
  for view in (
    "v_county_entry_local_divisions",
    "v_local_division_entry_settlements",
    "v_county_entry_settlements",
  ):
    connection.execute(f'DROP VIEW IF EXISTS "{view}"')
  connection.execute(
    "CREATE VIEW v_county_entry_local_divisions AS "
    "SELECT d.*,e.region,e.upper_unit,e.intermediate_unit,e.county,"
    "z.subregion_name,z.direction_name,z.zone_type,z.primary_landform,z.primary_resource_tags "
    "FROM local_division_definition d "
    "JOIN county_economy_baseline e USING(county_id) "
    "JOIN county_subregion_definition z ON z.subregion_id=d.primary_subregion_id"
  )
  connection.execute(
    "CREATE VIEW v_local_division_entry_settlements AS "
    "SELECT m.division_id,d.division_name,d.division_type_code,d.is_county_core,"
    "m.membership_method,m.historical_membership_claim,m.source_unit_anchor_id AS membership_source_unit_anchor_id,"
    "m.distance_score_0_10000,s.*,z.subregion_name,z.direction_name,z.zone_type "
    "FROM settlement_local_division m "
    "JOIN local_division_definition d USING(division_id) "
    "JOIN settlement_node s USING(settlement_id) "
    "JOIN county_subregion_definition z ON z.subregion_id=s.subregion_id"
  )
  connection.execute(
    "CREATE VIEW v_county_entry_settlements AS "
    "SELECT s.*,e.region,e.upper_unit,e.intermediate_unit,e.county,"
    "z.subregion_name,z.direction_name,z.zone_type,z.primary_landform,z.primary_resource_tags,"
    "m.division_id,d.division_name "
    "FROM settlement_node s JOIN county_economy_baseline e USING(county_id) "
    "LEFT JOIN county_subregion_definition z USING(subregion_id) "
    "JOIN settlement_local_division m USING(settlement_id) "
    "JOIN local_division_definition d USING(division_id)"
  )
  connection.execute("PRAGMA user_version=7")
  connection.commit()
  connection.execute("PRAGMA foreign_keys=ON")
  return connection


def query_elapsed_ms(connection: sqlite3.Connection, sql: str, parameter: str) -> float:
  connection.execute(sql, (parameter,)).fetchall()
  samples = []
  for _ in range(4):
    start = time.perf_counter()
    connection.execute(sql, (parameter,)).fetchall()
    samples.append((time.perf_counter() - start) * 1000)
  return round(max(samples), 3)


def validate_build(
  connection: sqlite3.Connection,
  source_database: Path,
  source_hash_before: str,
  anchors: list[dict[str, str]],
  divisions: list[dict[str, Any]],
  memberships: list[dict[str, Any]],
) -> dict[str, Any]:
  checks: dict[str, Any] = {}
  checks["source_database_sha256_before"] = source_hash_before
  checks["source_database_sha256_after"] = file_sha256(source_database)
  checks["source_database_unchanged"] = checks["source_database_sha256_before"] == checks["source_database_sha256_after"]
  checks["anchor_rows"] = connection.execute("SELECT COUNT(*) FROM historical_local_unit_anchor").fetchone()[0]
  checks["division_rows"] = connection.execute("SELECT COUNT(*) FROM local_division_definition").fetchone()[0]
  checks["division_rows_stably_ordered"] = [
    row["division_id"] for row in divisions
  ] == sorted(row["division_id"] for row in divisions)
  checks["division_counties"] = connection.execute("SELECT COUNT(DISTINCT county_id) FROM local_division_definition").fetchone()[0]
  checks["membership_rows"] = connection.execute("SELECT COUNT(*) FROM settlement_local_division").fetchone()[0]
  checks["membership_rows_stably_ordered"] = [
    row["settlement_id"] for row in memberships
  ] == sorted(row["settlement_id"] for row in memberships)
  checks["source_settlement_rows"] = connection.execute("SELECT COUNT(*) FROM settlement_node").fetchone()[0]
  checks["source_village_rows"] = connection.execute("SELECT COUNT(*) FROM settlement_node WHERE settlement_type_code='village'").fetchone()[0]
  checks["unassigned_settlement_count"] = connection.execute(
    "SELECT COUNT(*) FROM settlement_node s LEFT JOIN settlement_local_division m USING(settlement_id) WHERE m.settlement_id IS NULL"
  ).fetchone()[0]
  checks["cross_county_membership_count"] = connection.execute(
    "SELECT COUNT(*) FROM settlement_local_division m JOIN settlement_node s USING(settlement_id) "
    "JOIN local_division_definition d USING(division_id) WHERE m.county_id<>s.county_id OR m.county_id<>d.county_id"
  ).fetchone()[0]
  checks["county_core_count_mismatch"] = connection.execute(
    "SELECT COUNT(*) FROM (SELECT county_id,SUM(is_county_core) n FROM local_division_definition GROUP BY county_id HAVING n<>1)"
  ).fetchone()[0]
  checks["county_seat_core_membership_mismatch"] = connection.execute(
    "SELECT COUNT(*) FROM settlement_node s JOIN settlement_local_division m USING(settlement_id) "
    "JOIN local_division_definition d USING(division_id) "
    "WHERE s.settlement_type_code='county_seat' AND (d.is_county_core<>1 OR d.center_settlement_id<>s.settlement_id)"
  ).fetchone()[0]
  checks["market_town_center_mismatch"] = connection.execute(
    "SELECT COUNT(*) FROM settlement_node s LEFT JOIN local_division_definition d "
    "ON d.center_settlement_id=s.settlement_id "
    "WHERE s.settlement_type_code='market_town' AND (d.division_id IS NULL OR d.division_type_code<>'town')"
  ).fetchone()[0]
  checks["county_total_mismatch_count"] = connection.execute(
    "SELECT COUNT(*) FROM (SELECT d.county_id,"
    "SUM(d.resident_population_est) p,SUM(d.household_count_est) h,SUM(d.labor_force_est) l,"
    "e.population_est_1628 ep,e.household_count_est eh,e.labor_force_est el "
    "FROM local_division_definition d JOIN county_economy_baseline e USING(county_id) GROUP BY d.county_id "
    "HAVING p<>ep OR h<>eh OR l<>el)"
  ).fetchone()[0]
  checks["county_weight_mismatch_count"] = connection.execute(
    "SELECT COUNT(*) FROM (SELECT county_id,SUM(population_share_ppm) p,SUM(household_share_ppm) h,"
    "SUM(labor_share_ppm) l,SUM(area_share_ppm) a,SUM(farmland_share_ppm) f "
    "FROM local_division_definition GROUP BY county_id "
    "HAVING p<>1000000 OR h<>1000000 OR l<>1000000 OR a<>1000000 OR f<>1000000)"
  ).fetchone()[0]
  resource_mismatches = 0
  for resource in RESOURCE_COLUMNS:
    rows = connection.execute(
      f"SELECT d.county_id,SUM(d.{resource}*d.area_share_ppm)*1.0/1000000 projected,e.{resource} baseline "
      "FROM local_division_definition d JOIN county_economy_baseline e USING(county_id) GROUP BY d.county_id"
    ).fetchall()
    resource_mismatches += sum(abs(round(float(row[1])) - int(row[2])) > 1 for row in rows)
  checks["resource_weighted_mismatch_count"] = resource_mismatches
  checks["historical_boundary_claim_count"] = connection.execute(
    "SELECT COUNT(*) FROM local_division_definition WHERE boundary_historical_claim<>'no'"
  ).fetchone()[0]
  checks["generated_historical_name_misclaim_count"] = connection.execute(
    "SELECT COUNT(*) FROM local_division_definition WHERE source_unit_anchor_id='' AND historical_name_claim<>'no'"
  ).fetchone()[0]
  checks["historical_membership_misclaim_count"] = connection.execute(
    "SELECT COUNT(*) FROM settlement_local_division m "
    "LEFT JOIN historical_local_unit_anchor h ON h.anchor_id=m.source_unit_anchor_id "
    "WHERE m.historical_membership_claim='yes' AND "
    "(h.anchor_id IS NULL OR h.evidence_scope<>'documented_membership' OR h.matched_settlement_id<>m.settlement_id)"
  ).fetchone()[0]
  checks["daxing_division_count"] = connection.execute(
    "SELECT COUNT(*) FROM local_division_definition WHERE county_id='MING1628-0001'"
  ).fetchone()[0]
  checks["daxing_count_anchor_count"] = connection.execute(
    "SELECT COUNT(*) FROM historical_local_unit_anchor "
    "WHERE county_id='MING1628-0001' AND source_unit_type='li_count' AND unit_count=36 "
    "AND evidence_scope='count_only' AND historical_claim='yes'"
  ).fetchone()[0]
  checks["daxing_source_backed_division_count"] = connection.execute(
    "SELECT COUNT(*) FROM local_division_definition "
    "WHERE county_id='MING1628-0001' AND source_unit_anchor_id<>''"
  ).fetchone()[0]
  checks["wujiang_division_count"] = connection.execute(
    "SELECT COUNT(*) FROM local_division_definition WHERE county_id='MING1628-0156'"
  ).fetchone()[0]
  checks["wujiang_xiang_anchor_count"] = connection.execute(
    "SELECT COUNT(*) FROM historical_local_unit_anchor "
    "WHERE county_id='MING1628-0156' AND source_unit_type='xiang'"
  ).fetchone()[0]
  checks["wujiang_du_anchor_count"] = connection.execute(
    "SELECT COUNT(*) FROM historical_local_unit_anchor "
    "WHERE county_id='MING1628-0156' AND source_unit_type='du'"
  ).fetchone()[0]
  checks["wujiang_town_anchor_count"] = connection.execute(
    "SELECT COUNT(*) FROM historical_local_unit_anchor "
    "WHERE county_id='MING1628-0156' AND source_unit_type='zhen'"
  ).fetchone()[0]
  checks["wujiang_du_parent_mismatch_count"] = connection.execute(
    "SELECT COUNT(*) FROM historical_local_unit_anchor d "
    "LEFT JOIN historical_local_unit_anchor x ON x.anchor_id=d.parent_anchor_id "
    "WHERE d.county_id='MING1628-0156' AND d.source_unit_type='du' "
    "AND (x.anchor_id IS NULL OR x.county_id<>d.county_id OR x.source_unit_type<>'xiang')"
  ).fetchone()[0]
  checks["wujiang_source_backed_division_count"] = connection.execute(
    "SELECT COUNT(*) FROM local_division_definition WHERE county_id='MING1628-0156' AND source_unit_anchor_id<>''"
  ).fetchone()[0]
  checks["wujiang_documented_membership_count"] = connection.execute(
    "SELECT COUNT(*) FROM settlement_local_division WHERE county_id='MING1628-0156' AND historical_membership_claim='yes'"
  ).fetchone()[0]
  checks["wujiang_documented_membership_mismatch_count"] = connection.execute(
    "SELECT COUNT(*) FROM historical_local_unit_anchor h "
    "LEFT JOIN settlement_local_division m ON m.settlement_id=h.matched_settlement_id "
    "LEFT JOIN local_division_definition d ON d.division_id=m.division_id "
    "WHERE h.county_id='MING1628-0156' AND h.evidence_scope='documented_membership' "
    "AND (m.source_unit_anchor_id<>h.anchor_id OR m.historical_membership_claim<>'yes' "
    "OR d.source_unit_anchor_id<>h.parent_anchor_id)"
  ).fetchone()[0]
  checks["yangcheng_source_record_count"] = connection.execute(
    "SELECT COUNT(*) FROM historical_local_unit_anchor "
    "WHERE county_id='MING1628-0373' AND source_unit_type='source_record' "
    "AND source_year=1625 AND evidence_scope='source_exist_only' AND historical_claim='no'"
  ).fetchone()[0]
  checks["yangcheng_source_backed_division_count"] = connection.execute(
    "SELECT COUNT(*) FROM local_division_definition WHERE county_id='MING1628-0373' AND source_unit_anchor_id<>''"
  ).fetchone()[0]
  checks["foreign_key_check_count"] = len(connection.execute("PRAGMA foreign_key_check").fetchall())
  checks["user_version"] = connection.execute("PRAGMA user_version").fetchone()[0]
  checks["view_row_counts"] = {
    view: connection.execute(f'SELECT COUNT(*) FROM "{view}"').fetchone()[0]
    for view in (
      "v_county_entry_local_divisions",
      "v_local_division_entry_settlements",
      "v_county_entry_settlements",
    )
  }
  largest_county = connection.execute(
    "SELECT county_id FROM local_division_definition GROUP BY county_id ORDER BY COUNT(*) DESC,county_id LIMIT 1"
  ).fetchone()[0]
  largest_division = connection.execute(
    "SELECT division_id FROM settlement_local_division GROUP BY division_id ORDER BY COUNT(*) DESC,division_id LIMIT 1"
  ).fetchone()[0]
  checks["query_performance_ms"] = {
    "largest_county_divisions": query_elapsed_ms(
      connection,
      "SELECT * FROM v_county_entry_local_divisions WHERE county_id=? ORDER BY division_id",
      largest_county,
    ),
    "largest_division_settlements": query_elapsed_ms(
      connection,
      "SELECT * FROM v_local_division_entry_settlements WHERE division_id=? ORDER BY settlement_id",
      largest_division,
    ),
  }
  checks["query_performance_under_250ms"] = all(
    value < 250 for value in checks["query_performance_ms"].values()
  )
  connection.execute("ATTACH DATABASE ? AS source_v06", (str(source_database),))
  checks["v06_settlement_identity_mismatch_count"] = connection.execute(
    "SELECT COUNT(*) FROM settlement_node t JOIN source_v06.settlement_node s USING(settlement_id) "
    "WHERE t.county_id<>s.county_id OR t.subregion_id<>s.subregion_id OR t.settlement_name<>s.settlement_name "
    "OR t.settlement_type_code<>s.settlement_type_code OR t.resident_population<>s.resident_population "
    "OR t.labor_force_est<>s.labor_force_est"
  ).fetchone()[0]
  source_view_columns = [
    row[1] for row in connection.execute(
      "PRAGMA source_v06.table_info(v_county_entry_settlements)"
    ).fetchall()
  ]
  current_view_columns = [
    row[1] for row in connection.execute(
      "PRAGMA main.table_info(v_county_entry_settlements)"
    ).fetchall()
  ]
  checks["v06_county_entry_view_prefix_compatible"] = (
    current_view_columns[:len(source_view_columns)] == source_view_columns
  )
  checks["v07_county_entry_view_appended_columns"] = (
    current_view_columns[len(source_view_columns):] == ["division_id", "division_name"]
  )
  connection.execute("DETACH DATABASE source_v06")

  expected = {
    "source_database_unchanged": True,
    "anchor_rows": len(anchors),
    "division_rows": len(divisions),
    "division_rows_stably_ordered": True,
    "division_counties": EXPECTED_COUNTIES,
    "membership_rows": EXPECTED_SETTLEMENTS,
    "membership_rows_stably_ordered": True,
    "source_settlement_rows": EXPECTED_SETTLEMENTS,
    "source_village_rows": EXPECTED_VILLAGES,
    "unassigned_settlement_count": 0,
    "cross_county_membership_count": 0,
    "county_core_count_mismatch": 0,
    "county_seat_core_membership_mismatch": 0,
    "market_town_center_mismatch": 0,
    "county_total_mismatch_count": 0,
    "county_weight_mismatch_count": 0,
    "resource_weighted_mismatch_count": 0,
    "historical_boundary_claim_count": 0,
    "generated_historical_name_misclaim_count": 0,
    "historical_membership_misclaim_count": 0,
    "daxing_division_count": 36,
    "daxing_count_anchor_count": 1,
    "daxing_source_backed_division_count": 0,
    "wujiang_division_count": 29,
    "wujiang_xiang_anchor_count": 6,
    "wujiang_du_anchor_count": 29,
    "wujiang_town_anchor_count": 4,
    "wujiang_du_parent_mismatch_count": 0,
    "wujiang_source_backed_division_count": 29,
    "wujiang_documented_membership_count": 1,
    "wujiang_documented_membership_mismatch_count": 0,
    "yangcheng_source_record_count": 1,
    "yangcheng_source_backed_division_count": 0,
    "foreign_key_check_count": 0,
    "user_version": 7,
    "query_performance_under_250ms": True,
    "v06_settlement_identity_mismatch_count": 0,
    "v06_county_entry_view_prefix_compatible": True,
    "v07_county_entry_view_appended_columns": True,
  }
  errors = [
    f"{key}: expected {value}, got {checks.get(key)}"
    for key, value in expected.items()
    if checks.get(key) != value
  ]
  expected_view_rows = {
    "v_county_entry_local_divisions": len(divisions),
    "v_local_division_entry_settlements": EXPECTED_SETTLEMENTS,
    "v_county_entry_settlements": EXPECTED_SETTLEMENTS,
  }
  for view, expected_rows in expected_view_rows.items():
    actual = checks["view_row_counts"][view]
    if actual != expected_rows:
      errors.append(f"{view}: expected {expected_rows}, got {actual}")
  return {
    "status": "pass" if not errors else "fail",
    "ruleset_version": RULESET_VERSION,
    "errors": errors,
    "checks": checks,
  }


def main() -> None:
  parser = argparse.ArgumentParser(description="Build Ming 1628 county-local township/town divisions v0.7")
  parser.add_argument("--source-database", type=Path, default=DEFAULT_SOURCE_DATABASE)
  parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT_DIR)
  args = parser.parse_args()
  if not args.source_database.exists():
    raise SystemExit(f"Missing v0.6 SQLite database: {args.source_database}")
  source_hash_before = file_sha256(args.source_database)
  output_dir = args.output_dir
  output_dir.mkdir(parents=True, exist_ok=True)
  report_path = output_dir / "local_division_v0.7_validation_report.json"
  previous_validation: dict[str, Any] = {}
  if report_path.exists():
    try:
      previous_validation = json.loads(report_path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
      previous_validation = {}
  anchors_path = output_dir / "historical_local_unit_anchors_v0.7.csv"
  if not anchors_path.exists():
    raise SystemExit(f"Missing historical local-unit anchors: {anchors_path}")
  anchors = read_csv(anchors_path)
  if not anchors or list(anchors[0]) != ANCHOR_COLUMNS:
    raise RuntimeError("historical local-unit anchor columns do not match v0.7 schema")
  if len({row["anchor_id"] for row in anchors}) != len(anchors):
    raise RuntimeError("duplicate historical local-unit anchor id")
  source = sqlite3.connect(f"file:{args.source_database}?mode=ro", uri=True)
  source.row_factory = sqlite3.Row
  if source.execute("PRAGMA user_version").fetchone()[0] != 6:
    raise RuntimeError("v0.7 requires source SQLite user_version=6")
  print("[v0.7] building deterministic local divisions", flush=True)
  divisions, memberships, summaries = build_all(source, anchors)
  source.close()
  divisions.sort(key=lambda row: row["division_id"])
  memberships.sort(key=lambda row: row["settlement_id"])
  summaries.sort(key=lambda row: row["county_id"])
  source_manifest = build_source_manifest(args.source_database, anchors_path)

  division_path = output_dir / "local_division_definition_v0.7.csv"
  summary_path = output_dir / "county_local_division_summary_v0.7.csv"
  source_path = output_dir / "local_division_source_manifest_v0.7.csv"
  generated_dir = output_dir / "generated"
  membership_path = generated_dir / "settlement_local_division_v0.7.csv"
  write_csv_atomic(division_path, DIVISION_COLUMNS, divisions)
  write_csv_atomic(summary_path, SUMMARY_COLUMNS, summaries)
  write_csv_atomic(source_path, SOURCE_COLUMNS, source_manifest)
  write_csv_atomic(membership_path, MEMBERSHIP_COLUMNS, memberships)

  database_path = output_dir / "game_world_1628_v0.7.sqlite"
  print(f"[v0.7] installing {len(divisions):,} divisions and {len(memberships):,} memberships", flush=True)
  database = install_database(
    args.source_database, database_path, anchors, divisions, memberships,
  )
  validation = validate_build(
    database, args.source_database, source_hash_before, anchors, divisions, memberships,
  )
  if validation["status"] != "pass":
    database.close()
    raise RuntimeError("v0.7 validation failed: " + "; ".join(validation["errors"]))
  database.execute("VACUUM")
  database.close()
  temporary_database = database_path.with_suffix(database_path.suffix + ".tmp")
  if not temporary_database.exists():
    raise RuntimeError("temporary v0.7 database is missing")
  if database_path.exists():
    database_path.unlink()
  temporary_database.replace(database_path)
  validation["database_sha256"] = file_sha256(database_path)
  validation["generated_file_hashes"] = {
    division_path.name: file_sha256(division_path),
    membership_path.name: file_sha256(membership_path),
    summary_path.name: file_sha256(summary_path),
    source_path.name: file_sha256(source_path),
  }
  validation["input_hashes"] = {
    "source_database": source_hash_before,
    "historical_local_unit_anchors": file_sha256(anchors_path),
    "builder": file_sha256(Path(__file__).resolve()),
  }
  comparable_repeat = (
    previous_validation.get("status") == "pass"
    and previous_validation.get("ruleset_version") == RULESET_VERSION
    and previous_validation.get("input_hashes") == validation["input_hashes"]
  )
  database_hash_match = (
    comparable_repeat
    and previous_validation.get("database_sha256") == validation["database_sha256"]
  )
  generated_hashes_match = (
    comparable_repeat
    and previous_validation.get("generated_file_hashes") == validation["generated_file_hashes"]
  )
  validation["repeat_build"] = {
    "previous_comparable_run_found": comparable_repeat,
    "database_sha256_match": database_hash_match if comparable_repeat else None,
    "generated_file_hashes_match": generated_hashes_match if comparable_repeat else None,
    "deterministic_outputs_match": (
      database_hash_match and generated_hashes_match
      if comparable_repeat else None
    ),
  }
  if comparable_repeat and not (database_hash_match and generated_hashes_match):
    validation["status"] = "fail"
    validation["errors"].append("repeat build output SHA-256 mismatch")
  write_json_atomic(report_path, validation)
  if validation["status"] != "pass":
    raise RuntimeError("v0.7 repeat-build validation failed")
  print(json.dumps({
    "status": validation["status"],
    "divisions": len(divisions),
    "memberships": len(memberships),
    "database": str(database_path),
    "validation": str(report_path),
  }, ensure_ascii=False, indent=2), flush=True)


if __name__ == "__main__":
  main()
