#!/usr/bin/env python3
"""Build the Project Realm 1628 civil county hierarchy.

The script parses the plain-text extracts of Ming Shi, juan 40-46.  It keeps
the source's own hierarchy, removes units created after 1628, and applies a
small, explicit errata table for merged paragraphs and obvious conversion
errors in the simplified Wikisource extract.

Example:
    python3 tools/historical_data/build_ming_1628_geography.py \
      --input-dir tmp/research \
      --output docs/90_资料与归档/01_崇祯元年历史资料/data/1628/3.疆域与人口/county_hierarchy_1628.csv

If a source extract is missing, the script downloads it from Wikisource's
MediaWiki API.  No third-party Python package is required.
"""

from __future__ import annotations

import argparse
import csv
import json
import re
import urllib.parse
import urllib.request
from dataclasses import dataclass
from pathlib import Path


SNAPSHOT_YEAR = 1628

REGION_NAMES = {
    "京师": "北直隶（京师）",
    "南京": "南直隶（南京）",
    "山东等处承宣布政使司": "山东",
    "山西等处承宣布政使司": "山西",
    "河南等处承宣布政使司": "河南",
    "陕西等处承宣布政使司": "陕西",
    "四川等处承宣布政使司": "四川",
    "江西等处承宣布政使司": "江西",
    "湖广等处承宣布政使司": "湖广",
    "浙江等处承宣布政使司": "浙江",
    "福建等处承宣布政使司": "福建",
    "广东等处承宣布政使司": "广东",
    "广西等处承宣布政使司": "广西",
    "云南等处承宣布政使司": "云南",
    "贵州等处承宣布政使司": "贵州",
}

REGION_ORDER = list(REGION_NAMES.values())

# These are the itemized rows retained for the 1628 snapshot.  They are not
# presented as a correction of Ming Shi's national summary.  The differences
# between the summary counts and the itemized descriptions are preserved in
# the accompanying research note.
EXPECTED_ITEMIZED_COUNTS_1628 = {
    "北直隶（京师）": 116,
    "南直隶（南京）": 96,
    "山东": 89,
    "山西": 78,
    "河南": 96,
    "陕西": 96,
    "四川": 111,
    "江西": 77,
    "湖广": 108,
    "浙江": 75,
    "福建": 57,
    "广东": 75,
    "广西": 50,
    "云南": 31,
    "贵州": 13,
}

CHINESE_DIGITS = dict(zip("零一二三四五六七八九", range(10)))


@dataclass
class CountyRow:
    region: str
    upper_unit: str
    intermediate_unit: str
    source_name: str
    source_volume: int
    source_line: int
    source_text: str
    notes: str = ""


def chinese_number(value: str) -> int | None:
    value = value.replace("有", "").replace("两", "二")
    if not value:
        return None
    if "百" in value:
        left, right = value.split("百", 1)
        return (CHINESE_DIGITS.get(left, 1) if left else 1) * 100 + (
            chinese_number(right) or 0
        )
    if "十" in value:
        left, right = value.split("十", 1)
        return (CHINESE_DIGITS.get(left, 1) if left else 1) * 10 + (
            CHINESE_DIGITS.get(right, 0) if right else 0
        )
    return CHINESE_DIGITS.get(value)


def county_target(text: str) -> int | None:
    match = re.search(
        r"领[^。；：:]{0,25}?县([零一二三四五六七八九十百有两]+)", text
    )
    return chinese_number(match.group(1)) if match else None


def state_prefix(text: str) -> str | None:
    match = re.match(
        r"^([^府县司卫所军，。\s]{1,6}州)"
        r"(?=[，,\s]|元|旧|本|洪武|永乐|正德|嘉靖|万历|成化|隆庆|"
        r"宣德|弘治|天启|崇祯|[东西南北])",
        text,
    )
    return match.group(1) if match else None


