#!/usr/bin/env python3
"""Deterministically materialize one settlement leaf with the v0.6 people rules.

The generator reuses the validated v0.5 household/kinship topology, then applies
v0.6 education, occupation, registration, economic, prestige and local-power
profiles.  It supports villages and every new settlement archetype.  No AI or
unseeded randomness is used, and generated people always carry
``historical_claim=no``.
"""

from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import hashlib
import json
import math
from pathlib import Path
import sqlite3
import time
from typing import Any, Sequence

try:
  from . import generate_ming_1628_village_people as legacy
except ImportError:  # Direct script execution keeps the script directory on sys.path.
  import generate_ming_1628_village_people as legacy


REPO_ROOT = Path(__file__).resolve().parents[2]
DATA_ROOT = REPO_ROOT / "docs/90_资料与归档/01_崇祯元年历史资料/data/1628"
DEFAULT_DATABASE = DATA_ROOT / "13.模拟基础规则/game_world_1628_v1.0.sqlite"
DEFAULT_OUTPUT_DIR = DATA_ROOT / "9.教育职业身份与社会阶层"
RULESET_VERSION = "v0.6"
SCHEMA_VERSION = "settlement_people_v0.6"
DEFAULT_WORLD_SEED = "project-realm-1628"
SNAPSHOT_YEAR = 1628
SUPPORTED_DATABASE_VERSIONS = {6, 7, 8, 9, 10}


REGISTRATION_LABELS = {
  "civilian": "民户", "military": "军户", "artisan": "匠户", "salt": "灶盐户",
  "fish_boat": "渔船户", "post_transport": "驿运户", "medical_ritual": "医阴阳户",
  "literary_student": "儒学生监户", "official_security": "官校役籍",
  "mixed_unknown": "混合或不详",
}
ECONOMIC_LABELS = {
  "dependent_bonded": "依附奴仆", "landless_labor": "无地雇工", "tenant": "佃户",
  "smallholder": "小自耕户", "stable_proprietor": "稳定业主",
  "wealthy_master": "富裕业主或作坊主", "landlord_merchant_capital": "大地主或大商人",
}
PRESTIGE_LABELS = {
  "commoner": "普通人", "skilled": "熟练工匠或商人", "literate": "识字者",
  "local_elder": "地方长者", "student": "读书应试者", "degree_holder": "有功名者",
  "official": "官员", "retired_official": "退居官员", "religious_medical": "医药宗教名望",
}
SECTOR_LABELS = {
  "agriculture": "农业", "forestry_hunting": "林猎", "pastoral": "畜牧",
  "fishery_water": "渔业水产", "mining_salt": "矿盐", "food_processing": "食品加工",
  "textile_clothing": "纺织服饰", "ceramics_building": "陶瓷建材",
  "metal_wood_paper": "金木纸作", "transport_post_port": "交通驿运",
  "commerce_finance": "商业金融", "domestic_service": "生活服务",
  "medicine_health": "医药", "religion_ritual": "宗教礼仪",
  "education_culture": "教育文化", "government_admin": "官署行政",
  "military_security": "军事治安", "marginal_unfixed": "无定业边缘生计",
}


def clamp(value: float, low: float, high: float) -> float:
  return max(low, min(high, value))


def stable_digest(*parts: Any) -> bytes:
  text = "|".join([RULESET_VERSION, *(str(part) for part in parts)])
  return hashlib.sha256(text.encode("utf-8")).digest()


def stable_unit(*parts: Any) -> float:
  return int.from_bytes(stable_digest(*parts)[:8], "big") / (2**64 - 1)


def stable_int(low: int, high: int, *parts: Any) -> int:
  if high < low:
    raise ValueError(f"invalid deterministic range {low}..{high}")
  return low + int(stable_unit(*parts) * (high - low + 1)) % (high - low + 1)


def allocate_exact(total: int, weights: Sequence[float]) -> list[int]:
  if not weights:
    return []
  clean = [max(0.0, float(value)) for value in weights]
  if sum(clean) <= 0:
    clean = [1.0] * len(clean)
  exact = [total * value / sum(clean) for value in clean]
  result = [math.floor(value) for value in exact]
  remainder = total - sum(result)
  order = sorted(range(len(exact)), key=lambda index: (-(exact[index] - math.floor(exact[index])), index))
  for index in order[:remainder]:
    result[index] += 1
  if sum(result) != total:
    raise RuntimeError("exact allocation failed")
  return result


def rows_as_dicts(cursor: sqlite3.Cursor) -> list[dict[str, Any]]:
  return [dict(row) for row in cursor.fetchall()]


def file_sha256(path: Path) -> str:
  digest = hashlib.sha256()
  with path.open("rb") as stream:
    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
      digest.update(chunk)
  return digest.hexdigest()


def parent_settlement_id(object_id: str, marker: str) -> str | None:
  """Recover the stable parent settlement prefix from a leaf-zone or POI ID."""
  prefix, separator, ordinal = object_id.rpartition(marker)
  if separator and prefix and len(ordinal) == 3 and ordinal.isdigit():
    return prefix
  return None


