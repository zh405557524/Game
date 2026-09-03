#!/usr/bin/env python3
"""Export Xiao County's v1.0 world slice to deterministic prototype JSON.

The browser never opens SQLite. This script preserves the source model's
evidence boundaries and intentionally omits unexplored superior geography.
"""

from __future__ import annotations

import argparse
import json
import sqlite3
from pathlib import Path


SCRIPT_PATH = Path(__file__).resolve()
PROTOTYPE_ROOT = SCRIPT_PATH.parents[1]
REPOSITORY_ROOT = SCRIPT_PATH.parents[3]
DEFAULT_DATABASE = (
    REPOSITORY_ROOT
    / "docs/90_资料与归档/01_崇祯元年历史资料/data/1628/13.模拟基础规则/game_world_1628_v1.0.sqlite"
)
DEFAULT_OUTPUT = PROTOTYPE_ROOT / "src/data/xiao-county-world.json"
COUNTY_ID = "MING1628-0205"
FOCUS_SETTLEMENT_ID = "MING1628-0205-V2080"
COUNTY_SEAT_ID = "MING1628-0205-SC01"


def rows(connection: sqlite3.Connection, query: str, parameters: tuple = ()) -> list[dict]:
    return [dict(row) for row in connection.execute(query, parameters)]


def yes_no(value: object) -> bool:
    return str(value).lower() == "yes"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--database", type=Path, default=DEFAULT_DATABASE)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()

    connection = sqlite3.connect(f"file:{args.database}?mode=ro", uri=True)
    connection.row_factory = sqlite3.Row

    subregions = rows(
        connection,
        """
        SELECT subregion_id,subregion_name,direction_name,zone_type,
               primary_landform,secondary_landform,water_context,
               primary_resource_tags,area_share_ppm,population_share_ppm,
               farmland_share_ppm,village_count,center_rel_x_0_10000,
               center_rel_y_0_10000,agriculture_resource_0_100,
               forest_resource_0_100,pasture_resource_0_100,
               fishery_resource_0_100,salt_resource_0_100,
               fuel_resource_0_100,metal_resource_0_100,
               building_material_resource_0_100,generation_method,data_quality
        FROM county_subregion_definition
        WHERE county_id=?
        ORDER BY subregion_id
        """,
        (COUNTY_ID,),
    )

    divisions_raw = rows(
        connection,
        """
        SELECT division_id,primary_subregion_id,division_type_code,division_name,
               is_county_core,center_settlement_id,center_settlement_name,
               historical_name_claim,boundary_historical_claim,evidence_grade,
               assignment_method,resident_population_est,household_count_est,
               labor_force_est,area_km2_est,village_count,settlement_count,
               center_rel_x_0_10000,center_rel_y_0_10000,
               agriculture_resource_0_100,forest_resource_0_100,
               pasture_resource_0_100,fishery_resource_0_100,
               salt_resource_0_100,fuel_resource_0_100,metal_resource_0_100,
               building_material_resource_0_100,subregion_name,direction_name,
               zone_type,primary_landform,primary_resource_tags
        FROM v_county_entry_local_divisions
        WHERE county_id=?
        ORDER BY is_county_core DESC,division_type_code DESC,division_id
        """,
        (COUNTY_ID,),
    )

    divisions = []
    for row in divisions_raw:
        divisions.append(
            {
                "id": row["division_id"],
                "name": row["division_name"],
                "type": row["division_type_code"],
                "typeLabel": "镇级计算区" if row["division_type_code"] == "town" else "乡级计算区",
                "isCountyCore": bool(row["is_county_core"]),
                "centerSettlementId": row["center_settlement_id"],
                "centerSettlementName": row["center_settlement_name"],
                "residentPopulation": row["resident_population_est"],
                "householdCount": row["household_count_est"],
                "laborForce": row["labor_force_est"],
                "areaKm2": round(row["area_km2_est"], 2),
                "villageCount": row["village_count"],
                "settlementCount": row["settlement_count"],
                "relativeX": row["center_rel_x_0_10000"],
                "relativeY": row["center_rel_y_0_10000"],
                "subregionId": row["primary_subregion_id"],
                "subregionName": row["subregion_name"],
                "directionName": row["direction_name"],
                "zoneType": row["zone_type"],
                "primaryLandform": row["primary_landform"],
                "primaryResourceTags": [
                    item for item in row["primary_resource_tags"].split(";") if item
                ],
                "resources": {
                    "agriculture": row["agriculture_resource_0_100"],
                    "forest": row["forest_resource_0_100"],
                    "pasture": row["pasture_resource_0_100"],
                    "fishery": row["fishery_resource_0_100"],
                    "salt": row["salt_resource_0_100"],
                    "fuel": row["fuel_resource_0_100"],
                    "metal": row["metal_resource_0_100"],
                    "buildingMaterial": row["building_material_resource_0_100"],
                },
                "historicalNameClaim": yes_no(row["historical_name_claim"]),
                "historicalBoundaryClaim": yes_no(row["boundary_historical_claim"]),
                "evidenceGrade": row["evidence_grade"],
                "assignmentMethod": row["assignment_method"],
            }
        )

    settlements_raw = rows(
        connection,
        """
        SELECT division_id,division_name,division_type_code,is_county_core,
               membership_method,historical_membership_claim,settlement_id,
               subregion_id,settlement_type_code,settlement_name,name_source_type,
               historical_name_claim,urban_rural,resident_population,
               labor_force_est,relative_x_0_10000,relative_y_0_10000,
               population_allocation_method,subregion_name,direction_name,zone_type
        FROM v_local_division_entry_settlements
        WHERE county_id=?
        ORDER BY division_id,settlement_id
        """,
        (COUNTY_ID,),
    )

    type_labels = {
        "county_seat": "县城",
        "market_town": "镇市",
        "village": "村落",
        "transport_port_station": "港驿",
        "resource_industrial": "产业聚落",
        "military_settlement": "军屯",
    }
    settlements = []
    for row in settlements_raw:
        settlements.append(
            {
                "id": row["settlement_id"],
                "name": row["settlement_name"],
                "type": row["settlement_type_code"],
                "typeLabel": type_labels.get(row["settlement_type_code"], "聚落"),
                "residentPopulation": row["resident_population"],
                "laborForce": row["labor_force_est"],
                "relativeX": row["relative_x_0_10000"],
                "relativeY": row["relative_y_0_10000"],
                "divisionId": row["division_id"],
                "divisionName": row["division_name"],
                "divisionType": row["division_type_code"],
                "subregionId": row["subregion_id"],
                "subregionName": row["subregion_name"],
                "directionName": row["direction_name"],
                "zoneType": row["zone_type"],
                "urbanRural": row["urban_rural"],
                "nameSourceType": row["name_source_type"],
                "historicalNameClaim": yes_no(row["historical_name_claim"]),
                "membershipMethod": row["membership_method"],
                "historicalMembershipClaim": yes_no(row["historical_membership_claim"]),
                "populationAllocationMethod": row["population_allocation_method"],
            }
        )

    historical_rows = rows(
        connection,
        """
        SELECT p.person_id,p.name,p.birth_year,p.birth_year_quality,p.death_year,
               p.death_year_quality,p.age_1628,p.life_stage_1628,p.alive_status_1628,
               p.primary_county_id,p.primary_county_association,
               p.highest_exam_before_1628,p.office_count_before_1628,
               p.highest_office_before_1628,p.person_types_1628,p.gentry_status_1628,
               p.historical_influence_0_100,p.influence_1628_0_100,p.evidence_grade,
               p.source_titles,p.license_status,p.commercial_release_ready,
               a.association_type_code,a.association_type_name,a.present_in_county_1628,
               a.opening_relevance,a.mapping_method,a.evidence_grade AS association_evidence_grade
        FROM person_county_association a
        JOIN historical_person_catalog p USING(person_id)
        WHERE a.county_id=?
        ORDER BY p.influence_1628_0_100 DESC,p.historical_influence_0_100 DESC,p.name,
                 a.association_type_code
        """,
        (COUNTY_ID,),
    )

    historical_people_by_id: dict[str, dict] = {}
    for row in historical_rows:
        person = historical_people_by_id.setdefault(
            row["person_id"],
            {
                "id": row["person_id"],
                "name": row["name"],
                "birthYear": row["birth_year"] or None,
                "birthYearQuality": row["birth_year_quality"],
                "deathYear": row["death_year"] or None,
                "deathYearQuality": row["death_year_quality"],
                "ageAtSnapshot": row["age_1628"] or None,
                "lifeStage": row["life_stage_1628"],
                "aliveStatus": row["alive_status_1628"],
                "primaryCountyId": row["primary_county_id"],
                "primaryCountyAssociation": row["primary_county_association"],
                "highestExamBeforeSnapshot": row["highest_exam_before_1628"],
                "officeCountBeforeSnapshot": row["office_count_before_1628"],
                "highestOfficeBeforeSnapshot": row["highest_office_before_1628"],
                "personTypes": [item for item in row["person_types_1628"].split(";") if item],
                "gentryStatus": row["gentry_status_1628"],
                "historicalInfluence": row["historical_influence_0_100"],
                "openingInfluence": row["influence_1628_0_100"],
                "evidenceGrade": row["evidence_grade"],
                "sourceTitles": [item for item in row["source_titles"].split(";") if item],
                "licenseStatus": row["license_status"],
                "commercialReleaseReady": yes_no(row["commercial_release_ready"]),
                "associations": [],
            },
        )
        person["associations"].append(
            {
                "type": row["association_type_code"],
                "name": row["association_type_name"],
                "presentAtSnapshot": yes_no(row["present_in_county_1628"]),
                "openingRelevance": yes_no(row["opening_relevance"]),
                "mappingMethod": row["mapping_method"],
                "evidenceGrade": row["association_evidence_grade"],
            }
        )

    quota_codes = (
        "magistrate_official",
        "county_assistant_official",
        "clerk",
        "tax_grain_agent",
        "county_school_teacher",
    )
    placeholders = ",".join("?" for _ in quota_codes)
    role_quotas = rows(
        connection,
        f"""
        SELECT occupation_code,occupation_name_zh_hans,sector_code,
               worker_count_est,worker_share_ppm,evidence_type,estimation_method
        FROM county_occupation_quota
        WHERE county_id=? AND occupation_code IN ({placeholders})
        ORDER BY occupation_code
        """,
        (COUNTY_ID, *quota_codes),
    )

    coordinates = [
        (row["relativeX"], row["relativeY"])
        for row in settlements
    ]
    focus = next(row for row in settlements if row["id"] == FOCUS_SETTLEMENT_ID)
    county_seat = next(row for row in settlements if row["id"] == COUNTY_SEAT_ID)
    assert len(divisions) == 40
    assert sum(division["type"] == "town" for division in divisions) == 9
    assert sum(division["type"] == "township" for division in divisions) == 31
    assert focus["divisionId"] == "MING1628-0205-LD033"
    assert county_seat["divisionId"] == "MING1628-0205-LD001"

    payload = {
        "metadata": {
            "schemaVersion": "project_realm_xiao_county_prototype_v1",
            "countyId": COUNTY_ID,
            "countyName": "萧县",
            "sourceDatabase": args.database.name,
            "sourceUserVersion": connection.execute("PRAGMA user_version").fetchone()[0],
            "commercialReleaseReady": False,
            "superiorGeographyIncluded": False,
            "evidenceBoundary": "乡镇名称、边界和聚落隶属默认为确定性计算投影，不包装为确切史实。",
        },
        "coordinateBounds": {
            "minX": min(x for x, _ in coordinates),
            "maxX": max(x for x, _ in coordinates),
            "minY": min(y for _, y in coordinates),
            "maxY": max(y for _, y in coordinates),
        },
        "subregions": subregions,
        "divisions": divisions,
        "settlements": settlements,
        "administration": {
            "levels": [
                {
                    "id": "county",
                    "label": "县",
                    "name": "萧县县署",
                    "nature": "正式县级行政",
                "authority": "县署处理钱粮、文书、治安与县署请求；普通村民不能读取县级总账。",
                },
                {
                    "id": "local_division",
                    "label": "乡／镇",
                    "name": "乡镇计算区",
                    "nature": "县内运算与导航层",
                    "authority": "作为人口、资源与聚落汇总入口，不自动等同史实行政机构。",
                },
                {
                    "id": "settlement",
                    "label": "聚落",
                    "name": "县城、镇市与村落",
                    "nature": "人物实际活动与事件发生层",
                    "authority": "里甲、村庄首事、宗族与行业关系只在有角色或关系时生效。",
                },
            ],
            "roleQuotaProjection": role_quotas,
            "warning": "职业配额是结构投影，不代表同时在任的官缺数量，也不能替代具体人物证据。",
        },
        "historicalPeople": list(historical_people_by_id.values()),
        "validation": {
            "divisionCount": len(divisions),
            "townCount": sum(division["type"] == "town" for division in divisions),
            "townshipCount": sum(division["type"] == "township" for division in divisions),
            "subregionCount": len(subregions),
            "settlementCount": len(settlements),
            "historicalPersonCount": len(historical_people_by_id),
            "focusHierarchy": ["萧县", "南江桥乡", "七里村"],
            "countySeatHierarchy": ["萧县", "萧城关镇", "萧县城"],
        },
    }

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(
        json.dumps(
            {
                "output": str(args.output),
                "divisions": len(divisions),
                "settlements": len(settlements),
                "historicalPeople": len(historical_people_by_id),
            },
            ensure_ascii=False,
        )
    )


if __name__ == "__main__":
    main()