def extract_unit_name(
    text: str,
    upper_unit: str,
    *,
    inside_state: bool = False,
    force_county: bool = False,
) -> str:
    value = text.strip().lstrip("○").strip()
    boundaries: list[int] = []

    def add_boundary(pattern: str) -> None:
        match = re.search(pattern, value)
        if match and match.start() > 0:
            boundaries.append(match.start())

    add_boundary(r"倚")
    add_boundary(r"[，,\s]")
    add_boundary(r"府(?=[，,]?[东西南北])")

    state_relation = re.search(r"州(?=[，,]?[东西南北少])", value)
    if state_relation:
        if inside_state or upper_unit.endswith("州") or force_county:
            boundaries.append(state_relation.start())
        else:
            boundaries.append(state_relation.end())

    add_boundary(r"司(?=[东西南北])")
    add_boundary(r"卫(?=[东西南北])")
    add_boundary(r"所(?=[东西南北])")
    add_boundary(r"(?=[东西南北]{1,2}(?:有|距|滨|临))")
    add_boundary(
        r"(?=元(?:属|为|曰|治|至)|旧(?:属|治|为)|本|洪武|明玉珍|永乐|"
        r"正德|嘉靖|万历|成化|隆庆|宣德|弘治|天启|崇祯|宋(?:属|置)|"
        r"唐(?:属|置)|隋(?:属|置)|后周|太祖|国朝)"
    )

    if boundaries:
        name = value[: min(boundaries)]
    else:
        name = re.split(r"[。：；]", value, 1)[0]
    return name.strip(" 、，。")


def download_extract(volume: int) -> str:
    query = urllib.parse.urlencode(
        {
            "action": "query",
            "prop": "extracts",
            "explaintext": "1",
            "titles": f"明史/卷{volume}",
            "format": "json",
            "formatversion": "2",
            "variant": "zh-hans",
        }
    )
    url = f"https://zh.wikisource.org/w/api.php?{query}"
    request = urllib.request.Request(url, headers={"User-Agent": "ProjectRealmResearch/0.1"})
    with urllib.request.urlopen(request, timeout=60) as response:
        payload = json.loads(response.read().decode("utf-8"))
    pages = payload["query"]["pages"]
    if not pages or "extract" not in pages[0]:
        raise RuntimeError(f"Wikisource extract missing for Ming Shi volume {volume}")
    return pages[0]["extract"]


def load_extract(input_dir: Path, volume: int) -> str:
    path = input_dir / f"mingshi{volume}-hans.txt"
    if path.exists():
        return path.read_text(encoding="utf-8")
    text = download_extract(volume)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")
    return text


def inactive_in_1628(name: str, text: str) -> bool:
    if name in {"嘉禾", "新田"} and "崇祯十二年" in text:
        return True
    if name == "镇平" and "崇祯六年" in text:
        return True
    if name == "开平":
        return True
    if name == "施秉" and "崇祯四年" in text:
        return True
    return False