def resolve_location(
  database: Path,
  settlement_id: str | None,
  settlement_name: str | None,
  village_id: str | None,
  county_id: str | None,
  zone_id: str | None,
  poi_id: str | None,
) -> dict[str, Any]:
  if not database.exists():
    raise SystemExit(f"Missing Ming 1628 runtime SQLite database: {database}")
  connection = sqlite3.connect(f"file:{database}?mode=ro", uri=True)
  connection.row_factory = sqlite3.Row
  try:
    user_version = connection.execute("PRAGMA user_version").fetchone()[0]
    if user_version not in SUPPORTED_DATABASE_VERSIONS:
      supported = ", ".join(str(version) for version in sorted(SUPPORTED_DATABASE_VERSIONS))
      raise RuntimeError(
        f"v0.6 settlement people rules support database user_version in {{{supported}}}, got {user_version}"
      )
    resolved_id = settlement_id or village_id
    if poi_id:
      poi_parent_id = parent_settlement_id(poi_id, "-P")
      if not poi_parent_id:
        raise SystemExit(f"Invalid poi ID format: {poi_id}")
      poi = connection.execute(
        "SELECT * FROM v_zone_entry_pois WHERE settlement_id=? AND poi_id=?",
        (poi_parent_id, poi_id),
      ).fetchone()
      if not poi:
        raise SystemExit(f"No matching poi: {poi_id}")
      poi = dict(poi)
      resolved_id = poi["settlement_id"]
      zone_id = poi["zone_id"]
    elif zone_id and not resolved_id:
      zone_parent_id = parent_settlement_id(zone_id, "-B")
      if not zone_parent_id:
        raise SystemExit(f"Invalid zone ID format: {zone_id}")
      zone_match = connection.execute(
        "SELECT settlement_id FROM v_settlement_entry_zones "
        "WHERE settlement_id=? AND zone_id=?",
        (zone_parent_id, zone_id),
      ).fetchone()
      if not zone_match:
        raise SystemExit(f"No matching zone: {zone_id}")
      resolved_id = zone_match["settlement_id"]
      poi = None
    else:
      poi = None
    if resolved_id:
      settlements = rows_as_dicts(connection.execute(
        "SELECT * FROM v_county_entry_settlements WHERE settlement_id=?", (resolved_id,)
      ))
    elif settlement_name:
      if county_id:
        settlements = rows_as_dicts(connection.execute(
          "SELECT * FROM v_county_entry_settlements WHERE settlement_name=? AND county_id=? ORDER BY settlement_id",
          (settlement_name, county_id),
        ))
      else:
        settlements = rows_as_dicts(connection.execute(
          "SELECT * FROM v_county_entry_settlements WHERE settlement_name=? ORDER BY county_id,settlement_id",
          (settlement_name,),
        ))
    else:
      raise SystemExit("Specify --settlement-id, --village-id, --settlement-name or --poi-id")
    if not settlements:
      raise SystemExit("No matching settlement was found")
    if len(settlements) > 1:
      matches = ", ".join(f"{row['county']}:{row['settlement_id']}" for row in settlements[:12])
      raise SystemExit(f"Settlement name is ambiguous; add --county-id. Matches: {matches}")
    settlement = settlements[0]
    zones = rows_as_dicts(connection.execute(
      "SELECT * FROM v_settlement_entry_zones WHERE settlement_id=? ORDER BY zone_id",
      (settlement["settlement_id"],),
    ))
    if not zones:
      raise RuntimeError(f"settlement has no leaf zones: {settlement['settlement_id']}")
    if zone_id:
      matches = [row for row in zones if row["zone_id"] == zone_id]
      if not matches:
        raise SystemExit(f"Zone {zone_id} does not belong to settlement {settlement['settlement_id']}")
      zone = matches[0]
      zone_selection_method = "explicit"
    else:
      zone = zones[0]
      zone_selection_method = "single-zone" if len(zones) == 1 else "first-leaf-default"
    county_id = settlement["county_id"]
    subregion = dict(connection.execute(
      "SELECT * FROM county_subregion_definition WHERE subregion_id=?", (settlement["subregion_id"],)
    ).fetchone())
    economy = dict(connection.execute(
      "SELECT * FROM county_economy_baseline WHERE county_id=?", (county_id,)
    ).fetchone())
    culture = dict(connection.execute(
      "SELECT * FROM county_culture_education_baseline WHERE county_id=?", (county_id,)
    ).fetchone())
    education_profile = dict(connection.execute(
      "SELECT * FROM county_education_profile WHERE county_id=?", (county_id,)
    ).fetchone())
    social_profile = dict(connection.execute(
      "SELECT * FROM county_social_structure_baseline WHERE county_id=?", (county_id,)
    ).fetchone())
    occupation_definitions = rows_as_dicts(connection.execute(
      "SELECT * FROM occupation_definition ORDER BY occupation_code"
    ))
    county_quotas = rows_as_dicts(connection.execute(
      "SELECT * FROM county_occupation_quota WHERE county_id=? ORDER BY occupation_code", (county_id,)
    ))
    sector_quota = dict(connection.execute(
      "SELECT * FROM settlement_sector_quota WHERE settlement_id=?", (settlement["settlement_id"],)
    ).fetchone())
    zone_pois = rows_as_dicts(connection.execute(
      "SELECT * FROM v_zone_entry_pois WHERE settlement_id=? AND zone_id=? ORDER BY poi_id",
      (settlement["settlement_id"], zone["zone_id"]),
    ))
    source_surnames = rows_as_dicts(connection.execute(
      "SELECT surname,COUNT(*) person_count FROM historical_person_catalog "
      "WHERE primary_county_id=? AND surname<>'' GROUP BY surname ORDER BY surname", (county_id,)
    ))
    source_families = rows_as_dicts(connection.execute(
      "SELECT surname,SUM(member_count) member_count,COUNT(*) branch_count,"
      "SUM(CASE WHEN is_notable_lineage='yes' THEN 1 ELSE 0 END) notable_count "
      "FROM historical_family_lineage WHERE county_id=? GROUP BY surname ORDER BY surname", (county_id,)
    ))
    settlement_summary = dict(connection.execute(
      "SELECT * FROM county_settlement_summary WHERE county_id=?", (county_id,)
    ).fetchone())
  finally:
    connection.close()

  legacy_village = {
    "village_id": zone["zone_id"], "snapshot_year": SNAPSHOT_YEAR,
    "region": settlement["region"], "upper_unit": settlement["upper_unit"],
    "intermediate_unit": settlement["intermediate_unit"], "county": settlement["county"],
    "county_id": county_id, "subregion_id": settlement["subregion_id"],
    "subregion_name": settlement.get("subregion_name", ""),
    "direction_code": subregion["direction_code"], "direction_name": subregion["direction_name"],
    "zone_type": subregion["zone_type"], "primary_landform": subregion["primary_landform"],
    "water_context": subregion["water_context"], "primary_resource_tags": subregion["primary_resource_tags"],
    "render_biome_code": subregion["render_biome_code"],
    "village_name": f"{settlement['settlement_name']}·{zone['zone_name']}",
    "settlement_form": settlement["settlement_type_code"],
    "name_source_type": settlement["name_source_type"],
    "historical_name_claim": settlement["historical_name_claim"], "anchor_id": "",
    "relative_x_0_10000": zone["relative_x_0_10000"], "relative_y_0_10000": zone["relative_y_0_10000"],
    "population_weight_ppm": 0, "farmland_weight_ppm": 0,
    "projected_rural_population": zone["resident_population"],
    "render_seed": zone["render_seed"], "position_method": "v0.6 settlement leaf",
    "commercial_release_ready": "no",
  }
  return {
    "source_database_name": database.name, "database_user_version": user_version,
    "village": legacy_village, "subregion": subregion, "economy": economy,
    "culture": culture, "settlement": settlement_summary,
    "source_surnames": source_surnames, "source_families": source_families,
    "location": settlement, "zone": zone, "poi": poi,
    "zone_selection_method": zone_selection_method, "available_zone_count": len(zones),
    "education_profile": education_profile, "social_profile": social_profile,
    "occupation_definitions": occupation_definitions, "county_occupation_quotas": county_quotas,
    "settlement_sector_quota": sector_quota, "zone_pois": zone_pois,
  }


