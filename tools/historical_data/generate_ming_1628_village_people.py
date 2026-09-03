#!/usr/bin/env python3
"""Deterministically materialize one 1628 village into households and people.

The national simulation remains county based.  This tool reads the existing
v0.4 SQLite database and expands only one requested village.  Every generated
identity is a game construct with ``historical_claim=no``; county CBDB records
only influence surname weights and never place a source person in a village.
"""

from __future__ import annotations

import argparse
from collections import Counter, defaultdict
from itertools import combinations
import hashlib
import json
import math
from pathlib import Path
import re
import sqlite3
from typing import Any, Iterable, Sequence


REPO_ROOT = Path(__file__).resolve().parents[2]
DATA_ROOT = REPO_ROOT / "docs/90_资料与归档/01_崇祯元年历史资料/data/1628"
DEFAULT_DATABASE = (
  DATA_ROOT / "7.县级文化家族乡绅教育与人物/game_world_1628_v0.4.sqlite"
)
DEFAULT_OUTPUT_DIR = DATA_ROOT / "8.村庄家庭与人物关系"
RULESET_VERSION = "v0.5"
SCHEMA_VERSION = "village_people_v0.5"
DEFAULT_WORLD_SEED = "project-realm-1628"
SNAPSHOT_YEAR = 1628
WEIGHT_TOTAL = 1_000_000


# These are transparent game priors, not a claim about a 1628 surname census.
# County source counts are blended into them at generation time.
GENERAL_SURNAME_PRIOR = [
  ("王", 100), ("李", 96), ("张", 92), ("刘", 87), ("陈", 82),
  ("杨", 75), ("赵", 70), ("黄", 68), ("周", 65), ("吴", 63),
  ("徐", 58), ("孙", 56), ("胡", 52), ("朱", 50), ("高", 48),
  ("林", 45), ("何", 44), ("郭", 42), ("马", 40), ("罗", 38),
  ("梁", 36), ("宋", 34), ("郑", 33), ("谢", 31), ("韩", 30),
  ("唐", 29), ("冯", 27), ("于", 26), ("董", 25), ("萧", 23),
  ("程", 22), ("曹", 21), ("袁", 20), ("邓", 19), ("许", 18),
  ("傅", 17), ("沈", 16), ("曾", 15), ("彭", 14), ("吕", 13),
  ("苏", 12), ("卢", 11), ("蒋", 10), ("蔡", 10), ("汪", 9),
  ("魏", 9), ("钱", 8), ("戴", 8), ("顾", 7), ("翟", 5),
]
COMMON_SURNAMES = {surname for surname, _ in GENERAL_SURNAME_PRIOR}

HOUSEHOLD_SIZE_PRIOR = [
  (1, 2), (2, 5), (3, 9), (4, 14), (5, 18),
  (6, 19), (7, 15), (8, 10), (9, 5), (10, 3),
]

TRADITIONAL_SURNAME_TO_SIMPLIFIED = str.maketrans(
  {
    "劉": "刘", "趙": "赵", "錢": "钱", "孫": "孙", "吳": "吴",
    "鄭": "郑", "馮": "冯", "陳": "陈", "衛": "卫", "蔣": "蒋",
    "韓": "韩", "楊": "杨", "許": "许", "呂": "吕", "張": "张",
    "嚴": "严", "華": "华", "謝": "谢", "鄒": "邹", "竇": "窦",
    "蘇": "苏", "葛": "葛", "範": "范", "魯": "鲁", "韋": "韦",
    "馬": "马", "鳳": "凤", "費": "费", "薛": "薛", "賀": "贺",
    "羅": "罗", "畢": "毕", "郝": "郝", "樂": "乐", "傅": "傅",
    "齊": "齐", "鮑": "鲍", "湯": "汤", "鄔": "邬", "顧": "顾",
    "黃": "黄", "蕭": "萧", "貝": "贝", "臧": "臧", "龐": "庞",
    "紀": "纪", "項": "项", "藍": "蓝", "閔": "闵", "強": "强",
    "賈": "贾", "盧": "卢", "裘": "裘", "應": "应", "鄧": "邓",
    "諸": "诸", "鈕": "钮", "龔": "龚", "陸": "陆", "榮": "荣",
    "儲": "储", "靳": "靳", "車": "车", "欒": "栾", "厲": "厉",
    "龍": "龙", "葉": "叶", "韶": "韶", "薊": "蓟", "賴": "赖",
    "藺": "蔺", "喬": "乔", "聞": "闻", "譚": "谭", "勞": "劳",
    "郦": "郦", "邊": "边", "閻": "阎", "連": "连", "廖": "廖",
    "終": "终", "滿": "满", "廣": "广", "祿": "禄", "聶": "聂",
    "簡": "简", "饒": "饶", "關": "关", "權": "权", "蓋": "盖",
  }
)
# Fixed expansion derived from every surname character present in the pinned
# v0.4 CBDB person/family catalogs.  Source spellings remain available in the
# source tables; generated display names always pass through this map.
TRADITIONAL_SURNAME_TO_SIMPLIFIED.update(str.maketrans({
  "來": "来", "倫": "伦", "儀": "仪", "儲": "储", "劉": "刘",
  "勞": "劳", "勵": "励", "區": "区", "卻": "却", "厲": "厉",
  "叢": "丛", "吳": "吴", "呂": "吕", "喬": "乔", "單": "单",
  "嘗": "尝", "嚴": "严", "國": "国", "堯": "尧", "塗": "涂",
  "壽": "寿", "婁": "娄", "孫": "孙", "宮": "宫", "寧": "宁",
  "帥": "帅", "師": "师", "張": "张", "強": "强", "後": "后",
  "惲": "恽", "應": "应", "懷": "怀", "揚": "扬", "於": "于",
  "時": "时", "晉": "晋", "暢": "畅", "會": "会", "楊": "杨",
  "榮": "荣", "樂": "乐", "樓": "楼", "欽": "钦", "歐": "欧",
  "歸": "归", "況": "况", "湯": "汤", "溫": "温", "滿": "满",
  "潛": "潜", "烏": "乌", "無": "无", "爾": "尔", "獨": "独",
  "甕": "瓮", "畢": "毕", "盧": "卢", "眞": "真", "祿": "禄",
  "稅": "税", "竇": "窦", "簡": "简", "紀": "纪", "綽": "绰",
  "練": "练", "繆": "缪", "續": "续", "羅": "罗", "習": "习",
  "聞": "闻", "聶": "聂", "荊": "荆", "莊": "庄", "華": "华",
  "萇": "苌", "萬": "万", "葉": "叶", "蓋": "盖", "蔣": "蒋",
  "蕭": "萧", "薩": "萨", "藍": "蓝", "藺": "蔺", "蘇": "苏",
  "衛": "卫", "襲": "袭", "計": "计", "許": "许", "談": "谈",
  "諶": "谌", "諸": "诸", "謝": "谢", "譙": "谯", "譚": "谭",
  "豐": "丰", "貝": "贝", "貢": "贡", "貴": "贵", "費": "费",
  "賀": "贺", "賈": "贾", "賴": "赖", "趙": "赵", "車": "车",
  "軒": "轩", "連": "连", "過": "过", "達": "达", "遲": "迟",
  "邊": "边", "郟": "郏", "鄒": "邹", "鄔": "邬", "鄧": "邓",
  "鄭": "郑", "鄺": "邝", "釋": "释", "鈕": "钮", "錢": "钱",
  "鍾": "钟", "鎖": "锁", "鐘": "钟", "門": "门", "閃": "闪",
  "開": "开", "閔": "闵", "閭": "闾", "閻": "阎", "闕": "阙",
  "關": "关", "闞": "阚", "陰": "阴", "陳": "陈", "陸": "陆",
  "陽": "阳", "隨": "随", "雙": "双", "鞏": "巩", "韋": "韦",
  "韓": "韩", "項": "项", "順": "顺", "須": "须", "頡": "颉",
  "顏": "颜", "顔": "颜", "顧": "顾", "饒": "饶", "馬": "马",
  "馮": "冯", "駱": "骆", "魚": "鱼", "魯": "鲁", "鮑": "鲍",
  "鮮": "鲜", "麥": "麦", "黃": "黄", "齊": "齐", "龍": "龙",
  "龐": "庞", "龔": "龚",
}))

MALE_PERSONAL_CHARS = list(
  "安邦昌成德福贵和嘉健杰良民明宁平庆荣瑞盛顺泰廷文武贤祥兴义勇正志忠"
)
FEMALE_PERSONAL_CHARS = list(
  "安春翠娥芳桂荷慧兰莲梅宁佩巧秋如淑桃婉香秀英玉月贞珠"
)
SCHOLARLY_CHARS = list(
  "伯承崇道德弘济景敬礼明谦仁儒士守思廷文修彦义元正宗"
)
GENERATION_CHARS = list(
  "世德文宗承继守永正明廷国安昌兴荣福祥仁义礼智信"
)
COURTESY_PREFIXES = ["子", "伯", "仲", "叔", "季", "景", "士", "公", "元", "允"]
COURTESY_SUFFIXES = ["安", "达", "德", "和", "明", "谦", "仁", "文", "修", "正", "远", "直"]