def parse_extracts(input_dir: Path) -> list[CountyRow]:
    rows: list[CountyRow] = []

    for volume in range(40, 47):
        region: str | None = None
        upper_unit: str | None = None
        waiting_for: str | None = None
        upper_target: int | None = None
        upper_count = 0
        upper_is_civil = False
        intermediate_unit = ""
        intermediate_remaining: int | None = None

        for line_number, raw in enumerate(load_extract(input_dir, volume).splitlines(), 1):
            text = raw.strip()

            region_heading = re.match(r"^== ([^=]+) ==$", text)
            if region_heading:
                region = REGION_NAMES.get(region_heading.group(1))
                upper_unit = None
                waiting_for = None
                continue
            if not region:
                continue

            upper_heading = re.match(r"^=== ([^=]+) ===$", text)
            if upper_heading:
                upper_unit = upper_heading.group(1).strip()
                waiting_for = "upper"
                upper_target = None
                upper_count = 0
                intermediate_unit = ""
                intermediate_remaining = None
                upper_is_civil = not any(
                    marker in upper_unit
                    for marker in (
                        "都指挥使司",
                        "行都指挥使司",
                        "留守司",
                        "卫军民指挥使司",
                        "千户所",
                        "宣抚司",
                        "招讨司",
                        "长官司",
                    )
                )
                continue

            intermediate_heading = re.match(r"^==== ([^=]+) ====$", text)
            if intermediate_heading:
                intermediate_unit = intermediate_heading.group(1).strip()
                waiting_for = "intermediate"
                intermediate_remaining = None
                continue

            if not text or text.startswith("="):
                continue

            if waiting_for == "upper":
                upper_target = county_target(text)
                upper_count = 0
                intermediate_unit = ""
                intermediate_remaining = None
                waiting_for = None
                continue

            if waiting_for == "intermediate":
                intermediate_remaining = county_target(text) or 0
                waiting_for = None
                continue

            # A few prefectures, most notably all of Jiangxi and Henan's
            # Guide Fu, are plain paragraphs rather than level-3 headings.
            headerless = re.match(
                r"^([\u4e00-\u9fff]{1,8}府)[ ，]*(?:元|旧|本|洪武|明玉珍|太祖)"
                r".*?领[^。；：:]{0,25}?县([零一二三四五六七八九十百有两]+)",
                text,
            )
            if headerless and "元" not in headerless.group(1)[:-1]:
                upper_unit = (
                    "南康府" if headerless.group(1) == "南唐府" else headerless.group(1)
                )
                upper_target = chinese_number(headerless.group(2))
                upper_count = 0
                upper_is_civil = True
                intermediate_unit = ""
                intermediate_remaining = None
                continue

            if not upper_unit or not upper_is_civil:
                continue

            child_target = county_target(text)
            if child_target is not None:
                intermediate_unit = extract_unit_name(text, upper_unit)
                intermediate_remaining = child_target
                continue

            if upper_target is None and not (
                intermediate_remaining and intermediate_remaining > 0
            ):
                continue

            inside_state = intermediate_remaining is not None and intermediate_remaining > 0

            # If a heading-formatted state has no counties, later paragraphs
            # positioned directly against the prefecture can still be direct
            # counties (for example, Shanglin and Wuyuan in Si'en Fu).
            force_direct = False
            if intermediate_remaining == 0:
                if upper_target is not None and re.match(r"^.{1,10}，?府[东西南北]", text):
                    force_direct = True
                    intermediate_unit = ""
                    intermediate_remaining = None
                else:
                    continue

            inside_state = intermediate_remaining is not None and intermediate_remaining > 0

            # A direct state with no county children is not a county.  County
            # descriptions that explicitly say "改为县" are retained.
            prefix = state_prefix(text)
            force_county = "改为县" in text or "改为印江县" in text or "复置县" in text
            if (
                not inside_state
                and not force_direct
                and prefix
                and not upper_unit.endswith("州")
                and not force_county
            ):
                intermediate_unit = prefix
                intermediate_remaining = 0
                continue

            # A state can occur after its prefecture's own county count is
            # complete.  Handle its declared child count before applying the
            # upper-unit stop condition.
            if upper_target is not None and upper_count >= upper_target and not inside_state:
                continue

            name = extract_unit_name(
                text,
                upper_unit,
                inside_state=inside_state,
                force_county=force_county,
            )
            if not name:
                continue
            if (
                not inside_state
                and not force_direct
                and name.endswith(("州", "府", "司", "卫", "所", "军"))
            ):
                continue
            if inactive_in_1628(name, text):
                continue

            rows.append(
                CountyRow(
                    region=region,
                    upper_unit=upper_unit,
                    intermediate_unit=intermediate_unit if inside_state else "",
                    source_name=name,
                    source_volume=volume,
                    source_line=line_number,
                    source_text=text,
                )
            )
            upper_count += 1
            if inside_state:
                intermediate_remaining -= 1

    return rows


def apply_errata(rows: list[CountyRow]) -> list[CountyRow]:
    # Remove a native office accidentally consumed as a county in the compact
    # Guizhou typography, then add the three actual Pingyue counties.
    rows = [
        row
        for row in rows
        if row.source_name != "凯里长官司"
        and not (
            row.region == "广东"
            and row.upper_unit == "琼州府"
            and row.source_name in {"东安", "西宁"}
        )
    ]

    additions = [
        CountyRow("南直隶（南京）", "淮安府", "", "沭阳", 40, 276, "沭阳府北……海州元曰海宁州……", "源文将沭阳县与海州合并为一段，人工拆分"),
        CountyRow("广东", "罗定州", "", "东安", 45, 257, "东安州东……", "罗定州为布政司直隶州"),
        CountyRow("广东", "罗定州", "", "西宁", 45, 258, "西宁州西……", "罗定州为布政司直隶州"),
        CountyRow("贵州", "平越军民府", "", "馀庆", 46, 315, "馀庆州西……万历二十九年六月改为县……", "州西为方位语，不是州名"),
        CountyRow("贵州", "平越军民府", "", "瓮安", 46, 316, "瓮安州西北……万历二十九年四月改为县……", "州西北为方位语，不是州名"),
        CountyRow("贵州", "平越军民府", "", "湄潭", 46, 317, "湄潭州北……万历二十九年置……", "州北为方位语，不是州名"),
    ]
    rows.extend(additions)
    return rows