def education_level(person: dict[str, Any], household: dict[str, Any], world_seed: str) -> str:
  if person["is_literate"] != "yes":
    return "L0"
  if person["is_classically_educated"] == "yes":
    return "L4"
  score = (
    household["wealth_index_0_100"] * 0.42
    + min(100, person["age_1628"] * 2.0) * 0.18
    + stable_unit(world_seed, person["person_id"], "literacy-band") * 40
  )
  return "L3" if score >= 68 else "L2" if score >= 42 else "L1"


def assign_education(payload: dict[str, Any], source: dict[str, Any], world_seed: str) -> None:
  households = {row["household_id"]: row for row in payload["households"]}
  people = payload["people"]
  floors = {
    "L0": (0, 0, 4, 0, 0), "L1": (18, 4, 8, 0, 2),
    "L2": (38, 18, 28, 4, 12), "L3": (62, 55, 58, 22, 55),
    "L4": (82, 78, 60, 75, 68),
  }
  education_pois = [row for row in source["zone_pois"] if row["sector_code"] == "education_culture"]
  for person in people:
    household = households[person["household_id"]]
    level = education_level(person, household, world_seed)
    base = floors[level]
    skills = [round(clamp(value + stable_int(-4, 10, world_seed, person["person_id"], index, "education-skill"), 0, 100)) for index, value in enumerate(base)]
    person["literacy_level_code"] = level
    person["reading_skill_0_100"], person["writing_skill_0_100"], person["numeracy_skill_0_100"], person["classics_skill_0_100"], person["document_skill_0_100"] = skills
    if level == "L0":
      route = "none"
    elif level == "L1":
      route = "home_learning"
    elif level == "L2":
      route = "family_school" if household["wealth_index_0_100"] >= 66 else "village_school"
    elif level == "L3":
      route = "official_local_school" if source["location"]["settlement_type_code"] == "county_seat" and stable_unit(world_seed, person["person_id"], "official-school") < 0.35 else "family_school"
    else:
      route = "academy" if education_pois and stable_unit(world_seed, person["person_id"], "academy-route") < 0.35 else "official_local_school" if source["location"]["settlement_type_code"] in {"county_seat", "market_town"} else "family_school"
    person["education_route_code"] = route
    person["credential_code"] = "none"
    person["education_status_code"] = "studying" if 7 <= person["age_1628"] <= 20 and level != "L0" else "completed" if level != "L0" else "not_enrolled"
    person["education_institution_id"] = education_pois[stable_int(0, len(education_pois) - 1, world_seed, person["person_id"], "education-poi")]["poi_id"] if education_pois else ""
    person["education_source_type"] = "county_v0.4_hard_total_v0.6_profile_projection"

  profile = source["education_profile"]
  location_factor = {
    "county_seat": 3.0, "market_town": 1.5, "village": 0.35,
    "military_settlement": 0.4, "resource_industrial": 0.25, "transport_port_station": 0.5,
  }[source["location"]["settlement_type_code"]]
  population_factor = len(people) / max(1, int(profile["population_est_1628"])) * location_factor
  targets = {
    "jinshi": round(profile["jinshi_est"] * population_factor),
    "gongshi": round(profile["gongshi_est"] * population_factor),
    "juren": round(profile["juren_est"] * population_factor),
    "jiansheng": round(profile["jiansheng_est"] * population_factor),
    "gongsheng": round(profile["gongsheng_est"] * population_factor),
    "shengyuan": round(profile["shengyuan_est"] * population_factor),
    "candidate": round(profile["candidate_est"] * population_factor),
  }
  eligible = [
    person for person in people
    if person["sex"] == "male" and person["age_1628"] >= 16 and person["literacy_level_code"] == "L4"
  ]
  eligible.sort(key=lambda person: (
    -person["classics_skill_0_100"],
    households[person["household_id"]]["wealth_index_0_100"],
    stable_unit(world_seed, person["person_id"], "credential-order"),
  ))
  cursor = 0
  for credential in ("jinshi", "gongshi", "juren", "jiansheng", "gongsheng", "shengyuan", "candidate"):
    count = min(targets[credential], len(eligible) - cursor)
    for person in eligible[cursor:cursor + count]:
      person["credential_code"] = credential
      person["education_status_code"] = "examining" if credential == "candidate" else "completed"
    cursor += count