OCCUPATION_LABELS = {
  "grain_farmer": "粮农",
  "vegetable_gardener": "菜农",
  "tenant_farmer": "佃农",
  "textile_household": "纺织户",
  "fisher": "渔户",
  "woodcutter": "樵户",
  "charcoal_burner": "烧炭户",
  "mason": "砖瓦石作户",
  "carrier": "车脚运输户",
  "market_vendor": "集市商贩",
  "food_processor": "粮食加工户",
  "craft_household": "手工业户",
  "pastoral_household": "牧户",
  "salt_worker": "盐业户",
  "miner": "矿冶户",
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
    raise ValueError(f"Invalid deterministic range {low}..{high}")
  return low + int(stable_unit(*parts) * (high - low + 1)) % (high - low + 1)


def stable_choice(values: Sequence[Any], *parts: Any) -> Any:
  if not values:
    raise ValueError("Cannot choose from an empty sequence")
  return values[stable_int(0, len(values) - 1, *parts)]


def weighted_choice(weighted_values: Sequence[tuple[Any, float]], *parts: Any) -> Any:
  candidates = [(value, max(0.0, float(weight))) for value, weight in weighted_values]
  total = sum(weight for _, weight in candidates)
  if total <= 0:
    raise ValueError("Weighted choice requires a positive total")
  marker = stable_unit(*parts) * total
  cumulative = 0.0
  for value, weight in candidates:
    cumulative += weight
    if marker <= cumulative:
      return value
  return candidates[-1][0]


def allocate_exact(total: int, weights: Sequence[float], minimum: int = 0) -> list[int]:
  if not weights:
    return []
  if minimum * len(weights) > total:
    raise ValueError("Minimum allocation exceeds total")
  positive = [max(0.0, float(weight)) for weight in weights]
  weight_sum = sum(positive)
  if weight_sum <= 0:
    positive = [1.0] * len(weights)
    weight_sum = float(len(weights))
  remaining = total - minimum * len(weights)
  exact = [remaining * weight / weight_sum for weight in positive]
  result = [minimum + math.floor(value) for value in exact]
  remainder = total - sum(result)
  order = sorted(
    range(len(weights)),
    key=lambda index: (-(exact[index] - math.floor(exact[index])), index),
  )
  for index in order[:remainder]:
    result[index] += 1
  if sum(result) != total:
    raise RuntimeError("Exact allocation failed")
  return result


def file_sha256(path: Path) -> str:
  digest = hashlib.sha256()
  with path.open("rb") as stream:
    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
      digest.update(chunk)
  return digest.hexdigest()


def safe_filename(value: str) -> str:
  clean = re.sub(r"[^\w\-\u3400-\u9fff]+", "_", value, flags=re.UNICODE).strip("_")
  return clean or "village"


def write_text_atomic(path: Path, text: str) -> None:
  path.parent.mkdir(parents=True, exist_ok=True)
  temporary = path.with_suffix(path.suffix + ".tmp")
  with temporary.open("w", encoding="utf-8", newline="\n") as stream:
    stream.write(text)
  temporary.replace(path)


def write_json_atomic(path: Path, value: Any) -> None:
  text = json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n"
  write_text_atomic(path, text)


def rows_as_dicts(cursor: sqlite3.Cursor) -> list[dict[str, Any]]:
  return [dict(row) for row in cursor.fetchall()]


def load_source_bundle(
  database: Path,
  village_id: str | None,
  village_name: str | None,
  county_id: str | None,
) -> dict[str, Any]:
  if not database.exists():
    raise SystemExit(
      f"Missing v0.4 SQLite database: {database}\n"
      "Run tools/historical_data/build_ming_1628_culture.py first."
    )
  connection = sqlite3.connect(f"file:{database}?mode=ro", uri=True)
  connection.row_factory = sqlite3.Row
  try:
    user_version = connection.execute("PRAGMA user_version").fetchone()[0]
    if user_version != 4:
      raise RuntimeError(f"Village people v0.5 requires SQLite user_version=4, got {user_version}")
    if village_id:
      villages = rows_as_dicts(
        connection.execute(
          "SELECT * FROM v_county_entry_villages WHERE village_id=?", (village_id,)
        )
      )
    elif village_name:
      if county_id:
        villages = rows_as_dicts(
          connection.execute(
            "SELECT * FROM v_county_entry_villages WHERE village_name=? AND county_id=?",
            (village_name, county_id),
          )
        )
      else:
        villages = rows_as_dicts(
          connection.execute(
            "SELECT * FROM v_county_entry_villages WHERE village_name=? ORDER BY county_id,village_id",
            (village_name,),
          )
        )
    else:
      raise SystemExit("Specify --village-id or --village-name")
    if not villages:
      raise SystemExit("No matching village was found")
    if len(villages) > 1:
      matches = ", ".join(
        f"{row['county']}:{row['village_id']}" for row in villages[:12]
      )
      raise SystemExit(f"Village name is ambiguous; add --county-id. Matches: {matches}")
    village = villages[0]
    resolved_county_id = village["county_id"]
    subregion = dict(
      connection.execute(
        "SELECT * FROM county_subregion_definition WHERE subregion_id=?",
        (village["subregion_id"],),
      ).fetchone()
    )
    economy = dict(
      connection.execute(
        "SELECT * FROM county_economy_baseline WHERE county_id=?",
        (resolved_county_id,),
      ).fetchone()
    )
    culture = dict(
      connection.execute(
        "SELECT * FROM county_culture_education_baseline WHERE county_id=?",
        (resolved_county_id,),
      ).fetchone()
    )
    settlement = dict(
      connection.execute(
        "SELECT * FROM county_settlement_summary WHERE county_id=?",
        (resolved_county_id,),
      ).fetchone()
    )
    source_surnames = rows_as_dicts(
      connection.execute(
        "SELECT surname,COUNT(*) AS person_count FROM historical_person_catalog "
        "WHERE primary_county_id=? AND surname<>'' GROUP BY surname ORDER BY surname",
        (resolved_county_id,),
      )
    )
    source_families = rows_as_dicts(
      connection.execute(
        "SELECT surname,SUM(member_count) AS member_count,COUNT(*) AS branch_count,"
        "SUM(CASE WHEN is_notable_lineage='yes' THEN 1 ELSE 0 END) AS notable_count "
        "FROM historical_family_lineage WHERE county_id=? GROUP BY surname ORDER BY surname",
        (resolved_county_id,),
      )
    )
  finally:
    connection.close()
  return {
    "source_database_name": database.name,
    "database_user_version": user_version,
    "village": village,
    "subregion": subregion,
    "economy": economy,
    "culture": culture,
    "settlement": settlement,
    "source_surnames": source_surnames,
    "source_families": source_families,
  }


def simplified_surname(value: str) -> str:
  return str(value or "").translate(TRADITIONAL_SURNAME_TO_SIMPLIFIED).strip()


def village_surname_hint(village_name: str) -> str:
  match = re.match(r"^(.{1,2})家(?:村|庄|屯|堡|寨|沟|湾|浜|浦|圩|台|原|店|坞|里)$", village_name)
  if not match:
    return ""
  root = simplified_surname(match.group(1))
  return root if root in COMMON_SURNAMES else ""


def build_surname_model(source: dict[str, Any]) -> list[dict[str, Any]]:
  prior = dict(GENERAL_SURNAME_PRIOR)
  people_counts: Counter[str] = Counter()
  family_counts: Counter[str] = Counter()
  for row in source["source_surnames"]:
    surname = simplified_surname(row["surname"])
    if surname:
      people_counts[surname] += int(row["person_count"] or 0)
  for row in source["source_families"]:
    surname = simplified_surname(row["surname"])
    if surname:
      family_counts[surname] += (
        int(row["member_count"] or 0)
        + int(row["branch_count"] or 0) * 2
        + int(row["notable_count"] or 0) * 3
      )
  names = set(prior) | set(people_counts) | set(family_counts)
  max_people = max(people_counts.values(), default=1)
  max_family = max(family_counts.values(), default=1)
  coverage = int(source["culture"]["data_coverage_0_100"])
  lineage = int(source["culture"]["lineage_organization_potential_0_100"])
  hint = village_surname_hint(source["village"]["village_name"])
  rows: list[dict[str, Any]] = []
  for surname in sorted(names):
    prior_score = float(prior.get(surname, 5))
    people_score = 100.0 * people_counts[surname] / max_people
    family_score = 100.0 * family_counts[surname] / max_family
    evidence_mix = 0.20 + 0.25 * coverage / 100.0
    weight = prior_score * (1.0 - evidence_mix) + (
      people_score * 0.65 + family_score * 0.35
    ) * evidence_mix
    if people_counts[surname] or family_counts[surname]:
      weight *= 1.0 + lineage / 250.0
    if hint and surname == hint:
      weight *= 3.0 + lineage / 40.0
    rows.append(
      {
        "surname_zh_hans": surname,
        "generation_weight": round(weight, 6),
        "county_source_person_count": people_counts[surname],
        "county_source_family_weight": family_counts[surname],
        "village_name_hint": "yes" if surname == hint else "no",
        "historical_village_lineage_claim": "no",
      }
    )
  rows.sort(key=lambda row: (-row["generation_weight"], row["surname_zh_hans"]))
  return rows[:60]


def household_occupation_weights(source: dict[str, Any]) -> list[tuple[str, float]]:
  subregion = source["subregion"]
  economy = source["economy"]
  agriculture = int(subregion["agriculture_resource_0_100"])
  forest = int(subregion["forest_resource_0_100"])
  pasture = int(subregion["pasture_resource_0_100"])
  fishery = int(subregion["fishery_resource_0_100"])
  salt = int(subregion["salt_resource_0_100"])
  fuel = int(subregion["fuel_resource_0_100"])
  metal = int(subregion["metal_resource_0_100"])
  building = int(subregion["building_material_resource_0_100"])
  market = int(economy["local_market_0_100"])
  transport = int(economy["transport_access_0_100"])
  return [
    ("grain_farmer", max(18, agriculture * 1.80)),
    ("vegetable_gardener", max(4, agriculture * 0.30 + market * 0.15)),
    ("textile_household", max(3, int(economy["textile_initial_1628_0_100"]) * 0.55)),
    ("fisher", fishery * 0.75),
    ("woodcutter", forest * 0.70),
    ("charcoal_burner", fuel * 0.35),
    ("mason", building * 0.45),
    ("carrier", transport * 0.40),
    ("market_vendor", market * 0.38),
    ("food_processor", int(economy["salt_food_initial_1628_0_100"]) * 0.35),
    ("craft_household", int(economy["industrial_initial_1628_0_100"]) * 0.28),
    ("pastoral_household", pasture * 0.75),
    ("salt_worker", salt * 0.80),
    ("miner", metal * 0.75),
  ]


def build_households(
  source: dict[str, Any], surname_model: Sequence[dict[str, Any]], world_seed: str
) -> list[dict[str, Any]]:
  village = source["village"]
  economy = source["economy"]
  culture = source["culture"]
  population = int(village["projected_rural_population"])
  county_household_size = float(economy["population_est_1628"]) / max(
    1, int(economy["household_count_est"])
  )
  household_count = max(1, round(population / county_household_size))
  sizes = [
    weighted_choice(
      HOUSEHOLD_SIZE_PRIOR,
      world_seed,
      village["village_id"],
      "household-size",
      index,
    )
    for index in range(1, household_count + 1)
  ]
  delta = population - sum(sizes)
  adjustment_round = 0
  while delta:
    direction = 1 if delta > 0 else -1
    eligible = [
      index for index, size in enumerate(sizes)
      if (direction > 0 and size < 12) or (direction < 0 and size > 1)
    ]
    eligible.sort(
      key=lambda index: stable_unit(
        world_seed,
        village["village_id"],
        "household-size-adjust",
        adjustment_round,
        index,
      )
    )
    if not eligible:
      raise RuntimeError("Unable to reconcile village household sizes")
    for index in eligible:
      if delta == 0:
        break
      sizes[index] += direction
      delta -= direction
    adjustment_round += 1
  surname_weights = [
    (row["surname_zh_hans"], row["generation_weight"]) for row in surname_model
  ]
  occupation_weights = household_occupation_weights(source)
  households: list[dict[str, Any]] = []
  for ordinal, size in enumerate(sizes, 1):
    household_id = f"{village['village_id']}-H{ordinal:03d}"
    surname = weighted_choice(
      surname_weights, world_seed, village["village_id"], household_id, "surname"
    )
    occupation_code = weighted_choice(
      occupation_weights, world_seed, village["village_id"], household_id, "occupation"
    )
    wealth_jitter = -28 + 56 * stable_unit(world_seed, household_id, "wealth")
    wealth = round(
      clamp(
        24
        + int(source["subregion"]["agriculture_resource_0_100"]) * 0.22
        + int(economy["local_market_0_100"]) * 0.20
        + int(source["subregion"]["building_material_resource_0_100"]) * 0.08
        + wealth_jitter,
        5,
        95,
      )
    )
    households.append(
      {
        "household_id": household_id,
        "village_id": village["village_id"],
        "ordinal": ordinal,
        "household_size": size,
        "household_surname_zh_hans": surname,
        "clan_id": "",
        "household_type": "",
        "primary_occupation_code": occupation_code,
        "primary_occupation": OCCUPATION_LABELS[occupation_code],
        "wealth_index_0_100": wealth,
        "social_stratum": "",
        "population_share_ppm": 0,
        "farmland_share_ppm": 0,
        "relative_x_0_10000": 0,
        "relative_y_0_10000": 0,
        "historical_claim": "no",
        "generation_method": "deterministic household projection v0.5",
      }
    )

  ranked = sorted(households, key=lambda row: (-row["wealth_index_0_100"], row["household_id"]))
  gentry_count = max(1, round(household_count * (0.01 + int(culture["gentry_power_0_100"]) / 2500)))
  proprietor_limit = max(gentry_count + 1, round(household_count * 0.18))
  tenant_start = round(household_count * 0.76)
  for rank, household in enumerate(ranked):
    if rank < gentry_count:
      household["social_stratum"] = "地方富户/乡绅结构代理"
    elif rank < proprietor_limit:
      household["social_stratum"] = "自耕富农或业主"
    elif rank >= tenant_start:
      household["social_stratum"] = "佃户或雇工户"
      if household["primary_occupation_code"] == "grain_farmer":
        household["primary_occupation_code"] = "tenant_farmer"
        household["primary_occupation"] = OCCUPATION_LABELS["tenant_farmer"]
    else:
      household["social_stratum"] = "普通自耕或手工业户"

  by_surname: dict[str, list[dict[str, Any]]] = defaultdict(list)
  for household in households:
    by_surname[household["household_surname_zh_hans"]].append(household)
  lineage = int(culture["lineage_organization_potential_0_100"])
  clan_ordinal = 0
  clan_centers: dict[str, tuple[int, int]] = {}
  for surname in sorted(by_surname):
    members = sorted(
      by_surname[surname],
      key=lambda row: stable_unit(world_seed, village["village_id"], surname, row["household_id"], "clan-order"),
    )
    cursor = 0
    while cursor < len(members):
      clan_ordinal += 1
      maximum = max(2, 2 + round(lineage / 18))
      chunk_size = stable_int(1, maximum, world_seed, village["village_id"], surname, cursor, "clan-size")
      chunk = members[cursor:cursor + chunk_size]
      clan_id = f"{village['village_id']}-C{clan_ordinal:03d}"
      center = (
        stable_int(1200, 8800, world_seed, clan_id, "center-x"),
        stable_int(1200, 8800, world_seed, clan_id, "center-y"),
      )
      clan_centers[clan_id] = center
      for household in chunk:
        household["clan_id"] = clan_id
      cursor += len(chunk)

  for household in households:
    center_x, center_y = clan_centers[household["clan_id"]]
    angle = stable_unit(world_seed, household["household_id"], "house-angle") * math.tau
    radius = 180 + 950 * math.sqrt(stable_unit(world_seed, household["household_id"], "house-radius"))
    household["relative_x_0_10000"] = round(clamp(center_x + math.cos(angle) * radius, 100, 9900))
    household["relative_y_0_10000"] = round(clamp(center_y + math.sin(angle) * radius, 100, 9900))

  population_shares = allocate_exact(
    WEIGHT_TOTAL, [household["household_size"] for household in households]
  )
  land_weights = []
  for household in households:
    multiplier = {
      "地方富户/乡绅结构代理": 3.8,
      "自耕富农或业主": 2.1,
      "普通自耕或手工业户": 1.0,
      "佃户或雇工户": 0.25,
    }[household["social_stratum"]]
    if household["primary_occupation_code"] not in {
      "grain_farmer", "tenant_farmer", "vegetable_gardener", "pastoral_household"
    }:
      multiplier *= 0.55
    land_weights.append(max(0.05, multiplier * (0.7 + household["wealth_index_0_100"] / 100)))
  land_shares = allocate_exact(WEIGHT_TOTAL, land_weights)
  for household, population_share, land_share in zip(households, population_shares, land_shares):
    household["population_share_ppm"] = population_share
    household["farmland_share_ppm"] = land_share
  return households


def make_member(
  role: str,
  sex: str,
  age: int,
  generation: int | None,
  lineage_member: bool,
  surname_mode: str,
) -> dict[str, Any]:
  return {
    "household_role": role,
    "sex": sex,
    "age_1628": int(clamp(age, 0, 85)),
    "lineage_generation": generation,
    "lineage_member": lineage_member,
    "surname_mode": surname_mode,
  }


def spaced_child_ages(count: int, maximum_oldest_age: int, *key: Any) -> list[int]:
  if count <= 0:
    return []
  gaps = [stable_int(1, 3, *key, "gap", index) for index in range(1, count)]
  minimum_oldest = sum(gaps)
  maximum = max(minimum_oldest, maximum_oldest_age)
  oldest = stable_int(minimum_oldest, maximum, *key, "oldest")
  ages = [oldest]
  for gap in gaps:
    ages.append(ages[-1] - gap)
  return ages


def build_household_member_blueprint(
  household: dict[str, Any], world_seed: str
) -> tuple[str, list[dict[str, Any]], list[tuple[str, int, int]]]:
  """Return household type, member rows and primitive local-index relations."""
  size = int(household["household_size"])
  key = (world_seed, household["household_id"], "topology")
  members: list[dict[str, Any]] = []
  relations: list[tuple[str, int, int]] = []

  def add(member: dict[str, Any]) -> int:
    members.append(member)
    return len(members) - 1

  def couple(first: int, second: int) -> None:
    relations.append(("spouse", first, second))

  def parents(parent_indexes: Iterable[int], child_indexes: Iterable[int]) -> None:
    for parent_index in parent_indexes:
      for child_index in child_indexes:
        relations.append(("parent_child", parent_index, child_index))

  if size == 1:
    sex = "male" if stable_unit(*key, "sex") < 0.62 else "female"
    age = stable_int(18, 78, *key, "age")
    add(make_member("户主", sex, age, 1, True, "household"))
    return "单人户", members, relations

  if size == 2:
    mode = stable_unit(*key, "mode")
    if mode < 0.58:
      male_age = stable_int(20, 61, *key, "male-age")
      female_age = int(clamp(male_age + stable_int(-6, 3, *key, "female-delta"), 18, 60))
      head = add(make_member("户主", "male", male_age, 1, True, "household"))
      spouse = add(make_member("配偶", "female", female_age, None, False, "marriage_in"))
      couple(head, spouse)
      return "夫妇户", members, relations
    if mode < 0.82:
      parent_sex = "male" if stable_unit(*key, "parent-sex") < 0.45 else "female"
      parent_age = stable_int(34, 68, *key, "parent-age")
      child_age = stable_int(5, max(5, min(30, parent_age - 17)), *key, "child-age")
      parent = add(make_member("户主", parent_sex, parent_age, 1, True, "household"))
      child_sex = "male" if stable_unit(*key, "child-sex") < 0.51 else "female"
      child = add(make_member("子女", child_sex, child_age, 2, True, "household"))
      parents([parent], [child])
      return "单亲户", members, relations
    older_age = stable_int(18, 68, *key, "older-age")
    younger_age = max(8, older_age - stable_int(1, min(12, max(1, older_age - 8)), *key, "gap"))
    first_sex = "male" if stable_unit(*key, "first-sex") < 0.51 else "female"
    second_sex = "male" if stable_unit(*key, "second-sex") < 0.51 else "female"
    add(make_member("户主", first_sex, older_age, 1, True, "household"))
    add(make_member("同胞", second_sex, younger_age, 1, True, "household"))
    relations.append(("sibling", 0, 1))
    return "同胞户", members, relations

  if size <= 5:
    single_parent = stable_unit(*key, "single-parent") < 0.14
    child_count = size - (1 if single_parent else 2)
    child_ages = spaced_child_ages(
      child_count, min(23, 5 + child_count * 4), *key, "children"
    )
    oldest_child_age = child_ages[0]
    head_age = max(24, oldest_child_age + stable_int(18, 28, *key, "head-gap"))
    head_sex = (
      "female" if single_parent and stable_unit(*key, "single-sex") < 0.58 else "male"
    )
    head = add(make_member("户主", head_sex, head_age, 1, True, "household"))
    parent_indexes = [head]
    if not single_parent:
      spouse_age = int(
        clamp(
          max(
            oldest_child_age + 15,
            head_age + stable_int(-6, 3, *key, "spouse-delta"),
          ),
          18,
          58,
        )
      )
      spouse = add(make_member("配偶", "female", spouse_age, None, False, "marriage_in"))
      couple(head, spouse)
      parent_indexes.append(spouse)
    child_indexes = []
    for index, age in enumerate(child_ages):
      sex = "male" if stable_unit(*key, "child-sex", index) < 0.51 else "female"
      child_indexes.append(add(make_member("子女", sex, age, 2, True, "household")))
    parents(parent_indexes, child_indexes)
    return ("单亲子女户" if single_parent else "核心家庭"), members, relations

  if size <= 7:
    elder_count = 2 if size == 7 and stable_unit(*key, "elder-count") < 0.62 else 1
    child_count = size - elder_count - 2
    child_ages = spaced_child_ages(
      child_count, min(22, 7 + child_count * 3), *key, "children"
    )
    oldest_child_age = child_ages[0]
    head_age = max(29, oldest_child_age + stable_int(18, 25, *key, "head-gap"))
    elder_age = int(clamp(head_age + stable_int(20, 30, *key, "elder-gap"), 52, 84))
    elder_indexes = []
    if elder_count == 2:
      elder_father = add(make_member("长辈", "male", elder_age, 0, True, "household"))
      elder_indexes.append(elder_father)
      elder_mother_age = int(
        clamp(
          max(
            head_age + 15,
            elder_age + stable_int(-7, 1, *key, "elder-mother-delta"),
          ),
          48,
          82,
        )
      )
      elder_mother = add(make_member("长辈配偶", "female", elder_mother_age, None, False, "marriage_in"))
      elder_indexes.append(elder_mother)
      couple(elder_father, elder_mother)
    else:
      elder_sex = "male" if stable_unit(*key, "single-elder-sex") < 0.44 else "female"
      elder_indexes.append(
        add(
          make_member(
            "长辈",
            elder_sex,
            elder_age,
            0 if elder_sex == "male" else None,
            elder_sex == "male",
            "household" if elder_sex == "male" else "marriage_in",
          )
        )
      )
    head = add(make_member("户主", "male", head_age, 1, True, "household"))
    spouse_age = int(
      clamp(
        max(
          oldest_child_age + 15,
          head_age + stable_int(-6, 3, *key, "spouse-delta"),
        ),
        18,
        56,
      )
    )
    spouse = add(make_member("配偶", "female", spouse_age, None, False, "marriage_in"))
    couple(head, spouse)
    parents(elder_indexes, [head])
    child_indexes = []
    for index, age in enumerate(child_ages):
      sex = "male" if stable_unit(*key, "child-sex", index) < 0.51 else "female"
      child_indexes.append(add(make_member("子女", sex, age, 2, True, "household")))
    parents([head, spouse], child_indexes)
    return "三代直系家庭", members, relations

  grandchild_count = size - 4
  adult_son_minimum_age = max(24, 18 + 3 * max(0, grandchild_count - 1))
  adult_son_age = stable_int(adult_son_minimum_age, max(37, adult_son_minimum_age), *key, "adult-son-age")
  elder_age = int(clamp(adult_son_age + stable_int(20, 30, *key, "elder-gap"), 48, 72))
  elder = add(make_member("户主", "male", elder_age, 0, True, "household"))
  elder_spouse_age = int(
    clamp(
      max(
        adult_son_age + 15,
        elder_age + stable_int(-7, 1, *key, "elder-spouse-delta"),
      ),
      42,
      70,
    )
  )
  elder_spouse = add(make_member("配偶", "female", elder_spouse_age, None, False, "marriage_in"))
  couple(elder, elder_spouse)
  son = add(make_member("已婚子", "male", adult_son_age, 1, True, "household"))
  grandchild_ages = spaced_child_ages(
    grandchild_count,
    min(17, max(1, adult_son_age - 18)),
    *key,
    "grandchildren",
  )
  oldest_grandchild_age = grandchild_ages[0]
  daughter_in_law_age = int(
    clamp(
      max(
        oldest_grandchild_age + 15,
        adult_son_age + stable_int(-5, 2, *key, "daughter-in-law-delta"),
      ),
      18,
      38,
    )
  )
  daughter_in_law = add(make_member("子媳", "female", daughter_in_law_age, None, False, "marriage_in"))
  couple(son, daughter_in_law)
  parents([elder, elder_spouse], [son])
  grandchildren = []
  for index, age in enumerate(grandchild_ages):
    sex = "male" if stable_unit(*key, "grandchild-sex", index) < 0.51 else "female"
    grandchildren.append(add(make_member("孙辈", sex, age, 2, True, "household")))
  parents([son, daughter_in_law], grandchildren)
  return "多代联合家庭", members, relations


def assign_people_and_kin(
  households: list[dict[str, Any]], source: dict[str, Any], surname_model: Sequence[dict[str, Any]], world_seed: str
) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
  village = source["village"]
  culture = source["culture"]
  surname_weights = [
    (row["surname_zh_hans"], row["generation_weight"]) for row in surname_model
  ]
  people: list[dict[str, Any]] = []
  primitive_blueprints: list[tuple[str, str, str, str]] = []
  household_people: dict[str, list[dict[str, Any]]] = {}
  for household in households:
    household_type, members, local_relations = build_household_member_blueprint(household, world_seed)
    household["household_type"] = household_type
    local_people: list[dict[str, Any]] = []
    for local_ordinal, member in enumerate(members, 1):
      person_id = f"{household['household_id']}-P{local_ordinal:02d}"
      surname = household["household_surname_zh_hans"]
      if member["surname_mode"] == "marriage_in":
        for attempt in range(16):
          candidate = weighted_choice(
            surname_weights, world_seed, person_id, "natal-surname", attempt
          )
          if candidate != household["household_surname_zh_hans"]:
            surname = candidate
            break
      age = int(member["age_1628"])
      person = {
        "person_id": person_id,
        "village_id": village["village_id"],
        "household_id": household["household_id"],
        "clan_id": household["clan_id"] if member["lineage_member"] else "",
        "household_role": member["household_role"],
        "surname_zh_hans": surname,
        "given_name_zh_hans": "",
        "name_zh_hans": "",
        "record_style_name": "",
        "courtesy_name_zh_hans": "",
        "sex": member["sex"],
        "birth_year_est": SNAPSHOT_YEAR - age,
        "age_1628": age,
        "life_stage_1628": (
          "幼儿" if age < 7 else "儿童" if age < 13 else "少年" if age < 18
          else "成年" if age < 60 else "老年"
        ),
        "lineage_generation": member["lineage_generation"],
        "is_literate": "no",
        "is_classically_educated": "no",
        "primary_occupation": "",
        "social_roles": [],
        "is_core_npc": "no",
        "historical_claim": "no",
        "name_source_type": "generated_period_style",
        "generation_method": "deterministic person materialization v0.5",
      }
      people.append(person)
      local_people.append(person)
    household_people[household["household_id"]] = local_people
    for relation_type, first_index, second_index in local_relations:
      primitive_blueprints.append(
        (
          relation_type,
          local_people[first_index]["person_id"],
          local_people[second_index]["person_id"],
          household["household_id"],
        )
      )

  # Assign literacy by ranked exact targets so the village reproduces county rates.
  for sex, percentage_key in (
    ("male", "male_basic_literacy_mid_pct"),
    ("female", "female_basic_literacy_mid_pct"),
  ):
    sex_people = [person for person in people if person["sex"] == sex]
    eligible = [person for person in sex_people if person["age_1628"] >= 7]
    target = min(len(eligible), round(len(sex_people) * float(culture[percentage_key]) / 100))
    ranked = sorted(
      eligible,
      key=lambda person: (
        -(
          next(row["wealth_index_0_100"] for row in households if row["household_id"] == person["household_id"])
          + (12 if 12 <= person["age_1628"] <= 55 else 0)
          + 35 * stable_unit(world_seed, person["person_id"], "literacy-rank")
        ),
        person["person_id"],
      ),
    )
    for person in ranked[:target]:
      person["is_literate"] = "yes"

  literate_eligible = [
    person for person in people if person["is_literate"] == "yes" and person["age_1628"] >= 10
  ]
  classical_target = min(
    len(literate_eligible),
    round(len(people) * float(culture["classical_education_mid_pct"]) / 100),
  )
  wealth_by_household = {row["household_id"]: row["wealth_index_0_100"] for row in households}
  classical_ranked = sorted(
    literate_eligible,
    key=lambda person: (
      -(
        wealth_by_household[person["household_id"]]
        + (10 if person["sex"] == "male" else 0)
        + 30 * stable_unit(world_seed, person["person_id"], "classical-rank")
      ),
      person["person_id"],
    ),
  )
  for person in classical_ranked[:classical_target]:
    person["is_classically_educated"] = "yes"

  used_names: set[str] = set()
  lineage = int(culture["lineage_organization_potential_0_100"])
  for person in sorted(people, key=lambda row: row["person_id"]):
    pool = (
      FEMALE_PERSONAL_CHARS
      if person["sex"] == "female"
      else SCHOLARLY_CHARS
      if person["is_classically_educated"] == "yes"
      else MALE_PERSONAL_CHARS
    )
    for attempt in range(128):
      use_generation = (
        person["clan_id"]
        and person["lineage_generation"] is not None
        and stable_unit(world_seed, person["person_id"], "use-generation")
        < 0.28 + lineage / 220
      )
      personal = stable_choice(pool, world_seed, person["person_id"], "personal", attempt)
      if use_generation:
        generation = stable_choice(
          GENERATION_CHARS,
          world_seed,
          person["clan_id"],
          person["lineage_generation"],
          "generation-char",
        )
        if generation == personal:
          personal = stable_choice(pool, world_seed, person["person_id"], "personal-alt", attempt)
        given = generation + personal
      elif stable_unit(world_seed, person["person_id"], "two-char", attempt) < (
        0.52 if person["is_literate"] == "yes" else 0.32
      ):
        first = stable_choice(pool, world_seed, person["person_id"], "first", attempt)
        given = first + personal if first != personal else personal
      else:
        given = personal
      full_name = person["surname_zh_hans"] + given
      if full_name not in used_names:
        used_names.add(full_name)
        person["given_name_zh_hans"] = given
        person["name_zh_hans"] = full_name
        break
    if not person["name_zh_hans"]:
      raise RuntimeError(f"Unable to create a unique village display name for {person['person_id']}")
    if person["sex"] == "female" and person["age_1628"] >= 18:
      person["record_style_name"] = person["surname_zh_hans"] + "氏"
    else:
      person["record_style_name"] = person["name_zh_hans"]
    if (
      person["sex"] == "male"
      and person["age_1628"] >= 20
      and person["is_classically_educated"] == "yes"
    ):
      courtesy = stable_choice(COURTESY_PREFIXES, world_seed, person["person_id"], "courtesy-prefix")
      courtesy += stable_choice(COURTESY_SUFFIXES, world_seed, person["person_id"], "courtesy-suffix")
      person["courtesy_name_zh_hans"] = courtesy

  household_by_id = {row["household_id"]: row for row in households}
  for person in people:
    household = household_by_id[person["household_id"]]
    age = person["age_1628"]
    if age < 7:
      occupation = "幼儿"
    elif age < 13:
      occupation = "随家学习与协助家计" if person["is_literate"] == "yes" else "协助家计"
    elif age < 18:
      if person["is_classically_educated"] == "yes":
        occupation = "读书少年"
      elif household["primary_occupation_code"] in {"craft_household", "mason", "textile_household"}:
        occupation = household["primary_occupation"] + "学徒"
      else:
        occupation = "随户营生"
    elif person["is_classically_educated"] == "yes":
      occupation = "读书人"
    elif person["household_role"] in {"户主", "已婚子"}:
      occupation = household["primary_occupation"]
    elif person["household_role"] in {"配偶", "子媳", "长辈配偶"}:
      if household["primary_occupation_code"] in {"grain_farmer", "tenant_farmer", "vegetable_gardener"}:
        occupation = "农事与家庭纺织"
      else:
        occupation = household["primary_occupation"] + "协作"
    elif age >= 60:
      occupation = "退居并协助家计"
    else:
      occupation = household["primary_occupation"] + "帮工"
    person["primary_occupation"] = occupation

  relationships: list[dict[str, Any]] = []
  seen: set[tuple[str, str, str]] = set()

  def add_relationship(
    relation_type: str,
    from_person_id: str,
    to_person_id: str,
    directed: bool,
    strength_low: int,
    strength_high: int,
    origin: str,
    since_year: int | None = None,
  ) -> None:
    first, second = from_person_id, to_person_id
    if not directed and second < first:
      first, second = second, first
    key = (relation_type, first, second)
    if first == second or key in seen:
      return
    seen.add(key)
    relation_id = "VR-" + hashlib.sha256(
      f"{RULESET_VERSION}|{world_seed}|{village['village_id']}|{relation_type}|{first}|{second}".encode("utf-8")
    ).hexdigest()[:20]
    if since_year is None:
      people_by_id = {person["person_id"]: person for person in people}
      earliest = max(
        people_by_id[first]["birth_year_est"] + 7,
        people_by_id[second]["birth_year_est"] + 7,
      )
      earliest = int(clamp(earliest, 1545, SNAPSHOT_YEAR - 1))
      since_year = stable_int(earliest, SNAPSHOT_YEAR - 1, world_seed, relation_id, "since")
    relationships.append(
      {
        "relationship_id": relation_id,
        "from_person_id": first,
        "to_person_id": second,
        "relation_type": relation_type,
        "directed": "yes" if directed else "no",
        "strength_0_100": stable_int(strength_low, strength_high, world_seed, relation_id, "strength"),
        "since_year_est": since_year,
        "origin": origin,
        "historical_claim": "no",
      }
    )

  people_by_id = {person["person_id"]: person for person in people}
  for relation_type, first, second, _ in primitive_blueprints:
    if relation_type == "spouse":
      younger_age = min(people_by_id[first]["age_1628"], people_by_id[second]["age_1628"])
      duration_high = max(1, min(35, younger_age - 18))
      duration = stable_int(1, duration_high, world_seed, first, second, "marriage-duration")
      add_relationship("spouse", first, second, False, 78, 98, "household_topology", SNAPSHOT_YEAR - duration)
    elif relation_type == "parent_child":
      add_relationship(
        "parent_child", first, second, True, 80, 99, "household_topology",
        people_by_id[second]["birth_year_est"],
      )
    else:
      younger_birth = max(people_by_id[first]["birth_year_est"], people_by_id[second]["birth_year_est"])
      add_relationship("sibling", first, second, False, 68, 95, "household_topology", younger_birth)

  # Derive sibling edges from shared parents.  Cousin and wider kin remain clan queries.
  children_by_parent: dict[str, list[str]] = defaultdict(list)
  for relationship in relationships:
    if relationship["relation_type"] == "parent_child":
      children_by_parent[relationship["from_person_id"]].append(relationship["to_person_id"])
  for children in children_by_parent.values():
    for first, second in combinations(sorted(set(children)), 2):
      younger_birth = max(people_by_id[first]["birth_year_est"], people_by_id[second]["birth_year_est"])
      add_relationship("sibling", first, second, False, 65, 93, "derived_shared_parent", younger_birth)

  return people, relationships


def add_village_social_structure(
  households: list[dict[str, Any]],
  people: list[dict[str, Any]],
  relationships: list[dict[str, Any]],
  source: dict[str, Any],
  world_seed: str,
) -> None:
  village = source["village"]
  people_by_id = {row["person_id"]: row for row in people}
  household_people: dict[str, list[dict[str, Any]]] = defaultdict(list)
  for person in people:
    household_people[person["household_id"]].append(person)
  household_heads = {
    household_id: sorted(
      rows,
      key=lambda row: (
        0 if row["household_role"] == "户主" else 1,
        -row["age_1628"],
        row["person_id"],
      ),
    )[0]
    for household_id, rows in household_people.items()
  }
  household_by_id = {row["household_id"]: row for row in households}
  seen = {
    (row["relation_type"], row["from_person_id"], row["to_person_id"])
    for row in relationships
  }

  def add(
    relation_type: str,
    first_id: str,
    second_id: str,
    directed: bool,
    low: int,
    high: int,
    origin: str,
  ) -> None:
    first, second = first_id, second_id
    if not directed and second < first:
      first, second = second, first
    key = (relation_type, first, second)
    if first == second or key in seen:
      return
    seen.add(key)
    relation_id = "VR-" + hashlib.sha256(
      f"{RULESET_VERSION}|{world_seed}|{village['village_id']}|{relation_type}|{first}|{second}".encode("utf-8")
    ).hexdigest()[:20]
    earliest = max(
      people_by_id[first]["birth_year_est"] + 7,
      people_by_id[second]["birth_year_est"] + 7,
    )
    earliest = int(clamp(earliest, 1545, SNAPSHOT_YEAR - 1))
    relationships.append(
      {
        "relationship_id": relation_id,
        "from_person_id": first,
        "to_person_id": second,
        "relation_type": relation_type,
        "directed": "yes" if directed else "no",
        "strength_0_100": stable_int(low, high, world_seed, relation_id, "strength"),
        "since_year_est": stable_int(earliest, SNAPSHOT_YEAR - 1, world_seed, relation_id, "since"),
        "origin": origin,
        "historical_claim": "no",
      }
    )

  # One generated village headman role, explicitly not asserted as a historical officeholder.
  eligible_headmen = [
    person for person in people
    if person["sex"] == "male"
    and person["age_1628"] >= 32
    and person["household_role"] == "户主"
  ]
  if not eligible_headmen:
    eligible_headmen = [
      person for person in people
      if person["age_1628"] >= 32 and person["household_role"] == "户主"
    ]
  headman = max(
    eligible_headmen,
    key=lambda person: (
      household_by_id[person["household_id"]]["wealth_index_0_100"]
      + (20 if person["is_literate"] == "yes" else 0)
      + 15 * stable_unit(world_seed, person["person_id"], "headman"),
      person["person_id"],
    ),
  )
  headman["social_roles"].append("村中首事（生成角色，不主张史实官职）")

  # Clan elders are membership hubs; do not create all-pairs cousin edges.
  clan_households: dict[str, list[dict[str, Any]]] = defaultdict(list)
  for household in households:
    clan_households[household["clan_id"]].append(household)
  for clan_id, clan_rows in sorted(clan_households.items()):
    if len(clan_rows) < 3:
      continue
    candidates = [household_heads[row["household_id"]] for row in clan_rows]
    elder = max(candidates, key=lambda person: (person["age_1628"], person["person_id"]))
    elder["social_roles"].append("族中长者")
    for household in clan_rows:
      other = household_heads[household["household_id"]]
      if other["person_id"] != elder["person_id"]:
        add("lineage_leadership", elder["person_id"], other["person_id"], True, 48, 78, "generated_clan_structure")

  # Teachers and students are constrained by the county literacy/classical targets.
  classical_adults = [
    person for person in people
    if person["is_classically_educated"] == "yes" and person["age_1628"] >= 22
  ]
  teacher_count = min(len(classical_adults), max(1, round(len(classical_adults) / 14)))
  teachers = sorted(
    classical_adults,
    key=lambda person: (-person["age_1628"], person["person_id"]),
  )[:teacher_count]
  for teacher in teachers:
    teacher["primary_occupation"] = "塾师"
    teacher["social_roles"].append("村塾教习")
  students = [
    person for person in people
    if person["is_literate"] == "yes" and 7 <= person["age_1628"] <= 20
  ]
  for index, student in enumerate(sorted(students, key=lambda row: row["person_id"])):
    if teachers:
      teacher = teachers[index % len(teachers)]
      add("teacher_student", teacher["person_id"], student["person_id"], True, 52, 82, "county_literacy_projection")

  # Neighbour links are sparse: two closest households each, represented by heads.
  for household in households:
    distances = []
    for other in households:
      if other["household_id"] == household["household_id"]:
        continue
      distance = math.hypot(
        household["relative_x_0_10000"] - other["relative_x_0_10000"],
        household["relative_y_0_10000"] - other["relative_y_0_10000"],
      )
      distances.append((distance, other["household_id"]))
    for _, other_id in sorted(distances)[:2]:
      add(
        "neighbor",
        household_heads[household["household_id"]]["person_id"],
        household_heads[other_id]["person_id"],
        False,
        32,
        68,
        "generated_household_proximity",
      )

  # Land tenure connects generated high-land households to low-land households.
  landlords = [
    row for row in households if row["social_stratum"] == "地方富户/乡绅结构代理"
  ]
  tenants = [row for row in households if row["social_stratum"] == "佃户或雇工户"]
  for index, tenant in enumerate(sorted(tenants, key=lambda row: row["household_id"])):
    if not landlords:
      break
    landlord = landlords[index % len(landlords)]
    add(
      "landlord_tenant",
      household_heads[landlord["household_id"]]["person_id"],
      household_heads[tenant["household_id"]]["person_id"],
      True,
      35,
      72,
      "gentry_and_land_projection",
    )

  # Apprenticeship uses existing occupation labels instead of inventing industries.
  apprentices = [person for person in people if "学徒" in person["primary_occupation"]]
  adult_workers = [
    person for person in people
    if 25 <= person["age_1628"] <= 65 and person["household_role"] in {"户主", "已婚子"}
  ]
  for apprentice in apprentices:
    household = household_by_id[apprentice["household_id"]]
    candidates = [
      worker for worker in adult_workers
      if household_by_id[worker["household_id"]]["primary_occupation_code"]
      == household["primary_occupation_code"]
      and worker["person_id"] != apprentice["person_id"]
    ]
    if candidates:
      master = stable_choice(sorted(candidates, key=lambda row: row["person_id"]), world_seed, apprentice["person_id"], "master")
      add("master_apprentice", master["person_id"], apprentice["person_id"], True, 50, 82, "occupation_projection")

  # One acquaintance edge per adult household head keeps the graph sparse.
  heads = sorted(household_heads.values(), key=lambda row: row["person_id"])
  for head in heads:
    candidates = [
      other for other in heads
      if other["person_id"] != head["person_id"]
      and abs(other["age_1628"] - head["age_1628"]) <= 15
    ]
    if candidates:
      other = stable_choice(candidates, world_seed, head["person_id"], "acquaintance")
      add("acquaintance", head["person_id"], other["person_id"], False, 30, 70, "generated_sparse_social_graph")

  # Select the active cast after graph construction.
  degree: Counter[str] = Counter()
  for relationship in relationships:
    degree[relationship["from_person_id"]] += 1
    degree[relationship["to_person_id"]] += 1
  core_candidates = [person for person in people if person["age_1628"] >= 12]
  core_count = min(len(core_candidates), max(24, round(math.sqrt(len(people)) * 1.5)))
  ranked_people = sorted(
    core_candidates,
    key=lambda person: (
      -(
        (100 if person["person_id"] == headman["person_id"] else 0)
        + (50 if "村塾教习" in person["social_roles"] else 0)
        + (35 if "族中长者" in person["social_roles"] else 0)
        + (22 if household_by_id[person["household_id"]]["social_stratum"] == "地方富户/乡绅结构代理" else 0)
        + (18 if person["is_classically_educated"] == "yes" else 0)
        + (8 if person["household_role"] == "户主" else 0)
        + min(18, degree[person["person_id"]] * 2)
        + 8 * stable_unit(world_seed, person["person_id"], "core-rank")
      ),
      person["person_id"],
    ),
  )
  for person in ranked_people[:core_count]:
    person["is_core_npc"] = "yes"
  relationships.sort(key=lambda row: row["relationship_id"])


def count_values(rows: Sequence[dict[str, Any]], key: str) -> list[dict[str, Any]]:
  counts = Counter(str(row[key]) for row in rows)
  return [
    {"value": value, "count": count}
    for value, count in sorted(counts.items(), key=lambda item: (-item[1], item[0]))
  ]


def build_summary(
  households: Sequence[dict[str, Any]],
  people: Sequence[dict[str, Any]],
  relationships: Sequence[dict[str, Any]],
) -> dict[str, Any]:
  male = [row for row in people if row["sex"] == "male"]
  female = [row for row in people if row["sex"] == "female"]
  male_literate = sum(row["is_literate"] == "yes" for row in male)
  female_literate = sum(row["is_literate"] == "yes" for row in female)
  return {
    "household_count": len(households),
    "person_count": len(people),
    "male_count": len(male),
    "female_count": len(female),
    "mean_age": round(sum(row["age_1628"] for row in people) / len(people), 2),
    "literate_count": sum(row["is_literate"] == "yes" for row in people),
    "male_literacy_pct": round(100 * male_literate / max(1, len(male)), 2),
    "female_literacy_pct": round(100 * female_literate / max(1, len(female)), 2),
    "total_literacy_pct": round(
      100 * sum(row["is_literate"] == "yes" for row in people) / len(people), 2
    ),
    "classically_educated_count": sum(
      row["is_classically_educated"] == "yes" for row in people
    ),
    "core_npc_count": sum(row["is_core_npc"] == "yes" for row in people),
    "relationship_count": len(relationships),
    "household_types": count_values(households, "household_type"),
    "social_strata": count_values(households, "social_stratum"),
    "top_surnames": count_values(people, "surname_zh_hans")[:12],
    "top_household_occupations": count_values(households, "primary_occupation")[:12],
    "relationship_types": count_values(relationships, "relation_type"),
  }


def validate_payload(payload: dict[str, Any]) -> dict[str, Any]:
  village = payload["village"]
  households = payload["households"]
  people = payload["people"]
  relationships = payload["relationships"]
  household_ids = [row["household_id"] for row in households]
  person_ids = [row["person_id"] for row in people]
  valid_people = set(person_ids)
  errors: list[str] = []
  if len(people) != int(village["projected_rural_population"]):
    errors.append("person count does not equal projected village population")
  if sum(row["household_size"] for row in households) != len(people):
    errors.append("household sizes do not sum to person count")
  if len(household_ids) != len(set(household_ids)):
    errors.append("duplicate household_id")
  if len(person_ids) != len(set(person_ids)):
    errors.append("duplicate person_id")
  if len({row["name_zh_hans"] for row in people}) != len(people):
    errors.append("duplicate village display names")
  if any(not row["name_zh_hans"] for row in people):
    errors.append("blank generated name")
  if sum(row["population_share_ppm"] for row in households) != WEIGHT_TOTAL:
    errors.append("household population weights do not sum to 1,000,000")
  if sum(row["farmland_share_ppm"] for row in households) != WEIGHT_TOTAL:
    errors.append("household farmland weights do not sum to 1,000,000")
  relation_keys: set[tuple[str, str, str]] = set()
  people_by_id = {row["person_id"]: row for row in people}
  parent_age_errors = 0
  for row in relationships:
    if row["from_person_id"] not in valid_people or row["to_person_id"] not in valid_people:
      errors.append(f"relationship foreign key failure: {row['relationship_id']}")
      break
    if row["from_person_id"] == row["to_person_id"]:
      errors.append(f"self relationship: {row['relationship_id']}")
      break
    key = (row["relation_type"], row["from_person_id"], row["to_person_id"])
    if key in relation_keys:
      errors.append(f"duplicate relationship: {key}")
      break
    relation_keys.add(key)
    if row["relation_type"] == "parent_child":
      if (
        people_by_id[row["from_person_id"]]["age_1628"]
        - people_by_id[row["to_person_id"]]["age_1628"]
        < 15
      ):
        parent_age_errors += 1
  if parent_age_errors:
    errors.append(f"parent-child age constraint failures: {parent_age_errors}")
  if any(row["historical_claim"] != "no" for row in people):
    errors.append("generated people must not assert historical identity")
  if any(row["historical_claim"] != "no" for row in relationships):
    errors.append("generated relationships must not assert historical identity")
  return {
    "status": "pass" if not errors else "fail",
    "errors": errors,
    "checks": {
      "population_exact": not any("person count" in error for error in errors),
      "household_population_exact": not any("household sizes" in error for error in errors),
      "unique_household_ids": len(household_ids) == len(set(household_ids)),
      "unique_person_ids": len(person_ids) == len(set(person_ids)),
      "unique_village_display_names": len({row["name_zh_hans"] for row in people}) == len(people),
      "population_weights_exact": sum(row["population_share_ppm"] for row in households) == WEIGHT_TOTAL,
      "farmland_weights_exact": sum(row["farmland_share_ppm"] for row in households) == WEIGHT_TOTAL,
      "relationship_foreign_keys_complete": all(
        row["from_person_id"] in valid_people and row["to_person_id"] in valid_people
        for row in relationships
      ),
      "parent_child_minimum_age_gap_15": parent_age_errors == 0,
      "generated_historical_claims_zero": all(row["historical_claim"] == "no" for row in people),
    },
  }


def person_relation_labels(
  person_id: str,
  relationships: Sequence[dict[str, Any]],
  people_by_id: dict[str, dict[str, Any]],
  limit: int = 4,
) -> str:
  labels = {
    "spouse": "配偶",
    "parent_child": "亲子",
    "sibling": "同胞",
    "lineage_leadership": "宗族",
    "teacher_student": "师生",
    "neighbor": "邻里",
    "landlord_tenant": "租佃",
    "master_apprentice": "师徒",
    "acquaintance": "相识",
  }
  result = []
  for row in relationships:
    if row["from_person_id"] == person_id:
      other_id = row["to_person_id"]
    elif row["to_person_id"] == person_id:
      other_id = row["from_person_id"]
    else:
      continue
    result.append(f"{labels.get(row['relation_type'], row['relation_type'])}:{people_by_id[other_id]['name_zh_hans']}")
  return "；".join(result[:limit]) + ("……" if len(result) > limit else "")


def render_preview(payload: dict[str, Any], json_relative_path: str) -> str:
  village = payload["village"]
  summary = payload["summary"]
  people = payload["people"]
  households = payload["households"]
  relationships = payload["relationships"]
  people_by_id = {row["person_id"]: row for row in people}
  household_by_id = {row["household_id"]: row for row in households}
  core_people = [row for row in people if row["is_core_npc"] == "yes"]
  core_people.sort(
    key=lambda row: (
      0 if row["social_roles"] else 1,
      -len(person_relation_labels(row["person_id"], relationships, people_by_id)),
      row["person_id"],
    )
  )
  lines = [
    f"# {village['county']}·{village['village_name']}人物关系代码生成样例",
    "",
    "> 本文件由 `generate_ming_1628_village_people.py` 确定性生成，不是人工编写人物。",
    "> 所有人名、家庭与关系均为游戏化推定，`historical_claim=no`；不冒充1628年真实村民名册。",
    "",
    "## 村庄概况",
    "",
    "| 项目 | 结果 |",
    "|---|---:|",
    f"| 村庄ID | `{village['village_id']}` |",
    f"| 所属 | {village['region']} · {village['upper_unit']} · {village['county']} · {village['subregion_name']} |",
    f"| 地貌与资源 | {village['primary_landform']}；{village['primary_resource_tags']} |",
    f"| 投影人口 | {summary['person_count']}人 |",
    f"| 家庭 | {summary['household_count']}户 |",
    f"| 男 / 女 | {summary['male_count']} / {summary['female_count']} |",
    f"| 基础识字 | {summary['literate_count']}人（{summary['total_literacy_pct']}%） |",
    f"| 经典教育 | {summary['classically_educated_count']}人 |",
    f"| 完整关系边 | {summary['relationship_count']}条 |",
    f"| 核心可见角色 | {summary['core_npc_count']}人 |",
    "",
    "## 分布摘要",
    "",
    "- 主要姓氏：" + "、".join(f"{row['value']}（{row['count']}人）" for row in summary["top_surnames"][:8]),
    "- 主要户业：" + "、".join(f"{row['value']}（{row['count']}户）" for row in summary["top_household_occupations"][:8]),
    "- 家庭形态：" + "、".join(f"{row['value']}（{row['count']}户）" for row in summary["household_types"]),
    "",
    "## 核心人物",
    "",
    "| 姓名 | 年龄/性别 | 家庭与身份 | 职业 | 教育 | 关系摘要 |",
    "|---|---|---|---|---|---|",
  ]
  for person in core_people:
    household = household_by_id[person["household_id"]]
    sex = "男" if person["sex"] == "male" else "女"
    roles = "、".join(person["social_roles"]) or household["social_stratum"]
    education = (
      "经典教育" if person["is_classically_educated"] == "yes"
      else "识字" if person["is_literate"] == "yes" else "未标记识字"
    )
    relation_text = person_relation_labels(person["person_id"], relationships, people_by_id)
    lines.append(
      f"| {person['name_zh_hans']} | {person['age_1628']}岁/{sex} | "
      f"{household['household_id'].rsplit('-', 1)[-1]}·{person['household_role']}；{roles} | "
      f"{person['primary_occupation']} | {education} | {relation_text} |"
    )

  lines.extend(
    [
      "",
      "## 前十二户展开",
      "",
    ]
  )
  for household in households[:12]:
    members = sorted(
      [person for person in people if person["household_id"] == household["household_id"]],
      key=lambda row: row["person_id"],
    )
    member_text = "、".join(
      f"{person['name_zh_hans']}（{person['household_role']}，{person['age_1628']}岁）"
      for person in members
    )
    lines.extend(
      [
        f"### {household['household_id'].rsplit('-', 1)[-1]} · {household['household_surname_zh_hans']}姓{household['household_type']}",
        "",
        f"- 生计：{household['primary_occupation']}；阶层：{household['social_stratum']}；户内耕地权重：{household['farmland_share_ppm']}/1,000,000。",
        f"- 成员：{member_text}",
        "",
      ]
    )

  lines.extend(
    [
      "## 数据边界与重建",
      "",
      "- 县级CBDB人物和家族只参与姓氏权重，不会被自动设为本村居民。",
      "- 同宗以 `clan_id` 表达；不会把同姓者全部展开为两两亲属关系。",
      "- 全国Tick仍然只计算1,168县；本文件仅在进入村庄时生成。",
      f"- 完整JSON：`{json_relative_path}`。",
      "",
      "```bash",
      "python3 tools/historical_data/generate_ming_1628_village_people.py \\",
      f"  --village-id {village['village_id']} \\",
      f"  --world-seed {payload['metadata']['world_seed']}",
      "```",
      "",
    ]
  )
  return "\n".join(lines)


def generate_payload(source: dict[str, Any], world_seed: str, database_sha256: str) -> dict[str, Any]:
  surname_model = build_surname_model(source)
  households = build_households(source, surname_model, world_seed)
  people, relationships = assign_people_and_kin(
    households, source, surname_model, world_seed
  )
  add_village_social_structure(
    households, people, relationships, source, world_seed
  )
  summary = build_summary(households, people, relationships)
  payload: dict[str, Any] = {
    "metadata": {
      "schema_version": SCHEMA_VERSION,
      "ruleset_version": RULESET_VERSION,
      "snapshot_year": SNAPSHOT_YEAR,
      "world_seed": world_seed,
      "source_database": source["source_database_name"],
      "source_database_sha256": database_sha256,
      "source_database_user_version": source["database_user_version"],
      "generation_type": "deterministic_code_generation",
      "historical_identity_policy": "generated residents are never source historical persons",
      "commercial_release_ready": "no",
    },
    "village": source["village"],
    "county_inputs": {
      "average_household_size": round(
        float(source["economy"]["population_est_1628"])
        / max(1, int(source["economy"]["household_count_est"])),
        4,
      ),
      "local_market_0_100": source["economy"]["local_market_0_100"],
      "transport_access_0_100": source["economy"]["transport_access_0_100"],
      "industrial_initial_1628_0_100": source["economy"]["industrial_initial_1628_0_100"],
      "lineage_organization_potential_0_100": source["culture"]["lineage_organization_potential_0_100"],
      "gentry_power_0_100": source["culture"]["gentry_power_0_100"],
      "male_basic_literacy_mid_pct": source["culture"]["male_basic_literacy_mid_pct"],
      "female_basic_literacy_mid_pct": source["culture"]["female_basic_literacy_mid_pct"],
      "classical_education_mid_pct": source["culture"]["classical_education_mid_pct"],
      "data_coverage_0_100": source["culture"]["data_coverage_0_100"],
    },
    "surname_model": surname_model,
    "summary": summary,
    "households": households,
    "people": people,
    "relationships": relationships,
  }
  validation = validate_payload(payload)
  payload["validation"] = validation
  fingerprint_payload = json.dumps(payload, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
  payload["metadata"]["generation_fingerprint"] = hashlib.sha256(
    fingerprint_payload.encode("utf-8")
  ).hexdigest()
  if validation["status"] != "pass":
    raise RuntimeError("Village people validation failed: " + "; ".join(validation["errors"]))
  return payload


def main() -> None:
  parser = argparse.ArgumentParser(
    description="Deterministically generate one Ming 1628 village's households, people and relations."
  )
  parser.add_argument("--database", type=Path, default=DEFAULT_DATABASE)
  parser.add_argument("--village-id")
  parser.add_argument("--village-name")
  parser.add_argument("--county-id")
  parser.add_argument("--world-seed", default=DEFAULT_WORLD_SEED)
  parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT_DIR)
  args = parser.parse_args()

  source = load_source_bundle(
    args.database, args.village_id, args.village_name, args.county_id
  )
  payload = generate_payload(source, args.world_seed, file_sha256(args.database))
  village = payload["village"]
  stem = f"{village['village_id']}_{safe_filename(village['village_name'])}_{RULESET_VERSION}"
  json_path = args.output_dir / "generated" / f"{stem}.json"
  preview_path = args.output_dir / f"sample_{stem}.md"
  report_path = args.output_dir / f"sample_{stem}_validation.json"

  write_json_atomic(json_path, payload)
  preview = render_preview(payload, f"generated/{json_path.name}")
  write_text_atomic(preview_path, preview)
  report = {
    "schema_version": SCHEMA_VERSION,
    "village_id": village["village_id"],
    "village_name": village["village_name"],
    "generation_fingerprint": payload["metadata"]["generation_fingerprint"],
    "full_json_sha256": file_sha256(json_path),
    "preview_sha256": file_sha256(preview_path),
    "summary": payload["summary"],
    "validation": payload["validation"],
  }
  write_json_atomic(report_path, report)
  print(
    json.dumps(
      {
        "json": str(json_path),
        "preview": str(preview_path),
        "validation_report": str(report_path),
        "generation_fingerprint": payload["metadata"]["generation_fingerprint"],
        "summary": payload["summary"],
      },
      ensure_ascii=False,
      indent=2,
    )
  )


if __name__ == "__main__":
  main()