NORMALIZATION = {
    "井径": "井陉",
    "稿城": "藁城",
    "钜鹿": "巨鹿",
    "钜野": "巨野",
    "成安安": "成安",
    "径": "泾",
    "教义": "孝义",
    "荥河": "荣河",
    "金谿": "金溪",
    "兰谿": "兰溪",
    "慈谿": "慈溪",
    "荣经": "荥经",
}


def normalized_county_name(row: CountyRow) -> str:
    name = NORMALIZATION.get(row.source_name, row.source_name)
    if row.region == "浙江" and name == "蒲江":
        name = "浦江"
    name = name.replace("馀", "余").replace("谿", "溪")
    return name if name.endswith("县") else f"{name}县"


def normalized_intermediate_unit(name: str) -> str:
    """Remove the source paragraph's historical gloss from a subordinate state.

    Wikisource sometimes joins a state name and its Yuan-era description, for
    example ``昌平州元昌平县``.  The hierarchy field should retain only
    ``昌平州`` while the untouched source line remains available separately.
    """

    match = re.match(r"^(.+?州)", name)
    return match.group(1) if match else name


def upper_unit_type(name: str) -> str:
    if name.endswith("军民府"):
        return "军民府"
    if name.endswith("府"):
        return "府"
    if name.endswith("州"):
        return "直隶州"
    return "其他"


def validate(rows: list[CountyRow]) -> None:
    counts = {region: 0 for region in REGION_ORDER}
    seen: set[tuple[str, str, str, str]] = set()
    for row in rows:
        counts[row.region] += 1
        key = (
            row.region,
            row.upper_unit,
            normalized_intermediate_unit(row.intermediate_unit),
            normalized_county_name(row),
        )
        if key in seen:
            raise RuntimeError(f"Duplicate county hierarchy row: {key}")
        seen.add(key)

    if counts != EXPECTED_ITEMIZED_COUNTS_1628:
        raise RuntimeError(
            "Itemized county counts changed; reconcile before writing output. "
            f"actual={counts}, expected={EXPECTED_ITEMIZED_COUNTS_1628}"
        )


def write_csv(rows: list[CountyRow], output: Path) -> None:
    region_rank = {name: index for index, name in enumerate(REGION_ORDER)}
    rows.sort(
        key=lambda row: (
            region_rank[row.region],
            row.source_volume,
            row.source_line,
            row.upper_unit,
            normalized_intermediate_unit(row.intermediate_unit),
            normalized_county_name(row),
        )
    )
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(
            stream,
            fieldnames=[
                "snapshot_year",
                "region",
                "upper_unit",
                "upper_unit_type",
                "intermediate_unit",
                "county",
                "source_name",
                "source_volume",
                "source_line",
                "status_1628",
                "evidence_grade",
                "notes",
            ],
        )
        writer.writeheader()
        for row in rows:
            writer.writerow(
                {
                    "snapshot_year": SNAPSHOT_YEAR,
                    "region": row.region,
                    "upper_unit": row.upper_unit,
                    "upper_unit_type": upper_unit_type(row.upper_unit),
                    "intermediate_unit": normalized_intermediate_unit(
                        row.intermediate_unit
                    ),
                    "county": normalized_county_name(row),
                    "source_name": row.source_name,
                    "source_volume": row.source_volume,
                    "source_line": row.source_line,
                    "status_1628": "active",
                    "evidence_grade": "documented_reconstructed",
                    "notes": row.notes,
                }
            )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input-dir", type=Path, default=Path("tmp/research"))
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(
            "docs/90_资料与归档/01_崇祯元年历史资料/data/1628/3.疆域与人口/"
            "county_hierarchy_1628.csv"
        ),
    )
    args = parser.parse_args()

    rows = apply_errata(parse_extracts(args.input_dir))
    validate(rows)
    write_csv(rows, args.output)
    print(f"Wrote {len(rows)} county rows to {args.output}")


if __name__ == "__main__":
    main()