def local_occupation_counts(source: dict[str, Any]) -> tuple[int, list[tuple[dict[str, Any], int]]]:
  definitions = {row["occupation_code"]: row for row in source["occupation_definitions"]}
  quotas = source["county_occupation_quotas"]
  labor = int(source["zone"]["labor_force_est"])
  settlement_type = source["location"]["settlement_type_code"]
  sector_quota = source["settlement_sector_quota"]
  sector_weights = [int(sector_quota[f"{code}_count"]) for code in SECTOR_LABELS]
  zone_sector_counts = allocate_exact(labor, sector_weights)
  result: list[tuple[dict[str, Any], int]] = []
  for sector, sector_count in zip(SECTOR_LABELS, zone_sector_counts):
    rows = [row for row in quotas if row["sector_code"] == sector]
    weights = []
    for row in rows:
      definition = definitions[row["occupation_code"]]
      affinity = definition["settlement_affinity"].split(";")
      multiplier = 1.0 if settlement_type in affinity else 0.12
      weights.append(float(row["worker_count_est"]) * multiplier)
    counts = allocate_exact(sector_count, weights)
    result.extend((definitions[row["occupation_code"]], count) for row, count in zip(rows, counts) if count)
  return labor, result


def assign_occupations(payload: dict[str, Any], source: dict[str, Any], world_seed: str) -> None:
  people = payload["people"]
  households = {row["household_id"]: row for row in payload["households"]}
  labor, occupation_counts = local_occupation_counts(source)
  candidates = [person for person in people if 13 <= person["age_1628"] <= 75]
  candidates.sort(key=lambda person: (
    0 if 18 <= person["age_1628"] <= 59 else 1,
    -person["age_1628"] if person["age_1628"] < 60 else person["age_1628"],
    stable_unit(world_seed, person["person_id"], "labor-candidate"),
  ))
  workers = candidates[:min(labor, len(candidates))]
  worker_ids = {person["person_id"] for person in workers}
  definitions = {row["occupation_code"]: row for row in source["occupation_definitions"]}
  poi_by_sector: dict[str, list[dict[str, Any]]] = defaultdict(list)
  for poi in source["zone_pois"]:
    poi_by_sector[poi["sector_code"]].append(poi)
  for person in people:
    age = person["age_1628"]
    person["activity_status_code"] = "child" if age < 7 else "learning_and_household_help" if age < 13 else "student" if person["education_status_code"] in {"studying", "examining"} else "retired_partial" if age >= 60 else "household_dependent"
    person["primary_occupation_code"] = ""
    person["secondary_occupation_code"] = ""
    person["seasonal_occupation_code"] = ""
    person["skill_grade_0_100"] = 0
    person["workplace_poi_id"] = ""
    person["occupation_source_type"] = "v0.6_county_quota_and_location_projection"

  occupation_groups = sorted(occupation_counts, key=lambda item: (
    -int(item[0]["minimum_literacy_level"]), -int(item[0]["minimum_classics_0_100"]),
    -int(item[0]["minimum_numeracy_0_100"]), item[0]["occupation_code"],
  ))
  available = {person["person_id"]: person for person in workers}
  assigned: list[tuple[dict[str, Any], dict[str, Any]]] = []
  slot_index = 0
  for definition, requested_count in occupation_groups:
    eligible = []
    for person in available.values():
      level = int(person["literacy_level_code"][1])
      if person["age_1628"] < int(definition["minimum_age"]):
        continue
      if level < int(definition["minimum_literacy_level"]):
        continue
      if person["numeracy_skill_0_100"] < int(definition["minimum_numeracy_0_100"]):
        continue
      if person["classics_skill_0_100"] < int(definition["minimum_classics_0_100"]):
        continue
      sex_weight = float(definition["male_weight"] if person["sex"] == "male" else definition["female_weight"])
      if sex_weight <= 0:
        continue
      eligible.append((
        sex_weight * 30
        + person["reading_skill_0_100"] * int(definition["minimum_literacy_level"]) * 0.05
        + stable_unit(world_seed, person["person_id"], definition["occupation_code"], "occupation-fit") * 25,
        person,
      ))
    eligible.sort(key=lambda item: (item[0], item[1]["person_id"]), reverse=True)
    selected = [item[1] for item in eligible[:requested_count]]
    for person in selected:
      available.pop(person["person_id"])
      assigned.append((person, definition))
    missing_count = requested_count - len(selected)
    for fallback_offset in range(missing_count):
      if not available:
        break
      # Rare formal slots can remain unfilled locally; the worker receives a
      # low-requirement livelihood from the same or agricultural sector.
      person = min(available.values(), key=lambda row: (
        row["age_1628"] < 13,
        stable_unit(world_seed, row["person_id"], slot_index + fallback_offset, "fallback"),
      ))
      fallback_candidates = [
        row for row in definitions.values()
        if row["sector_code"] == definition["sector_code"]
        and int(row["minimum_literacy_level"]) == 0
        and int(row["minimum_age"]) <= person["age_1628"]
      ]
      if not fallback_candidates:
        fallback_candidates = [definitions["farmhand"]]
      fallback_definition = min(fallback_candidates, key=lambda row: row["occupation_code"])
      available.pop(person["person_id"])
      assigned.append((person, fallback_definition))
    slot_index += requested_count
    if not available:
      break
  # If age structure offered fewer candidates than the county labor projection,
  # every eligible person is still assigned exactly once; the shortfall is explicit.
  for person, definition in assigned:
    person["activity_status_code"] = "primary_worker"
    person["primary_occupation_code"] = definition["occupation_code"]
    person["primary_occupation"] = definition["occupation_name_zh_hans"]
    person["skill_grade_0_100"] = round(clamp(
      18 + min(45, max(0, person["age_1628"] - int(definition["minimum_age"])) * 1.1)
      + person["literacy_level_code"].endswith(("3", "4")) * 10
      + stable_unit(world_seed, person["person_id"], "skill-grade") * 25,
      5, 95,
    ))
    pois = poi_by_sector.get(definition["sector_code"], [])
    if pois:
      person["workplace_poi_id"] = pois[stable_int(0, len(pois) - 1, world_seed, person["person_id"], "workplace")]["poi_id"]
    if definition["secondary_allowed"] != "conditional" and stable_unit(world_seed, person["person_id"], "secondary") < 0.26:
      secondary_map = {
        "agriculture": "cotton_spinner" if person["sex"] == "female" else "porter",
        "forestry_hunting": "charcoal_burner", "pastoral": "market_vendor",
        "fishery_water": "fish_processor", "mining_salt": "charcoal_burner",
        "textile_clothing": "market_vendor", "food_processing": "market_vendor",
        "commerce_finance": "accountant", "transport_post_port": "market_vendor",
      }
      secondary = secondary_map.get(definition["sector_code"])
      if secondary in definitions and secondary != definition["occupation_code"]:
        person["secondary_occupation_code"] = secondary
    if definition["sector_code"] in {"agriculture", "forestry_hunting", "fishery_water", "pastoral"}:
      person["seasonal_occupation_code"] = "harvest_worker" if definition["occupation_code"] != "harvest_worker" else "porter"

  # Household livelihood follows the modal working occupation, not every member.
  for household in payload["households"]:
    member_codes = [
      person["primary_occupation_code"] for person in people
      if person["household_id"] == household["household_id"] and person["primary_occupation_code"]
    ]
    if member_codes:
      code = Counter(member_codes).most_common(1)[0][0]
      household["primary_occupation_code"] = code
      household["primary_occupation"] = definitions[code]["occupation_name_zh_hans"]
      household["livelihood_sector_code"] = definitions[code]["sector_code"]
    else:
      household["livelihood_sector_code"] = "household_dependency"
  payload["summary"]["target_labor_force"] = labor
  payload["summary"]["assigned_primary_workers"] = len(assigned)
  payload["summary"]["labor_assignment_shortfall"] = max(0, labor - len(assigned))


