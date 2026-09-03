#!/usr/bin/env python3
"""Build Project Realm county culture, education, lineage and people data v0.4.

The global simulation unit remains the county.  Source-backed people,
institutions and relationships are query catalogs; they are not iterated by a
global game tick.  No person, family or institution name is generated.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import re
import shutil
import sqlite3
import statistics
import time
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any, Iterable, Sequence


SNAPSHOT_YEAR = 1628
RULESET_VERSION = "v0.4"
EXPECTED_COUNTIES = 1_168
NONCOMMERCIAL_MARK = "no"
CBDB_FILENAME = "cbdb_20260822.sqlite3"
CBDB_SHA256 = "25861a3506ace7163348557f1ba0f59ef24cbe49f408f8cdde3041bd0083dffb"
ACADEMIES_FILENAME = "ACADEMY_Data.csv"
ACADEMIES_MD5 = "c60772d89e7bd52d8140417ec0051ceb"

DEFAULT_DATA_ROOT = Path("docs/90_资料与归档/01_崇祯元年历史资料/data/1628")
DEFAULT_ECONOMY_DIR = DEFAULT_DATA_ROOT / "5.县级人口矿产与产业商业"
DEFAULT_SETTLEMENT_DIR = DEFAULT_DATA_ROOT / "6.县内区域与村庄"
DEFAULT_OUTPUT_DIR = DEFAULT_DATA_ROOT / "7.县级文化家族乡绅教育与人物"
DEFAULT_INPUT_DIR = Path("tmp/research/ming_culture_v0.4")

CBDB_STRUCTURE_URL = "https://cbdb.hsites.harvard.edu/structure-cbdb"
CBDB_SOURCES_URL = "https://cbdb.hsites.harvard.edu/cbdb-sources"
CBDB_ACCESS_URL = (
  "https://cbdb.hsites.harvard.edu/important-announcement-about-access-cbdb-data-china"
)
ACADEMIES_URL = "https://cbdb.hsites.harvard.edu/chinese-academies-data"
ACADEMIES_DOI = "doi:10.7910/DVN/J6XRIV"
LOGART_URL = "https://logart.mpiwg-berlin.mpg.de/"
LITERACY_LSE_URL = (
  "https://www.lse.ac.uk/Economic-History/Assets/Documents/WorkingPapers/"
  "Economic-History/2009/WP122.pdf"
)
LITERACY_ELMAN_URL = (
  "https://www.princeton.edu/~elman/documents/"
  "Elman%20-%20Unintended%20Consequences%20-%209789004277595_198-219_Elman_off"
)

EXAM_KEYWORDS = ("進士", "进士", "舉人", "举人", "生員", "生员", "貢生", "贡生", "監生", "监生")
JINSHI_KEYWORDS = ("進士", "进士")
JUREN_KEYWORDS = ("舉人", "举人")
SHENGYUAN_KEYWORDS = ("生員", "生员", "庠生")
GONGSHENG_KEYWORDS = ("貢生", "贡生", "監生", "监生")

TRADITIONAL_TO_SIMPLIFIED = str.maketrans(
  "縣餘崑陽東書學廣興寧貴溪龍鳳慶歸義華陰臺灣萬國門澤蘇吳會長樂處麗雲開關漢濟衛軍後寶豐順德應滄趙盧臨濱鹽鎮劍劉齊黃蕭鄉舉進貢員祠廟舊嶺臺",
  "县余昆阳东书学广兴宁贵溪龙凤庆归义华阴台湾万国门泽苏吴会长乐处丽云开关汉济卫军后宝丰顺德应沧赵卢临滨盐镇剑刘齐黄萧乡举进贡员祠庙旧岭台",
)


BASELINE_COLUMNS = [
  "county_id", "snapshot_year", "region", "upper_unit", "intermediate_unit", "county",
  "population_est_1628", "official_school_expected_0_100", "verified_school_count",
  "verified_academy_count", "verified_literary_society_count",
  "education_structure_potential_0_100", "education_evidence_0_100",
  "education_degree_1628_0_100", "imperial_exam_culture_0_100",
  "literati_network_0_100", "publishing_book_culture_0_100", "cultural_influence_0_100",
  "documented_family_lineage_count", "notable_lineage_count", "gentry_person_count",
  "lineage_organization_potential_0_100", "gentry_power_0_100",
  "elite_network_density_0_100", "alive_confirmed_count_1628",
  "alive_probable_count_1628", "deceased_legacy_person_count",
  "national_level_person_count", "regional_level_person_count",
  "recent_exam_record_count", "historical_exam_record_count", "literati_person_count",
  "work_record_count_before_1628", "kinship_edge_count", "social_edge_count_before_1628",
  "male_basic_literacy_low_pct", "male_basic_literacy_mid_pct",
  "male_basic_literacy_high_pct", "female_basic_literacy_low_pct",
  "female_basic_literacy_mid_pct", "female_basic_literacy_high_pct",
  "total_basic_literacy_low_pct", "total_basic_literacy_mid_pct",
  "total_basic_literacy_high_pct", "classical_education_low_pct",
  "classical_education_mid_pct", "classical_education_high_pct",
  "data_coverage_0_100", "mapping_method", "evidence_mix_method",
  "literacy_estimation_method", "independent_source_count", "manual_anchor_count",
  "unmapped_source_record_count", "source_manifest_version", "commercial_release_ready",
]

OVERVIEW_COLUMNS = [
  "county_id", "snapshot_year", "region", "upper_unit", "intermediate_unit", "county",
  "population_est_1628", "total_basic_literacy_mid_pct", "male_basic_literacy_mid_pct",
  "female_basic_literacy_mid_pct", "classical_education_mid_pct",
  "education_degree_1628_0_100", "imperial_exam_culture_0_100",
  "publishing_book_culture_0_100", "cultural_influence_0_100",
  "lineage_organization_potential_0_100", "gentry_power_0_100",
  "elite_network_density_0_100", "verified_academy_count",
  "documented_family_lineage_count", "alive_confirmed_count_1628",
  "alive_probable_count_1628", "representative_source_families",
  "representative_source_people", "data_coverage_0_100", "commercial_release_ready",
]

INSTITUTION_COLUMNS = [
  "institution_anchor_id", "county_id", "county", "institution_name", "institution_type",
  "begin_year", "end_year", "active_in_1628", "evidence_tier", "academy_dataset_id",
  "cbdb_inst_code", "cbdb_inst_name_code", "source_id", "source_title", "source_url",
  "source_locator", "mapping_method", "license_status", "commercial_release_ready",
]

PERSON_COLUMNS = [
  "person_id", "cbdb_person_id", "name", "surname", "given_name", "gender",
  "birth_year", "birth_year_quality", "death_year", "death_year_quality", "age_1628",
  "life_stage_1628", "alive_status_1628", "primary_county_id",
  "primary_county_association", "highest_exam_before_1628", "office_count_before_1628",
  "highest_office_before_1628", "work_count_before_1628", "total_source_work_count",
  "person_types_1628", "gentry_status_1628", "gentry_evidence",
  "historical_influence_0_100", "influence_1628_0_100",
  "post_1628_achievement_metadata", "spoiler_sensitive", "source_ids", "source_titles",
  "evidence_grade", "license_status", "commercial_release_ready",
]

ASSOCIATION_COLUMNS = [
  "association_id", "person_id", "county_id", "association_type_code",
  "association_type_name", "first_year", "last_year", "date_quality",
  "present_in_county_1628", "opening_relevance", "source_id", "source_pages",
  "mapping_method", "evidence_grade", "license_status", "commercial_release_ready",
]

RELATIONSHIP_COLUMNS = [
  "relationship_id", "from_person_id", "to_person_id", "relation_category",
  "relation_code", "relation_name", "first_year", "last_year", "active_by_1628",
  "source_id", "source_pages", "evidence_grade", "license_status",
  "commercial_release_ready",
]

GROUP_COLUMNS = [
  "membership_id", "group_id", "group_type", "group_name", "person_id", "county_id",
  "event_year", "member_role", "source_id", "source_pages", "evidence_grade",
  "license_status", "commercial_release_ready",
]

FAMILY_COLUMNS = [
  "family_id", "county_id", "county", "surname", "historical_lineage_name",
  "derived_descriptor", "member_count", "generation_count_est", "elite_member_count",
  "is_notable_lineage", "evidence_basis", "source_ids", "evidence_grade",
  "license_status", "commercial_release_ready",
]

FAMILY_MEMBERSHIP_COLUMNS = [
  "membership_id", "family_id", "person_id", "county_id", "membership_basis",
  "source_ids", "evidence_grade", "license_status", "commercial_release_ready",
]

SOURCE_MANIFEST_COLUMNS = [
  "source_id", "source_title", "source_kind", "pinned_version", "local_path",
  "upstream_url", "checksum_algorithm", "checksum_value", "license_status",
  "usage_in_v0_4", "commercial_release_ready", "notes",
]


def clean_text(value: Any) -> str:
  if value is None:
    return ""
  return re.sub(r"\s+", " ", str(value)).strip()


def int_or_none(value: Any) -> int | None:
  if value in (None, "", 0, "0"):
    return None
  try:
    return int(value)
  except (TypeError, ValueError):
    return None


def number(row: dict[str, Any], key: str) -> float:
  value = row.get(key, 0)
  try:
    return float(value or 0)
  except (TypeError, ValueError):
    return 0.0


def clamp(value: float, low: float = 0.0, high: float = 100.0) -> float:
  return max(low, min(high, value))


def score(value: float) -> int:
  return int(round(clamp(value)))


def file_digest(path: Path, algorithm: str = "sha256") -> str:
  hasher = hashlib.new(algorithm)
  with path.open("rb") as stream:
    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
      hasher.update(chunk)
  return hasher.hexdigest()


def stable_id(prefix: str, *parts: Any, length: int = 20) -> str:
  payload = "|".join(clean_text(part) for part in parts)
  return f"{prefix}-{hashlib.sha256(payload.encode('utf-8')).hexdigest()[:length]}"


def read_csv(path: Path) -> list[dict[str, str]]:
  with path.open(encoding="utf-8-sig", newline="") as stream:
    return list(csv.DictReader(stream))


def write_csv_atomic(path: Path, columns: Sequence[str], rows: Iterable[dict[str, Any]]) -> None:
  path.parent.mkdir(parents=True, exist_ok=True)
  temporary = path.with_suffix(path.suffix + ".tmp")
  with temporary.open("w", encoding="utf-8", newline="") as stream:
    writer = csv.DictWriter(stream, fieldnames=columns, lineterminator="\n", extrasaction="ignore")
    writer.writeheader()
    for row in rows:
      writer.writerow({column: clean_text(row.get(column, "")) for column in columns})
  temporary.replace(path)


def write_json_atomic(path: Path, value: Any) -> None:
  temporary = path.with_suffix(path.suffix + ".tmp")
  with temporary.open("w", encoding="utf-8", newline="\n") as stream:
    json.dump(value, stream, ensure_ascii=False, indent=2, sort_keys=True)
    stream.write("\n")
  temporary.replace(path)


def normalize_place_name(value: str) -> str:
  text = clean_text(value).translate(TRADITIONAL_TO_SIMPLIFIED)
  text = re.sub(r"[省府州路道縣县軍军衛卫所區区鄉乡鎮镇城\s·・（）()\[\]【】,，。/\\-]", "", text)
  return text


def haversine_km(lon1: float, lat1: float, lon2: float, lat2: float) -> float:
  radius = 6371.0088
  phi1, phi2 = math.radians(lat1), math.radians(lat2)
  delta_phi = math.radians(lat2 - lat1)
  delta_lon = math.radians(lon2 - lon1)
  value = (
    math.sin(delta_phi / 2) ** 2
    + math.cos(phi1) * math.cos(phi2) * math.sin(delta_lon / 2) ** 2
  )
  return 2 * radius * math.asin(math.sqrt(value))


class CountyMapper:
  def __init__(self, counties: Sequence[dict[str, Any]]) -> None:
    self.counties = list(counties)
    self.by_id = {row["county_id"]: row for row in counties}
    self.by_name: dict[str, list[dict[str, Any]]] = defaultdict(list)
    self.grid: dict[tuple[int, int], list[dict[str, Any]]] = defaultdict(list)
    for row in counties:
      row["_lon"] = float(row["longitude"])
      row["_lat"] = float(row["latitude"])
      row["_radius"] = max(25.0, math.sqrt(float(row["area_km2_est"]) / math.pi))
      stem = normalize_place_name(row["county"])
      if stem:
        self.by_name[stem].append(row)
      self.grid[(math.floor(row["_lon"]), math.floor(row["_lat"]))].append(row)

  def nearest(self, lon: float, lat: float) -> tuple[dict[str, Any], float, float] | None:
    candidates: list[dict[str, Any]] = []
    cell_x, cell_y = math.floor(lon), math.floor(lat)
    for dx in range(-3, 4):
      for dy in range(-3, 4):
        candidates.extend(self.grid.get((cell_x + dx, cell_y + dy), []))
    if not candidates:
      return None
    distances = sorted(
      (
        (haversine_km(lon, lat, row["_lon"], row["_lat"]), row)
        for row in candidates
      ),
      key=lambda item: (item[0], item[1]["county_id"]),
    )
    first_distance, first = distances[0]
    second_distance = distances[1][0] if len(distances) > 1 else 99999.0
    return first, first_distance, second_distance

  def map_county_address(
    self,
    name: str,
    admin_type: str,
    lon: Any,
    lat: Any,
  ) -> tuple[str, str] | None:
    if admin_type and admin_type != "Xian":
      return None
    stem = normalize_place_name(name)
    named = self.by_name.get(stem, [])
    longitude = float(lon) if lon not in (None, "") else None
    latitude = float(lat) if lat not in (None, "") else None
    if len(named) == 1:
      return named[0]["county_id"], "exact_county_name"
    if longitude is not None and latitude is not None:
      nearest = self.nearest(longitude, latitude)
      if nearest:
        county, distance, second = nearest
        if distance <= county["_radius"] and second - distance >= 1.0:
          return county["county_id"], "unique_coordinate_service_radius"
        if named:
          named_distances = sorted(
            (
              (haversine_km(longitude, latitude, row["_lon"], row["_lat"]), row)
              for row in named
            ),
            key=lambda item: (item[0], item[1]["county_id"]),
          )
          if named_distances[0][0] <= named_distances[0][1]["_radius"]:
            return named_distances[0][1]["county_id"], "name_and_coordinate"
    return None

  def map_freeform(
    self,
    address: str,
    lon: Any = None,
    lat: Any = None,
  ) -> tuple[str, str] | None:
    if lon not in (None, "") and lat not in (None, ""):
      nearest = self.nearest(float(lon), float(lat))
      if nearest:
        county, distance, second = nearest
        if distance <= county["_radius"] and second - distance >= 1.0:
          return county["county_id"], "dataset_coordinate_service_radius"
    normalized = normalize_place_name(address)
    matches: list[tuple[int, dict[str, Any]]] = []
    for stem, counties in self.by_name.items():
      if len(stem) >= 2 and stem in normalized:
        for county in counties:
          matches.append((len(stem), county))
    if matches:
      maximum = max(length for length, _ in matches)
      winners = {row["county_id"]: row for length, row in matches if length == maximum}
      if len(winners) == 1:
        row = next(iter(winners.values()))
        return row["county_id"], "unique_county_name_in_address"
    return None


def percentile_scores(values: dict[str, float]) -> dict[str, int]:
  ordered = sorted((math.log1p(max(0.0, value)), key) for key, value in values.items())
  result: dict[str, int] = {}
  total = len(ordered)
  index = 0
  while index < total:
    end = index + 1
    while end < total and ordered[end][0] == ordered[index][0]:
      end += 1
    average_rank = (index + end - 1) / 2
    pct = 50 if total == 1 else int(round(100 * average_rank / (total - 1)))
    for _, key in ordered[index:end]:
      result[key] = pct
    index = end
  return result


def saturation(value: float, k: float) -> float:
  return 100.0 * (1.0 - math.exp(-max(0.0, value) / k))


def extract_years(value: str, *, latest: bool = False) -> int | None:
  years = [int(item) for item in re.findall(r"(?<!\d)([5-9]\d{2}|1\d{3}|20\d{2})(?!\d)", value)]
  if not years:
    return None
  return max(years) if latest else min(years)


def date_quality(year: int | None, range_code: Any) -> str:
  if year is None:
    return "unknown"
  if int_or_none(range_code):
    return "estimated_range"
  return "recorded_year"


def alive_status(person: dict[str, Any]) -> tuple[str, str, int | str]:
  birth = int_or_none(person.get("c_birthyear"))
  death = int_or_none(person.get("c_deathyear"))
  earliest = int_or_none(person.get("c_fl_earliest_year"))
  latest = int_or_none(person.get("c_fl_latest_year"))
  if birth and birth <= SNAPSHOT_YEAR and death and death >= SNAPSHOT_YEAR:
    age = SNAPSHOT_YEAR - birth
    return "alive_confirmed", life_stage(age), age
  if death and death < SNAPSHOT_YEAR:
    return "deceased_legacy", "deceased", ""
  if birth and birth <= SNAPSHOT_YEAR:
    age = SNAPSHOT_YEAR - birth
    if age <= 90 and (death is None or death >= SNAPSHOT_YEAR):
      return "alive_probable", life_stage(age), age
    return "deceased_legacy", "deceased_probable", ""
  if earliest and earliest <= SNAPSHOT_YEAR <= (latest or SNAPSHOT_YEAR):
    return "alive_probable", "adult_age_unknown", ""
  return "deceased_legacy", "date_incomplete_legacy", ""


def life_stage(age: int) -> str:
  if age < 7:
    return "child"
  if age < 16:
    return "adolescent"
  if age < 21:
    return "young_adult"
  if age < 45:
    return "adult"
  if age < 65:
    return "mature"
  return "elder"


def life_stage_multiplier(stage: str) -> float:
  return {
    "child": 0.04,
    "adolescent": 0.12,
    "young_adult": 0.30,
    "adult": 0.75,
    "mature": 1.00,
    "elder": 0.82,
    "adult_age_unknown": 0.55,
  }.get(stage, 0.0)


def entry_level(description: str) -> int:
  if any(item in description for item in JINSHI_KEYWORDS):
    return 100
  if any(item in description for item in JUREN_KEYWORDS):
    return 78
  if any(item in description for item in GONGSHENG_KEYWORDS):
    return 58
  if any(item in description for item in SHENGYUAN_KEYWORDS):
    return 48
  return 25


def relationship_category(description: str, source: str) -> str:
  if source == "kin":
    if any(word in description for word in ("妻", "夫", "婚", "姻", "岳", "婿")):
      return "marriage"
    return "kinship"
  if any(word in description for word in ("師", "师", "弟子", "受業", "受业", "講學", "讲学")):
    return "teacher_student"
  return "social_association"


def source_string(values: Iterable[Any]) -> str:
  cleaned = sorted({clean_text(value) for value in values if clean_text(value)})
  return ";".join(cleaned)


def prepare_temp_person_table(
  connection: sqlite3.Connection,
  table: str,
  person_ids: Iterable[int],
) -> None:
  connection.execute(f"DROP TABLE IF EXISTS temp.{table}")
  connection.execute(f"CREATE TEMP TABLE {table}(person_id INTEGER PRIMARY KEY)")
  connection.executemany(
    f"INSERT INTO {table}(person_id) VALUES (?)",
    ((person_id,) for person_id in sorted(set(person_ids))),
  )


def extract_cbdb(
  cbdb_path: Path,
  mapper: CountyMapper,
  county_by_id: dict[str, dict[str, Any]],
) -> dict[str, Any]:
  connection = sqlite3.connect(f"file:{cbdb_path}?mode=ro", uri=True)
  connection.row_factory = sqlite3.Row
  address_rows = {
    row["c_addr_id"]: dict(row)
    for row in connection.execute(
      "SELECT c_addr_id,c_name_chn,c_admin_type,x_coord,y_coord,c_firstyear,c_lastyear "
      "FROM ADDR_CODES WHERE c_admin_type='Xian'"
    )
  }
  address_map: dict[int, tuple[str, str]] = {}
  for address_id, row in address_rows.items():
    mapped = mapper.map_county_address(
      row["c_name_chn"], row["c_admin_type"], row["x_coord"], row["y_coord"]
    )
    if mapped:
      address_map[address_id] = mapped

  candidate_query = """
    SELECT * FROM BIOG_MAIN
    WHERE (
      c_dy=19 AND (
        NULLIF(c_birthyear,0)<=1628 OR NULLIF(c_index_year,0)<=1628 OR
        NULLIF(c_fl_earliest_year,0)<=1628 OR NULLIF(c_fl_latest_year,0)<=1628
      )
    ) OR (c_dy IN (20,80) AND NULLIF(c_birthyear,0)<=1628)
    ORDER BY c_personid
  """
  candidates = {row["c_personid"]: dict(row) for row in connection.execute(candidate_query)}
  candidate_count_initial = len(candidates)
  prepare_temp_person_table(connection, "candidate_people", candidates)

  address_type_names = {
    row["c_addr_type"]: clean_text(row["c_addr_desc_chn"])
    for row in connection.execute("SELECT * FROM BIOG_ADDR_CODES")
  }
  raw_associations: list[dict[str, Any]] = []
  associated_people: set[int] = set()
  for row in connection.execute(
    """
    SELECT d.* FROM BIOG_ADDR_DATA d
    JOIN candidate_people p ON p.person_id=d.c_personid
    WHERE COALESCE(d.c_delete,0)=0
    ORDER BY d.c_personid,d.c_sequence,d.c_addr_type,d.c_addr_id
    """
  ):
    mapped = address_map.get(row["c_addr_id"])
    if not mapped:
      continue
    county_id, mapping_method = mapped
    associated_people.add(row["c_personid"])
    raw_associations.append(
      {
        "cbdb_person_id": row["c_personid"],
        "county_id": county_id,
        "association_type_code": f"address_{row['c_addr_type']}",
        "association_type_name": address_type_names.get(row["c_addr_type"], "地点联系"),
        "first_year": int_or_none(row["c_firstyear"]),
        "last_year": int_or_none(row["c_lastyear"]),
        "date_quality": "recorded_range" if int_or_none(row["c_firstyear"]) else "undated",
        "source_id": int_or_none(row["c_source"]),
        "source_pages": clean_text(row["c_pages"]),
        "mapping_method": mapping_method,
        "address_type": row["c_addr_type"],
      }
    )

  for person_id, person in candidates.items():
    address_id = int_or_none(person.get("c_index_addr_id"))
    mapped = address_map.get(address_id or -1)
    if not mapped:
      continue
    if person_id in associated_people:
      continue
    county_id, mapping_method = mapped
    associated_people.add(person_id)
    raw_associations.append(
      {
        "cbdb_person_id": person_id,
        "county_id": county_id,
        "association_type_code": "index_address",
        "association_type_name": "索引籍贯",
        "first_year": "",
        "last_year": "",
        "date_quality": "undated_index",
        "source_id": "",
        "source_pages": "",
        "mapping_method": mapping_method,
        "address_type": 1,
      }
    )

  prepare_temp_person_table(connection, "mapped_people", associated_people)
  source_records: dict[int, list[dict[str, Any]]] = defaultdict(list)
  for row in connection.execute(
    """
    SELECT s.c_personid,s.c_textid,s.c_pages,s.c_main_source,
           COALESCE(t.c_title_chn,t.c_title,'') source_title
    FROM BIOG_SOURCE_DATA s
    JOIN mapped_people p ON p.person_id=s.c_personid
    LEFT JOIN TEXT_CODES t ON t.c_textid=s.c_textid
    ORDER BY s.c_personid,COALESCE(s.c_main_source,0) DESC,s.c_textid
    """
  ):
    source_records[row["c_personid"]].append(dict(row))
  mapped_person_ids = {person_id for person_id in associated_people if source_records.get(person_id)}
  prepare_temp_person_table(connection, "mapped_people", mapped_person_ids)
  candidates = {person_id: candidates[person_id] for person_id in mapped_person_ids}
  raw_associations = [
    row for row in raw_associations if row["cbdb_person_id"] in mapped_person_ids
  ]

  entry_records: dict[int, list[dict[str, Any]]] = defaultdict(list)
  group_seed_rows: list[dict[str, Any]] = []
  for row in connection.execute(
    """
    SELECT e.*,c.c_entry_desc_chn FROM ENTRY_DATA e
    JOIN mapped_people p ON p.person_id=e.c_personid
    LEFT JOIN ENTRY_CODES c USING(c_entry_code)
    ORDER BY e.c_personid,e.c_year,e.c_entry_code,e.c_sequence
    """
  ):
    record = dict(row)
    entry_records[row["c_personid"]].append(record)
    description = clean_text(row["c_entry_desc_chn"])
    year = int_or_none(row["c_year"])
    if year and year <= SNAPSHOT_YEAR and any(keyword in description for keyword in EXAM_KEYWORDS):
      group_seed_rows.append(record)

  work_records: dict[int, list[dict[str, Any]]] = defaultdict(list)
  for row in connection.execute(
    """
    SELECT b.c_personid,b.c_textid,b.c_role_id,b.c_year,b.c_source,b.c_pages,
           COALESCE(t.c_title_chn,t.c_title,'') c_title_chn,
           COALESCE(r.c_role_desc_chn,'') c_role_desc_chn
    FROM BIOG_TEXT_DATA b
    JOIN mapped_people p ON p.person_id=b.c_personid
    LEFT JOIN TEXT_CODES t ON t.c_textid=b.c_textid
    LEFT JOIN TEXT_ROLE_CODES r ON r.c_role_id=b.c_role_id
    ORDER BY b.c_personid,b.c_year,b.c_textid,b.c_role_id
    """
  ):
    work_records[row["c_personid"]].append(dict(row))

  office_records: dict[int, list[dict[str, Any]]] = defaultdict(list)
  posting_associations: list[dict[str, Any]] = []
  for row in connection.execute(
    """
    SELECT o.c_personid,o.c_posting_id,o.c_firstyear,o.c_lastyear,o.c_source,o.c_pages,
           COALESCE(c.c_office_chn,'') c_office_chn,a.c_addr_id
    FROM POSTED_TO_OFFICE_DATA o
    JOIN mapped_people p ON p.person_id=o.c_personid
    LEFT JOIN OFFICE_CODES c ON c.c_office_id=o.c_office_id
    LEFT JOIN POSTED_TO_ADDR_DATA a
      ON a.c_posting_id=o.c_posting_id AND a.c_personid=o.c_personid
    ORDER BY o.c_personid,o.c_firstyear,o.c_posting_id,a.c_addr_id
    """
  ):
    record = dict(row)
    office_records[row["c_personid"]].append(record)
    mapped = address_map.get(row["c_addr_id"] or -1)
    first_year = int_or_none(row["c_firstyear"])
    if mapped and (first_year is None or first_year <= SNAPSHOT_YEAR):
      county_id, mapping_method = mapped
      posting_associations.append(
        {
          "cbdb_person_id": row["c_personid"],
          "county_id": county_id,
          "association_type_code": "office_posting",
          "association_type_name": f"任职：{clean_text(row['c_office_chn']) or '职官'}",
          "first_year": first_year,
          "last_year": int_or_none(row["c_lastyear"]),
          "date_quality": "recorded_range" if first_year else "undated",
          "source_id": int_or_none(row["c_source"]),
          "source_pages": clean_text(row["c_pages"]),
          "mapping_method": mapping_method,
          "address_type": 100,
        }
      )
  raw_associations.extend(posting_associations)

  relationship_rows: list[dict[str, Any]] = []
  kin_codes = {
    row["c_kincode"]: dict(row)
    for row in connection.execute("SELECT * FROM KINSHIP_CODES")
  }
  seen_relationships: set[tuple[Any, ...]] = set()
  for row in connection.execute(
    """
    SELECT k.* FROM KIN_DATA k
    JOIN mapped_people a ON a.person_id=k.c_personid
    JOIN mapped_people b ON b.person_id=k.c_kin_id
    WHERE k.c_personid<>k.c_kin_id
    ORDER BY k.c_personid,k.c_kin_id,k.c_kin_code,k.c_source
    """
  ):
    if not row["c_source"]:
      continue
    first_person, second_person = sorted((row["c_personid"], row["c_kin_id"]))
    relation = kin_codes.get(row["c_kin_code"], {})
    relation_name = clean_text(relation.get("c_kinrel_chn")) or "亲属"
    category = relationship_category(relation_name, "kin")
    key = (first_person, second_person, category, row["c_kin_code"], row["c_source"])
    if key in seen_relationships:
      continue
    seen_relationships.add(key)
    relationship_rows.append(
      {
        "relationship_id": stable_id("REL", *key),
        "from_person_id": f"CBDB-{first_person}",
        "to_person_id": f"CBDB-{second_person}",
        "relation_category": category,
        "relation_code": f"KIN-{row['c_kin_code']}",
        "relation_name": relation_name,
        "first_year": "",
        "last_year": "",
        "active_by_1628": "yes",
        "source_id": row["c_source"] or "",
        "source_pages": clean_text(row["c_pages"]),
        "evidence_grade": "B",
        "license_status": "CBDB_research_use_conditions",
        "commercial_release_ready": NONCOMMERCIAL_MARK,
      }
    )

  assoc_codes = {
    row["c_assoc_code"]: clean_text(row["c_assoc_desc_chn"])
    for row in connection.execute("SELECT * FROM ASSOC_CODES")
  }
  for row in connection.execute(
    """
    SELECT a.* FROM ASSOC_DATA a
    JOIN mapped_people p ON p.person_id=a.c_personid
    JOIN mapped_people q ON q.person_id=a.c_assoc_id
    WHERE a.c_personid<>a.c_assoc_id
    ORDER BY a.c_personid,a.c_assoc_id,a.c_assoc_code,a.c_source
    """
  ):
    if not row["c_source"]:
      continue
    first_person, second_person = sorted((row["c_personid"], row["c_assoc_id"]))
    relation_name = assoc_codes.get(row["c_assoc_code"], "社会关系")
    category = relationship_category(relation_name, "association")
    first_year = int_or_none(row["c_assoc_first_year"])
    last_year = int_or_none(row["c_assoc_last_year"])
    key = (first_person, second_person, category, row["c_assoc_code"], row["c_source"])
    if key in seen_relationships:
      continue
    seen_relationships.add(key)
    person_a = candidates[first_person]
    person_b = candidates[second_person]
    death_a = int_or_none(person_a.get("c_deathyear"))
    death_b = int_or_none(person_b.get("c_deathyear"))
    active = (
      first_year is not None and first_year <= SNAPSHOT_YEAR
    ) or (
      first_year is None and death_a is not None and death_b is not None
      and max(death_a, death_b) <= SNAPSHOT_YEAR
    )
    relationship_rows.append(
      {
        "relationship_id": stable_id("REL", *key),
        "from_person_id": f"CBDB-{first_person}",
        "to_person_id": f"CBDB-{second_person}",
        "relation_category": category,
        "relation_code": f"ASSOC-{row['c_assoc_code']}",
        "relation_name": relation_name,
        "first_year": first_year or "",
        "last_year": last_year or "",
        "active_by_1628": "yes" if active else "no",
        "source_id": row["c_source"] or "",
        "source_pages": clean_text(row["c_pages"]),
        "evidence_grade": "B",
        "license_status": "CBDB_research_use_conditions",
        "commercial_release_ready": NONCOMMERCIAL_MARK,
      }
    )

  institution_memberships = [
    dict(row)
    for row in connection.execute(
      """
      SELECT b.*,n.c_inst_name_hz FROM BIOG_INST_DATA b
      JOIN mapped_people p ON p.person_id=b.c_personid
      LEFT JOIN SOCIAL_INSTITUTION_NAME_CODES n USING(c_inst_name_code)
      ORDER BY b.c_personid,b.c_inst_name_code,b.c_inst_code
      """
    )
  ]
  connection.close()
  return {
    "candidates": candidates,
    "source_records": source_records,
    "raw_associations": raw_associations,
    "entry_records": entry_records,
    "group_seed_rows": group_seed_rows,
    "work_records": work_records,
    "office_records": office_records,
    "relationships": relationship_rows,
    "institution_memberships": institution_memberships,
    "address_map_count": len(address_map),
    "candidate_count_initial": candidate_count_initial,
    "mapped_person_count": len(candidates),
  }


def before_snapshot(record_year: Any, person: dict[str, Any]) -> bool:
  year = int_or_none(record_year)
  if year is not None:
    return year <= SNAPSHOT_YEAR
  death = int_or_none(person.get("c_deathyear"))
  return death is not None and death <= SNAPSHOT_YEAR


def build_people_and_associations(
  extracted: dict[str, Any],
) -> tuple[list[dict[str, Any]], list[dict[str, Any]], dict[int, dict[str, Any]]]:
  candidates: dict[int, dict[str, Any]] = extracted["candidates"]
  source_records: dict[int, list[dict[str, Any]]] = extracted["source_records"]
  entries: dict[int, list[dict[str, Any]]] = extracted["entry_records"]
  works: dict[int, list[dict[str, Any]]] = extracted["work_records"]
  offices: dict[int, list[dict[str, Any]]] = extracted["office_records"]
  relationship_degree: Counter[int] = Counter()
  for edge in extracted["relationships"]:
    relationship_degree[int(edge["from_person_id"].split("-")[-1])] += 1
    relationship_degree[int(edge["to_person_id"].split("-")[-1])] += 1

  association_priority = {
    "address_8": 0,
    "address_6": 1,
    "address_16": 2,
    "address_1": 3,
    "index_address": 4,
    "address_2": 5,
    "office_posting": 6,
    "address_4": 7,
    "address_5": 8,
    "address_9": 9,
  }
  associations_by_person: dict[int, list[dict[str, Any]]] = defaultdict(list)
  for row in extracted["raw_associations"]:
    associations_by_person[row["cbdb_person_id"]].append(row)

  association_rows: list[dict[str, Any]] = []
  primary: dict[int, dict[str, Any]] = {}
  for person_id in sorted(candidates):
    records = associations_by_person.get(person_id, [])
    if not records:
      continue
    records = sorted(
      records,
      key=lambda item: (
        association_priority.get(item["association_type_code"], 50),
        item["county_id"],
        int_or_none(item.get("first_year")) or 9999,
      ),
    )
    primary[person_id] = records[0]
    fallback_source = ""
    sources = source_records.get(person_id, [])
    if sources:
      fallback_source = sources[0]["c_textid"]
    seen: set[tuple[Any, ...]] = set()
    for record in records:
      source_id = record.get("source_id") or fallback_source
      key = (
        person_id,
        record["county_id"],
        record["association_type_code"],
        record.get("first_year") or "",
        record.get("last_year") or "",
        source_id,
      )
      if key in seen:
        continue
      seen.add(key)
      first_year = int_or_none(record.get("first_year"))
      last_year = int_or_none(record.get("last_year"))
      present = False
      if record["association_type_code"] == "office_posting":
        present = (
          first_year is not None
          and first_year <= SNAPSHOT_YEAR
          and (last_year is None or last_year >= SNAPSHOT_YEAR)
        )
      elif record.get("address_type") in {2, 4, 6, 18, 19}:
        present = (
          first_year is not None
          and first_year <= SNAPSHOT_YEAR
          and (last_year is None or last_year >= SNAPSHOT_YEAR)
        )
      opening_relevance = first_year is None or first_year <= SNAPSHOT_YEAR
      association_rows.append(
        {
          "association_id": stable_id("PCA", *key),
          "person_id": f"CBDB-{person_id}",
          "county_id": record["county_id"],
          "association_type_code": record["association_type_code"],
          "association_type_name": record["association_type_name"],
          "first_year": first_year or "",
          "last_year": last_year or "",
          "date_quality": record["date_quality"],
          "present_in_county_1628": "yes" if present else "no",
          "opening_relevance": "yes" if opening_relevance else "no",
          "source_id": source_id,
          "source_pages": record.get("source_pages", ""),
          "mapping_method": record["mapping_method"],
          "evidence_grade": "A" if record["mapping_method"] == "exact_county_name" else "B",
          "license_status": "CBDB_research_use_conditions",
          "commercial_release_ready": NONCOMMERCIAL_MARK,
        }
      )

  people: list[dict[str, Any]] = []
  person_lookup: dict[int, dict[str, Any]] = {}
  for person_id in sorted(primary):
    person = candidates[person_id]
    sources = source_records[person_id]
    source_ids = source_string(row["c_textid"] for row in sources)
    source_titles = source_string(row["source_title"] for row in sources[:8])
    status, stage, age = alive_status(person)
    all_entries = entries.get(person_id, [])
    all_works = works.get(person_id, [])
    all_offices = offices.get(person_id, [])
    opening_entries = [
      row for row in all_entries if before_snapshot(row.get("c_year"), person)
    ]
    opening_works = [
      row for row in all_works if before_snapshot(row.get("c_year"), person)
    ]
    opening_offices = [
      row for row in all_offices if before_snapshot(row.get("c_firstyear"), person)
    ]
    exam_entries = [
      row for row in opening_entries
      if any(keyword in clean_text(row.get("c_entry_desc_chn")) for keyword in EXAM_KEYWORDS)
    ]
    highest_exam = ""
    highest_exam_score = 0
    for record in exam_entries:
      description = clean_text(record.get("c_entry_desc_chn"))
      level = entry_level(description)
      if level > highest_exam_score:
        highest_exam_score = level
        highest_exam = description
    highest_office = next(
      (clean_text(row.get("c_office_chn")) for row in opening_offices if clean_text(row.get("c_office_chn"))),
      "",
    )
    types: list[str] = []
    if exam_entries:
      types.append("degree_holder")
    if opening_offices:
      types.append("official")
    if opening_works:
      types.append("author_editor")
    if relationship_degree[person_id] >= 4:
      types.append("networked_literatus")
    if not types:
      types.append("source_person")
    gentry_evidence: list[str] = []
    if exam_entries:
      gentry_evidence.append("功名")
    if opening_offices:
      gentry_evidence.append("官职")
    gentry_status = "yes" if gentry_evidence else "no_evidence"
    historical_raw = (
      min(35, max((entry_level(clean_text(row.get("c_entry_desc_chn"))) for row in all_entries), default=0) * 0.35)
      + min(25, len(all_offices) * 3.0)
      + min(20, len(all_works) * 2.0)
      + min(20, math.log1p(relationship_degree[person_id]) * 6.0)
    )
    opening_raw = (
      highest_exam_score * 0.35
      + min(25, len(opening_offices) * 3.0)
      + min(20, len(opening_works) * 2.0)
      + min(20, math.log1p(relationship_degree[person_id]) * 6.0)
    )
    current_influence = 0
    if status.startswith("alive"):
      current_influence = score(opening_raw * life_stage_multiplier(stage))
    later_entries = sum(
      1 for row in all_entries
      if int_or_none(row.get("c_year")) and int(row["c_year"]) > SNAPSHOT_YEAR
    )
    later_offices = sum(
      1 for row in all_offices
      if int_or_none(row.get("c_firstyear")) and int(row["c_firstyear"]) > SNAPSHOT_YEAR
    )
    later_works = sum(
      1 for row in all_works
      if int_or_none(row.get("c_year")) and int(row["c_year"]) > SNAPSHOT_YEAR
    )
    row = {
      "person_id": f"CBDB-{person_id}",
      "cbdb_person_id": person_id,
      "name": clean_text(person.get("c_name_chn")),
      "surname": clean_text(person.get("c_surname_chn")),
      "given_name": clean_text(person.get("c_mingzi_chn")),
      "gender": "female" if person.get("c_female") == 1 else "male_or_unspecified",
      "birth_year": int_or_none(person.get("c_birthyear")) or "",
      "birth_year_quality": date_quality(
        int_or_none(person.get("c_birthyear")), person.get("c_by_range")
      ),
      "death_year": int_or_none(person.get("c_deathyear")) or "",
      "death_year_quality": date_quality(
        int_or_none(person.get("c_deathyear")), person.get("c_dy_range")
      ),
      "age_1628": age,
      "life_stage_1628": stage,
      "alive_status_1628": status,
      "primary_county_id": primary[person_id]["county_id"],
      "primary_county_association": primary[person_id]["association_type_name"],
      "highest_exam_before_1628": highest_exam,
      "office_count_before_1628": len(opening_offices),
      "highest_office_before_1628": highest_office,
      "work_count_before_1628": len(opening_works),
      "total_source_work_count": len(all_works),
      "person_types_1628": ";".join(types),
      "gentry_status_1628": gentry_status,
      "gentry_evidence": ";".join(gentry_evidence),
      "historical_influence_0_100": score(historical_raw),
      "influence_1628_0_100": current_influence,
      "post_1628_achievement_metadata": (
        f"entry_records={later_entries};office_records={later_offices};work_records={later_works}"
      ),
      "spoiler_sensitive": "yes" if status.startswith("alive") and (later_entries + later_offices + later_works) else "no",
      "source_ids": source_ids,
      "source_titles": source_titles,
      "evidence_grade": "A" if int_or_none(person.get("c_birthyear")) and source_titles else "B",
      "license_status": "CBDB_research_use_conditions",
      "commercial_release_ready": NONCOMMERCIAL_MARK,
      "_opening_exam_count": len(exam_entries),
      "_opening_entries": opening_entries,
      "_opening_works": opening_works,
      "_opening_offices": opening_offices,
    }
    people.append(row)
    person_lookup[person_id] = row
  association_rows.sort(key=lambda row: (row["person_id"], row["county_id"], row["association_id"]))
  return people, association_rows, person_lookup


def build_institutions(
  cbdb_path: Path,
  academies_path: Path,
  manual_anchors: Sequence[dict[str, str]],
  mapper: CountyMapper,
  county_by_id: dict[str, dict[str, Any]],
) -> list[dict[str, Any]]:
  connection = sqlite3.connect(f"file:{cbdb_path}?mode=ro", uri=True)
  connection.row_factory = sqlite3.Row
  cbdb_institutions = {
    row["c_inst_code"]: dict(row)
    for row in connection.execute(
      """
      SELECT s.*,n.c_inst_name_hz,t.c_inst_type_hz
      FROM SOCIAL_INSTITUTION_CODES s
      LEFT JOIN SOCIAL_INSTITUTION_NAME_CODES n USING(c_inst_name_code)
      LEFT JOIN SOCIAL_INSTITUTION_TYPES t USING(c_inst_type_code)
      """
    )
  }
  institution_addresses: dict[int, list[sqlite3.Row]] = defaultdict(list)
  for row in connection.execute(
    """
    SELECT a.*,c.c_name_chn,c.c_admin_type,c.x_coord,c.y_coord
    FROM SOCIAL_INSTITUTION_ADDR a
    LEFT JOIN ADDR_CODES c ON c.c_addr_id=a.c_inst_addr_id
    ORDER BY a.c_inst_code,a.c_inst_addr_id
    """
  ):
    institution_addresses[row["c_inst_code"]].append(row)

  rows_by_key: dict[tuple[str, str], dict[str, Any]] = {}
  with academies_path.open(encoding="utf-8-sig", newline="") as stream:
    academy_rows = list(csv.DictReader(stream))
  curated_ids = set(range(1, 126)) | {199, 36, 384, 481, 510, 542, 900, 969, 1010, 1109, 1136, 1386}
  for source_row in academy_rows:
    dataset_id = int_or_none(source_row.get("id"))
    name = clean_text(source_row.get("书院名"))
    if not dataset_id or not name:
      continue
    cbdb_inst_code = int_or_none(source_row.get("书院id(CBDB)"))
    cbdb_inst_name_code = int_or_none(source_row.get("书院名id(CBDB)"))
    cbdb_row = cbdb_institutions.get(cbdb_inst_code or -1, {})
    begin_year = extract_years(
      clean_text(source_row.get("建院时间")) + " " + clean_text(source_row.get("建院年号"))
    ) or int_or_none(cbdb_row.get("c_inst_begin_year")) or int_or_none(cbdb_row.get("c_inst_first_known_year"))
    dynasty_text = clean_text(source_row.get("建院朝代")) + clean_text(source_row.get("信息來源"))
    pre_1628 = (
      (begin_year is not None and begin_year <= SNAPSHOT_YEAR)
      or any(item in dynasty_text for item in ("宋", "元", "明", "遼", "辽", "金"))
      or int_or_none(cbdb_row.get("c_inst_begin_dy")) in {15, 16, 17, 18, 19}
    )
    if not pre_1628:
      continue
    destroyed_text = clean_text(source_row.get("被毀時間")) + " " + clean_text(source_row.get("销毁的建筑（时间）"))
    end_year = extract_years(destroyed_text)
    mapped: tuple[str, str] | None = None
    for address in institution_addresses.get(cbdb_inst_code or -1, []):
      mapped = mapper.map_county_address(
        address["c_name_chn"], address["c_admin_type"], address["x_coord"], address["y_coord"]
      )
      if mapped:
        break
    if not mapped:
      mapped = mapper.map_freeform(
        clean_text(source_row.get("书院地址")),
        source_row.get("书院地址x坐标"),
        source_row.get("书院地址y坐标"),
      )
    if not mapped:
      continue
    county_id, mapping_method = mapped
    if dataset_id in curated_ids:
      tier, grade = "researcher_curated", "A"
    elif dataset_id <= 1392:
      tier, grade = "CBDB_record", "B"
    else:
      tier, grade = "automatic_extraction", "C"
    source_title = clean_text(source_row.get("书院出处")) or "中国书院数据"
    source_locator = f"academy_dataset_id={dataset_id}"
    pages = clean_text(source_row.get("书院出处页码"))
    if pages:
      source_locator += f"; pages={pages}"
    result = {
      "institution_anchor_id": stable_id("INST", county_id, name),
      "county_id": county_id,
      "county": county_by_id[county_id]["county"],
      "institution_name": name,
      "institution_type": "academy",
      "begin_year": begin_year or "",
      "end_year": end_year or "",
      "active_in_1628": "yes" if not end_year or end_year >= SNAPSHOT_YEAR else "no",
      "evidence_tier": tier,
      "academy_dataset_id": dataset_id,
      "cbdb_inst_code": cbdb_inst_code or "",
      "cbdb_inst_name_code": cbdb_inst_name_code or cbdb_row.get("c_inst_name_code", ""),
      "source_id": f"ACADEMY-{dataset_id}",
      "source_title": source_title,
      "source_url": ACADEMIES_URL,
      "source_locator": source_locator,
      "mapping_method": mapping_method,
      "license_status": "research_use_only",
      "commercial_release_ready": NONCOMMERCIAL_MARK,
      "_grade": grade,
    }
    key = (county_id, normalize_place_name(name))
    existing = rows_by_key.get(key)
    if not existing or ("ABC".index(grade) < "ABC".index(existing["_grade"])):
      rows_by_key[key] = result

  for code, cbdb_row in cbdb_institutions.items():
    if cbdb_row.get("c_inst_type_code") != 4:
      continue
    begin_year = int_or_none(cbdb_row.get("c_inst_begin_year")) or int_or_none(cbdb_row.get("c_inst_first_known_year"))
    if begin_year and begin_year > SNAPSHOT_YEAR:
      continue
    mapped = None
    for address in institution_addresses.get(code, []):
      mapped = mapper.map_county_address(
        address["c_name_chn"], address["c_admin_type"], address["x_coord"], address["y_coord"]
      )
      if mapped:
        break
    name = clean_text(cbdb_row.get("c_inst_name_hz"))
    if not mapped or not name:
      continue
    county_id, mapping_method = mapped
    result = {
      "institution_anchor_id": stable_id("INST", county_id, name),
      "county_id": county_id,
      "county": county_by_id[county_id]["county"],
      "institution_name": name,
      "institution_type": "literary_society",
      "begin_year": begin_year or "",
      "end_year": int_or_none(cbdb_row.get("c_inst_end_year")) or "",
      "active_in_1628": "yes",
      "evidence_tier": "CBDB_record",
      "academy_dataset_id": "",
      "cbdb_inst_code": code,
      "cbdb_inst_name_code": cbdb_row.get("c_inst_name_code", ""),
      "source_id": cbdb_row.get("c_source", ""),
      "source_title": "CBDB社会机构记录",
      "source_url": CBDB_STRUCTURE_URL,
      "source_locator": f"SOCIAL_INSTITUTION_CODES.c_inst_code={code}",
      "mapping_method": mapping_method,
      "license_status": "CBDB_research_use_conditions",
      "commercial_release_ready": NONCOMMERCIAL_MARK,
      "_grade": "B",
    }
    rows_by_key[(county_id, normalize_place_name(name))] = result

  connection.close()
  for anchor in manual_anchors:
    if anchor["anchor_type"] not in {"institution_academy", "institution_school", "literary_society"}:
      continue
    county_id = anchor["county_id"]
    institution_type = {
      "institution_academy": "academy",
      "institution_school": "official_school",
      "literary_society": "literary_society",
    }[anchor["anchor_type"]]
    result = {
      "institution_anchor_id": stable_id("INST", county_id, anchor["anchor_name"]),
      "county_id": county_id,
      "county": anchor["county"],
      "institution_name": anchor["anchor_name"],
      "institution_type": institution_type,
      "begin_year": anchor["start_year"],
      "end_year": anchor["end_year"],
      "active_in_1628": anchor["active_in_1628"],
      "evidence_tier": "manual_verified",
      "academy_dataset_id": "",
      "cbdb_inst_code": "3764" if anchor["anchor_name"] == "东林书院" else "",
      "cbdb_inst_name_code": "2050" if anchor["anchor_name"] == "东林书院" else "",
      "source_id": anchor["anchor_id"],
      "source_title": anchor["source_title"],
      "source_url": anchor["source_url"],
      "source_locator": anchor["source_locator"],
      "mapping_method": "manual_county_anchor",
      "license_status": anchor["license_status"],
      "commercial_release_ready": NONCOMMERCIAL_MARK,
      "_grade": anchor["evidence_grade"],
    }
    rows_by_key[(county_id, normalize_place_name(anchor["anchor_name"]))] = result
  return sorted(rows_by_key.values(), key=lambda row: (row["county_id"], row["institution_name"]))


def build_groups(
  extracted: dict[str, Any],
  person_lookup: dict[int, dict[str, Any]],
  institutions: Sequence[dict[str, Any]],
) -> list[dict[str, Any]]:
  rows: list[dict[str, Any]] = []
  for record in extracted["group_seed_rows"]:
    person = person_lookup.get(record["c_personid"])
    year = int_or_none(record.get("c_year"))
    if not person or not year:
      continue
    description = clean_text(record.get("c_entry_desc_chn"))
    group_id = f"EXAM-{year}-{record['c_entry_code']}"
    rows.append(
      {
        "membership_id": stable_id("GRP", group_id, person["person_id"], record.get("c_sequence")),
        "group_id": group_id,
        "group_type": "exam_cohort",
        "group_name": f"{year}年 {description}",
        "person_id": person["person_id"],
        "county_id": person["primary_county_id"],
        "event_year": year,
        "member_role": description,
        "source_id": record.get("c_source") or person["source_ids"].split(";")[0],
        "source_pages": clean_text(record.get("c_pages")),
        "evidence_grade": "A" if record.get("c_source") else "B",
        "license_status": "CBDB_research_use_conditions",
        "commercial_release_ready": NONCOMMERCIAL_MARK,
      }
    )
  institution_lookup: dict[tuple[int, int], dict[str, Any]] = {}
  for institution in institutions:
    name_code = int_or_none(institution.get("cbdb_inst_name_code"))
    inst_code = int_or_none(institution.get("cbdb_inst_code"))
    if name_code and inst_code:
      institution_lookup[(name_code, inst_code)] = institution
  for record in extracted["institution_memberships"]:
    person = person_lookup.get(record["c_personid"])
    if not person:
      continue
    institution = institution_lookup.get((record["c_inst_name_code"], record["c_inst_code"]))
    if not institution:
      continue
    begin_year = int_or_none(record.get("c_bi_begin_year"))
    if begin_year and begin_year > SNAPSHOT_YEAR:
      continue
    group_id = institution["institution_anchor_id"]
    rows.append(
      {
        "membership_id": stable_id("GRP", group_id, person["person_id"], record.get("c_bi_role_code")),
        "group_id": group_id,
        "group_type": institution["institution_type"],
        "group_name": institution["institution_name"],
        "person_id": person["person_id"],
        "county_id": institution["county_id"],
        "event_year": begin_year or "",
        "member_role": f"CBDB机构角色代码{record['c_bi_role_code']}",
        "source_id": record.get("c_source") or institution["source_id"],
        "source_pages": clean_text(record.get("c_pages")),
        "evidence_grade": "B",
        "license_status": "CBDB_research_use_conditions",
        "commercial_release_ready": NONCOMMERCIAL_MARK,
      }
    )
  unique = {row["membership_id"]: row for row in rows}
  return sorted(unique.values(), key=lambda row: (row["group_id"], row["person_id"], row["membership_id"]))


class UnionFind:
  def __init__(self) -> None:
    self.parent: dict[str, str] = {}

  def add(self, value: str) -> None:
    self.parent.setdefault(value, value)

  def find(self, value: str) -> str:
    parent = self.parent[value]
    if parent != value:
      self.parent[value] = self.find(parent)
    return self.parent[value]

  def union(self, first: str, second: str) -> None:
    self.add(first)
    self.add(second)
    root_a, root_b = self.find(first), self.find(second)
    if root_a != root_b:
      low, high = sorted((root_a, root_b))
      self.parent[high] = low


def build_families(
  people: Sequence[dict[str, Any]],
  relationships: Sequence[dict[str, Any]],
  manual_anchors: Sequence[dict[str, str]],
  county_by_id: dict[str, dict[str, Any]],
) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
  people_by_id = {row["person_id"]: row for row in people}
  union = UnionFind()
  edge_by_pair: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
  for edge in relationships:
    if edge["relation_category"] not in {"kinship", "marriage"}:
      continue
    first = people_by_id.get(edge["from_person_id"])
    second = people_by_id.get(edge["to_person_id"])
    if not first or not second:
      continue
    if first["primary_county_id"] != second["primary_county_id"]:
      continue
    if not first["surname"] or first["surname"] != second["surname"]:
      continue
    union.union(first["person_id"], second["person_id"])
    pair = tuple(sorted((first["person_id"], second["person_id"])))
    edge_by_pair[pair].append(edge)
  components: dict[str, list[str]] = defaultdict(list)
  for person_id in union.parent:
    components[union.find(person_id)].append(person_id)

  manual_names: dict[tuple[str, str], str] = {}
  manual_source: dict[tuple[str, str], dict[str, str]] = {}
  for anchor in manual_anchors:
    if anchor["anchor_type"] != "family_lineage":
      continue
    match = re.search(r"(.)氏$", anchor["anchor_name"])
    if not match:
      continue
    key = (anchor["county_id"], match.group(1))
    manual_names[key] = anchor["anchor_name"]
    manual_source[key] = anchor

  family_rows: list[dict[str, Any]] = []
  membership_rows: list[dict[str, Any]] = []
  used_manual_names: set[tuple[str, str]] = set()
  ordered_components = sorted(
    (sorted(members) for members in components.values() if len(members) >= 2),
    key=lambda members: (people_by_id[members[0]]["primary_county_id"], people_by_id[members[0]]["surname"], -len(members), members),
  )
  for members in ordered_components:
    representative = people_by_id[members[0]]
    county_id = representative["primary_county_id"]
    surname = representative["surname"]
    sources: set[str] = set()
    relation_names: list[str] = []
    for pair, edges in edge_by_pair.items():
      if pair[0] in members and pair[1] in members:
        for edge in edges:
          if edge["source_id"]:
            sources.add(str(edge["source_id"]))
          relation_names.append(edge["relation_name"])
    elite = sum(
      1 for person_id in members
      if people_by_id[person_id]["gentry_status_1628"] == "yes"
      or int(people_by_id[person_id]["historical_influence_0_100"]) >= 55
    )
    generations = 3 if any(word in name for name in relation_names for word in ("祖", "孫", "孙")) else 2
    notable = generations >= 2 and elite >= 2
    manual_key = (county_id, surname)
    historical_name = ""
    if manual_key in manual_names and manual_key not in used_manual_names:
      historical_name = manual_names[manual_key]
      used_manual_names.add(manual_key)
      sources.add(manual_source[manual_key]["anchor_id"])
      notable = True
    family_id = stable_id("FAM", county_id, surname, *members)
    family_rows.append(
      {
        "family_id": family_id,
        "county_id": county_id,
        "county": county_by_id[county_id]["county"],
        "surname": surname,
        "historical_lineage_name": historical_name,
        "derived_descriptor": f"{county_by_id[county_id]['county']}{surname}姓CBDB关系支系",
        "member_count": len(members),
        "generation_count_est": generations,
        "elite_member_count": elite,
        "is_notable_lineage": "yes" if notable else "no",
        "evidence_basis": "CBDB source-backed kinship/marriage connected component",
        "source_ids": source_string(sources),
        "evidence_grade": "A" if historical_name else "B",
        "license_status": "CBDB_research_use_conditions",
        "commercial_release_ready": NONCOMMERCIAL_MARK,
      }
    )
    for person_id in members:
      membership_rows.append(
        {
          "membership_id": stable_id("FMM", family_id, person_id),
          "family_id": family_id,
          "person_id": person_id,
          "county_id": county_id,
          "membership_basis": "source-backed kinship/marriage graph",
          "source_ids": source_string(sources),
          "evidence_grade": "B",
          "license_status": "CBDB_research_use_conditions",
          "commercial_release_ready": NONCOMMERCIAL_MARK,
        }
      )
  for key, name in sorted(manual_names.items()):
    if key in used_manual_names:
      continue
    county_id, surname = key
    anchor = manual_source[key]
    family_rows.append(
      {
        "family_id": stable_id("FAM", county_id, surname, anchor["anchor_id"]),
        "county_id": county_id,
        "county": county_by_id[county_id]["county"],
        "surname": surname,
        "historical_lineage_name": name,
        "derived_descriptor": "",
        "member_count": 0,
        "generation_count_est": 2,
        "elite_member_count": 2,
        "is_notable_lineage": "yes",
        "evidence_basis": anchor["evidence_summary"],
        "source_ids": anchor["anchor_id"],
        "evidence_grade": anchor["evidence_grade"],
        "license_status": anchor["license_status"],
        "commercial_release_ready": NONCOMMERCIAL_MARK,
      }
    )
  family_rows.sort(key=lambda row: (row["county_id"], row["surname"], row["family_id"]))
  membership_rows.sort(key=lambda row: (row["family_id"], row["person_id"]))
  return family_rows, membership_rows


def calibrate_literacy(
  rows: list[dict[str, Any]],
  drivers: dict[str, float],
  target: float,
  minimum: float,
  maximum: float,
  prefix: str,
) -> None:
  weights = {row["county_id"]: float(row["population_est_1628"]) for row in rows}
  total_weight = sum(weights.values())
  weighted_mean = sum(drivers[key] * weights[key] for key in drivers) / total_weight
  weighted_variance = sum(
    weights[key] * (drivers[key] - weighted_mean) ** 2 for key in drivers
  ) / total_weight
  standard_deviation = math.sqrt(weighted_variance) or 1.0
  standardized = {key: (value - weighted_mean) / standard_deviation for key, value in drivers.items()}

  def outcome(intercept: float, key: str) -> float:
    logistic = 1.0 / (1.0 + math.exp(-(intercept + 0.82 * standardized[key])))
    return minimum + (maximum - minimum) * logistic

  low, high = -12.0, 12.0
  for _ in range(100):
    midpoint = (low + high) / 2
    mean = sum(outcome(midpoint, key) * weights[key] for key in drivers) / total_weight
    if mean < target:
      low = midpoint
    else:
      high = midpoint
  intercept = (low + high) / 2
  for row in rows:
    county_id = row["county_id"]
    midpoint = round(outcome(intercept, county_id), 2)
    uncertainty = 0.18 + 0.22 * (1.0 - int(row["data_coverage_0_100"]) / 100.0)
    low_value = round(clamp(midpoint * (1.0 - uncertainty), minimum, maximum), 2)
    high_value = round(clamp(midpoint * (1.0 + uncertainty), minimum, maximum), 2)
    row[f"{prefix}_low_pct"] = f"{low_value:.2f}"
    row[f"{prefix}_mid_pct"] = f"{midpoint:.2f}"
    row[f"{prefix}_high_pct"] = f"{high_value:.2f}"


def weighted_rate(rows: Sequence[dict[str, Any]], column: str) -> float:
  population = sum(float(row["population_est_1628"]) for row in rows)
  return sum(
    float(row["population_est_1628"]) * float(row[column]) for row in rows
  ) / population


def build_county_baselines(
  economies: Sequence[dict[str, Any]],
  people: Sequence[dict[str, Any]],
  relationships: Sequence[dict[str, Any]],
  institutions: Sequence[dict[str, Any]],
  families: Sequence[dict[str, Any]],
  manual_anchors: Sequence[dict[str, str]],
  globally_unmapped_people: int,
) -> tuple[list[dict[str, Any]], list[dict[str, Any]], dict[str, Any]]:
  county_ids = [row["county_id"] for row in economies]
  economy_by_id = {row["county_id"]: row for row in economies}
  people_by_id = {row["person_id"]: row for row in people}
  stats: dict[str, dict[str, Any]] = {
    county_id: {
      "people": 0,
      "alive_confirmed": 0,
      "alive_probable": 0,
      "deceased": 0,
      "national": 0,
      "regional": 0,
      "recent_exam": 0,
      "all_exam": 0,
      "literati": 0,
      "works": 0,
      "gentry": 0,
      "officials": 0,
      "kin": 0,
      "social": 0,
      "academy": 0,
      "school": 0,
      "society": 0,
      "family": 0,
      "notable_family": 0,
      "sources": set(),
      "manual": [],
      "representative_people": [],
      "representative_families": [],
    }
    for county_id in county_ids
  }
  for person in people:
    county_id = person["primary_county_id"]
    county = stats[county_id]
    county["people"] += 1
    status = person["alive_status_1628"]
    if status == "alive_confirmed":
      county["alive_confirmed"] += 1
    elif status == "alive_probable":
      county["alive_probable"] += 1
    else:
      county["deceased"] += 1
    historical = int(person["historical_influence_0_100"])
    if historical >= 80:
      county["national"] += 1
    elif historical >= 60:
      county["regional"] += 1
    exam_records = person.get("_opening_entries", [])
    for record in exam_records:
      description = clean_text(record.get("c_entry_desc_chn"))
      if not any(keyword in description for keyword in EXAM_KEYWORDS):
        continue
      county["all_exam"] += 1
      year = int_or_none(record.get("c_year"))
      if year and SNAPSHOT_YEAR - 99 <= year <= SNAPSHOT_YEAR:
        county["recent_exam"] += 1
    county["works"] += int(person["work_count_before_1628"])
    if person["person_types_1628"] != "source_person":
      county["literati"] += 1
    if person["gentry_status_1628"] == "yes":
      county["gentry"] += 1
    if int(person["office_count_before_1628"]) > 0:
      county["officials"] += 1
    county["sources"].update(item for item in person["source_ids"].split(";") if item)
    opening_display_score = (
      int(person["influence_1628_0_100"])
      if status.startswith("alive")
      else int(person["historical_influence_0_100"])
    )
    if person["name"]:
      county["representative_people"].append((opening_display_score, person["name"]))

  for edge in relationships:
    first = people_by_id.get(edge["from_person_id"])
    second = people_by_id.get(edge["to_person_id"])
    if not first or not second:
      continue
    first_county = first["primary_county_id"]
    second_county = second["primary_county_id"]
    if edge["relation_category"] in {"kinship", "marriage"} and first_county == second_county:
      stats[first_county]["kin"] += 1
    if edge["relation_category"] in {"teacher_student", "social_association"} and edge["active_by_1628"] == "yes":
      stats[first_county]["social"] += 1
      if second_county != first_county:
        stats[second_county]["social"] += 1

  for institution in institutions:
    county = stats[institution["county_id"]]
    if institution["institution_type"] == "academy":
      county["academy"] += 1
    elif institution["institution_type"] == "official_school":
      county["school"] += 1
    elif institution["institution_type"] == "literary_society":
      county["society"] += 1
    county["sources"].add(str(institution["source_id"]))

  for family in families:
    county = stats[family["county_id"]]
    county["family"] += 1
    if family["is_notable_lineage"] == "yes":
      county["notable_family"] += 1
    county["sources"].update(item for item in str(family["source_ids"]).split(";") if item)
    if family["historical_lineage_name"]:
      county["representative_families"].append(family["historical_lineage_name"])

  for anchor in manual_anchors:
    stats[anchor["county_id"]]["manual"].append(anchor)
    stats[anchor["county_id"]]["sources"].add(anchor["anchor_id"])

  population = {row["county_id"]: max(1.0, float(row["population_est_1628"])) for row in economies}
  rate_metrics: dict[str, dict[str, float]] = {}
  for metric in [
    "people", "recent_exam", "all_exam", "literati", "works", "gentry", "officials",
    "academy", "family", "notable_family",
  ]:
    rate_metrics[metric] = {
      county_id: stats[county_id][metric] * 100000.0 / population[county_id]
      for county_id in county_ids
    }
  rate_metrics["kin"] = {
    county_id: stats[county_id]["kin"] / max(1, stats[county_id]["people"])
    for county_id in county_ids
  }
  rate_metrics["social"] = {
    county_id: stats[county_id]["social"] / max(1, stats[county_id]["people"])
    for county_id in county_ids
  }
  rate_metrics["density"] = {
    row["county_id"]: float(row["population_density_per_km2"]) for row in economies
  }
  rate_metrics["population"] = population
  percentiles = {metric: percentile_scores(values) for metric, values in rate_metrics.items()}

  baseline_rows: list[dict[str, Any]] = []
  literacy_drivers: dict[str, float] = {}
  for economy in economies:
    county_id = economy["county_id"]
    local = stats[county_id]
    administrative = number(economy, "administrative_centrality_0_100")
    commerce = number(economy, "commercial_prosperity_1628_0_100")
    urbanization = number(economy, "urbanization_rate_0_100")
    local_market = number(economy, "local_market_0_100")
    transport = number(economy, "transport_access_0_100")
    paper = number(economy, "forestry_paper_potential_0_100")
    agriculture = number(economy, "agriculture_resource_0_100")
    resilience = number(economy, "economic_resilience_0_100")
    education_structure = score(
      administrative * 0.25 + commerce * 0.20 + urbanization * 0.15
      + local_market * 0.15 + transport * 0.10 + paper * 0.10
      + percentiles["density"][county_id] * 0.05
    )
    official_school_expected = score(
      max(30, administrative * 0.35 + local_market * 0.20 + transport * 0.15
      + percentiles["population"][county_id] * 0.20 + education_structure * 0.10)
    )
    institution_pct = max(
      percentiles["academy"][county_id],
      score(min(100, local["academy"] * 18 + local["school"] * 20 + local["society"] * 10)),
    )
    recent_exam_pct = percentiles["recent_exam"][county_id]
    all_exam_pct = percentiles["all_exam"][county_id]
    literati_pct = percentiles["literati"][county_id]
    work_pct = percentiles["works"][county_id]
    education_evidence = score(
      recent_exam_pct * 0.40 + institution_pct * 0.20 + literati_pct * 0.15
      + work_pct * 0.15 + all_exam_pct * 0.10
    )
    person_saturation = saturation(rate_metrics["people"][county_id], 8.0)
    source_saturation = saturation(len(local["sources"]), 6.0)
    exam_saturation = saturation(rate_metrics["all_exam"][county_id], 4.0)
    institution_saturation = saturation(local["academy"] + local["school"] + local["society"], 2.0)
    manual_saturation = saturation(len(local["manual"]), 1.0)
    coverage = score(
      person_saturation * 0.35 + source_saturation * 0.25 + exam_saturation * 0.20
      + institution_saturation * 0.10 + manual_saturation * 0.10
    )
    evidence_weight = 0.60 * coverage / 100.0
    education = score((1.0 - evidence_weight) * education_structure + evidence_weight * education_evidence)
    exam_culture = score(
      recent_exam_pct * 0.50 + all_exam_pct * 0.20 + institution_pct * 0.15
      + literati_pct * 0.15
    )
    manual_academy = max(
      [int(anchor["opening_weight_0_100"]) for anchor in local["manual"] if anchor["anchor_type"] == "institution_academy"]
      or [0]
    )
    manual_publishing = max(
      [int(anchor["opening_weight_0_100"]) for anchor in local["manual"] if anchor["anchor_type"] == "publishing_book_trade"]
      or [0]
    )
    manual_family = max(
      [int(anchor["opening_weight_0_100"]) for anchor in local["manual"] if anchor["anchor_type"] == "family_lineage"]
      or [0]
    )
    literati_network = score(
      percentiles["social"][county_id] * 0.30 + literati_pct * 0.20
      + exam_culture * 0.15 + institution_pct * 0.10 + commerce * 0.10
      + manual_academy * 0.15
    )
    publishing = score(
      paper * 0.25 + commerce * 0.25 + urbanization * 0.15 + transport * 0.15
      + work_pct * 0.10 + manual_publishing * 0.10
    )
    legacy_person_pct = score((percentiles["literati"][county_id] + percentiles["works"][county_id]) / 2)
    cultural_influence = score(
      education * 0.30 + exam_culture * 0.20 + literati_network * 0.20
      + publishing * 0.15 + institution_pct * 0.10 + legacy_person_pct * 0.05
    )
    rural_population_pct = percentiles["population"][county_id]
    lineage_structure = score(
      rural_population_pct * 0.25 + agriculture * 0.25 + resilience * 0.20
      + local_market * 0.15 + percentiles["population"][county_id] * 0.15
    )
    family_evidence = score(
      max(percentiles["family"][county_id], manual_family) * 0.30
      + percentiles["kin"][county_id] * 0.25
      + max(percentiles["notable_family"][county_id], manual_family) * 0.25
      + all_exam_pct * 0.10 + percentiles["officials"][county_id] * 0.10
    )
    lineage = score((1.0 - evidence_weight) * lineage_structure + evidence_weight * family_evidence)
    gentry_structure = score(
      agriculture * 0.20 + resilience * 0.20 + local_market * 0.20
      + percentiles["population"][county_id] * 0.15 + education * 0.15
      + administrative * 0.10
    )
    gentry_evidence = score(
      percentiles["gentry"][county_id] * 0.35 + all_exam_pct * 0.25
      + percentiles["officials"][county_id] * 0.20
      + max(percentiles["notable_family"][county_id], manual_family) * 0.20
    )
    gentry_power = score((1.0 - evidence_weight) * gentry_structure + evidence_weight * gentry_evidence)
    elite_network = score(
      percentiles["social"][county_id] * 0.35 + percentiles["kin"][county_id] * 0.25
      + literati_pct * 0.20 + all_exam_pct * 0.20
    )
    row: dict[str, Any] = {
      "county_id": county_id,
      "snapshot_year": SNAPSHOT_YEAR,
      "region": economy["region"],
      "upper_unit": economy["upper_unit"],
      "intermediate_unit": economy.get("intermediate_unit", ""),
      "county": economy["county"],
      "population_est_1628": economy["population_est_1628"],
      "official_school_expected_0_100": official_school_expected,
      "verified_school_count": local["school"],
      "verified_academy_count": local["academy"],
      "verified_literary_society_count": local["society"],
      "education_structure_potential_0_100": education_structure,
      "education_evidence_0_100": education_evidence,
      "education_degree_1628_0_100": education,
      "imperial_exam_culture_0_100": exam_culture,
      "literati_network_0_100": literati_network,
      "publishing_book_culture_0_100": publishing,
      "cultural_influence_0_100": cultural_influence,
      "documented_family_lineage_count": local["family"],
      "notable_lineage_count": local["notable_family"],
      "gentry_person_count": local["gentry"],
      "lineage_organization_potential_0_100": lineage,
      "gentry_power_0_100": gentry_power,
      "elite_network_density_0_100": elite_network,
      "alive_confirmed_count_1628": local["alive_confirmed"],
      "alive_probable_count_1628": local["alive_probable"],
      "deceased_legacy_person_count": local["deceased"],
      "national_level_person_count": local["national"],
      "regional_level_person_count": local["regional"],
      "recent_exam_record_count": local["recent_exam"],
      "historical_exam_record_count": local["all_exam"],
      "literati_person_count": local["literati"],
      "work_record_count_before_1628": local["works"],
      "kinship_edge_count": local["kin"],
      "social_edge_count_before_1628": local["social"],
      "data_coverage_0_100": coverage,
      "mapping_method": "exact historical county name; CHGIS coordinate service radius; ambiguous prefecture-only records unassigned",
      "evidence_mix_method": "r=0.60*coverage/100; population-normalized log1p nationwide percentiles",
      "literacy_estimation_method": "population-weighted logistic calibration with coverage uncertainty interval",
      "independent_source_count": len(local["sources"]),
      "manual_anchor_count": len(local["manual"]),
      "unmapped_source_record_count": 0,
      "source_manifest_version": RULESET_VERSION,
      "commercial_release_ready": NONCOMMERCIAL_MARK,
    }
    baseline_rows.append(row)
    literacy_drivers[county_id] = (
      education * 0.35 + exam_culture * 0.20 + commerce * 0.15
      + urbanization * 0.10 + publishing * 0.10 + administrative * 0.10
    )

  calibrate_literacy(baseline_rows, literacy_drivers, 18.0, 6.0, 35.0, "male_basic_literacy")
  calibrate_literacy(baseline_rows, literacy_drivers, 3.0, 0.5, 9.0, "female_basic_literacy")
  calibrate_literacy(baseline_rows, literacy_drivers, 2.5, 0.2, 8.0, "classical_education")
  for row in baseline_rows:
    male_mid = float(row["male_basic_literacy_mid_pct"])
    female_mid = float(row["female_basic_literacy_mid_pct"])
    total_mid = round(male_mid * 0.51 + female_mid * 0.49, 2)
    uncertainty = 0.18 + 0.22 * (1.0 - int(row["data_coverage_0_100"]) / 100.0)
    row["total_basic_literacy_mid_pct"] = f"{total_mid:.2f}"
    row["total_basic_literacy_low_pct"] = f"{max(0.0, total_mid * (1.0 - uncertainty)):.2f}"
    row["total_basic_literacy_high_pct"] = f"{min(100.0, total_mid * (1.0 + uncertainty)):.2f}"

  overview_rows: list[dict[str, Any]] = []
  baseline_by_id = {row["county_id"]: row for row in baseline_rows}
  for economy in economies:
    county_id = economy["county_id"]
    row = baseline_by_id[county_id]
    people_names = []
    seen_names: set[str] = set()
    for _, name in sorted(stats[county_id]["representative_people"], key=lambda item: (-item[0], item[1])):
      if name not in seen_names:
        seen_names.add(name)
        people_names.append(name)
      if len(people_names) == 3:
        break
    family_names = sorted(set(stats[county_id]["representative_families"]))[:3]
    overview_rows.append(
      {
        **{column: row.get(column, "") for column in OVERVIEW_COLUMNS},
        "representative_source_families": ";".join(family_names),
        "representative_source_people": ";".join(people_names),
      }
    )
  calibration = {
    "population_weighted_male_mid_pct": round(weighted_rate(baseline_rows, "male_basic_literacy_mid_pct"), 6),
    "population_weighted_female_mid_pct": round(weighted_rate(baseline_rows, "female_basic_literacy_mid_pct"), 6),
    "population_weighted_total_mid_pct": round(weighted_rate(baseline_rows, "total_basic_literacy_mid_pct"), 6),
    "population_weighted_classical_mid_pct": round(weighted_rate(baseline_rows, "classical_education_mid_pct"), 6),
    "globally_unmapped_candidate_people": globally_unmapped_people,
  }
  return baseline_rows, overview_rows, calibration


def build_source_manifest(
  cbdb_path: Path,
  academies_path: Path,
  economy_path: Path,
  source_database: Path,
  manual_anchor_path: Path,
) -> list[dict[str, Any]]:
  return [
    {
      "source_id": "CBDB-20260822",
      "source_title": "China Biographical Database SQLite",
      "source_kind": "biographical_relational_database",
      "pinned_version": CBDB_FILENAME,
      "local_path": str(cbdb_path),
      "upstream_url": "https://raw.githubusercontent.com/cbdb-project/cbdb_sqlite/master/latest.json",
      "checksum_algorithm": "SHA-256",
      "checksum_value": CBDB_SHA256,
      "license_status": "CBDB_research_use_conditions",
      "usage_in_v0_4": "people, addresses, examinations, offices, kinship, associations, institutions, texts",
      "commercial_release_ready": NONCOMMERCIAL_MARK,
      "notes": CBDB_ACCESS_URL,
    },
    {
      "source_id": "ACADEMIES-DVN-J6XRIV-V1",
      "source_title": "Chinese Academies Data",
      "source_kind": "academy_dataset",
      "pinned_version": "Harvard Dataverse V1; 2957 rows",
      "local_path": str(academies_path),
      "upstream_url": ACADEMIES_URL,
      "checksum_algorithm": "MD5",
      "checksum_value": ACADEMIES_MD5,
      "license_status": "research_use_only",
      "usage_in_v0_4": "academy anchors established no later than 1628",
      "commercial_release_ready": NONCOMMERCIAL_MARK,
      "notes": ACADEMIES_DOI,
    },
    {
      "source_id": "PROJECT-REALM-ECONOMY-V0.2",
      "source_title": "Project Realm county economy baseline v0.2",
      "source_kind": "local_derived_baseline",
      "pinned_version": "v0.2",
      "local_path": str(economy_path),
      "upstream_url": "",
      "checksum_algorithm": "SHA-256",
      "checksum_value": file_digest(economy_path),
      "license_status": "inherits_CHGIS_noncommercial_boundary",
      "usage_in_v0_4": "population, commerce, urbanization, transport, paper, administration",
      "commercial_release_ready": NONCOMMERCIAL_MARK,
      "notes": "county coordinate lineage remains non-commercial",
    },
    {
      "source_id": "PROJECT-REALM-WORLD-V0.3",
      "source_title": "Project Realm game world SQLite v0.3",
      "source_kind": "local_sqlite_parent",
      "pinned_version": "PRAGMA user_version=3",
      "local_path": str(source_database),
      "upstream_url": "",
      "checksum_algorithm": "SHA-256",
      "checksum_value": file_digest(source_database),
      "license_status": "inherits_CHGIS_noncommercial_boundary",
      "usage_in_v0_4": "SQLite inheritance and county foreign keys",
      "commercial_release_ready": NONCOMMERCIAL_MARK,
      "notes": "complete v0.3 database is locally rebuildable and Git-ignored",
    },
    {
      "source_id": "PROJECT-REALM-CULTURE-MANUAL-V0.4",
      "source_title": "Manually verified culture anchors v0.4",
      "source_kind": "manual_scholarly_anchors",
      "pinned_version": "v0.4",
      "local_path": str(manual_anchor_path),
      "upstream_url": LOGART_URL,
      "checksum_algorithm": "SHA-256",
      "checksum_value": file_digest(manual_anchor_path),
      "license_status": "citation_and_source_specific",
      "usage_in_v0_4": "Donglin academy legacy, Jianyang publishing, Qufu Kong lineage",
      "commercial_release_ready": NONCOMMERCIAL_MARK,
      "notes": "Each row contains its own source URL and license status.",
    },
    {
      "source_id": "LITERACY-CALIBRATION-V0.4",
      "source_title": "Ming literacy research boundary and conservative game calibration",
      "source_kind": "scholarly_calibration",
      "pinned_version": "v0.4 assumptions",
      "local_path": "",
      "upstream_url": LITERACY_LSE_URL,
      "checksum_algorithm": "",
      "checksum_value": "",
      "license_status": "citation_only",
      "usage_in_v0_4": "national anchors: male 18%, female 3%, classical education 2.5%",
      "commercial_release_ready": NONCOMMERCIAL_MARK,
      "notes": f"Secondary boundary reference: {LITERACY_ELMAN_URL}",
    },
  ]


def sqlite_type(column: str) -> str:
  if column.endswith("_pct"):
    return "REAL"
  if (
    column.endswith("_0_100")
    or column.endswith("_count")
    or column.endswith("_year")
    or column in {
      "snapshot_year", "cbdb_person_id", "age_1628", "begin_year", "end_year",
      "start_year", "opening_weight_0_100", "academy_dataset_id", "cbdb_inst_code",
      "cbdb_inst_name_code", "event_year", "member_count", "generation_count_est",
      "elite_member_count", "office_count_before_1628", "work_count_before_1628",
      "total_source_work_count", "independent_source_count", "manual_anchor_count",
      "unmapped_source_record_count", "verified_school_count", "verified_academy_count",
      "verified_literary_society_count", "documented_family_lineage_count",
      "notable_lineage_count", "gentry_person_count", "alive_confirmed_count_1628",
      "alive_probable_count_1628", "deceased_legacy_person_count",
      "national_level_person_count", "regional_level_person_count",
      "recent_exam_record_count", "historical_exam_record_count", "literati_person_count",
      "work_record_count_before_1628", "kinship_edge_count", "social_edge_count_before_1628",
      "population_est_1628",
    }
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
  column_sql = ",".join(f'"{column}"' for column in columns)
  placeholders = ",".join("?" for _ in columns)
  connection.executemany(
    f'INSERT INTO "{table}" ({column_sql}) VALUES ({placeholders})',
    ([row.get(column, "") for column in columns] for row in rows),
  )


def build_sqlite(
  source_database: Path,
  target_database: Path,
  baselines: Sequence[dict[str, Any]],
  overviews: Sequence[dict[str, Any]],
  institutions: Sequence[dict[str, Any]],
  manual_anchors: Sequence[dict[str, Any]],
  manual_columns: Sequence[str],
  people: Sequence[dict[str, Any]],
  associations: Sequence[dict[str, Any]],
  relationships: Sequence[dict[str, Any]],
  groups: Sequence[dict[str, Any]],
  families: Sequence[dict[str, Any]],
  family_memberships: Sequence[dict[str, Any]],
  source_manifest: Sequence[dict[str, Any]],
) -> None:
  if target_database.exists():
    target_database.unlink()
  source = sqlite3.connect(source_database)
  target = sqlite3.connect(target_database)
  source.backup(target)
  source.close()
  target.execute("PRAGMA foreign_keys=ON")
  target.execute("PRAGMA journal_mode=DELETE")
  target.execute("PRAGMA synchronous=OFF")
  for view in [
    "v_county_culture_overview", "v_county_people", "v_county_families",
    "v_county_cultural_institutions", "v_person_network_edges",
  ]:
    target.execute(f'DROP VIEW IF EXISTS "{view}"')

  create_table(
    target, "county_culture_education_baseline", BASELINE_COLUMNS, ["county_id"],
    [("county_id", "county_economy_baseline", "county_id")],
  )
  create_table(
    target, "county_culture_gameplay_overview", OVERVIEW_COLUMNS, ["county_id"],
    [("county_id", "county_economy_baseline", "county_id")],
  )
  create_table(
    target, "cultural_education_institution_anchor", INSTITUTION_COLUMNS,
    ["institution_anchor_id"], [("county_id", "county_economy_baseline", "county_id")],
  )
  create_table(
    target, "historical_culture_manual_anchors", manual_columns, ["anchor_id"],
    [("county_id", "county_economy_baseline", "county_id")],
  )
  create_table(target, "historical_person_catalog", PERSON_COLUMNS, ["person_id"])
  create_table(
    target, "person_county_association", ASSOCIATION_COLUMNS, ["association_id"],
    [
      ("person_id", "historical_person_catalog", "person_id"),
      ("county_id", "county_economy_baseline", "county_id"),
    ],
  )
  create_table(
    target, "historical_person_relationship", RELATIONSHIP_COLUMNS, ["relationship_id"],
    [
      ("from_person_id", "historical_person_catalog", "person_id"),
      ("to_person_id", "historical_person_catalog", "person_id"),
    ],
  )
  create_table(
    target, "person_group_membership", GROUP_COLUMNS, ["membership_id"],
    [
      ("person_id", "historical_person_catalog", "person_id"),
      ("county_id", "county_economy_baseline", "county_id"),
    ],
  )
  create_table(
    target, "historical_family_lineage", FAMILY_COLUMNS, ["family_id"],
    [("county_id", "county_economy_baseline", "county_id")],
  )
  create_table(
    target, "person_family_membership", FAMILY_MEMBERSHIP_COLUMNS, ["membership_id"],
    [
      ("family_id", "historical_family_lineage", "family_id"),
      ("person_id", "historical_person_catalog", "person_id"),
      ("county_id", "county_economy_baseline", "county_id"),
    ],
  )
  create_table(target, "culture_source_manifest", SOURCE_MANIFEST_COLUMNS, ["source_id"])

  insert_rows(target, "county_culture_education_baseline", BASELINE_COLUMNS, baselines)
  insert_rows(target, "county_culture_gameplay_overview", OVERVIEW_COLUMNS, overviews)
  insert_rows(target, "cultural_education_institution_anchor", INSTITUTION_COLUMNS, institutions)
  insert_rows(target, "historical_culture_manual_anchors", manual_columns, manual_anchors)
  insert_rows(target, "historical_person_catalog", PERSON_COLUMNS, people)
  insert_rows(target, "person_county_association", ASSOCIATION_COLUMNS, associations)
  insert_rows(target, "historical_person_relationship", RELATIONSHIP_COLUMNS, relationships)
  insert_rows(target, "person_group_membership", GROUP_COLUMNS, groups)
  insert_rows(target, "historical_family_lineage", FAMILY_COLUMNS, families)
  insert_rows(target, "person_family_membership", FAMILY_MEMBERSHIP_COLUMNS, family_memberships)
  insert_rows(target, "culture_source_manifest", SOURCE_MANIFEST_COLUMNS, source_manifest)

  target.execute("CREATE INDEX idx_culture_county ON county_culture_education_baseline(county_id)")
  target.execute("CREATE INDEX idx_person_primary_county ON historical_person_catalog(primary_county_id,person_id)")
  target.execute("CREATE INDEX idx_person_assoc_county ON person_county_association(county_id,person_id)")
  target.execute("CREATE INDEX idx_person_assoc_person ON person_county_association(person_id,county_id)")
  target.execute("CREATE INDEX idx_relationship_from ON historical_person_relationship(from_person_id)")
  target.execute("CREATE INDEX idx_relationship_to ON historical_person_relationship(to_person_id)")
  target.execute("CREATE INDEX idx_group_county ON person_group_membership(county_id,group_id)")
  target.execute("CREATE INDEX idx_family_county ON historical_family_lineage(county_id,family_id)")
  target.execute("CREATE INDEX idx_family_member_family ON person_family_membership(family_id,person_id)")
  target.execute("CREATE INDEX idx_institution_county ON cultural_education_institution_anchor(county_id,institution_type)")

  target.execute(
    "CREATE VIEW v_county_culture_overview AS SELECT * FROM county_culture_gameplay_overview"
  )
  target.execute(
    "CREATE VIEW v_county_people AS SELECT a.association_id,a.county_id,a.association_type_code,"
    "a.association_type_name,a.present_in_county_1628,a.opening_relevance,"
    "p.* FROM person_county_association a JOIN historical_person_catalog p USING(person_id)"
  )
  target.execute(
    "CREATE VIEW v_county_families AS SELECT f.*,COUNT(m.person_id) AS linked_member_rows "
    "FROM historical_family_lineage f LEFT JOIN person_family_membership m USING(family_id) "
    "GROUP BY f.family_id"
  )
  target.execute(
    "CREATE VIEW v_county_cultural_institutions AS SELECT * "
    "FROM cultural_education_institution_anchor"
  )
  target.execute(
    "CREATE VIEW v_person_network_edges AS SELECT r.*,"
    "a.name AS from_person_name,b.name AS to_person_name "
    "FROM historical_person_relationship r "
    "JOIN historical_person_catalog a ON a.person_id=r.from_person_id "
    "JOIN historical_person_catalog b ON b.person_id=r.to_person_id"
  )
  target.execute("PRAGMA user_version=4")
  target.execute("ANALYZE")
  target.commit()
  target.execute("VACUUM")
  target.close()


def validate_output(
  database_path: Path,
  baselines: Sequence[dict[str, Any]],
  overviews: Sequence[dict[str, Any]],
  institutions: Sequence[dict[str, Any]],
  manual_anchors: Sequence[dict[str, Any]],
  people: Sequence[dict[str, Any]],
  associations: Sequence[dict[str, Any]],
  relationships: Sequence[dict[str, Any]],
  groups: Sequence[dict[str, Any]],
  families: Sequence[dict[str, Any]],
  family_memberships: Sequence[dict[str, Any]],
  source_manifest: Sequence[dict[str, Any]],
  calibration: dict[str, Any],
  output_paths: Sequence[Path],
) -> dict[str, Any]:
  if len(baselines) != EXPECTED_COUNTIES or len(overviews) != EXPECTED_COUNTIES:
    raise RuntimeError("Both county v0.4 tables must contain exactly 1,168 rows")
  if len({row["county_id"] for row in baselines}) != EXPECTED_COUNTIES:
    raise RuntimeError("County baseline county_id values are not unique")
  for row in baselines:
    for column in BASELINE_COLUMNS:
      if column not in row or row[column] is None:
        raise RuntimeError(f"Missing baseline value: {row['county_id']} {column}")
      if column.endswith("_0_100") and not 0 <= int(row[column]) <= 100:
        raise RuntimeError(f"Index out of range: {row['county_id']} {column}={row[column]}")
  targets = {
    "population_weighted_male_mid_pct": 18.0,
    "population_weighted_female_mid_pct": 3.0,
    "population_weighted_classical_mid_pct": 2.5,
  }
  for key, target in targets.items():
    if abs(float(calibration[key]) - target) > 0.01:
      raise RuntimeError(f"Literacy calibration failed: {key}={calibration[key]}")

  if any(not row["source_ids"] or not row["name"] for row in people):
    raise RuntimeError("Every named person must have a name and at least one source id")
  if any(not row["source_id"] for row in associations):
    raise RuntimeError("Every person-county association must have a source id")
  if any(not row["source_id"] for row in relationships):
    raise RuntimeError("Every person relationship must have a source id")
  if any(not row["source_id"] or not row["source_title"] for row in institutions):
    raise RuntimeError("Every institution must have source identity and title")
  if any(not row["source_ids"] for row in families):
    raise RuntimeError("Every family row must have source evidence")
  manual_family_names = {
    row["anchor_name"] for row in manual_anchors if row["anchor_type"] == "family_lineage"
  }
  unsupported_family_names = [
    row["historical_lineage_name"] for row in families
    if row["historical_lineage_name"] and row["historical_lineage_name"] not in manual_family_names
  ]
  if unsupported_family_names:
    raise RuntimeError(f"Generated or unsupported historical family names: {unsupported_family_names[:5]}")

  baseline_by_county = {row["county_id"]: row for row in baselines}
  medians = {
    column: statistics.median(int(row[column]) for row in baselines)
    for column in [
      "literati_network_0_100", "publishing_book_culture_0_100",
      "lineage_organization_potential_0_100", "gentry_power_0_100",
    ]
  }
  wuxi = baseline_by_county["MING1628-0165"]
  jianyang = baseline_by_county["MING1628-0956"]
  qufu = baseline_by_county["MING1628-0240"]
  wuxian = baseline_by_county["MING1628-0154"]
  literacy_median = statistics.median(float(row["total_basic_literacy_mid_pct"]) for row in baselines)
  if not any(row["county_id"] == "MING1628-0165" and "东林书院" in row["institution_name"] for row in institutions):
    raise RuntimeError("Wuxi Donglin Academy anchor is missing")
  if int(wuxi["literati_network_0_100"]) <= medians["literati_network_0_100"]:
    raise RuntimeError("Wuxi literati network must exceed the national median")
  if int(jianyang["publishing_book_culture_0_100"]) <= medians["publishing_book_culture_0_100"]:
    raise RuntimeError("Jianyang publishing culture must exceed the national median")
  if not any(row["county_id"] == "MING1628-0240" and row["historical_lineage_name"] == "曲阜孔氏" for row in families):
    raise RuntimeError("Qufu Kong lineage anchor is missing")
  if int(qufu["lineage_organization_potential_0_100"]) <= medians["lineage_organization_potential_0_100"]:
    raise RuntimeError("Qufu lineage potential must exceed the national median")
  if int(qufu["gentry_power_0_100"]) <= medians["gentry_power_0_100"]:
    raise RuntimeError("Qufu gentry power must exceed the national median")
  if float(wuxian["total_basic_literacy_mid_pct"]) <= literacy_median:
    raise RuntimeError("Wu County literacy must exceed the national median")

  people_by_id = {row["person_id"]: row for row in people}
  required_people = {
    "CBDB-30598": ("MING1628-0162", 66),
    "CBDB-34252": ("MING1628-0157", 15),
    "CBDB-30713": ("MING1628-0900", 18),
    "CBDB-65721": ("MING1628-0835", 9),
  }
  for person_id, (county_id, age) in required_people.items():
    person = people_by_id.get(person_id)
    if not person:
      raise RuntimeError(f"Required source person missing: {person_id}")
    if person["primary_county_id"] != county_id or int(person["age_1628"]) != age:
      raise RuntimeError(f"Required person county/age mismatch: {person_id}")
  if people_by_id["CBDB-30598"]["alive_status_1628"] != "alive_confirmed":
    raise RuntimeError("Xu Guangqi must be alive_confirmed in 1628")
  for person_id in ["CBDB-34252", "CBDB-30713", "CBDB-65721"]:
    if int(people_by_id[person_id]["influence_1628_0_100"]) >= int(people_by_id[person_id]["historical_influence_0_100"]):
      raise RuntimeError(f"Later historical reputation leaked into 1628 influence: {person_id}")

  sparse = min(baselines, key=lambda row: (int(row["data_coverage_0_100"]), row["county_id"]))
  dense = max(baselines, key=lambda row: (int(row["data_coverage_0_100"]), row["county_id"]))
  if int(sparse["education_degree_1628_0_100"]) <= 0 or int(sparse["gentry_power_0_100"]) <= 0:
    raise RuntimeError("A sparse county incorrectly collapsed to zero structural indices")
  sparse_ratio = (
    float(sparse["male_basic_literacy_high_pct"]) - float(sparse["male_basic_literacy_low_pct"])
  ) / float(sparse["male_basic_literacy_mid_pct"])
  dense_ratio = (
    float(dense["male_basic_literacy_high_pct"]) - float(dense["male_basic_literacy_low_pct"])
  ) / float(dense["male_basic_literacy_mid_pct"])
  if sparse_ratio <= dense_ratio:
    raise RuntimeError("Sparse-county literacy uncertainty is not wider than dense-county uncertainty")

  connection = sqlite3.connect(database_path)
  connection.row_factory = sqlite3.Row
  expected_counts = {
    "county_culture_education_baseline": len(baselines),
    "county_culture_gameplay_overview": len(overviews),
    "cultural_education_institution_anchor": len(institutions),
    "historical_culture_manual_anchors": len(manual_anchors),
    "historical_person_catalog": len(people),
    "person_county_association": len(associations),
    "historical_person_relationship": len(relationships),
    "person_group_membership": len(groups),
    "historical_family_lineage": len(families),
    "person_family_membership": len(family_memberships),
    "culture_source_manifest": len(source_manifest),
  }
  sqlite_counts = {
    table: connection.execute(f'SELECT COUNT(*) FROM "{table}"').fetchone()[0]
    for table in expected_counts
  }
  if sqlite_counts != expected_counts:
    raise RuntimeError(f"CSV/SQLite row counts differ: {sqlite_counts} != {expected_counts}")
  foreign_key_errors = len(connection.execute("PRAGMA foreign_key_check").fetchall())
  user_version = connection.execute("PRAGMA user_version").fetchone()[0]
  if foreign_key_errors or user_version != 4:
    raise RuntimeError(
      f"SQLite integrity failed: foreign_keys={foreign_key_errors}, user_version={user_version}"
    )
  largest = connection.execute(
    "SELECT primary_county_id county_id,COUNT(*) n FROM historical_person_catalog "
    "GROUP BY primary_county_id ORDER BY n DESC,primary_county_id LIMIT 1"
  ).fetchone()
  started = time.perf_counter()
  result_rows = connection.execute(
    "SELECT * FROM v_county_people WHERE county_id=? ORDER BY person_id,association_id",
    (largest["county_id"],),
  ).fetchall()
  query_ms = (time.perf_counter() - started) * 1000
  if query_ms > 250:
    raise RuntimeError(f"Largest person-county query exceeded 250 ms: {query_ms:.3f}")
  connection.close()

  fingerprint = hashlib.sha256()
  output_hashes: dict[str, str] = {}
  for path in sorted(output_paths, key=lambda item: item.name):
    digest = file_digest(path)
    output_hashes[str(path)] = digest
    fingerprint.update(str(path.name).encode("utf-8"))
    fingerprint.update(digest.encode("ascii"))
  return {
    "status": "pass",
    "snapshot_year": SNAPSHOT_YEAR,
    "ruleset_version": RULESET_VERSION,
    "counts": expected_counts,
    "literacy_calibration": calibration,
    "named_data": {
      "generated_person_names": 0,
      "generated_family_names": 0,
      "generated_institution_names": 0,
      "source_backed_people": len(people),
      "source_backed_institutions": len(institutions),
    },
    "sqlite": {
      "user_version": user_version,
      "foreign_key_errors": foreign_key_errors,
      "database_size_bytes": database_path.stat().st_size,
      "sha256": file_digest(database_path),
    },
    "performance": {
      "largest_person_county_id": largest["county_id"],
      "largest_primary_person_count": largest["n"],
      "county_view_rows": len(result_rows),
      "query_ms": round(query_ms, 3),
    },
    "sparse_county_example": {
      "county_id": sparse["county_id"],
      "county": sparse["county"],
      "coverage": sparse["data_coverage_0_100"],
      "education": sparse["education_degree_1628_0_100"],
      "gentry": sparse["gentry_power_0_100"],
      "male_literacy_interval": [
        sparse["male_basic_literacy_low_pct"], sparse["male_basic_literacy_high_pct"]
      ],
    },
    "sanity_checks": {
      "wuxi_donglin_and_network_above_median": True,
      "jianyang_publishing_above_median": True,
      "qufu_kong_lineage_and_gentry_above_median": True,
      "wu_county_literacy_above_median": True,
      "required_people_age_and_county": True,
      "future_reputation_separated_from_1628": True,
    },
    "output_sha256": output_hashes,
    "deterministic_build_fingerprint": fingerprint.hexdigest(),
    "commercial_release_ready": NONCOMMERCIAL_MARK,
  }


def main() -> None:
  parser = argparse.ArgumentParser()
  parser.add_argument("--economy-dir", type=Path, default=DEFAULT_ECONOMY_DIR)
  parser.add_argument("--settlement-dir", type=Path, default=DEFAULT_SETTLEMENT_DIR)
  parser.add_argument("--input-dir", type=Path, default=DEFAULT_INPUT_DIR)
  parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT_DIR)
  parser.add_argument(
    "--manual-anchors-csv",
    type=Path,
    default=DEFAULT_OUTPUT_DIR / "historical_culture_manual_anchors_v0.4.csv",
  )
  parser.add_argument(
    "--determinism-reference-dir",
    type=Path,
    help="Optional independently built v0.4 directory whose CSV/SQLite hashes must match.",
  )
  args = parser.parse_args()
  economy_path = args.economy_dir / "county_economy_baseline_v0.2.csv"
  source_database = args.settlement_dir / "game_world_1628_v0.3.sqlite"
  cbdb_path = args.input_dir / CBDB_FILENAME
  academies_path = args.input_dir / ACADEMIES_FILENAME
  required = [economy_path, source_database, cbdb_path, academies_path, args.manual_anchors_csv]
  missing = [str(path) for path in required if not path.exists()]
  if missing:
    raise SystemExit("Missing v0.4 inputs:\n- " + "\n- ".join(missing))
  if file_digest(cbdb_path) != CBDB_SHA256:
    raise RuntimeError("Pinned CBDB SHA-256 verification failed")
  if file_digest(academies_path, "md5") != ACADEMIES_MD5:
    raise RuntimeError("Pinned Chinese Academies MD5 verification failed")

  args.output_dir.mkdir(parents=True, exist_ok=True)
  economies = sorted(read_csv(economy_path), key=lambda row: row["county_id"])
  if len(economies) != EXPECTED_COUNTIES or len({row["county_id"] for row in economies}) != EXPECTED_COUNTIES:
    raise RuntimeError("v0.4 requires exactly 1,168 unique county economy rows")
  county_by_id = {row["county_id"]: row for row in economies}
  manual_anchors = read_csv(args.manual_anchors_csv)
  if any(row["county_id"] not in county_by_id for row in manual_anchors):
    raise RuntimeError("Manual culture anchor references an unknown county_id")
  mapper = CountyMapper(economies)

  print("[v0.4] extracting pinned CBDB people, places and relationships", flush=True)
  extracted = extract_cbdb(cbdb_path, mapper, county_by_id)
  print(
    f"[v0.4] mapped {extracted['mapped_person_count']} of "
    f"{extracted['candidate_count_initial']} dated candidates",
    flush=True,
  )
  people, associations, person_lookup = build_people_and_associations(extracted)
  relationships = extracted["relationships"]
  print("[v0.4] mapping pre-1628 academies and literary societies", flush=True)
  institutions = build_institutions(
    cbdb_path, academies_path, manual_anchors, mapper, county_by_id
  )
  groups = build_groups(extracted, person_lookup, institutions)
  families, family_memberships = build_families(
    people, relationships, manual_anchors, county_by_id
  )
  globally_unmapped = extracted["candidate_count_initial"] - extracted["mapped_person_count"]
  baselines, overviews, calibration = build_county_baselines(
    economies, people, relationships, institutions, families, manual_anchors,
    globally_unmapped,
  )
  source_manifest = build_source_manifest(
    cbdb_path, academies_path, economy_path, source_database, args.manual_anchors_csv
  )

  baseline_path = args.output_dir / "county_culture_education_baseline_v0.4.csv"
  overview_path = args.output_dir / "county_culture_gameplay_overview_v0.4.csv"
  institution_path = args.output_dir / "cultural_education_institution_anchor_v0.4.csv"
  source_manifest_path = args.output_dir / "culture_source_manifest_v0.4.csv"
  database_path = args.output_dir / "game_world_1628_v0.4.sqlite"
  report_path = args.output_dir / "culture_v0.4_validation_report.json"
  temporary_database = database_path.with_suffix(database_path.suffix + ".tmp")
  generated_temporary = args.output_dir / ".generated_v0.4.tmp"
  generated_final = args.output_dir / "generated"
  if generated_temporary.exists():
    shutil.rmtree(generated_temporary)
  generated_temporary.mkdir(parents=True)

  person_path = generated_temporary / "historical_person_catalog_v0.4.csv"
  association_path = generated_temporary / "person_county_association_v0.4.csv"
  relationship_path = generated_temporary / "historical_person_relationship_v0.4.csv"
  group_path = generated_temporary / "person_group_membership_v0.4.csv"
  family_path = generated_temporary / "historical_family_lineage_v0.4.csv"
  family_membership_path = generated_temporary / "person_family_membership_v0.4.csv"

  print("[v0.4] writing deterministic CSV catalogs", flush=True)
  write_csv_atomic(baseline_path, BASELINE_COLUMNS, baselines)
  write_csv_atomic(overview_path, OVERVIEW_COLUMNS, overviews)
  write_csv_atomic(institution_path, INSTITUTION_COLUMNS, institutions)
  write_csv_atomic(source_manifest_path, SOURCE_MANIFEST_COLUMNS, source_manifest)
  write_csv_atomic(person_path, PERSON_COLUMNS, people)
  write_csv_atomic(association_path, ASSOCIATION_COLUMNS, associations)
  write_csv_atomic(relationship_path, RELATIONSHIP_COLUMNS, relationships)
  write_csv_atomic(group_path, GROUP_COLUMNS, groups)
  write_csv_atomic(family_path, FAMILY_COLUMNS, families)
  write_csv_atomic(family_membership_path, FAMILY_MEMBERSHIP_COLUMNS, family_memberships)

  manual_columns = list(manual_anchors[0].keys()) if manual_anchors else []
  print("[v0.4] inheriting v0.3 SQLite and installing v0.4 query views", flush=True)
  build_sqlite(
    source_database, temporary_database, baselines, overviews, institutions,
    manual_anchors, manual_columns, people, associations, relationships, groups,
    families, family_memberships, source_manifest,
  )
  temporary_database.replace(database_path)
  if generated_final.exists():
    shutil.rmtree(generated_final)
  generated_temporary.replace(generated_final)

  final_generated_paths = [
    generated_final / person_path.name,
    generated_final / association_path.name,
    generated_final / relationship_path.name,
    generated_final / group_path.name,
    generated_final / family_path.name,
    generated_final / family_membership_path.name,
  ]
  output_paths = [
    baseline_path, overview_path, institution_path, args.manual_anchors_csv,
    source_manifest_path, *final_generated_paths,
  ]
  print("[v0.4] running row, source, calibration, foreign-key and performance checks", flush=True)
  report = validate_output(
    database_path, baselines, overviews, institutions, manual_anchors, people,
    associations, relationships, groups, families, family_memberships,
    source_manifest, calibration, output_paths,
  )
  if args.determinism_reference_dir:
    relative_outputs = [
      Path("county_culture_education_baseline_v0.4.csv"),
      Path("county_culture_gameplay_overview_v0.4.csv"),
      Path("cultural_education_institution_anchor_v0.4.csv"),
      Path("culture_source_manifest_v0.4.csv"),
      Path("generated/historical_person_catalog_v0.4.csv"),
      Path("generated/person_county_association_v0.4.csv"),
      Path("generated/historical_person_relationship_v0.4.csv"),
      Path("generated/person_group_membership_v0.4.csv"),
      Path("generated/historical_family_lineage_v0.4.csv"),
      Path("generated/person_family_membership_v0.4.csv"),
      Path("game_world_1628_v0.4.sqlite"),
    ]
    mismatches: list[str] = []
    for relative in relative_outputs:
      current = args.output_dir / relative
      reference = args.determinism_reference_dir / relative
      if not reference.exists() or file_digest(current) != file_digest(reference):
        mismatches.append(str(relative))
    reference_report_path = args.determinism_reference_dir / "culture_v0.4_validation_report.json"
    reference_fingerprint = ""
    if reference_report_path.exists():
      with reference_report_path.open(encoding="utf-8") as stream:
        reference_fingerprint = json.load(stream).get("deterministic_build_fingerprint", "")
    if mismatches or reference_fingerprint != report["deterministic_build_fingerprint"]:
      raise RuntimeError(
        "Independent determinism comparison failed: "
        f"mismatches={mismatches}, reference_fingerprint={reference_fingerprint}"
      )
    report["determinism"] = {
      "independent_repeat_build_verified": True,
      "compared_output_count": len(relative_outputs),
      "csv_sqlite_hashes_identical": True,
      "build_fingerprint_identical": True,
      "reference_directory": str(args.determinism_reference_dir),
    }
  write_json_atomic(report_path, report)
  print(json.dumps(report, ensure_ascii=False, indent=2), flush=True)


if __name__ == "__main__":
  main()