def assign_social_axes(payload: dict[str, Any], source: dict[str, Any], world_seed: str) -> None:
  households = payload["households"]
  people = payload["people"]
  profile = source["social_profile"]
  registration_codes = list(REGISTRATION_LABELS)
  registration_counts = allocate_exact(
    len(households), [profile[f"registration_{code}_share_ppm"] for code in registration_codes]
  )
  registration_slots = [code for code, count in zip(registration_codes, registration_counts) for _ in range(count)]
  registration_slots.sort(key=lambda code: stable_unit(world_seed, source["zone"]["zone_id"], code, "registration-slot"))
  household_order = sorted(households, key=lambda row: stable_unit(world_seed, row["household_id"], "registration-household"))
  for household, code in zip(household_order, registration_slots):
    household["registration_status_code"] = code
    household["registration_status"] = REGISTRATION_LABELS[code]
    household["effective_service_obligation_code"] = {
      "military": "military_service_or_substitution", "artisan": "artisan_service_or_commutation",
      "salt": "salt_production_obligation", "post_transport": "post_transport_service",
      "official_security": "security_or_official_service",
    }.get(code, "general_tax_and_corvee")

  economic_codes = list(ECONOMIC_LABELS)
  economic_counts = allocate_exact(
    len(households), [profile[f"economic_{code}_share_ppm"] for code in economic_codes]
  )
  economic_slots = [code for code, count in zip(economic_codes, economic_counts) for _ in range(count)]
  ranked = sorted(households, key=lambda row: (row["wealth_index_0_100"], row["farmland_share_ppm"], row["household_id"]))
  for household, code in zip(ranked, economic_slots):
    household["economic_stratum_code"] = code
    household["economic_stratum"] = ECONOMIC_LABELS[code]
    household["social_stratum"] = ECONOMIC_LABELS[code]

  members_by_household: dict[str, list[dict[str, Any]]] = defaultdict(list)
  for person in people:
    members_by_household[person["household_id"]].append(person)
  occupation_sectors = {
    row["occupation_code"]: row["sector_code"] for row in source["occupation_definitions"]
  }

  # Preserve the exact county-derived registration totals while ensuring the
  # legal register is not collapsed into the same thing as actual livelihood.
  def ensure_registration_cross(registration_code: str, livelihood_sector: str, avoid_code: str) -> None:
    sector_households = [
      household for household in households
      if any(
        occupation_sectors.get(person["primary_occupation_code"]) == livelihood_sector
        for person in members_by_household[household["household_id"]]
      )
    ]
    if any(household["registration_status_code"] == registration_code for household in sector_households):
      return
    recipients = [
      household for household in sector_households
      if household["registration_status_code"] not in {registration_code, avoid_code}
    ] or [household for household in sector_households if household["registration_status_code"] != registration_code]
    donors = [household for household in households if household["registration_status_code"] == registration_code]
    if not recipients or not donors:
      return
    recipient = min(recipients, key=lambda row: row["household_id"])
    donor = min(donors, key=lambda row: row["household_id"])
    for field in ("registration_status_code", "registration_status", "effective_service_obligation_code"):
      recipient[field], donor[field] = donor[field], recipient[field]

  ensure_registration_cross("artisan", "agriculture", "military")
  ensure_registration_cross("military", "commerce_finance", "artisan")

  # Poor degree holders were historically possible. If a scene contains a
  # degree holder but deterministic wealth ranking placed all of them in
  # comfortable households, exchange two household economic slots. Totals do
  # not change; the four social axes remain genuinely independent.
  poor_codes = {"dependent_bonded", "landless_labor", "tenant"}
  degree_people = [person for person in people if person["credential_code"] not in {"none", "candidate"}]
  if degree_people and not any(
    next(household for household in households if household["household_id"] == person["household_id"])["economic_stratum_code"] in poor_codes
    for person in degree_people
  ):
    target_person = min(
      degree_people,
      key=lambda person: (person["credential_code"] != "shengyuan", person["person_id"]),
    )
    target_household = next(
      household for household in households if household["household_id"] == target_person["household_id"]
    )
    degree_household_ids = {person["household_id"] for person in degree_people}
    donors = [
      household for household in households
      if household["economic_stratum_code"] in poor_codes
      and household["household_id"] not in degree_household_ids
    ]
    if donors and target_household["economic_stratum_code"] not in poor_codes:
      donor = min(donors, key=lambda row: (row["wealth_index_0_100"], row["household_id"]))
      for field in ("economic_stratum_code", "economic_stratum", "social_stratum"):
        target_household[field], donor[field] = donor[field], target_household[field]

  household_by_id = {row["household_id"]: row for row in households}
  for person in people:
    household = household_by_id[person["household_id"]]
    occupation = person["primary_occupation_code"]
    credential = person["credential_code"]
    if occupation in {"magistrate_official", "county_assistant_official"}:
      prestige = "official"
    elif credential not in {"none", "candidate"}:
      prestige = "degree_holder"
    elif credential == "candidate":
      prestige = "student"
    elif occupation in {"physician", "buddhist_monk", "daoist_priest", "ritual_specialist"}:
      prestige = "religious_medical"
    elif person["is_literate"] == "yes":
      prestige = "literate"
    elif person["skill_grade_0_100"] >= 70:
      prestige = "skilled"
    elif person["age_1628"] >= 60 and household["wealth_index_0_100"] >= 55:
      prestige = "local_elder"
    else:
      prestige = "commoner"
    person["prestige_status_code"] = prestige
    person["prestige_status"] = PRESTIGE_LABELS[prestige]
    local_roles = []
    if person["household_role"] == "户主":
      local_roles.append("household_head")
    if "族中长者" in person["social_roles"]:
      local_roles.append("clan_elder")
    if any("村中首事" in role for role in person["social_roles"]):
      local_roles.append("village_headman")
    if occupation == "community_head":
      local_roles.append("lijia_service")
    if occupation == "tax_grain_agent":
      local_roles.append("grain_head")
    if occupation in {"broker", "shopkeeper"} and person["skill_grade_0_100"] >= 65:
      local_roles.append("market_guild_head")
    if occupation in {"clerk", "yamen_runner"}:
      local_roles.append("yamen_broker")
    if occupation in {"magistrate_official", "county_assistant_official"}:
      local_roles.append("local_official")
    person["local_power_role_codes"] = sorted(set(local_roles)) or ["none"]
    person["registration_status_code"] = household["registration_status_code"]
    person["economic_stratum_code"] = household["economic_stratum_code"]
    person["social_stratum"] = household["economic_stratum"]


def rebuild_summary(payload: dict[str, Any], source: dict[str, Any]) -> None:
  people = payload["people"]
  households = payload["households"]
  definitions = {row["occupation_code"]: row for row in source["occupation_definitions"]}
  poor_codes = {"dependent_bonded", "landless_labor", "tenant"}
  wealthy_codes = {"wealthy_master", "landlord_merchant_capital"}
  payload["summary"].update({
    "literacy_levels": [
      {"value": code, "count": count}
      for code, count in sorted(Counter(row["literacy_level_code"] for row in people).items())
    ],
    "credentials": [
      {"value": code, "count": count}
      for code, count in sorted(Counter(row["credential_code"] for row in people).items(), key=lambda item: (-item[1], item[0]))
    ],
    "top_person_occupations": [
      {"value": definitions[code]["occupation_name_zh_hans"], "code": code, "count": count}
      for code, count in Counter(row["primary_occupation_code"] for row in people if row["primary_occupation_code"]).most_common(12)
    ],
    "registration_statuses": [
      {"value": REGISTRATION_LABELS[code], "code": code, "count": count}
      for code, count in Counter(row["registration_status_code"] for row in households).most_common()
    ],
    "economic_strata_v06": [
      {"value": ECONOMIC_LABELS[code], "code": code, "count": count}
      for code, count in Counter(row["economic_stratum_code"] for row in households).most_common()
    ],
    "axis_cross_examples": {
      "wealthy_without_degree": sum(
        person["economic_stratum_code"] in wealthy_codes and person["credential_code"] == "none"
        for person in people
      ),
      "poor_degree_holder": sum(
        person["economic_stratum_code"] in poor_codes and person["credential_code"] not in {"none", "candidate"}
        for person in people
      ),
      "military_register_commerce_worker": sum(
        person["registration_status_code"] == "military"
        and definitions.get(person["primary_occupation_code"], {}).get("sector_code") == "commerce_finance"
        for person in people
      ),
      "artisan_register_agricultural_worker": sum(
        person["registration_status_code"] == "artisan"
        and definitions.get(person["primary_occupation_code"], {}).get("sector_code") == "agriculture"
        for person in people
      ),
    },
  })


def validate_v06(payload: dict[str, Any], source: dict[str, Any]) -> dict[str, Any]:
  errors: list[str] = []
  people = payload["people"]
  households = payload["households"]
  valid_occupations = {row["occupation_code"] for row in source["occupation_definitions"]}
  if len(people) != int(source["zone"]["resident_population"]):
    errors.append("person count does not match selected leaf population")
  if any(row["historical_claim"] != "no" for row in people):
    errors.append("generated people assert historical identity")
  if any(row["name_zh_hans"] != row["name_zh_hans"].translate(legacy.TRADITIONAL_SURNAME_TO_SIMPLIFIED) for row in people):
    errors.append("generated display name contains a known traditional surname form")
  if any(row["primary_occupation_code"] and row["primary_occupation_code"] not in valid_occupations for row in people):
    errors.append("unknown primary occupation code")
  if any(not 0 <= row[field] <= 100 for row in people for field in (
    "reading_skill_0_100", "writing_skill_0_100", "numeracy_skill_0_100",
    "classics_skill_0_100", "document_skill_0_100", "skill_grade_0_100",
  )):
    errors.append("education or skill index outside 0..100")
  formal = {"magistrate_official", "county_assistant_official", "clerk", "county_school_teacher", "academy_teacher"}
  if any(row["age_1628"] < 18 and row["primary_occupation_code"] in formal for row in people):
    errors.append("underage formal official/teacher")
  if any(row["primary_occupation_code"] == "clerk" and row["literacy_level_code"] not in {"L3", "L4"} for row in people):
    errors.append("clerk without document literacy")
  if any(row["primary_occupation_code"] in {"county_school_teacher", "academy_teacher"} and row["literacy_level_code"] != "L4" for row in people):
    errors.append("classical teacher without L4 education")
  relation_types = {row["relation_type"] for row in payload["relationships"]}
  allowed_relations = {"spouse", "parent_child", "sibling", "lineage_leadership", "teacher_student", "neighbor", "landlord_tenant", "master_apprentice", "acquaintance"}
  if not relation_types <= allowed_relations:
    errors.append("v0.6 introduced an unsupported relationship type")
  if any(not row.get("registration_status_code") or not row.get("economic_stratum_code") for row in households):
    errors.append("missing household social axis")
  if any(not row.get("prestige_status_code") or not row.get("local_power_role_codes") for row in people):
    errors.append("missing person prestige or local-power axis")
  if any(
    row["credential_code"] != "none" and row["literacy_level_code"] != "L4"
    for row in people
  ):
    errors.append("credential without compatible classical education")
  compatibility_fields = ("is_literate", "is_classically_educated", "primary_occupation", "social_stratum")
  if any(any(field not in row for field in compatibility_fields) for row in people):
    errors.append("missing v0.5 compatibility field")
  cross_examples = payload["summary"]["axis_cross_examples"]
  cross_examples_required = (
    source["location"]["settlement_type_code"] == "county_seat"
    and any(person["credential_code"] not in {"none", "candidate"} for person in people)
    and len(households) >= 20
  )
  cross_examples_pass = not cross_examples_required or all(value > 0 for value in cross_examples.values())
  if not cross_examples_pass:
    errors.append("county-seat sample did not demonstrate all four independent-axis combinations")
  return {
    "status": "pass" if not errors else "fail", "errors": errors,
    "checks": {
      "population_exact": len(people) == int(source["zone"]["resident_population"]),
      "generated_historical_claims_zero": all(row["historical_claim"] == "no" for row in people),
      "generated_names_simplified": all(
        row["name_zh_hans"] == row["name_zh_hans"].translate(legacy.TRADITIONAL_SURNAME_TO_SIMPLIFIED)
        for row in people
      ),
      "occupation_codes_valid": all(not row["primary_occupation_code"] or row["primary_occupation_code"] in valid_occupations for row in people),
      "new_relationship_types_zero": relation_types <= allowed_relations,
      "four_social_axes_complete": (
        all(row.get("registration_status_code") and row.get("economic_stratum_code") for row in households)
        and all(row.get("prestige_status_code") and row.get("local_power_role_codes") for row in people)
      ),
      "formal_role_constraints": not any(row["age_1628"] < 18 and row["primary_occupation_code"] in formal for row in people),
      "credential_education_compatible": all(
        row["credential_code"] == "none" or row["literacy_level_code"] == "L4" for row in people
      ),
      "four_axis_cross_examples_required": cross_examples_required,
      "four_axis_cross_examples_present": cross_examples_pass,
    },
  }


def generate_payload(source: dict[str, Any], world_seed: str, database_sha256: str) -> dict[str, Any]:
  legacy.RULESET_VERSION = RULESET_VERSION
  legacy_payload = legacy.generate_payload(source, world_seed, database_sha256)
  assign_education(legacy_payload, source, world_seed)
  assign_occupations(legacy_payload, source, world_seed)
  assign_social_axes(legacy_payload, source, world_seed)
  rebuild_summary(legacy_payload, source)
  validation = validate_v06(legacy_payload, source)
  if validation["status"] != "pass":
    raise RuntimeError("v0.6 settlement people validation failed: " + "; ".join(validation["errors"]))
  legacy_payload["validation"] = validation
  legacy_payload["metadata"].update({
    "schema_version": SCHEMA_VERSION, "ruleset_version": RULESET_VERSION,
    "source_database_user_version": source["database_user_version"],
    "generation_type": "deterministic_code_generation_no_ai",
    "historical_identity_policy": "all generated residents and unsourced role holders have historical_claim=no",
  })
  legacy_payload["location"] = {
    "settlement_id": source["location"]["settlement_id"],
    "settlement_name": source["location"]["settlement_name"],
    "settlement_type_code": source["location"]["settlement_type_code"],
    "settlement_resident_population": source["location"]["resident_population"],
    "zone_id": source["zone"]["zone_id"], "zone_name": source["zone"]["zone_name"],
    "zone_resident_population": source["zone"]["resident_population"],
    "zone_selection_method": source["zone_selection_method"],
    "available_zone_count": source["available_zone_count"],
    "poi_id": source["poi"]["poi_id"] if source["poi"] else "",
    "poi_name": source["poi"]["poi_name"] if source["poi"] else "",
  }
  fingerprint_payload = dict(legacy_payload)
  fingerprint_metadata = dict(legacy_payload["metadata"])
  fingerprint_metadata.pop("generation_fingerprint", None)
  fingerprint_payload["metadata"] = fingerprint_metadata
  fingerprint_source = json.dumps(fingerprint_payload, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
  legacy_payload["metadata"]["generation_fingerprint"] = hashlib.sha256(fingerprint_source.encode("utf-8")).hexdigest()
  return legacy_payload


def render_preview(payload: dict[str, Any]) -> str:
  location = payload["location"]
  summary = payload["summary"]
  people = payload["people"]
  households = {row["household_id"]: row for row in payload["households"]}
  core = [row for row in people if row["is_core_npc"] == "yes"]
  core.sort(key=lambda row: (0 if row["local_power_role_codes"] != ["none"] else 1, -row["skill_grade_0_100"], row["person_id"]))
  lines = [
    f"# {location['settlement_name']}·{location['zone_name']}人物生成样例 v0.6", "",
    "> 本文件由确定性Python代码生成，不使用AI。所有普通人物均为游戏角色，`historical_claim=no`。", "",
    "## 场景概况", "", "| 项目 | 结果 |", "|---|---:|",
    f"| 聚落 | {location['settlement_name']}（`{location['settlement_type_code']}`） |",
    f"| 叶级区域 | {location['zone_name']}（`{location['zone_id']}`） |",
    f"| 区域人口 | {summary['person_count']}人 |", f"| 家庭 | {summary['household_count']}户 |",
    f"| 目标/已分配劳动力 | {summary['target_labor_force']} / {summary['assigned_primary_workers']} |",
    f"| 识字人口 | {summary['literate_count']}人 |", f"| 经典教育 | {summary['classically_educated_count']}人 |",
    f"| 关系边 | {summary['relationship_count']}条（沿用v0.5关系类型） |", "",
    "## 分布摘要", "",
    "- 职业：" + "、".join(f"{row['value']}（{row['count']}人）" for row in summary["top_person_occupations"][:8]),
    "- 户籍：" + "、".join(f"{row['value']}（{row['count']}户）" for row in summary["registration_statuses"]),
    "- 经济：" + "、".join(f"{row['value']}（{row['count']}户）" for row in summary["economic_strata_v06"]),
    "- 教育：" + "、".join(f"{row['value']}（{row['count']}人）" for row in summary["literacy_levels"]),
    "", "## 核心人物", "",
    "| 姓名 | 年龄/性别 | 教育与功名 | 主业/副业 | 四轴身份 | 地方角色 |",
    "|---|---|---|---|---|---|",
  ]
  for person in core[:40]:
    household = households[person["household_id"]]
    sex = "男" if person["sex"] == "male" else "女"
    secondary = person["secondary_occupation_code"] or "—"
    roles = "、".join(person["local_power_role_codes"])
    lines.append(
      f"| {person['name_zh_hans']} | {person['age_1628']}岁/{sex} | "
      f"{person['literacy_level_code']}·{person['education_route_code']}·{person['credential_code']} | "
      f"{person['primary_occupation']} / {secondary} | "
      f"{household['registration_status']}；{household['economic_stratum']}；{person['prestige_status']} | {roles} |"
    )
  lines.extend([
    "", "## 边界", "",
    "- 户籍身份、实际职业、经济处境、功名声望和地方权力分别保存，互不强制等同。",
    "- 县城和镇市只展开当前叶级人口块；其余人口仍保持聚合状态。",
    "- 机构是工作地点兴趣点，不重复计入居民人口。",
    "- 本版不运行求学、转业、阶层流动或人物行动Tick。", "",
  ])
  return "\n".join(lines)


def main() -> None:
  parser = argparse.ArgumentParser(
    description="Generate one Ming 1628 settlement leaf's people with the v0.6 rules without AI"
  )
  parser.add_argument("--database", type=Path, default=DEFAULT_DATABASE)
  parser.add_argument("--settlement-id")
  parser.add_argument("--settlement-name")
  parser.add_argument("--village-id")
  parser.add_argument("--county-id")
  parser.add_argument("--zone-id")
  parser.add_argument("--poi-id")
  parser.add_argument("--world-seed", default=DEFAULT_WORLD_SEED)
  parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT_DIR)
  args = parser.parse_args()
  source = resolve_location(
    args.database, args.settlement_id, args.settlement_name, args.village_id,
    args.county_id, args.zone_id, args.poi_id,
  )
  database_sha256 = file_sha256(args.database)
  generation_start = time.perf_counter()
  payload = generate_payload(source, args.world_seed, database_sha256)
  generation_elapsed_ms = round((time.perf_counter() - generation_start) * 1000, 3)
  location = payload["location"]
  stem = f"{location['zone_id']}_{legacy.safe_filename(location['settlement_name'])}_{RULESET_VERSION}"
  json_path = args.output_dir / "generated" / "people_samples" / f"{stem}.json"
  preview_path = args.output_dir / f"sample_{stem}.md"
  report_path = args.output_dir / f"sample_{stem}_validation.json"
  legacy.write_json_atomic(json_path, payload)
  legacy.write_text_atomic(preview_path, render_preview(payload))
  report = {
    "schema_version": SCHEMA_VERSION, "settlement_id": location["settlement_id"],
    "zone_id": location["zone_id"], "generation_fingerprint": payload["metadata"]["generation_fingerprint"],
    "full_json_sha256": file_sha256(json_path), "preview_sha256": file_sha256(preview_path),
    "generation_elapsed_ms_excluding_database_load_hash_and_file_io": generation_elapsed_ms,
    "performance_target_ms": 250,
    "performance_target_pass": generation_elapsed_ms < 250,
    "summary": payload["summary"], "validation": payload["validation"],
  }
  legacy.write_json_atomic(report_path, report)
  print(json.dumps({
    "json": str(json_path), "preview": str(preview_path), "validation_report": str(report_path),
    "generation_fingerprint": payload["metadata"]["generation_fingerprint"],
    "generation_elapsed_ms": generation_elapsed_ms,
    "summary": payload["summary"],
  }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
  main()
