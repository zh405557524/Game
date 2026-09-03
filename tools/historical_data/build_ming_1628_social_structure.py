#!/usr/bin/env python3
"""Build Project Realm's education, occupation and social structure data v0.6.

The builder inherits the pinned v0.4 SQLite database, keeps every v0.3 village
identifier/name, repartitions population exactly across a unified settlement
catalog, and installs deterministic county/settlement query interfaces.  All
ordinary-person and unsourced-place outputs are game projections and never
assert historical identity.
"""

from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import csv
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import sqlite3
import statistics
import time
from typing import Any, Iterable, Iterator, Sequence


REPO_ROOT = Path(__file__).resolve().parents[2]
DATA_ROOT = REPO_ROOT / "docs/90_资料与归档/01_崇祯元年历史资料/data/1628"
DEFAULT_CULTURE_DIR = DATA_ROOT / "7.县级文化家族乡绅教育与人物"
DEFAULT_OUTPUT_DIR = DATA_ROOT / "9.教育职业身份与社会阶层"
DEFAULT_SOURCE_DATABASE = DEFAULT_CULTURE_DIR / "game_world_1628_v0.4.sqlite"
DEFAULT_CBDB_DATABASE = REPO_ROOT / "tmp/research/ming_culture_v0.4/cbdb_20260822.sqlite3"
PINNED_CBDB_SHA256 = "25861a3506ace7163348557f1ba0f59ef24cbe49f408f8cdde3041bd0083dffb"
RULESET_VERSION = "v0.6"
SNAPSHOT_YEAR = 1628
WEIGHT_TOTAL = 1_000_000
EXPECTED_COUNTIES = 1_168
EXPECTED_VILLAGES = 505_684
EXPECTED_OCCUPATIONS = 150
EXPECTED_OCCUPATION_ROWS = EXPECTED_COUNTIES * EXPECTED_OCCUPATIONS
EXPECTED_TOTAL_POPULATION = 209_249_000
COMMERCIAL_RELEASE_READY = "no"


SECTORS = [
  ("agriculture", "农业", 620.0),
  ("forestry_hunting", "林猎", 25.0),
  ("pastoral", "畜牧", 30.0),
  ("fishery_water", "渔业水产", 25.0),
  ("mining_salt", "矿盐", 18.0),
  ("food_processing", "食品加工", 35.0),
  ("textile_clothing", "纺织服饰", 55.0),
  ("ceramics_building", "陶瓷建材", 35.0),
  ("metal_wood_paper", "金木纸作", 45.0),
  ("transport_post_port", "交通驿运", 28.0),
  ("commerce_finance", "商业金融", 35.0),
  ("domestic_service", "生活服务", 20.0),
  ("medicine_health", "医药", 5.0),
  ("religion_ritual", "宗教礼仪", 4.0),
  ("education_culture", "教育文化", 8.0),
  ("government_admin", "官署行政", 7.0),
  ("military_security", "军事治安", 8.0),
  ("marginal_unfixed", "无定业边缘生计", 5.0),
]
SECTOR_NAMES = {code: name for code, name, _ in SECTORS}
SECTOR_PRIORS = {code: weight for code, _, weight in SECTORS}
SECTOR_CODES = [code for code, _, _ in SECTORS]


# sector, industry code/name, occupation code/name, relative weight
OCCUPATION_CATALOG: list[tuple[str, str, str, str, str, float]] = [
  ("agriculture", "grain", "粮作", "grain_farmer", "粮农", 18),
  ("agriculture", "grain", "粮作", "rice_farmer", "稻农", 12),
  ("agriculture", "grain", "粮作", "wheat_millet_farmer", "麦粟农", 11),
  ("agriculture", "grain", "粮作", "dryland_farmer", "旱地农", 10),
  ("agriculture", "garden", "园艺", "vegetable_gardener", "菜农", 5),
  ("agriculture", "garden", "园艺", "orchard_grower", "果农", 4),
  ("agriculture", "cash_crop", "经济作物", "tea_grower", "茶农", 3),
  ("agriculture", "cash_crop", "经济作物", "sugarcane_grower", "蔗农", 2),
  ("agriculture", "cash_crop", "经济作物", "cotton_grower", "棉农", 5),
  ("agriculture", "cash_crop", "经济作物", "mulberry_grower", "桑农", 4),
  ("agriculture", "cash_crop", "经济作物", "hemp_grower", "麻农", 3),
  ("agriculture", "cash_crop", "经济作物", "oil_crop_grower", "油料作物农", 3),
  ("agriculture", "waterworks", "农田水利", "irrigation_worker", "农田水利工", 3),
  ("agriculture", "labor", "农事劳作", "plowman", "犁手", 4),
  ("agriculture", "labor", "农事劳作", "harvest_worker", "收割短工", 4),
  ("agriculture", "labor", "农事劳作", "farmhand", "长工", 5),
  ("agriculture", "tenure", "租佃经营", "tenant_cultivator", "佃农", 12),
  ("agriculture", "tenure", "租佃经营", "estate_steward", "庄田管事", 2),
  ("forestry_hunting", "timber", "林木", "woodcutter", "樵夫", 18),
  ("forestry_hunting", "timber", "林木", "timber_feller", "伐木工", 14),
  ("forestry_hunting", "timber", "林木", "forest_guard", "山林看守", 5),
  ("forestry_hunting", "fuel", "林产燃料", "charcoal_burner", "烧炭工", 12),
  ("forestry_hunting", "forest_product", "林副产", "resin_collector", "漆脂采集者", 5),
  ("forestry_hunting", "hunting", "狩猎", "hunter", "猎户", 7),
  ("forestry_hunting", "gathering", "采集", "wild_product_gatherer", "山货采集者", 8),
  ("pastoral", "cattle", "大牲畜", "cattle_herder", "牧牛人", 16),
  ("pastoral", "horse", "马政牧养", "horse_herder", "牧马人", 8),
  ("pastoral", "sheep_goat", "羊畜", "sheep_goat_herder", "牧羊人", 14),
  ("pastoral", "pig", "家畜", "pig_raiser", "养猪户", 18),
  ("pastoral", "poultry", "禽畜", "poultry_raiser", "禽畜饲养者", 14),
  ("pastoral", "breeding", "育畜", "animal_breeder", "育畜人", 5),
  ("pastoral", "fodder", "牧草饲料", "fodder_worker", "草料工", 6),
  ("fishery_water", "river_fish", "江河捕捞", "river_fisher", "江河渔民", 18),
  ("fishery_water", "lake_fish", "湖泊捕捞", "lake_fisher", "湖泊渔民", 14),
  ("fishery_water", "sea_fish", "沿海捕捞", "coastal_fisher", "海洋渔民", 12),
  ("fishery_water", "boat_fish", "船居捕捞", "boat_fisher", "船户渔民", 9),
  ("fishery_water", "aquaculture", "水面养殖", "aquaculture_keeper", "鱼塘看守", 6),
  ("fishery_water", "gear", "渔具", "net_maker", "织网匠", 5),
  ("fishery_water", "processing", "水产加工", "fish_processor", "鱼货腌晒工", 6),
  ("mining_salt", "coal", "煤矿", "coal_miner", "煤矿工", 10),
  ("mining_salt", "iron", "铁矿", "iron_miner", "铁矿工", 10),
  ("mining_salt", "copper", "铜矿", "copper_miner", "铜矿工", 5),
  ("mining_salt", "lead_zinc", "铅锌矿", "lead_zinc_miner", "铅锌矿工", 3),
  ("mining_salt", "tin", "锡矿", "tin_miner", "锡矿工", 3),
  ("mining_salt", "precious", "金银矿", "precious_metal_miner", "金银矿工", 2),
  ("mining_salt", "quarry", "采石", "quarry_worker", "采石工", 10),
  ("mining_salt", "sea_salt", "海盐", "salt_boiler", "煎盐灶丁", 8),
  ("mining_salt", "well_salt", "井卤盐", "brine_well_worker", "井盐工", 6),
  ("mining_salt", "salt_logistics", "盐业运输", "salt_transport_worker", "盐场脚夫", 6),
  ("food_processing", "milling", "粮食加工", "miller", "磨坊工", 16),
  ("food_processing", "milling", "粮食加工", "rice_husker", "碾米工", 12),
  ("food_processing", "oil", "榨油", "oil_presser", "榨油工", 10),
  ("food_processing", "brew", "酿造", "brewer", "酿酒工", 10),
  ("food_processing", "condiment", "酱作", "soy_sauce_maker", "酱坊工", 6),
  ("food_processing", "bean", "豆制品", "tofu_maker", "豆腐坊工", 7),
  ("food_processing", "meat", "屠宰", "butcher", "屠户", 7),
  ("food_processing", "flour_food", "面食", "noodle_baker", "面食作坊工", 8),
  ("food_processing", "tea", "制茶", "tea_processor", "制茶工", 5),
  ("textile_clothing", "cotton", "棉纺织", "cotton_spinner", "棉纺工", 16),
  ("textile_clothing", "cotton", "棉纺织", "cotton_weaver", "棉织工", 14),
  ("textile_clothing", "silk", "丝织", "silk_reeler", "缫丝工", 9),
  ("textile_clothing", "silk", "丝织", "silk_weaver", "丝织工", 9),
  ("textile_clothing", "hemp", "麻纺织", "hemp_spinner", "麻纺工", 7),
  ("textile_clothing", "hemp", "麻纺织", "hemp_weaver", "麻织工", 7),
  ("textile_clothing", "finishing", "染整", "dyer", "染匠", 6),
  ("textile_clothing", "finishing", "染整", "fuller", "练布工", 4),
  ("textile_clothing", "clothing", "衣作", "tailor", "裁缝", 7),
  ("textile_clothing", "clothing", "衣作", "embroiderer", "绣工", 4),
  ("textile_clothing", "footwear", "鞋履", "shoemaker", "鞋匠", 5),
  ("ceramics_building", "pottery", "陶器", "potter", "陶工", 12),
  ("ceramics_building", "porcelain", "瓷业", "porcelain_worker", "瓷工", 9),
  ("ceramics_building", "kiln", "窑业", "kiln_fireman", "窑工", 9),
  ("ceramics_building", "brick_tile", "砖瓦", "brick_tile_maker", "砖瓦工", 12),
  ("ceramics_building", "masonry", "营造", "mason", "泥瓦匠", 13),
  ("ceramics_building", "stone", "石作", "stonecutter", "石匠", 9),
  ("ceramics_building", "timber_build", "木构营造", "carpenter_builder", "大木匠", 10),
  ("ceramics_building", "finish", "营造", "plasterer", "圬工", 5),
  ("ceramics_building", "waterwell", "井作", "well_digger", "井匠", 4),
  ("metal_wood_paper", "ironwork", "铁作", "blacksmith", "铁匠", 14),
  ("metal_wood_paper", "foundry", "冶铸", "foundry_worker", "冶铸工", 9),
  ("metal_wood_paper", "nonferrous", "铜锡作", "coppersmith", "铜匠", 7),
  ("metal_wood_paper", "nonferrous", "铜锡作", "tinsmith", "锡匠", 4),
  ("metal_wood_paper", "precious", "金银作", "silversmith", "金银匠", 4),
  ("metal_wood_paper", "tools", "器具", "toolmaker", "农具匠", 8),
  ("metal_wood_paper", "woodcraft", "木器", "furniture_carpenter", "细木匠", 10),
  ("metal_wood_paper", "bamboo", "竹器", "bamboo_craftsman", "竹匠", 7),
  ("metal_wood_paper", "paper", "纸业", "papermaker", "纸工", 7),
  ("metal_wood_paper", "printing", "印刷", "printer", "刻印工", 5),
  ("metal_wood_paper", "printing", "装帧", "bookbinder", "装订工", 3),
  ("metal_wood_paper", "lacquer", "漆器", "lacquerware_maker", "漆器工", 4),
  ("transport_post_port", "portage", "脚力", "porter", "脚夫", 17),
  ("transport_post_port", "cart", "车运", "cart_driver", "车夫", 12),
  ("transport_post_port", "pack", "驮运", "pack_animal_driver", "驮夫", 9),
  ("transport_post_port", "boat", "水运", "boatman", "船工", 15),
  ("transport_post_port", "sea", "海运", "sailor", "海船水手", 7),
  ("transport_post_port", "canal", "河渠", "canal_worker", "河渠工", 7),
  ("transport_post_port", "ferry", "渡运", "ferryman", "渡夫", 7),
  ("transport_post_port", "dock", "码头", "dockworker", "码头装卸工", 8),
  ("transport_post_port", "post", "驿递", "courier", "铺兵递夫", 6),
  ("transport_post_port", "inn_stable", "旅舍马店", "inn_stable_worker", "马店伙计", 7),
  ("commerce_finance", "peddling", "行商零售", "peddler", "货郎", 13),
  ("commerce_finance", "market", "集市零售", "market_vendor", "集市商贩", 15),
  ("commerce_finance", "grain", "粮商", "grain_merchant", "粮商", 8),
  ("commerce_finance", "cloth", "布帛商", "cloth_merchant", "布商", 7),
  ("commerce_finance", "salt", "盐商", "salt_merchant", "盐商", 4),
  ("commerce_finance", "timber", "木材商", "timber_merchant", "木材商", 4),
  ("commerce_finance", "shop", "坐商", "shopkeeper", "铺户", 13),
  ("commerce_finance", "broker", "牙行", "broker", "牙人", 7),
  ("commerce_finance", "accounts", "账房", "accountant", "账房", 7),
  ("commerce_finance", "credit", "典当钱业", "pawnbroker_moneychanger", "典当钱业从业者", 4),
  ("domestic_service", "food_service", "饮食服务", "cook", "厨役", 12),
  ("domestic_service", "lodging", "旅舍", "innkeeper", "店家", 10),
  ("domestic_service", "household", "家内服务", "domestic_servant", "家仆", 18),
  ("domestic_service", "laundry", "洗补", "washer_seamstress", "洗补妇", 12),
  ("domestic_service", "grooming", "修面理发", "barber", "剃头匠", 7),
  ("domestic_service", "bath", "浴堂", "bathhouse_attendant", "浴堂伙计", 4),
  ("domestic_service", "performance", "演艺", "entertainer", "伎艺人", 6),
  ("medicine_health", "medicine", "行医", "physician", "医生", 13),
  ("medicine_health", "herbs", "药材", "herbalist", "采药识药者", 12),
  ("medicine_health", "pharmacy", "药铺", "pharmacist", "药铺掌柜", 9),
  ("medicine_health", "birth", "产育", "midwife", "稳婆", 10),
  ("medicine_health", "trauma", "伤科", "bone_setter", "接骨伤科医", 7),
  ("religion_ritual", "buddhist", "佛教", "buddhist_monk", "僧人", 12),
  ("religion_ritual", "daoist", "道教", "daoist_priest", "道士", 10),
  ("religion_ritual", "ritual", "民间礼仪", "ritual_specialist", "礼生法事从业者", 8),
  ("religion_ritual", "temple", "寺观照管", "temple_caretaker", "庙祝香火看守", 8),
  ("religion_ritual", "calendar", "阴阳术数", "fortune_calendar_practitioner", "阴阳生术数者", 7),
  ("education_culture", "basic_school", "蒙学", "village_teacher", "村塾师", 15),
  ("education_culture", "family_school", "家塾", "family_tutor", "家塾师", 10),
  ("education_culture", "official_school", "官学", "county_school_teacher", "府州县学教习", 6),
  ("education_culture", "academy", "书院", "academy_teacher", "书院讲习", 5),
  ("education_culture", "writing", "抄写", "copyist", "抄书人", 9),
  ("education_culture", "oral_culture", "说唱", "storyteller", "说书艺人", 7),
  ("education_culture", "art", "书画", "painter_calligrapher", "书画艺人", 5),
  ("government_admin", "magistracy", "县署官职", "magistrate_official", "县署正官", 2),
  ("government_admin", "magistracy", "县署佐贰", "county_assistant_official", "县署佐贰首领", 3),
  ("government_admin", "clerical", "六房书办", "clerk", "书吏", 14),
  ("government_admin", "runner", "衙门差役", "yamen_runner", "衙役", 16),
  ("government_admin", "tax", "钱粮职役", "tax_grain_agent", "粮役税役", 10),
  ("government_admin", "community", "里甲乡约", "community_head", "里甲首事", 10),
  ("military_security", "garrison", "卫所军役", "garrison_soldier", "卫所军士", 18),
  ("military_security", "cavalry", "骑军", "cavalryman", "骑军", 6),
  ("military_security", "archery", "弓兵", "archer", "弓兵", 8),
  ("military_security", "watch", "巡守", "guard_watchman", "守望巡丁", 12),
  ("military_security", "militia", "乡兵", "militia_member", "乡兵民壮", 12),
  ("military_security", "fortification", "城防工役", "fortification_worker", "城防工役", 8),
  ("marginal_unfixed", "casual", "零散劳作", "itinerant_laborer", "流动短工", 20),
  ("marginal_unfixed", "begging", "乞讨", "beggar", "乞者", 6),
  ("marginal_unfixed", "vagrant", "流寓无业", "vagrant", "流民无定业者", 8),
  ("marginal_unfixed", "illicit", "私贩", "illicit_trader", "私贩者", 5),
]


EDUCATION_DEFINITIONS = [
  ("literacy_level", "L0", "不识字", 0, 0, 0, 0, 0, 0, "没有稳定文字能力"),
  ("literacy_level", "L1", "常用字辨识", 1, 18, 4, 8, 0, 2, "能辨识少量姓名、地名和常用字"),
  ("literacy_level", "L2", "实用阅读", 2, 38, 18, 28, 4, 12, "能读简短告示、账目和书信"),
  ("literacy_level", "L3", "读写文书", 3, 62, 55, 58, 22, 55, "能书写契约、账册和普通公文"),
  ("literacy_level", "L4", "经典读写", 4, 82, 78, 60, 75, 68, "具备经典教育或相当文字训练"),
  ("education_route", "none", "未受正式教育", 0, 0, 0, 0, 0, 0, "家庭生产中获得非文字技能"),
  ("education_route", "home_learning", "家庭启蒙", 1, 0, 0, 0, 0, 0, "由家庭成员启蒙"),
  ("education_route", "village_school", "村塾", 2, 0, 0, 0, 0, 0, "村内蒙学或私塾"),
  ("education_route", "family_school", "家塾", 2, 0, 0, 0, 0, 0, "富户或士绅家庭聘师"),
  ("education_route", "lineage_school", "族学", 2, 0, 0, 0, 0, 0, "宗族共同维持的教育"),
  ("education_route", "community_school", "社学", 2, 0, 0, 0, 0, 0, "地方社学或类似公共蒙学"),
  ("education_route", "official_local_school", "府州县学", 3, 0, 0, 0, 0, 0, "地方官学体系"),
  ("education_route", "academy", "书院", 3, 0, 0, 0, 0, 0, "书院讲习或肄业"),
  ("education_route", "imperial_academy", "国子监", 4, 0, 0, 0, 0, 0, "国学或国子监经历"),
  ("education_route", "apprenticeship", "师徒传习", 2, 0, 0, 0, 0, 0, "工艺、商业或职业技能传习"),
  ("education_route", "medical_religious", "医药或宗教传习", 2, 0, 0, 0, 0, 0, "专业师承或寺观训练"),
  ("credential", "none", "无功名", 0, 0, 0, 0, 0, 0, "没有正式功名"),
  ("credential", "candidate", "应试者", 1, 0, 0, 0, 0, 0, "具有应试准备但未取得功名"),
  ("credential", "shengyuan", "生员", 2, 0, 0, 0, 0, 0, "府州县学诸生"),
  ("credential", "gongsheng", "贡生", 3, 0, 0, 0, 0, 0, "由地方贡入国学"),
  ("credential", "jiansheng", "监生", 3, 0, 0, 0, 0, 0, "国子监学生"),
  ("credential", "juren", "举人", 4, 0, 0, 0, 0, 0, "乡试中式"),
  ("credential", "gongshi", "贡士", 5, 0, 0, 0, 0, 0, "会试中式、殿试前资格"),
  ("credential", "jinshi", "进士", 6, 0, 0, 0, 0, 0, "殿试取得进士"),
  ("credential", "military_shengyuan", "武生员", 2, 0, 0, 0, 0, 0, "武学或武科生员"),
  ("credential", "military_juren", "武举人", 4, 0, 0, 0, 0, 0, "武乡试中式"),
  ("credential", "military_jinshi", "武进士", 6, 0, 0, 0, 0, 0, "武科进士"),
  ("education_status", "not_enrolled", "未受教", 0, 0, 0, 0, 0, 0, "当前无固定教育活动"),
  ("education_status", "studying", "在学", 1, 0, 0, 0, 0, 0, "当前正在学习"),
  ("education_status", "interrupted", "间断学习", 1, 0, 0, 0, 0, 0, "因家计等原因中断"),
  ("education_status", "completed", "已结业", 2, 0, 0, 0, 0, 0, "已完成当前层次训练"),
  ("education_status", "examining", "应试", 3, 0, 0, 0, 0, 0, "处于科举应试阶段"),
  ("education_status", "teaching", "授业", 4, 0, 0, 0, 0, 0, "以教授或传习为业"),
]


SOCIAL_STATUS_DEFINITIONS = [
  ("registration", "civilian", "民户", 0, "民籍及一般民户"),
  ("registration", "military", "军户", 0, "军籍或卫籍；不等同当前职业"),
  ("registration", "artisan", "匠户", 0, "匠籍或军匠、民匠等"),
  ("registration", "salt", "灶盐户", 0, "灶籍、盐籍及军灶籍"),
  ("registration", "fish_boat", "渔船户", 0, "渔业、船居或马船相关登记"),
  ("registration", "post_transport", "驿运户", 0, "站籍、铺兵、驿递等役务来源"),
  ("registration", "medical_ritual", "医阴阳户", 0, "医籍、阴阳籍及相关专业登记"),
  ("registration", "literary_student", "儒学生监户", 0, "儒籍、生员籍、监籍"),
  ("registration", "official_security", "官校役籍", 0, "官籍、校尉、弓兵、力士等"),
  ("registration", "mixed_unknown", "混合或不详", 0, "复合身份、附籍或资料不足"),
  ("economic", "dependent_bonded", "依附奴仆", 1, "依附于主家的家仆、奴婢或类似人口"),
  ("economic", "landless_labor", "无地雇工", 2, "主要依靠短工、长工或零散劳动"),
  ("economic", "tenant", "佃户", 3, "租种土地并承担租佃义务"),
  ("economic", "smallholder", "小自耕户", 4, "拥有少量土地或生产资料"),
  ("economic", "stable_proprietor", "稳定业主", 5, "稳定自耕或独立手工业经营"),
  ("economic", "wealthy_master", "富裕业主或作坊主", 6, "富农、作坊主或较大商户"),
  ("economic", "landlord_merchant_capital", "大地主或大商人", 7, "拥有显著土地、商业或雇佣资本"),
  ("prestige", "commoner", "普通人", 0, "无额外地方声望标记"),
  ("prestige", "skilled", "熟练工匠或商人", 1, "因专业技能或经营能力获得声望"),
  ("prestige", "literate", "识字者", 2, "具备实用文字能力"),
  ("prestige", "local_elder", "地方长者", 3, "年高、富望或为乡里所重"),
  ("prestige", "student", "读书应试者", 4, "读书、应试但未取得正式功名"),
  ("prestige", "degree_holder", "有功名者", 5, "生员及以上功名"),
  ("prestige", "official", "官员", 6, "当前担任正式官职"),
  ("prestige", "retired_official", "退居官员", 6, "致仕、罢官或退居地方"),
  ("prestige", "religious_medical", "医药宗教名望", 3, "以医术、宗教或礼仪专业受重"),
  ("local_power", "none", "无地方权力角色", 0, "未标记地方权力"),
  ("local_power", "household_head", "户长", 1, "家庭户主或主要决策者"),
  ("local_power", "clan_elder", "族长或族中长者", 2, "宗族内部领导"),
  ("local_power", "village_headman", "村中首事", 2, "村落公共事务首事"),
  ("local_power", "lijia_service", "里甲职役", 3, "里长、甲首或相关职役"),
  ("local_power", "grain_head", "粮长", 3, "钱粮征解相关地方角色"),
  ("local_power", "market_guild_head", "市场或行业首事", 3, "市场、会馆或行业网络首事"),
  ("local_power", "yamen_broker", "衙门中介", 3, "书吏、衙役或诉讼钱粮中介"),
  ("local_power", "local_official", "地方官员", 4, "正式地方官职"),
]


SETTLEMENT_ARCHETYPES = [
  ("village", "自然村", "rural", 1, "现有v0.3村庄，保留ID、名称和位置"),
  ("market_town", "镇市", "urban", 2, "县域内集市与专业镇市"),
  ("county_seat", "县城", "urban", 3, "每县固定一个县治人口节点"),
  ("military_settlement", "军事聚落", "rural", 2, "营堡、屯戍或军役聚居代理"),
  ("resource_industrial", "资源产业聚落", "rural", 2, "矿场、盐场、窑场或林场聚居代理"),
  ("transport_port_station", "交通港驿", "rural", 2, "码头、渡口、驿铺或交通节点聚居代理"),
]


POI_DEFINITIONS = [
  ("county_yamen", "县署", "government_admin", "county_seat", 35),
  ("official_school", "府州县学", "education_culture", "county_seat", 24),
  ("academy", "书院", "education_culture", "county_seat;market_town", 18),
  ("village_school", "村塾家塾", "education_culture", "village;market_town", 12),
  ("market", "集市", "commerce_finance", "village;market_town;county_seat", 50),
  ("guild_hall", "会馆行业公所", "commerce_finance", "market_town;county_seat", 30),
  ("workshop", "作坊", "metal_wood_paper", "village;market_town;county_seat;resource_industrial", 25),
  ("kiln", "窑场", "ceramics_building", "resource_industrial", 45),
  ("mine", "矿场", "mining_salt", "resource_industrial", 60),
  ("saltworks", "盐场", "mining_salt", "resource_industrial", 60),
  ("dock", "码头渡口", "transport_post_port", "market_town;transport_port_station;county_seat", 45),
  ("post_station", "驿站递铺", "transport_post_port", "transport_port_station;county_seat", 30),
  ("military_compound", "营堡军营", "military_security", "military_settlement;county_seat", 70),
  ("clinic_pharmacy", "医馆药铺", "medicine_health", "market_town;county_seat", 16),
  ("temple_monastery", "寺观祠庙", "religion_ritual", "village;market_town;county_seat", 18),
]


REGISTRATION_CODES = [
  "civilian", "military", "artisan", "salt", "fish_boat",
  "post_transport", "medical_ritual", "literary_student",
  "official_security", "mixed_unknown",
]
CBDB_HOUSEHOLD_ROLLUP = {
  0: "mixed_unknown", 1: "civilian", 2: "military", 3: "artisan",
  4: "official_security", 5: "salt", 6: "literary_student",
  7: "official_security", 8: "military", 9: "medical_ritual",
  10: "official_security", 11: "official_security", 12: "official_security",
  13: "civilian", 14: "military", 15: "military", 16: "military",
  17: "official_security", 18: "official_security", 19: "salt",
  20: "literary_student", 21: "literary_student", 23: "post_transport",
  24: "military", 26: "medical_ritual", 27: "official_security",
  28: "medical_ritual", 29: "post_transport", 30: "salt",
  31: "mixed_unknown", 32: "mixed_unknown", 33: "mixed_unknown",
  34: "mixed_unknown", 35: "mixed_unknown",
}
ECONOMIC_CODES = [
  "dependent_bonded", "landless_labor", "tenant", "smallholder",
  "stable_proprietor", "wealthy_master", "landlord_merchant_capital",
]


EDUCATION_COLUMNS = [
  "definition_type", "definition_code", "display_name_zh_hans", "level_order",
  "reading_floor_0_100", "writing_floor_0_100", "numeracy_floor_0_100",
  "classics_floor_0_100", "document_floor_0_100", "description",
  "source_type", "source_url", "commercial_release_ready",
]
OCCUPATION_COLUMNS = [
  "occupation_code", "sector_code", "sector_name_zh_hans", "industry_code",
  "industry_name_zh_hans", "occupation_name_zh_hans", "original_term_zh_hant",
  "relative_weight", "minimum_age", "maximum_age", "male_weight", "female_weight",
  "minimum_literacy_level", "minimum_numeracy_0_100", "minimum_classics_0_100",
  "settlement_affinity", "registration_affinity", "secondary_allowed",
  "primary_driver", "evidence_grade", "source_url", "commercial_release_ready",
]
SOCIAL_COLUMNS = [
  "axis_code", "status_code", "display_name_zh_hans", "level_order", "description",
  "source_type", "source_url", "commercial_release_ready",
]
CBDB_HOUSEHOLD_MAPPING_COLUMNS = [
  "cbdb_household_status_code", "source_label_zh_hant", "source_label_en",
  "registration_rollup_code", "registration_rollup_name_zh_hans",
  "mapping_note", "source_person_count_all_periods",
  "mapped_person_count_in_v04_catalog", "source_database_sha256",
  "evidence_boundary", "commercial_release_ready",
]
ARCHETYPE_COLUMNS = [
  "settlement_type_code", "display_name_zh_hans", "urban_rural", "hierarchy_depth",
  "description", "population_policy", "historical_claim_policy", "commercial_release_ready",
]
POI_COLUMNS = [
  "poi_type_code", "display_name_zh_hans", "sector_code", "allowed_settlement_types",
  "default_capacity", "resident_population_policy", "historical_claim_policy",
  "commercial_release_ready",
]
COUNTY_EDUCATION_COLUMNS = [
  "county_id", "snapshot_year", "region", "upper_unit", "intermediate_unit", "county",
  "population_est_1628", "labor_force_est", "male_population_est", "female_population_est",
  "male_literate_est", "female_literate_est", "total_literate_est", "classical_educated_est",
  "literacy_l0_count", "literacy_l1_count", "literacy_l2_count", "literacy_l3_count",
  "literacy_l4_count", "reading_skill_avg_0_100", "writing_skill_avg_0_100",
  "numeracy_skill_avg_0_100", "classics_skill_avg_0_100", "document_skill_avg_0_100",
  "candidate_est", "shengyuan_est", "gongsheng_est", "jiansheng_est", "juren_est",
  "gongshi_est", "jinshi_est", "military_degree_est", "education_degree_1628_0_100",
  "imperial_exam_culture_0_100", "verified_school_count", "verified_academy_count",
  "data_coverage_0_100", "estimation_method", "commercial_release_ready",
]
COUNTY_SOCIAL_COLUMNS = [
  "county_id", "snapshot_year", "region", "upper_unit", "intermediate_unit", "county",
  "population_est_1628", "household_count_est", "labor_force_est",
  *[f"registration_{code}_share_ppm" for code in REGISTRATION_CODES],
  *[f"economic_{code}_share_ppm" for code in ECONOMIC_CODES],
  "household_production_share_0_100", "wage_labor_share_0_100",
  "tenant_household_share_0_100", "dependent_population_share_0_100",
  "unfixed_livelihood_share_0_100", "prestige_mobility_0_100",
  "local_power_concentration_0_100", "registration_estimation_method",
  "economic_estimation_method", "cbdb_household_status_record_count",
  "cbdb_household_evidence_weight_0_100", "cbdb_household_status_codes_present",
  "cbdb_household_mapping_version", "commercial_release_ready",
]
COUNTY_OCCUPATION_COLUMNS = [
  "county_id", "snapshot_year", "region", "upper_unit", "intermediate_unit", "county",
  "occupation_code", "sector_code", "occupation_name_zh_hans", "worker_count_est",
  "worker_share_ppm", "raw_weight", "primary_driver", "evidence_type",
  "estimation_method", "commercial_release_ready",
]
COUNTY_OVERVIEW_COLUMNS = [
  "county_id", "snapshot_year", "region", "upper_unit", "intermediate_unit", "county",
  "population_est_1628", "labor_force_est", "total_literate_est", "classical_educated_est",
  "male_literacy_mid_pct", "female_literacy_mid_pct", "education_degree_1628_0_100",
  "top_occupation_1", "top_occupation_2", "top_occupation_3", "top_occupation_4",
  "top_sector_1", "top_sector_2", "top_sector_3", "dominant_registration_status",
  "dominant_economic_stratum", "local_power_concentration_0_100",
  "commercial_release_ready",
]
SETTLEMENT_COLUMNS = [
  "settlement_id", "snapshot_year", "county_id", "subregion_id", "settlement_type_code",
  "settlement_name", "name_source_type", "historical_name_claim", "source_village_id",
  "urban_rural", "resident_population", "labor_force_est", "relative_x_0_10000",
  "relative_y_0_10000", "render_seed", "population_allocation_method",
  "commercial_release_ready",
]
ZONE_COLUMNS = [
  "zone_id", "snapshot_year", "settlement_id", "county_id", "zone_name", "zone_type",
  "resident_population", "labor_force_est", "relative_x_0_10000", "relative_y_0_10000",
  "render_seed", "historical_claim", "commercial_release_ready",
]
POI_CATALOG_COLUMNS = [
  "poi_id", "snapshot_year", "settlement_id", "zone_id", "county_id", "poi_type_code",
  "poi_name", "capacity_est", "workforce_slots_est", "name_source_type",
  "historical_claim", "location_precision", "render_seed", "commercial_release_ready",
]
SECTOR_QUOTA_COLUMNS = [
  "settlement_id", "county_id", "labor_force_est",
  *[f"{code}_count" for code in SECTOR_CODES],
]


def clamp(value: float, low: float, high: float) -> float:
  return max(low, min(high, value))


def stable_digest(*parts: Any) -> bytes:
  value = "|".join([RULESET_VERSION, *(str(part) for part in parts)])
  return hashlib.sha256(value.encode("utf-8")).digest()


def stable_unit(*parts: Any) -> float:
  return int.from_bytes(stable_digest(*parts)[:8], "big") / (2**64 - 1)


def stable_int(low: int, high: int, *parts: Any) -> int:
  if high < low:
    raise ValueError(f"invalid deterministic range: {low}..{high}")
  return low + int(stable_unit(*parts) * (high - low + 1)) % (high - low + 1)


def allocate_exact(total: int, weights: Sequence[float], minimum: int = 0) -> list[int]:
  if not weights:
    return []
  if minimum * len(weights) > total:
    minimum = 0
  clean = [max(0.0, float(value)) for value in weights]
  if sum(clean) <= 0:
    clean = [1.0] * len(clean)
  remaining = total - minimum * len(clean)
  exact = [remaining * value / sum(clean) for value in clean]
  result = [minimum + math.floor(value) for value in exact]
  remainder = total - sum(result)
  order = sorted(
    range(len(exact)),
    key=lambda index: (-(exact[index] - math.floor(exact[index])), index),
  )
  for index in order[:remainder]:
    result[index] += 1
  if sum(result) != total:
    raise RuntimeError("largest-remainder allocation failed")
  return result


def file_sha256(path: Path) -> str:
  digest = hashlib.sha256()
  with path.open("rb") as stream:
    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
      digest.update(chunk)
  return digest.hexdigest()


def safe_name(value: str) -> str:
  return re.sub(r"[^0-9A-Za-z_\-\u3400-\u9fff]+", "_", value).strip("_") or "data"


def write_csv_atomic(path: Path, columns: Sequence[str], rows: Iterable[dict[str, Any]]) -> None:
  path.parent.mkdir(parents=True, exist_ok=True)
  temporary = path.with_suffix(path.suffix + ".tmp")
  with temporary.open("w", encoding="utf-8", newline="") as stream:
    writer = csv.DictWriter(stream, fieldnames=list(columns), extrasaction="ignore")
    writer.writeheader()
    for row in rows:
      writer.writerow({column: row.get(column, "") for column in columns})
  temporary.replace(path)


def write_json_atomic(path: Path, value: Any) -> None:
  path.parent.mkdir(parents=True, exist_ok=True)
  temporary = path.with_suffix(path.suffix + ".tmp")
  temporary.write_text(
    json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
    encoding="utf-8",
  )
  temporary.replace(path)


def read_csv(path: Path) -> list[dict[str, str]]:
  if not path.exists():
    return []
  with path.open("r", encoding="utf-8-sig", newline="") as stream:
    return list(csv.DictReader(stream))


def rows_as_dicts(cursor: sqlite3.Cursor) -> list[dict[str, Any]]:
  return [dict(row) for row in cursor.fetchall()]


def load_cbdb_household_evidence(
  source: sqlite3.Connection,
  cbdb_database: Path,
) -> tuple[list[dict[str, Any]], dict[str, dict[str, Any]]]:
  cbdb_hash = file_sha256(cbdb_database)
  if cbdb_hash != PINNED_CBDB_SHA256:
    raise RuntimeError(
      f"CBDB database hash mismatch: expected {PINNED_CBDB_SHA256}, got {cbdb_hash}"
    )
  cbdb = sqlite3.connect(f"file:{cbdb_database}?mode=ro", uri=True)
  cbdb.row_factory = sqlite3.Row
  try:
    code_rows = rows_as_dicts(cbdb.execute(
      "SELECT h.c_household_status_code,h.c_household_status_desc_chn,h.c_household_status_desc,"
      "COUNT(b.c_personid) source_person_count_all_periods "
      "FROM HOUSEHOLD_STATUS_CODES h LEFT JOIN BIOG_MAIN b USING(c_household_status_code) "
      "GROUP BY h.c_household_status_code,h.c_household_status_desc_chn,h.c_household_status_desc "
      "ORDER BY h.c_household_status_code"
    ))
    actual_codes = {int(row["c_household_status_code"]) for row in code_rows}
    missing_rollups = actual_codes - set(CBDB_HOUSEHOLD_ROLLUP)
    if missing_rollups:
      raise RuntimeError(f"unmapped CBDB household status codes: {sorted(missing_rollups)}")
    status_by_person = {
      int(person_id): int(status_code or 0)
      for person_id, status_code in cbdb.execute(
        "SELECT c_personid,c_household_status_code FROM BIOG_MAIN"
      )
    }
  finally:
    cbdb.close()

  mapped_catalog_counts: Counter[int] = Counter()
  county_rollup_counts: dict[str, Counter[str]] = defaultdict(Counter)
  county_source_codes: dict[str, set[int]] = defaultdict(set)
  for row in source.execute(
    "SELECT cbdb_person_id,primary_county_id FROM historical_person_catalog "
    "WHERE primary_county_id<>'' ORDER BY cbdb_person_id"
  ):
    code = status_by_person.get(int(row[0]), 0)
    mapped_catalog_counts[code] += 1
    if code == 0:
      continue
    county_id = str(row[1])
    rollup = CBDB_HOUSEHOLD_ROLLUP[code]
    county_rollup_counts[county_id][rollup] += 1
    county_source_codes[county_id].add(code)

  registration_names = {
    code: name for axis, code, name, _, _ in SOCIAL_STATUS_DEFINITIONS if axis == "registration"
  }
  mapping_rows = []
  for row in code_rows:
    code = int(row["c_household_status_code"])
    rollup = CBDB_HOUSEHOLD_ROLLUP[code]
    if code == 0:
      note = "CBDB未详代码；不作为县级结构证据，归入混合或不详"
    elif code in {31, 32, 33, 34, 35}:
      note = "复合户籍代码不强行拆分，归入混合或不详"
    elif code in {5, 19}:
      note = "灶/竈户按明代盐业役籍归入灶盐；CBDB英文释义仅保留为来源元数据"
    elif code == 13:
      note = "富户籍按法律登记归民户；财富程度由独立经济轴处理"
    elif code == 29:
      note = "马船籍暂按船马交通役务归驿运；地方证据充分时可覆写"
    else:
      note = "按CBDB原户籍代码语义归并；不等同人物实际职业"
    mapping_rows.append({
      "cbdb_household_status_code": code,
      "source_label_zh_hant": row["c_household_status_desc_chn"],
      "source_label_en": row["c_household_status_desc"],
      "registration_rollup_code": rollup,
      "registration_rollup_name_zh_hans": registration_names[rollup],
      "mapping_note": note,
      "source_person_count_all_periods": int(row["source_person_count_all_periods"]),
      "mapped_person_count_in_v04_catalog": mapped_catalog_counts[code],
      "source_database_sha256": cbdb_hash,
      "evidence_boundary": "CBDB人物样本偏向精英，只作代码映射与低权重县级证据，不视为户籍普查",
      "commercial_release_ready": COMMERCIAL_RELEASE_READY,
    })
  county_evidence = {
    county_id: {
      "rollup_counts": dict(counts),
      "source_codes": sorted(county_source_codes[county_id]),
      "record_count": sum(counts.values()),
    }
    for county_id, counts in county_rollup_counts.items()
  }
  return mapping_rows, county_evidence


def occupation_requirements(code: str, sector: str) -> tuple[int, int, int, int, float, float]:
  minimum_age = 13
  literacy = 0
  numeracy = 0
  classics = 0
  male_weight = 1.0
  female_weight = 0.65
  if code in {"cotton_spinner", "silk_reeler", "hemp_spinner", "embroiderer", "washer_seamstress", "midwife"}:
    female_weight = 1.35
    male_weight = 0.45
  if code in {"accountant", "clerk", "copyist", "shopkeeper", "broker", "pharmacist"}:
    literacy, numeracy = 3, 55
  if code == "clerk":
    minimum_age = 18
  if code in {"physician", "village_teacher", "family_tutor", "painter_calligrapher"}:
    literacy, numeracy, classics = 3, 30, 35
  if code in {"county_school_teacher", "academy_teacher"}:
    minimum_age, literacy, numeracy, classics = 22, 4, 45, 75
  if code in {"magistrate_official", "county_assistant_official"}:
    minimum_age, literacy, numeracy, classics = 24, 4, 55, 75
  if sector in {"government_admin", "military_security", "mining_salt", "transport_post_port"}:
    male_weight = max(male_weight, 1.25)
    female_weight = min(female_weight, 0.15)
  if code in {"domestic_servant", "market_vendor", "shopkeeper", "physician", "herbalist"}:
    female_weight = max(female_weight, 0.7)
  return minimum_age, literacy, numeracy, classics, male_weight, female_weight


def occupation_primary_driver(code: str, sector: str) -> str:
  special = {
    "coal_miner": "fuel_resource_0_100", "iron_miner": "metal_resource_0_100",
    "copper_miner": "metal_resource_0_100", "lead_zinc_miner": "metal_resource_0_100",
    "tin_miner": "metal_resource_0_100", "precious_metal_miner": "metal_resource_0_100",
    "salt_boiler": "salt_resource_0_100", "brine_well_worker": "salt_resource_0_100",
    "salt_transport_worker": "salt_resource_0_100", "printer": "publishing_book_culture_0_100",
    "bookbinder": "publishing_book_culture_0_100", "academy_teacher": "verified_academy_count",
    "county_school_teacher": "official_school_expected_0_100",
  }
  if code in special:
    return special[code]
  return {
    "agriculture": "agriculture_resource_0_100",
    "forestry_hunting": "forest_resource_0_100",
    "pastoral": "pasture_resource_0_100",
    "fishery_water": "fishery_resource_0_100",
    "mining_salt": "mining_smelting_initial_1628_0_100",
    "food_processing": "salt_food_initial_1628_0_100",
    "textile_clothing": "textile_initial_1628_0_100",
    "ceramics_building": "building_materials_initial_1628_0_100",
    "metal_wood_paper": "industrial_initial_1628_0_100",
    "transport_post_port": "transport_access_0_100",
    "commerce_finance": "commercial_prosperity_1628_0_100",
    "domestic_service": "urbanization_rate_0_100",
    "medicine_health": "education_degree_1628_0_100",
    "religion_ritual": "lineage_organization_potential_0_100",
    "education_culture": "education_degree_1628_0_100",
    "government_admin": "administrative_centrality_0_100",
    "military_security": "arms_initial_1628_0_100",
    "marginal_unfixed": "confirmed_disruption_penalty_0_100",
  }[sector]


def occupation_affinity(sector: str) -> str:
  return {
    "agriculture": "village;resource_industrial",
    "forestry_hunting": "village;resource_industrial",
    "pastoral": "village;military_settlement",
    "fishery_water": "village;market_town;transport_port_station",
    "mining_salt": "resource_industrial;village",
    "food_processing": "village;market_town;county_seat",
    "textile_clothing": "village;market_town;county_seat",
    "ceramics_building": "resource_industrial;market_town;county_seat",
    "metal_wood_paper": "resource_industrial;market_town;county_seat",
    "transport_post_port": "transport_port_station;market_town;county_seat",
    "commerce_finance": "market_town;county_seat;village",
    "domestic_service": "county_seat;market_town",
    "medicine_health": "county_seat;market_town;village",
    "religion_ritual": "village;market_town;county_seat",
    "education_culture": "county_seat;market_town;village",
    "government_admin": "county_seat",
    "military_security": "military_settlement;county_seat",
    "marginal_unfixed": "county_seat;market_town;transport_port_station;village",
  }[sector]


def build_occupation_definitions() -> list[dict[str, Any]]:
  if len(OCCUPATION_CATALOG) != EXPECTED_OCCUPATIONS:
    raise RuntimeError(
      f"occupation catalog must contain {EXPECTED_OCCUPATIONS} rows, got {len(OCCUPATION_CATALOG)}"
    )
  rows = []
  for sector, industry_code, industry_name, code, name, relative_weight in OCCUPATION_CATALOG:
    minimum_age, literacy, numeracy, classics, male_weight, female_weight = occupation_requirements(code, sector)
    registration = {
      "military_security": "military;official_security",
      "mining_salt": "artisan;salt;civilian",
      "fishery_water": "fish_boat;civilian",
      "transport_post_port": "post_transport;fish_boat;civilian",
      "education_culture": "literary_student;civilian",
      "government_admin": "official_security;literary_student;civilian",
      "medicine_health": "medical_ritual;civilian",
      "religion_ritual": "medical_ritual;civilian",
    }.get(sector, "civilian;artisan")
    rows.append({
      "occupation_code": code,
      "sector_code": sector,
      "sector_name_zh_hans": SECTOR_NAMES[sector],
      "industry_code": industry_code,
      "industry_name_zh_hans": industry_name,
      "occupation_name_zh_hans": name,
      "original_term_zh_hant": "",
      "relative_weight": relative_weight,
      "minimum_age": minimum_age,
      "maximum_age": 75,
      "male_weight": male_weight,
      "female_weight": female_weight,
      "minimum_literacy_level": literacy,
      "minimum_numeracy_0_100": numeracy,
      "minimum_classics_0_100": classics,
      "settlement_affinity": occupation_affinity(sector),
      "registration_affinity": registration,
      "secondary_allowed": "yes" if sector not in {"government_admin", "military_security"} else "conditional",
      "primary_driver": occupation_primary_driver(code, sector),
      "evidence_grade": "historical_taxonomy_game_weight",
      "source_url": "https://zh.wikisource.org/zh-hant/明史/卷77",
      "commercial_release_ready": COMMERCIAL_RELEASE_READY,
    })
  codes = [row["occupation_code"] for row in rows]
  if len(codes) != len(set(codes)):
    raise RuntimeError("duplicate occupation_code")
  return rows


def build_education_definitions() -> list[dict[str, Any]]:
  rows = []
  for row in EDUCATION_DEFINITIONS:
    definition_type, code, name, level, reading, writing, numeracy, classics, document, description = row
    rows.append({
      "definition_type": definition_type,
      "definition_code": code,
      "display_name_zh_hans": name,
      "level_order": level,
      "reading_floor_0_100": reading,
      "writing_floor_0_100": writing,
      "numeracy_floor_0_100": numeracy,
      "classics_floor_0_100": classics,
      "document_floor_0_100": document,
      "description": description,
      "source_type": "historical_structure_and_game_definition",
      "source_url": "https://zh.wikisource.org/zh/明史/卷69",
      "commercial_release_ready": COMMERCIAL_RELEASE_READY,
    })
  return rows


def build_social_definitions() -> list[dict[str, Any]]:
  return [{
    "axis_code": axis,
    "status_code": code,
    "display_name_zh_hans": name,
    "level_order": level,
    "description": description,
    "source_type": "historical_structure_and_game_rollup",
    "source_url": "https://zh.wikisource.org/zh-hant/明史/卷77",
    "commercial_release_ready": COMMERCIAL_RELEASE_READY,
  } for axis, code, name, level, description in SOCIAL_STATUS_DEFINITIONS]


def build_archetype_definitions() -> list[dict[str, Any]]:
  return [{
    "settlement_type_code": code,
    "display_name_zh_hans": name,
    "urban_rural": urban_rural,
    "hierarchy_depth": depth,
    "description": description,
    "population_policy": "exact county partition by largest remainder",
    "historical_claim_policy": "documented only with source; generated otherwise",
    "commercial_release_ready": COMMERCIAL_RELEASE_READY,
  } for code, name, urban_rural, depth, description in SETTLEMENT_ARCHETYPES]


def build_poi_definitions() -> list[dict[str, Any]]:
  return [{
    "poi_type_code": code,
    "display_name_zh_hans": name,
    "sector_code": sector,
    "allowed_settlement_types": allowed,
    "default_capacity": capacity,
    "resident_population_policy": "zero; residents belong to parent settlement",
    "historical_claim_policy": "documented only with source; generated otherwise",
    "commercial_release_ready": COMMERCIAL_RELEASE_READY,
  } for code, name, sector, allowed, capacity in POI_DEFINITIONS]


def sector_driver(sector: str, economy: dict[str, Any], culture: dict[str, Any]) -> float:
  values = {
    "agriculture": 0.78 * economy["agriculture_resource_0_100"] + 0.22 * economy["grain_surplus_potential_0_100"],
    "forestry_hunting": economy["forest_resource_0_100"],
    "pastoral": economy["pasture_resource_0_100"],
    "fishery_water": 0.75 * economy["fishery_resource_0_100"] + 0.25 * economy["water_access_0_100"],
    "mining_salt": max(economy["mining_smelting_initial_1628_0_100"], economy["salt_resource_0_100"]),
    "food_processing": economy["salt_food_initial_1628_0_100"],
    "textile_clothing": economy["textile_initial_1628_0_100"],
    "ceramics_building": max(economy["ceramics_initial_1628_0_100"], economy["building_materials_initial_1628_0_100"]),
    "metal_wood_paper": max(economy["industrial_initial_1628_0_100"], economy["forestry_paper_initial_1628_0_100"]),
    "transport_post_port": 0.55 * economy["transport_access_0_100"] + 0.45 * economy["waterborne_trade_0_100"],
    "commerce_finance": economy["commercial_prosperity_1628_0_100"],
    "domestic_service": 0.55 * economy["urbanization_rate_0_100"] + 0.45 * economy["local_market_0_100"],
    "medicine_health": 0.55 * culture["education_degree_1628_0_100"] + 0.45 * economy["urbanization_rate_0_100"],
    "religion_ritual": 0.55 * culture["lineage_organization_potential_0_100"] + 0.45 * culture["cultural_influence_0_100"],
    "education_culture": culture["education_degree_1628_0_100"],
    "government_admin": economy["administrative_centrality_0_100"],
    "military_security": 0.65 * economy["arms_initial_1628_0_100"] + 0.35 * economy["administrative_centrality_0_100"],
    "marginal_unfixed": 0.55 * economy["confirmed_disruption_penalty_0_100"] + 0.45 * economy["population_pressure_0_100"],
  }
  return float(clamp(values[sector], 0, 100))


def occupation_special_multiplier(code: str, economy: dict[str, Any], culture: dict[str, Any]) -> float:
  region = str(economy["region"])
  southern = any(token in region for token in ("南直隶", "浙江", "江西", "福建", "广东", "广西", "湖广", "四川", "云南", "贵州"))
  northern = any(token in region for token in ("北直隶", "山东", "河南", "山西", "陕西"))
  if code == "rice_farmer":
    return 1.45 if southern else 0.45 if northern else 0.85
  if code == "wheat_millet_farmer":
    return 1.35 if northern else 0.55 if southern else 0.9
  if code == "dryland_farmer":
    return 1.30 if northern or any(token in region for token in ("云南", "贵州")) else 0.62
  if code == "tea_grower":
    return (0.35 + economy["forest_resource_0_100"] / 55) if southern else 0.08
  if code == "sugarcane_grower":
    return 1.35 if any(token in region for token in ("广东", "广西", "福建")) else 0.06
  if code == "cotton_grower":
    return 1.30 if any(token in region for token in ("南直隶", "山东", "河南", "北直隶")) else 0.75
  if code == "mulberry_grower":
    return 1.45 if any(token in region for token in ("南直隶", "浙江", "四川")) else 0.45
  if code == "coal_miner":
    return economy["fuel_resource_0_100"] / 50 if economy["fuel_resource_0_100"] >= 25 else 0.0
  if code in {"iron_miner", "copper_miner", "lead_zinc_miner", "tin_miner", "precious_metal_miner"}:
    return economy["metal_resource_0_100"] / 50 if economy["metal_resource_0_100"] >= 20 else 0.0
  if code in {"salt_boiler", "brine_well_worker", "salt_transport_worker", "salt_merchant"}:
    return economy["salt_resource_0_100"] / 45 if economy["salt_resource_0_100"] >= 20 else 0.0
  if code in {"printer", "bookbinder"}:
    return 0.35 + culture["publishing_book_culture_0_100"] / 65
  if code in {"academy_teacher"}:
    return 0.15 + min(2.0, culture["verified_academy_count"] / 2)
  if code in {"county_school_teacher"}:
    return 0.35 + culture["official_school_expected_0_100"] / 70
  if code in {"magistrate_official", "county_assistant_official"}:
    return 0.25 + economy["administrative_centrality_0_100"] / 60
  if code in {"coastal_fisher", "sailor"}:
    return 0.2 + economy["waterborne_trade_0_100"] / 60
  return 1.0


def build_county_rows(
  connection: sqlite3.Connection,
  occupations: Sequence[dict[str, Any]],
  anchors: Sequence[dict[str, str]],
  cbdb_household_evidence: dict[str, dict[str, Any]],
) -> tuple[list[dict[str, Any]], list[dict[str, Any]], list[dict[str, Any]], list[dict[str, Any]], dict[str, dict[str, int]]]:
  economies = rows_as_dicts(connection.execute("SELECT * FROM county_economy_baseline ORDER BY county_id"))
  cultures = {
    row["county_id"]: row
    for row in rows_as_dicts(connection.execute("SELECT * FROM county_culture_education_baseline ORDER BY county_id"))
  }
  if len(economies) != EXPECTED_COUNTIES or len(cultures) != EXPECTED_COUNTIES:
    raise RuntimeError("v0.6 requires exactly 1,168 economy and culture rows")
  anchor_by_county: dict[str, list[dict[str, str]]] = defaultdict(list)
  for anchor in anchors:
    anchor_by_county[anchor.get("county_id", "")].append(anchor)
  education_rows: list[dict[str, Any]] = []
  social_rows: list[dict[str, Any]] = []
  occupation_rows: list[dict[str, Any]] = []
  overview_rows: list[dict[str, Any]] = []
  county_sector_counts: dict[str, dict[str, int]] = {}
  occupation_by_code = {row["occupation_code"]: row for row in occupations}
  for economy in economies:
    county_id = economy["county_id"]
    culture = cultures[county_id]
    population = int(economy["population_est_1628"])
    labor = int(economy["labor_force_est"])
    male_population = round(population * 0.51)
    female_population = population - male_population
    male_literate = round(male_population * float(culture["male_basic_literacy_mid_pct"]) / 100)
    female_literate = round(female_population * float(culture["female_basic_literacy_mid_pct"]) / 100)
    total_literate = male_literate + female_literate
    classical = min(total_literate, round(population * float(culture["classical_education_mid_pct"]) / 100))
    remaining_literate = max(0, total_literate - classical)
    education_factor = float(culture["education_degree_1628_0_100"]) / 100
    split = allocate_exact(
      remaining_literate,
      [0.50 - 0.18 * education_factor, 0.32, 0.18 + 0.18 * education_factor],
    )
    l1, l2, l3 = split
    l4 = classical
    l0 = population - total_literate
    level_counts = [l0, l1, l2, l3, l4]
    reading_avg = round(sum(count * score for count, score in zip(level_counts, [0, 18, 38, 62, 82])) / population)
    writing_avg = round(sum(count * score for count, score in zip(level_counts, [0, 4, 18, 55, 78])) / population)
    numeracy_avg = round(sum(count * score for count, score in zip(level_counts, [4, 8, 28, 58, 60])) / population)
    classics_avg = round(sum(count * score for count, score in zip(level_counts, [0, 0, 4, 22, 75])) / population)
    document_avg = round(sum(count * score for count, score in zip(level_counts, [0, 2, 12, 55, 68])) / population)
    shengyuan = max(0, round(classical * (0.006 + culture["official_school_expected_0_100"] / 10_000)))
    candidate = max(shengyuan, round(classical * (0.035 + culture["imperial_exam_culture_0_100"] / 5_000)))
    gongsheng = round(shengyuan * (0.035 + culture["imperial_exam_culture_0_100"] / 4_000))
    jiansheng = round(shengyuan * (0.025 + economy["administrative_centrality_0_100"] / 6_000))
    juren = round(shengyuan * (0.012 + culture["imperial_exam_culture_0_100"] / 5_000))
    gongshi = round(juren * 0.10)
    jinshi = round(juren * 0.06)
    military_degree = round(max(0, shengyuan * economy["arms_initial_1628_0_100"] / 12_000))
    education_row = {
      "county_id": county_id, "snapshot_year": SNAPSHOT_YEAR, "region": economy["region"],
      "upper_unit": economy["upper_unit"], "intermediate_unit": economy["intermediate_unit"],
      "county": economy["county"], "population_est_1628": population, "labor_force_est": labor,
      "male_population_est": male_population, "female_population_est": female_population,
      "male_literate_est": male_literate, "female_literate_est": female_literate,
      "total_literate_est": total_literate, "classical_educated_est": classical,
      "literacy_l0_count": l0, "literacy_l1_count": l1, "literacy_l2_count": l2,
      "literacy_l3_count": l3, "literacy_l4_count": l4,
      "reading_skill_avg_0_100": reading_avg, "writing_skill_avg_0_100": writing_avg,
      "numeracy_skill_avg_0_100": numeracy_avg, "classics_skill_avg_0_100": classics_avg,
      "document_skill_avg_0_100": document_avg, "candidate_est": candidate,
      "shengyuan_est": shengyuan, "gongsheng_est": gongsheng, "jiansheng_est": jiansheng,
      "juren_est": juren, "gongshi_est": gongshi, "jinshi_est": jinshi,
      "military_degree_est": military_degree,
      "education_degree_1628_0_100": culture["education_degree_1628_0_100"],
      "imperial_exam_culture_0_100": culture["imperial_exam_culture_0_100"],
      "verified_school_count": culture["verified_school_count"],
      "verified_academy_count": culture["verified_academy_count"],
      "data_coverage_0_100": culture["data_coverage_0_100"],
      "estimation_method": "v0.4 literacy hard totals + deterministic education-band and credential projection",
      "commercial_release_ready": COMMERCIAL_RELEASE_READY,
    }
    education_rows.append(education_row)

    registration_weights = [
      78 + economy["agriculture_resource_0_100"] * 0.10,
      3 + economy["arms_initial_1628_0_100"] * 0.12 + (8 if any(word in economy["region"] for word in ("北", "山西", "陕西")) else 0),
      3 + economy["industrial_initial_1628_0_100"] * 0.13,
      economy["salt_resource_0_100"] * 0.16,
      economy["fishery_resource_0_100"] * 0.10,
      economy["transport_access_0_100"] * 0.08,
      0.6 + culture["education_degree_1628_0_100"] * 0.015,
      0.5 + culture["imperial_exam_culture_0_100"] * 0.025,
      0.6 + economy["administrative_centrality_0_100"] * 0.025,
      2 + economy["population_pressure_0_100"] * 0.025,
    ]
    cbdb_evidence = cbdb_household_evidence.get(county_id, {
      "rollup_counts": {}, "source_codes": [], "record_count": 0,
    })
    cbdb_record_count = int(cbdb_evidence["record_count"])
    cbdb_evidence_weight = round(min(12.0, 2.2 * math.log1p(cbdb_record_count))) if cbdb_record_count else 0
    if cbdb_record_count:
      structural_total = sum(registration_weights)
      observed_weights = [
        float(cbdb_evidence["rollup_counts"].get(code, 0)) + 0.25 for code in REGISTRATION_CODES
      ]
      observed_total = sum(observed_weights)
      evidence_ratio = cbdb_evidence_weight / 100
      blended_registration_weights = [
        (1 - evidence_ratio) * structural / structural_total
        + evidence_ratio * observed / observed_total
        for structural, observed in zip(registration_weights, observed_weights)
      ]
    else:
      blended_registration_weights = registration_weights
    registration_shares = allocate_exact(WEIGHT_TOTAL, blended_registration_weights)
    commercial = economy["commercial_prosperity_1628_0_100"]
    disruption = economy["confirmed_disruption_penalty_0_100"]
    gentry = culture["gentry_power_0_100"]
    economic_weights = [
      2 + disruption * 0.10,
      9 + disruption * 0.18 + economy["population_pressure_0_100"] * 0.08,
      20 + economy["agriculture_resource_0_100"] * 0.12 + gentry * 0.06,
      34 + economy["agriculture_resource_0_100"] * 0.15,
      23 + economy["economic_resilience_0_100"] * 0.12,
      8 + commercial * 0.12 + economy["industrial_initial_1628_0_100"] * 0.06,
      2 + commercial * 0.06 + gentry * 0.05,
    ]
    economic_shares = allocate_exact(WEIGHT_TOTAL, economic_weights)
    social_row: dict[str, Any] = {
      "county_id": county_id, "snapshot_year": SNAPSHOT_YEAR, "region": economy["region"],
      "upper_unit": economy["upper_unit"], "intermediate_unit": economy["intermediate_unit"],
      "county": economy["county"], "population_est_1628": population,
      "household_count_est": economy["household_count_est"], "labor_force_est": labor,
      "household_production_share_0_100": round(clamp(82 - commercial * 0.24 + economy["agriculture_resource_0_100"] * 0.10, 45, 92)),
      "wage_labor_share_0_100": round(clamp(10 + commercial * 0.12 + disruption * 0.18, 5, 45)),
      "tenant_household_share_0_100": round(100 * economic_shares[2] / WEIGHT_TOTAL),
      "dependent_population_share_0_100": round(100 * economic_shares[0] / WEIGHT_TOTAL),
      "unfixed_livelihood_share_0_100": round(clamp(2 + disruption * 0.16 + economy["population_pressure_0_100"] * 0.06, 1, 24)),
      "prestige_mobility_0_100": round(clamp(0.45 * culture["education_degree_1628_0_100"] + 0.30 * commercial + 0.25 * culture["elite_network_density_0_100"], 0, 100)),
      "local_power_concentration_0_100": round(clamp(0.45 * gentry + 0.30 * culture["lineage_organization_potential_0_100"] + 0.25 * economy["administrative_centrality_0_100"], 0, 100)),
      "registration_estimation_method": "Ming household categories + county industry/resource/admin structural weights + capped CBDB code evidence blend",
      "economic_estimation_method": "land/agriculture/commerce/gentry/disruption deterministic structural shares",
      "cbdb_household_status_record_count": cbdb_record_count,
      "cbdb_household_evidence_weight_0_100": cbdb_evidence_weight,
      "cbdb_household_status_codes_present": ";".join(str(code) for code in cbdb_evidence["source_codes"]),
      "cbdb_household_mapping_version": "CBDB-20260822-to-v0.6",
      "commercial_release_ready": COMMERCIAL_RELEASE_READY,
    }
    for code, share in zip(REGISTRATION_CODES, registration_shares):
      social_row[f"registration_{code}_share_ppm"] = share
    for code, share in zip(ECONOMIC_CODES, economic_shares):
      social_row[f"economic_{code}_share_ppm"] = share
    social_rows.append(social_row)

    sector_raw = {
      sector: SECTOR_PRIORS[sector] * (0.35 + 1.30 * sector_driver(sector, economy, culture) / 100)
      for sector in SECTOR_CODES
    }
    occupation_raw: list[float] = []
    for definition in occupations:
      sector = definition["sector_code"]
      sector_total_relative = sum(
        float(row["relative_weight"]) for row in occupations if row["sector_code"] == sector
      )
      raw = sector_raw[sector] * float(definition["relative_weight"]) / sector_total_relative
      raw *= occupation_special_multiplier(definition["occupation_code"], economy, culture)
      for anchor in anchor_by_county.get(county_id, []):
        codes = {value.strip() for value in anchor.get("occupation_codes", "").split(";") if value.strip()}
        if definition["occupation_code"] in codes:
          raw *= 1 + float(anchor.get("effect_0_100", 0) or 0) / 100
      occupation_raw.append(max(0.0, raw))
    worker_counts = allocate_exact(labor, occupation_raw)
    worker_shares = allocate_exact(WEIGHT_TOTAL, occupation_raw)
    county_sector_counts[county_id] = {code: 0 for code in SECTOR_CODES}
    county_occupation_rows: list[dict[str, Any]] = []
    for definition, count, share, raw in zip(occupations, worker_counts, worker_shares, occupation_raw):
      row = {
        "county_id": county_id, "snapshot_year": SNAPSHOT_YEAR, "region": economy["region"],
        "upper_unit": economy["upper_unit"], "intermediate_unit": economy["intermediate_unit"],
        "county": economy["county"], "occupation_code": definition["occupation_code"],
        "sector_code": definition["sector_code"], "occupation_name_zh_hans": definition["occupation_name_zh_hans"],
        "worker_count_est": count, "worker_share_ppm": share, "raw_weight": round(raw, 8),
        "primary_driver": definition["primary_driver"], "evidence_type": "structural_projection_with_manual_anchors",
        "estimation_method": "normalized county drivers + largest remainder",
        "commercial_release_ready": COMMERCIAL_RELEASE_READY,
      }
      occupation_rows.append(row)
      county_occupation_rows.append(row)
      county_sector_counts[county_id][definition["sector_code"]] += count
    top_occupations = sorted(county_occupation_rows, key=lambda row: (-row["worker_count_est"], row["occupation_code"]))[:4]
    top_sectors = sorted(county_sector_counts[county_id].items(), key=lambda item: (-item[1], item[0]))[:3]
    overview_rows.append({
      "county_id": county_id, "snapshot_year": SNAPSHOT_YEAR, "region": economy["region"],
      "upper_unit": economy["upper_unit"], "intermediate_unit": economy["intermediate_unit"],
      "county": economy["county"], "population_est_1628": population, "labor_force_est": labor,
      "total_literate_est": total_literate, "classical_educated_est": classical,
      "male_literacy_mid_pct": culture["male_basic_literacy_mid_pct"],
      "female_literacy_mid_pct": culture["female_basic_literacy_mid_pct"],
      "education_degree_1628_0_100": culture["education_degree_1628_0_100"],
      **{f"top_occupation_{index + 1}": f"{row['occupation_name_zh_hans']}:{row['worker_count_est']}" for index, row in enumerate(top_occupations)},
      **{f"top_sector_{index + 1}": f"{SECTOR_NAMES[code]}:{count}" for index, (code, count) in enumerate(top_sectors)},
      "dominant_registration_status": REGISTRATION_CODES[max(range(len(registration_shares)), key=registration_shares.__getitem__)],
      "dominant_economic_stratum": ECONOMIC_CODES[max(range(len(economic_shares)), key=economic_shares.__getitem__)],
      "local_power_concentration_0_100": social_row["local_power_concentration_0_100"],
      "commercial_release_ready": COMMERCIAL_RELEASE_READY,
    })
  return education_rows, social_rows, occupation_rows, overview_rows, county_sector_counts


def create_table(
  connection: sqlite3.Connection,
  name: str,
  columns: Sequence[str],
  primary_key: Sequence[str] = (),
  foreign_keys: Sequence[tuple[Sequence[str], str, Sequence[str]]] = (),
) -> None:
  connection.execute(f'DROP TABLE IF EXISTS "{name}"')
  definitions = []
  real_names = {"relative_weight", "male_weight", "female_weight", "raw_weight"}
  integer_names = {
    "snapshot_year", "level_order", "minimum_age", "maximum_age",
    "default_capacity", "hierarchy_depth", "resident_population",
    "capacity_est", "workforce_slots_est", "relative_x_0_10000",
    "relative_y_0_10000", "minimum_literacy_level",
    "minimum_numeracy_0_100", "minimum_classics_0_100",
    "cbdb_household_status_code", "source_person_count_all_periods",
    "mapped_person_count_in_v04_catalog",
  }
  for column in columns:
    if column in real_names or column.endswith("_pct"):
      data_type = "REAL"
    elif (
      column in integer_names
      or column.endswith(("_count", "_est", "_0_100", "_ppm"))
      or "_est_" in column
    ):
      data_type = "INTEGER"
    else:
      data_type = "TEXT"
    constraint = ""
    if column.endswith("_0_100"):
      constraint = f' CHECK ("{column}" BETWEEN 0 AND 100)'
    elif column.endswith("_ppm"):
      constraint = f' CHECK ("{column}" BETWEEN 0 AND {WEIGHT_TOTAL})'
    elif column in {"relative_x_0_10000", "relative_y_0_10000"}:
      constraint = f' CHECK ("{column}" BETWEEN 0 AND 10000)'
    definitions.append(f'"{column}" {data_type} NOT NULL{constraint}')
  if primary_key:
    definitions.append("PRIMARY KEY (" + ",".join(f'"{column}"' for column in primary_key) + ")")
  for local_columns, parent_table, parent_columns in foreign_keys:
    local = ",".join(f'"{column}"' for column in local_columns)
    parent = ",".join(f'"{column}"' for column in parent_columns)
    definitions.append(f'FOREIGN KEY ({local}) REFERENCES "{parent_table}"({parent})')
  connection.execute(f'CREATE TABLE "{name}" ({",".join(definitions)})')


def insert_rows(connection: sqlite3.Connection, table: str, columns: Sequence[str], rows: Iterable[dict[str, Any]]) -> None:
  placeholders = ",".join("?" for _ in columns)
  names = ",".join(f'"{column}"' for column in columns)
  connection.executemany(
    f'INSERT INTO "{table}" ({names}) VALUES ({placeholders})',
    ([row.get(column, "") for column in columns] for row in rows),
  )


def install_small_tables(
  database: Path,
  source_database: Path,
  definitions: dict[str, tuple[Sequence[str], Sequence[dict[str, Any]], Sequence[str]]],
) -> sqlite3.Connection:
  database.parent.mkdir(parents=True, exist_ok=True)
  temporary = database.with_suffix(database.suffix + ".tmp")
  if temporary.exists():
    temporary.unlink()
  shutil.copy2(source_database, temporary)
  connection = sqlite3.connect(temporary)
  connection.row_factory = sqlite3.Row
  connection.execute("PRAGMA foreign_keys=OFF")
  county_fk = [(["county_id"], "county_economy_baseline", ["county_id"])]
  for name, (columns, rows, primary_key) in definitions.items():
    foreign_keys: list[tuple[Sequence[str], str, Sequence[str]]] = []
    if "county_id" in columns:
      foreign_keys.extend(county_fk)
    if name == "county_occupation_quota":
      foreign_keys.append((["occupation_code"], "occupation_definition", ["occupation_code"]))
    create_table(connection, name, columns, primary_key, foreign_keys)
    insert_rows(connection, name, columns, rows)
  create_table(connection, "settlement_node", SETTLEMENT_COLUMNS, ["settlement_id"], [
    *county_fk,
    (["subregion_id"], "county_subregion_definition", ["subregion_id"]),
    (["settlement_type_code"], "settlement_archetype_definition", ["settlement_type_code"]),
  ])
  create_table(connection, "settlement_zone", ZONE_COLUMNS, ["zone_id"], [
    *county_fk,
    (["settlement_id"], "settlement_node", ["settlement_id"]),
  ])
  create_table(connection, "settlement_poi", POI_CATALOG_COLUMNS, ["poi_id"], [
    *county_fk,
    (["settlement_id"], "settlement_node", ["settlement_id"]),
    (["zone_id"], "settlement_zone", ["zone_id"]),
    (["poi_type_code"], "institution_poi_definition", ["poi_type_code"]),
  ])
  create_table(connection, "settlement_sector_quota", SECTOR_QUOTA_COLUMNS, ["settlement_id"], [
    *county_fk,
    (["settlement_id"], "settlement_node", ["settlement_id"]),
  ])
  connection.commit()
  return connection


def unique_generated_name(base: str, used: set[str], ordinal: int) -> str:
  candidate = base
  attempt = ordinal
  while candidate in used:
    attempt += 1
    candidate = f"{base}{attempt}号"
  used.add(candidate)
  return candidate


def special_settlements(
  economy: dict[str, Any],
  subregions: Sequence[dict[str, Any]],
  rural_population: int,
) -> list[dict[str, Any]]:
  county_id = economy["county_id"]
  rows: list[dict[str, Any]] = []
  resource_score = max(
    economy["mining_smelting_initial_1628_0_100"], economy["salt_resource_0_100"],
    economy["ceramics_initial_1628_0_100"], economy["forestry_paper_initial_1628_0_100"],
  )
  transport_score = max(economy["transport_access_0_100"], economy["waterborne_trade_0_100"])
  frontier_bonus = 18 if any(token in economy["region"] for token in ("北直隶", "山西", "陕西")) else 0
  military_score = economy["arms_initial_1628_0_100"] + frontier_bonus
  candidates = []
  if resource_score >= 48:
    candidates.append(("resource_industrial", resource_score))
  if transport_score >= 58:
    candidates.append(("transport_port_station", transport_score))
  if military_score >= 58:
    candidates.append(("military_settlement", min(100, military_score)))
  if not candidates:
    return rows
  target_total = min(round(rural_population * 0.045), sum(max(120, round(rural_population * (0.0012 + score / 50_000))) for _, score in candidates))
  populations = allocate_exact(target_total, [score for _, score in candidates], minimum=80 if target_total >= 80 * len(candidates) else 0)
  for ordinal, ((settlement_type, score), population) in enumerate(zip(candidates, populations), 1):
    if population <= 0:
      continue
    preferred = {
      "resource_industrial": ("mining_zone", "hill_forest", "mountain_forest"),
      "transport_port_station": ("river_transport", "coast_fishery", "wetland_fishery"),
      "military_settlement": ("plateau_pasture", "mountain_forest", "county_core"),
    }[settlement_type]
    subregion = min(
      subregions,
      key=lambda row: (
        preferred.index(row["zone_type"]) if row["zone_type"] in preferred else len(preferred),
        row["subregion_id"],
      ),
    )
    label = {
      "resource_industrial": "产业场聚落",
      "transport_port_station": "港驿聚落",
      "military_settlement": "营堡聚落",
    }[settlement_type]
    rows.append({
      "settlement_id": f"{county_id}-S{settlement_type[0].upper()}{ordinal:02d}",
      "subregion_id": subregion["subregion_id"],
      "settlement_type_code": settlement_type,
      "base_name": f"{subregion['direction_name']}{label}",
      "resident_population": population,
      "relative_x_0_10000": int(subregion["center_rel_x_0_10000"]),
      "relative_y_0_10000": int(subregion["center_rel_y_0_10000"]),
      "score": score,
    })
  return rows


def local_sector_counts(
  labor: int,
  county_counts: dict[str, int],
  settlement_type: str,
) -> dict[str, int]:
  multipliers = {
    "village": {"agriculture": 1.55, "forestry_hunting": 1.35, "pastoral": 1.25, "government_admin": 0.08, "commerce_finance": 0.55},
    "market_town": {"agriculture": 0.35, "commerce_finance": 2.0, "transport_post_port": 1.7, "domestic_service": 1.7, "metal_wood_paper": 1.4},
    "county_seat": {"agriculture": 0.12, "government_admin": 4.0, "education_culture": 2.8, "commerce_finance": 2.1, "domestic_service": 2.0, "medicine_health": 2.2},
    "military_settlement": {"agriculture": 0.55, "military_security": 5.5, "transport_post_port": 1.4, "metal_wood_paper": 1.3},
    "resource_industrial": {"agriculture": 0.35, "mining_salt": 5.2, "ceramics_building": 2.1, "metal_wood_paper": 1.8},
    "transport_port_station": {"agriculture": 0.28, "transport_post_port": 5.0, "commerce_finance": 1.8, "fishery_water": 1.5},
  }[settlement_type]
  weights = [max(0.0, county_counts[code] * multipliers.get(code, 1.0)) for code in SECTOR_CODES]
  values = allocate_exact(labor, weights)
  return dict(zip(SECTOR_CODES, values))


def zone_rows_for_settlement(settlement: dict[str, Any]) -> list[dict[str, Any]]:
  population = int(settlement["resident_population"])
  labor = int(settlement["labor_force_est"])
  settlement_type = settlement["settlement_type_code"]
  if settlement_type in {"county_seat", "market_town"} or population > 1_000:
    zone_count = max(1, math.ceil(population / 750))
  else:
    zone_count = 1
  populations = allocate_exact(population, [1] * zone_count)
  labors = allocate_exact(labor, populations)
  direction_names = ["东坊", "南坊", "西坊", "北坊", "中坊", "东南坊", "西南坊", "东北坊", "西北坊"]
  rows = []
  for ordinal, (zone_population, zone_labor) in enumerate(zip(populations, labors), 1):
    if zone_count == 1:
      zone_name = {
        "village": "村中人口块",
        "military_settlement": "营堡人口块",
        "resource_industrial": "产业场人口块",
        "transport_port_station": "港驿人口块",
      }.get(settlement_type, "聚落人口块")
      zone_type = "single_population_block"
    else:
      direction = direction_names[(ordinal - 1) % len(direction_names)]
      cycle = (ordinal - 1) // len(direction_names) + 1
      zone_name = f"{direction}{cycle}片"
      zone_type = "urban_leaf_block"
    angle = stable_unit(settlement["settlement_id"], ordinal, "zone-angle") * math.tau
    radius = 120 + 900 * math.sqrt(stable_unit(settlement["settlement_id"], ordinal, "zone-radius"))
    rows.append({
      "zone_id": f"{settlement['settlement_id']}-B{ordinal:03d}",
      "snapshot_year": SNAPSHOT_YEAR, "settlement_id": settlement["settlement_id"],
      "county_id": settlement["county_id"], "zone_name": zone_name, "zone_type": zone_type,
      "resident_population": zone_population, "labor_force_est": zone_labor,
      "relative_x_0_10000": round(clamp(settlement["relative_x_0_10000"] + math.cos(angle) * radius, 0, 10000)),
      "relative_y_0_10000": round(clamp(settlement["relative_y_0_10000"] + math.sin(angle) * radius, 0, 10000)),
      "render_seed": stable_digest(settlement["settlement_id"], ordinal, "zone-render").hex()[:16],
      "historical_claim": "no", "commercial_release_ready": COMMERCIAL_RELEASE_READY,
    })
  return rows


def poi_types_for_settlement(
  settlement: dict[str, Any],
  economy: dict[str, Any],
  culture: dict[str, Any],
) -> list[str]:
  settlement_type = settlement["settlement_type_code"]
  if settlement_type == "county_seat":
    values = ["county_yamen", "official_school", "market", "clinic_pharmacy", "temple_monastery", "workshop"]
    if culture["verified_academy_count"] > 0:
      values.append("academy")
    if economy["arms_initial_1628_0_100"] >= 45:
      values.append("military_compound")
    if economy["waterborne_trade_0_100"] >= 45:
      values.append("dock")
    return values
  if settlement_type == "market_town":
    values = ["market", "workshop", "clinic_pharmacy", "temple_monastery"]
    if economy["waterborne_trade_0_100"] >= 45:
      values.append("dock")
    if culture["education_degree_1628_0_100"] >= 45:
      values.append("village_school")
    return values
  if settlement_type == "military_settlement":
    return ["military_compound", "market", "temple_monastery"]
  if settlement_type == "transport_port_station":
    return ["dock", "post_station", "market"]
  if settlement_type == "resource_industrial":
    resource_type = "saltworks" if economy["salt_resource_0_100"] >= max(economy["metal_resource_0_100"], economy["ceramics_initial_1628_0_100"]) else "mine" if economy["metal_resource_0_100"] >= economy["ceramics_initial_1628_0_100"] else "kiln"
    return [resource_type, "workshop", "market"]
  values = []
  population = int(settlement["resident_population"])
  if population >= 320 and stable_unit(settlement["settlement_id"], "market-poi") < economy["local_market_0_100"] / 350:
    values.append("market")
  if population >= 380 and stable_unit(settlement["settlement_id"], "school-poi") < culture["education_degree_1628_0_100"] / 500:
    values.append("village_school")
  if stable_unit(settlement["settlement_id"], "temple-poi") < 0.12:
    values.append("temple_monastery")
  if stable_unit(settlement["settlement_id"], "workshop-poi") < economy["industrial_initial_1628_0_100"] / 600:
    values.append("workshop")
  return values


def build_settlements(
  source: sqlite3.Connection,
  target: sqlite3.Connection,
  generated_dir: Path,
  county_sector_counts: dict[str, dict[str, int]],
) -> dict[str, Any]:
  generated_dir.mkdir(parents=True, exist_ok=True)
  settlement_path = generated_dir / "settlement_node_catalog_v0.6.csv"
  zone_path = generated_dir / "settlement_zone_catalog_v0.6.csv"
  poi_path = generated_dir / "settlement_poi_catalog_v0.6.csv"
  sector_path = generated_dir / "settlement_sector_quota_v0.6.csv"
  files = []
  for path, columns in ((settlement_path, SETTLEMENT_COLUMNS), (zone_path, ZONE_COLUMNS), (poi_path, POI_CATALOG_COLUMNS), (sector_path, SECTOR_QUOTA_COLUMNS)):
    stream = path.open("w", encoding="utf-8", newline="")
    writer = csv.DictWriter(stream, fieldnames=columns, extrasaction="ignore")
    writer.writeheader()
    files.append((stream, writer))
  (settlement_stream, settlement_writer), (zone_stream, zone_writer), (poi_stream, poi_writer), (sector_stream, sector_writer) = files
  counts: Counter[str] = Counter()
  total_population = 0
  total_urban = 0
  total_rural = 0
  max_zone_population = 0
  economies = rows_as_dicts(source.execute("SELECT * FROM county_economy_baseline ORDER BY county_id"))
  cultures = {row["county_id"]: row for row in rows_as_dicts(source.execute("SELECT * FROM county_culture_education_baseline"))}
  insert_settlements: list[dict[str, Any]] = []
  insert_zones: list[dict[str, Any]] = []
  insert_pois: list[dict[str, Any]] = []
  insert_sectors: list[dict[str, Any]] = []

  def flush() -> None:
    nonlocal insert_settlements, insert_zones, insert_pois, insert_sectors
    if insert_settlements:
      insert_rows(target, "settlement_node", SETTLEMENT_COLUMNS, insert_settlements)
      insert_settlements = []
    if insert_zones:
      insert_rows(target, "settlement_zone", ZONE_COLUMNS, insert_zones)
      insert_zones = []
    if insert_pois:
      insert_rows(target, "settlement_poi", POI_CATALOG_COLUMNS, insert_pois)
      insert_pois = []
    if insert_sectors:
      insert_rows(target, "settlement_sector_quota", SECTOR_QUOTA_COLUMNS, insert_sectors)
      insert_sectors = []

  try:
    for county_index, economy in enumerate(economies, 1):
      county_id = economy["county_id"]
      culture = cultures[county_id]
      villages = rows_as_dicts(source.execute(
        "SELECT * FROM village_catalog WHERE county_id=? ORDER BY village_id", (county_id,)
      ))
      subregions = rows_as_dicts(source.execute(
        "SELECT * FROM county_subregion_definition WHERE county_id=? ORDER BY subregion_id", (county_id,)
      ))
      subregion_by_id = {row["subregion_id"]: row for row in subregions}
      if not villages:
        raise RuntimeError(f"county has no villages: {county_id}")
      population = int(economy["population_est_1628"])
      urban_population = int(economy["urban_population_est"])
      rural_population = population - urban_population
      special = special_settlements(economy, subregions, rural_population)
      special_population = sum(row["resident_population"] for row in special)
      village_pool = rural_population - special_population
      village_populations = allocate_exact(village_pool, [int(row["population_weight_ppm"]) for row in villages])
      town_score = max(0, economy["commercial_prosperity_1628_0_100"] - 28)
      town_count = 0 if urban_population < 2_000 or town_score <= 0 else min(8, max(1, round(urban_population * town_score / 100 / 5_500)))
      seat_weight = 6 + economy["administrative_centrality_0_100"] / 12
      town_weights = [1 + economy["commercial_prosperity_1628_0_100"] / 100 + index * 0.02 for index in range(town_count)]
      urban_parts = allocate_exact(urban_population, [seat_weight, *town_weights])
      core = next((row for row in subregions if row["zone_type"] == "county_core"), subregions[0])
      used_names = {row["village_name"] for row in villages}
      settlements: list[dict[str, Any]] = []
      seat_name = unique_generated_name(f"{economy['county']}城", used_names, 1)
      settlements.append({
        "settlement_id": f"{county_id}-SC01", "snapshot_year": SNAPSHOT_YEAR, "county_id": county_id,
        "subregion_id": core["subregion_id"], "settlement_type_code": "county_seat",
        "settlement_name": seat_name, "name_source_type": "administrative_name_projection",
        "historical_name_claim": "administrative_seat_only", "source_village_id": "",
        "urban_rural": "urban", "resident_population": urban_parts[0], "labor_force_est": 0,
        "relative_x_0_10000": core["center_rel_x_0_10000"], "relative_y_0_10000": core["center_rel_y_0_10000"],
        "render_seed": stable_digest(county_id, "county-seat").hex()[:16],
        "population_allocation_method": "urban baseline largest remainder v0.6",
        "commercial_release_ready": COMMERCIAL_RELEASE_READY,
      })
      town_roots = ["东关", "南关", "西关", "北关", "河口", "桥头", "新集", "平码头"]
      for ordinal in range(1, town_count + 1):
        subregion = subregions[(ordinal + stable_int(0, len(subregions) - 1, county_id, "town-subregion")) % len(subregions)]
        name = unique_generated_name(town_roots[(ordinal - 1) % len(town_roots)] + "镇", used_names, ordinal)
        settlements.append({
          "settlement_id": f"{county_id}-SM{ordinal:03d}", "snapshot_year": SNAPSHOT_YEAR, "county_id": county_id,
          "subregion_id": subregion["subregion_id"], "settlement_type_code": "market_town",
          "settlement_name": name, "name_source_type": "generated_period_style",
          "historical_name_claim": "no", "source_village_id": "", "urban_rural": "urban",
          "resident_population": urban_parts[ordinal], "labor_force_est": 0,
          "relative_x_0_10000": subregion["center_rel_x_0_10000"], "relative_y_0_10000": subregion["center_rel_y_0_10000"],
          "render_seed": stable_digest(county_id, "market-town", ordinal).hex()[:16],
          "population_allocation_method": "urban baseline largest remainder v0.6",
          "commercial_release_ready": COMMERCIAL_RELEASE_READY,
        })
      for ordinal, special_row in enumerate(special, 1):
        name = unique_generated_name(special_row.pop("base_name"), used_names, ordinal)
        settlements.append({
          **special_row, "snapshot_year": SNAPSHOT_YEAR, "county_id": county_id,
          "settlement_name": name, "name_source_type": "generated_period_style",
          "historical_name_claim": "no", "source_village_id": "", "urban_rural": "rural",
          "labor_force_est": 0, "render_seed": stable_digest(special_row["settlement_id"], "render").hex()[:16],
          "population_allocation_method": "rural special-site capped allocation v0.6",
          "commercial_release_ready": COMMERCIAL_RELEASE_READY,
        })
      for village, village_population in zip(villages, village_populations):
        settlements.append({
          "settlement_id": village["village_id"], "snapshot_year": SNAPSHOT_YEAR, "county_id": county_id,
          "subregion_id": village["subregion_id"], "settlement_type_code": "village",
          "settlement_name": village["village_name"], "name_source_type": village["name_source_type"],
          "historical_name_claim": village["historical_name_claim"], "source_village_id": village["village_id"],
          "urban_rural": "rural", "resident_population": village_population, "labor_force_est": 0,
          "relative_x_0_10000": village["relative_x_0_10000"], "relative_y_0_10000": village["relative_y_0_10000"],
          "render_seed": village["render_seed"], "population_allocation_method": "exact rural largest remainder v0.6",
          "commercial_release_ready": COMMERCIAL_RELEASE_READY,
        })
      settlement_labors = allocate_exact(int(economy["labor_force_est"]), [row["resident_population"] for row in settlements])
      for settlement, settlement_labor in zip(settlements, settlement_labors):
        settlement["labor_force_est"] = settlement_labor
        settlement_writer.writerow({column: settlement.get(column, "") for column in SETTLEMENT_COLUMNS})
        insert_settlements.append(settlement)
        counts["settlements"] += 1
        counts[f"settlement_type:{settlement['settlement_type_code']}"] += 1
        total_population += int(settlement["resident_population"])
        if settlement["urban_rural"] == "urban":
          total_urban += int(settlement["resident_population"])
        else:
          total_rural += int(settlement["resident_population"])
        sector_counts = local_sector_counts(settlement_labor, county_sector_counts[county_id], settlement["settlement_type_code"])
        sector_row = {"settlement_id": settlement["settlement_id"], "county_id": county_id, "labor_force_est": settlement_labor}
        sector_row.update({f"{code}_count": sector_counts[code] for code in SECTOR_CODES})
        sector_writer.writerow(sector_row)
        insert_sectors.append(sector_row)
        zones = zone_rows_for_settlement(settlement)
        for zone in zones:
          zone_writer.writerow(zone)
          insert_zones.append(zone)
          counts["zones"] += 1
          max_zone_population = max(max_zone_population, int(zone["resident_population"]))
        poi_types = poi_types_for_settlement(settlement, economy, culture)
        for poi_ordinal, poi_type in enumerate(poi_types, 1):
          definition = next(row for row in POI_DEFINITIONS if row[0] == poi_type)
          zone = zones[(poi_ordinal - 1) % len(zones)]
          poi = {
            "poi_id": f"{settlement['settlement_id']}-P{poi_ordinal:03d}", "snapshot_year": SNAPSHOT_YEAR,
            "settlement_id": settlement["settlement_id"], "zone_id": zone["zone_id"], "county_id": county_id,
            "poi_type_code": poi_type, "poi_name": f"{settlement['settlement_name']}·{definition[1]}",
            "capacity_est": definition[4], "workforce_slots_est": max(1, round(definition[4] * 0.35)),
            "name_source_type": "generated_functional_name", "historical_claim": "no",
            "location_precision": "generated_gameplay_placement",
            "render_seed": stable_digest(settlement["settlement_id"], poi_type, poi_ordinal).hex()[:16],
            "commercial_release_ready": COMMERCIAL_RELEASE_READY,
          }
          poi_writer.writerow(poi)
          insert_pois.append(poi)
          counts["pois"] += 1
        if len(insert_settlements) >= 8_000:
          flush()
          target.commit()
      if county_index % 100 == 0:
        print(f"[v0.6] settlements {county_index}/{len(economies)} counties", flush=True)
    flush()
    target.commit()
  finally:
    for stream, _ in files:
      stream.close()
  return {
    "counts": dict(counts),
    "total_population": total_population,
    "total_urban_population": total_urban,
    "total_rural_population": total_rural,
    "max_zone_population": max_zone_population,
    "generated_files": [f"generated/{path.name}" for path in (settlement_path, zone_path, poi_path, sector_path)],
  }


def install_indexes_and_views(connection: sqlite3.Connection) -> None:
  statements = [
    "CREATE INDEX IF NOT EXISTS idx_social_occupation_county ON county_occupation_quota(county_id,worker_count_est DESC)",
    "CREATE INDEX IF NOT EXISTS idx_settlement_county ON settlement_node(county_id,settlement_type_code,settlement_id)",
    "CREATE UNIQUE INDEX IF NOT EXISTS idx_settlement_county_name ON settlement_node(county_id,settlement_name)",
    "CREATE INDEX IF NOT EXISTS idx_zone_settlement ON settlement_zone(settlement_id,zone_id)",
    "CREATE INDEX IF NOT EXISTS idx_poi_zone ON settlement_poi(zone_id,poi_type_code,poi_id)",
    "CREATE INDEX IF NOT EXISTS idx_poi_settlement ON settlement_poi(settlement_id,poi_type_code,poi_id)",
  ]
  for statement in statements:
    connection.execute(statement)
  for view in ("v_county_social_overview", "v_county_entry_settlements", "v_settlement_entry_zones", "v_zone_entry_pois", "v_settlement_occupation_profile"):
    connection.execute(f"DROP VIEW IF EXISTS {view}")
  connection.execute("CREATE VIEW v_county_social_overview AS SELECT * FROM county_education_occupation_class_overview")
  connection.execute(
    "CREATE VIEW v_county_entry_settlements AS "
    "SELECT s.*,e.region,e.upper_unit,e.intermediate_unit,e.county,z.subregion_name,z.direction_name,z.zone_type,z.primary_landform,z.primary_resource_tags "
    "FROM settlement_node s JOIN county_economy_baseline e USING(county_id) "
    "LEFT JOIN county_subregion_definition z USING(subregion_id)"
  )
  connection.execute(
    "CREATE VIEW v_settlement_entry_zones AS "
    "SELECT z.*,s.settlement_name,s.settlement_type_code,s.urban_rural "
    "FROM settlement_zone z JOIN settlement_node s USING(settlement_id)"
  )
  connection.execute(
    "CREATE VIEW v_zone_entry_pois AS "
    "SELECT p.*,s.settlement_name,z.zone_name,d.display_name_zh_hans AS poi_type_name,d.sector_code "
    "FROM settlement_poi p JOIN settlement_node s USING(settlement_id) "
    "JOIN settlement_zone z USING(zone_id) JOIN institution_poi_definition d USING(poi_type_code)"
  )
  connection.execute(
    "CREATE VIEW v_settlement_occupation_profile AS "
    "SELECT s.settlement_id,s.settlement_name,s.settlement_type_code,s.resident_population,q.* "
    "FROM settlement_node s JOIN settlement_sector_quota q USING(settlement_id)"
  )
  connection.execute("PRAGMA user_version=6")
  connection.commit()


def validate_build(
  connection: sqlite3.Connection,
  source: sqlite3.Connection,
  settlement_result: dict[str, Any],
) -> dict[str, Any]:
  checks: dict[str, Any] = {}
  checks["county_education_rows"] = connection.execute("SELECT COUNT(*) FROM county_education_profile").fetchone()[0]
  checks["county_social_rows"] = connection.execute("SELECT COUNT(*) FROM county_social_structure_baseline").fetchone()[0]
  checks["county_overview_rows"] = connection.execute("SELECT COUNT(*) FROM county_education_occupation_class_overview").fetchone()[0]
  checks["occupation_definition_rows"] = connection.execute("SELECT COUNT(*) FROM occupation_definition").fetchone()[0]
  checks["county_occupation_rows"] = connection.execute("SELECT COUNT(*) FROM county_occupation_quota").fetchone()[0]
  checks["cbdb_household_mapping_rows"] = connection.execute(
    "SELECT COUNT(*) FROM cbdb_household_status_mapping"
  ).fetchone()[0]
  checks["cbdb_household_mapping_orphan_count"] = connection.execute(
    "SELECT COUNT(*) FROM cbdb_household_status_mapping m "
    "LEFT JOIN social_status_definition s ON s.axis_code='registration' "
    "AND s.status_code=m.registration_rollup_code WHERE s.status_code IS NULL"
  ).fetchone()[0]
  checks["cbdb_household_hash_mismatch_count"] = connection.execute(
    "SELECT COUNT(*) FROM cbdb_household_status_mapping WHERE source_database_sha256<>?",
    (PINNED_CBDB_SHA256,),
  ).fetchone()[0]
  checks["cbdb_household_evidence_counties"] = connection.execute(
    "SELECT COUNT(*) FROM county_social_structure_baseline WHERE cbdb_household_status_record_count>0"
  ).fetchone()[0]
  checks["cbdb_household_evidence_present"] = checks["cbdb_household_evidence_counties"] > 0
  checks["cbdb_household_evidence_weight_violation_count"] = connection.execute(
    "SELECT COUNT(*) FROM county_social_structure_baseline "
    "WHERE cbdb_household_evidence_weight_0_100<0 OR cbdb_household_evidence_weight_0_100>12"
  ).fetchone()[0]
  checks["source_village_rows"] = source.execute("SELECT COUNT(*) FROM village_catalog").fetchone()[0]
  checks["v06_village_rows"] = connection.execute("SELECT COUNT(*) FROM settlement_node WHERE settlement_type_code='village'").fetchone()[0]
  checks["county_seat_rows"] = connection.execute("SELECT COUNT(*) FROM settlement_node WHERE settlement_type_code='county_seat'").fetchone()[0]
  checks["settlement_population"] = connection.execute("SELECT SUM(resident_population) FROM settlement_node").fetchone()[0]
  checks["settlement_urban_population"] = connection.execute("SELECT SUM(resident_population) FROM settlement_node WHERE urban_rural='urban'").fetchone()[0]
  checks["source_urban_population"] = source.execute("SELECT SUM(urban_population_est) FROM county_economy_baseline").fetchone()[0]
  checks["occupation_labor_mismatch_count"] = connection.execute(
    "SELECT COUNT(*) FROM (SELECT q.county_id,SUM(q.worker_count_est) total,e.labor_force_est expected "
    "FROM county_occupation_quota q JOIN county_economy_baseline e USING(county_id) GROUP BY q.county_id HAVING total<>expected)"
  ).fetchone()[0]
  checks["occupation_share_mismatch_count"] = connection.execute(
    "SELECT COUNT(*) FROM (SELECT county_id,SUM(worker_share_ppm) total FROM county_occupation_quota GROUP BY county_id HAVING total<>1000000)"
  ).fetchone()[0]
  checks["education_population_mismatch_count"] = connection.execute(
    "SELECT COUNT(*) FROM county_education_profile WHERE "
    "literacy_l0_count+literacy_l1_count+literacy_l2_count+literacy_l3_count+literacy_l4_count<>population_est_1628"
  ).fetchone()[0]
  checks["education_literate_mismatch_count"] = connection.execute(
    "SELECT COUNT(*) FROM county_education_profile WHERE "
    "literacy_l1_count+literacy_l2_count+literacy_l3_count+literacy_l4_count<>total_literate_est "
    "OR literacy_l4_count<>classical_educated_est"
  ).fetchone()[0]
  checks["social_registration_share_mismatch_count"] = connection.execute(
    "SELECT COUNT(*) FROM county_social_structure_baseline WHERE " + "+".join(f"registration_{code}_share_ppm" for code in REGISTRATION_CODES) + "<>1000000"
  ).fetchone()[0]
  checks["social_economic_share_mismatch_count"] = connection.execute(
    "SELECT COUNT(*) FROM county_social_structure_baseline WHERE " + "+".join(f"economic_{code}_share_ppm" for code in ECONOMIC_CODES) + "<>1000000"
  ).fetchone()[0]
  checks["zone_over_1000_count"] = connection.execute("SELECT COUNT(*) FROM settlement_zone WHERE resident_population>1000").fetchone()[0]
  checks["settlement_population_mismatch_count"] = connection.execute(
    "SELECT COUNT(*) FROM (SELECT s.county_id,SUM(s.resident_population) total,e.population_est_1628 expected "
    "FROM settlement_node s JOIN county_economy_baseline e USING(county_id) GROUP BY s.county_id HAVING total<>expected)"
  ).fetchone()[0]
  checks["settlement_labor_mismatch_count"] = connection.execute(
    "SELECT COUNT(*) FROM (SELECT s.county_id,SUM(s.labor_force_est) total,e.labor_force_est expected "
    "FROM settlement_node s JOIN county_economy_baseline e USING(county_id) GROUP BY s.county_id HAVING total<>expected)"
  ).fetchone()[0]
  checks["village_identity_mismatch_count"] = connection.execute(
    "SELECT COUNT(*) FROM village_catalog v LEFT JOIN settlement_node s ON s.settlement_id=v.village_id "
    "WHERE s.settlement_id IS NULL OR s.settlement_name<>v.village_name OR s.source_village_id<>v.village_id"
  ).fetchone()[0]
  checks["declared_foreign_key_count"] = sum(
    len(connection.execute(f'PRAGMA foreign_key_list("{table}")').fetchall())
    for table in (
      "county_education_profile", "county_social_structure_baseline",
      "county_occupation_quota", "county_education_occupation_class_overview",
      "historical_social_manual_anchors", "settlement_node", "settlement_zone",
      "settlement_poi", "settlement_sector_quota",
    )
  )
  checks["foreign_key_check_count"] = len(connection.execute("PRAGMA foreign_key_check").fetchall())
  checks["user_version"] = connection.execute("PRAGMA user_version").fetchone()[0]
  scenario_definitions = {
    "jiangnan_textile": ("MING1628-0154", "textile_clothing"),
    "shanxi_mining": ("MING1628-0373", "mining_salt"),
    "coastal_fishery": ("MING1628-1000", "fishery_water"),
    "mountain_forest": ("MING1628-0956", "forestry_hunting"),
    "canal_commerce": ("MING1628-0147", "commerce_finance"),
    "administrative_center": ("MING1628-0001", "government_admin"),
  }
  scenario_profiles: dict[str, Any] = {}
  for label, (county_id, sector_code) in scenario_definitions.items():
    shares = [
      float(row[0]) for row in connection.execute(
        "SELECT SUM(CASE WHEN sector_code=? THEN worker_count_est ELSE 0 END)*100.0/SUM(worker_count_est) "
        "FROM county_occupation_quota GROUP BY county_id ORDER BY county_id", (sector_code,),
      ).fetchall()
    ]
    target_share = float(connection.execute(
      "SELECT SUM(CASE WHEN sector_code=? THEN worker_count_est ELSE 0 END)*100.0/SUM(worker_count_est) "
      "FROM county_occupation_quota WHERE county_id=?", (sector_code, county_id),
    ).fetchone()[0])
    median_share = float(statistics.median(shares))
    scenario_profiles[label] = {
      "county_id": county_id, "sector_code": sector_code,
      "target_worker_share_pct": round(target_share, 3),
      "national_median_share_pct": round(median_share, 3),
      "above_national_median": target_share > median_share,
    }
  checks["occupation_scenario_profiles"] = scenario_profiles
  checks["occupation_scenario_profiles_pass"] = all(
    profile["above_national_median"] for profile in scenario_profiles.values()
  )
  checks["view_row_counts"] = {
    view: connection.execute(f'SELECT COUNT(*) FROM "{view}"').fetchone()[0]
    for view in (
      "v_county_social_overview", "v_county_entry_settlements",
      "v_settlement_entry_zones", "v_zone_entry_pois",
      "v_settlement_occupation_profile",
    )
  }
  expected_view_rows = {
    "v_county_social_overview": EXPECTED_COUNTIES,
    "v_county_entry_settlements": connection.execute("SELECT COUNT(*) FROM settlement_node").fetchone()[0],
    "v_settlement_entry_zones": connection.execute("SELECT COUNT(*) FROM settlement_zone").fetchone()[0],
    "v_zone_entry_pois": connection.execute("SELECT COUNT(*) FROM settlement_poi").fetchone()[0],
    "v_settlement_occupation_profile": connection.execute("SELECT COUNT(*) FROM settlement_node").fetchone()[0],
  }
  checks["view_row_count_mismatch_count"] = sum(
    checks["view_row_counts"][view] != expected_rows
    for view, expected_rows in expected_view_rows.items()
  )
  expected_column_types = {
    ("county_social_structure_baseline", "registration_estimation_method"): "TEXT",
    ("county_social_structure_baseline", "economic_estimation_method"): "TEXT",
    ("county_education_occupation_class_overview", "male_literacy_mid_pct"): "REAL",
    ("county_education_occupation_class_overview", "female_literacy_mid_pct"): "REAL",
    ("county_occupation_quota", "worker_count_est"): "INTEGER",
    ("county_occupation_quota", "raw_weight"): "REAL",
    ("cbdb_household_status_mapping", "cbdb_household_status_code"): "INTEGER",
    ("cbdb_household_status_mapping", "source_person_count_all_periods"): "INTEGER",
  }
  actual_column_types = {
    (table, row[1]): row[2]
    for table in {table for table, _ in expected_column_types}
    for row in connection.execute(f'PRAGMA table_info("{table}")').fetchall()
  }
  checks["schema_column_types"] = {
    f"{table}.{column}": actual_column_types.get((table, column), "missing")
    for table, column in expected_column_types
  }
  checks["schema_type_mismatch_count"] = sum(
    actual_column_types.get(key) != expected_type
    for key, expected_type in expected_column_types.items()
  )
  errors = []
  expected = {
    "county_education_rows": EXPECTED_COUNTIES,
    "county_social_rows": EXPECTED_COUNTIES,
    "county_overview_rows": EXPECTED_COUNTIES,
    "occupation_definition_rows": EXPECTED_OCCUPATIONS,
    "county_occupation_rows": EXPECTED_OCCUPATION_ROWS,
    "cbdb_household_mapping_rows": 34,
    "cbdb_household_mapping_orphan_count": 0,
    "cbdb_household_hash_mismatch_count": 0,
    "cbdb_household_evidence_present": True,
    "cbdb_household_evidence_weight_violation_count": 0,
    "source_village_rows": EXPECTED_VILLAGES,
    "v06_village_rows": EXPECTED_VILLAGES,
    "county_seat_rows": EXPECTED_COUNTIES,
    "settlement_population": EXPECTED_TOTAL_POPULATION,
    "settlement_urban_population": checks["source_urban_population"],
    "occupation_labor_mismatch_count": 0,
    "occupation_share_mismatch_count": 0,
    "education_population_mismatch_count": 0,
    "education_literate_mismatch_count": 0,
    "social_registration_share_mismatch_count": 0,
    "social_economic_share_mismatch_count": 0,
    "zone_over_1000_count": 0,
    "settlement_population_mismatch_count": 0,
    "settlement_labor_mismatch_count": 0,
    "village_identity_mismatch_count": 0,
    "declared_foreign_key_count": 17,
    "foreign_key_check_count": 0,
    "user_version": 6,
    "occupation_scenario_profiles_pass": True,
    "view_row_count_mismatch_count": 0,
    "schema_type_mismatch_count": 0,
  }
  for key, value in expected.items():
    if checks.get(key) != value:
      errors.append(f"{key}: expected {value}, got {checks.get(key)}")
  checks.update(settlement_result)
  return {"status": "pass" if not errors else "fail", "errors": errors, "checks": checks}


def build_source_manifest(source_database: Path, cbdb_database: Path, anchors_path: Path) -> list[dict[str, Any]]:
  rows = [
    {
      "source_id": "v0.4_sqlite", "source_title": "Project Realm game_world_1628_v0.4.sqlite",
      "pinned_version": "user_version=4", "content_hash": file_sha256(source_database),
      "source_url": "local:v0.4", "usage": "county economy, culture, settlements, people and institutions",
      "evidence_boundary": "inherits CHGIS and CBDB research restrictions", "commercial_release_ready": "no",
    },
    {
      "source_id": "mingshi_69", "source_title": "明史卷六十九·选举志",
      "pinned_version": "accessed 2026-08-30", "content_hash": "",
      "source_url": "https://zh.wikisource.org/zh/明史/卷69", "usage": "school, examination and entry-path taxonomy",
      "evidence_boundary": "institutional structure, not county enrolment counts", "commercial_release_ready": "no",
    },
    {
      "source_id": "mingshi_77", "source_title": "明史卷七十七·食货志",
      "pinned_version": "accessed 2026-08-30", "content_hash": "",
      "source_url": "https://zh.wikisource.org/zh-hant/明史/卷77", "usage": "household registration and livelihood taxonomy",
      "evidence_boundary": "legal categories, not 1628 county occupation census", "commercial_release_ready": "no",
    },
    {
      "source_id": "cbdb_20260822", "source_title": "China Biographical Database SQLite",
      "pinned_version": "cbdb_20260822", "content_hash": file_sha256(cbdb_database),
      "source_url": "https://cbdb.hsites.harvard.edu/structure-cbdb", "usage": "household-status source codes and separate status, entry, office and institution axes",
      "evidence_boundary": "elite-biased; code evidence is capped and is not an ordinary-household census", "commercial_release_ready": "no",
    },
    {
      "source_id": "work_ethics", "source_title": "Work Ethics and Work Valuations in Ming China, 1500-1644",
      "pinned_version": "IRSH article", "content_hash": "",
      "source_url": "https://www.cambridge.org/core/journals/international-review-of-social-history/article/work-ethics-and-work-valuations-in-a-period-of-commercialization-ming-china-15001644/4FA7B22851A0B0D2E37700E494A6570B",
      "usage": "household production, commercialization, women and occupational diversity",
      "evidence_boundary": "macro historical synthesis, not exact county shares", "commercial_release_ready": "no",
    },
    {
      "source_id": "manual_anchors", "source_title": "Historical social manual anchors v0.6",
      "pinned_version": RULESET_VERSION, "content_hash": file_sha256(anchors_path) if anchors_path.exists() else "",
      "source_url": "local:historical_social_manual_anchors_v0.6.csv", "usage": "selected county education/occupation anchors",
      "evidence_boundary": "only listed counties and fields", "commercial_release_ready": "no",
    },
  ]
  return rows


def main() -> None:
  parser = argparse.ArgumentParser(description="Build Ming 1628 education, occupation, class and settlement data v0.6")
  parser.add_argument("--source-database", type=Path, default=DEFAULT_SOURCE_DATABASE)
  parser.add_argument("--cbdb-database", type=Path, default=DEFAULT_CBDB_DATABASE)
  parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT_DIR)
  args = parser.parse_args()
  if not args.source_database.exists():
    raise SystemExit(f"Missing v0.4 SQLite database: {args.source_database}")
  if not args.cbdb_database.exists():
    raise SystemExit(f"Missing pinned CBDB SQLite database: {args.cbdb_database}")
  output_dir = args.output_dir
  output_dir.mkdir(parents=True, exist_ok=True)
  anchors_path = output_dir / "historical_social_manual_anchors_v0.6.csv"
  if not anchors_path.exists():
    raise SystemExit(f"Missing manual anchors input: {anchors_path}")
  start = time.perf_counter()
  occupations = build_occupation_definitions()
  education_definitions = build_education_definitions()
  social_definitions = build_social_definitions()
  archetypes = build_archetype_definitions()
  poi_definitions = build_poi_definitions()
  anchors = read_csv(anchors_path)
  source = sqlite3.connect(f"file:{args.source_database}?mode=ro", uri=True)
  source.row_factory = sqlite3.Row
  if source.execute("PRAGMA user_version").fetchone()[0] != 4:
    raise RuntimeError("v0.6 requires source SQLite user_version=4")
  cbdb_household_mapping_rows, cbdb_household_evidence = load_cbdb_household_evidence(
    source, args.cbdb_database,
  )
  print("[v0.6] building 1,168 county education, class and 150-occupation quotas", flush=True)
  education_rows, social_rows, occupation_rows, overview_rows, county_sector_counts = build_county_rows(
    source, occupations, anchors, cbdb_household_evidence,
  )

  write_csv_atomic(output_dir / "education_definition_v0.6.csv", EDUCATION_COLUMNS, education_definitions)
  write_csv_atomic(output_dir / "occupation_definition_v0.6.csv", OCCUPATION_COLUMNS, occupations)
  write_csv_atomic(output_dir / "social_status_definition_v0.6.csv", SOCIAL_COLUMNS, social_definitions)
  write_csv_atomic(
    output_dir / "cbdb_household_status_mapping_v0.6.csv",
    CBDB_HOUSEHOLD_MAPPING_COLUMNS,
    cbdb_household_mapping_rows,
  )
  write_csv_atomic(output_dir / "settlement_archetype_definition_v0.6.csv", ARCHETYPE_COLUMNS, archetypes)
  write_csv_atomic(output_dir / "institution_poi_definition_v0.6.csv", POI_COLUMNS, poi_definitions)
  write_csv_atomic(output_dir / "county_education_profile_v0.6.csv", COUNTY_EDUCATION_COLUMNS, education_rows)
  write_csv_atomic(output_dir / "county_social_structure_baseline_v0.6.csv", COUNTY_SOCIAL_COLUMNS, social_rows)
  write_csv_atomic(output_dir / "county_occupation_quota_v0.6.csv", COUNTY_OCCUPATION_COLUMNS, occupation_rows)
  write_csv_atomic(output_dir / "county_education_occupation_class_overview_v0.6.csv", COUNTY_OVERVIEW_COLUMNS, overview_rows)
  manifest_rows = build_source_manifest(args.source_database, args.cbdb_database, anchors_path)
  manifest_columns = ["source_id", "source_title", "pinned_version", "content_hash", "source_url", "usage", "evidence_boundary", "commercial_release_ready"]
  write_csv_atomic(output_dir / "social_source_manifest_v0.6.csv", manifest_columns, manifest_rows)

  database_path = output_dir / "game_world_1628_v0.6.sqlite"
  definitions = {
    "education_definition": (EDUCATION_COLUMNS, education_definitions, ["definition_type", "definition_code"]),
    "occupation_definition": (OCCUPATION_COLUMNS, occupations, ["occupation_code"]),
    "social_status_definition": (SOCIAL_COLUMNS, social_definitions, ["axis_code", "status_code"]),
    "cbdb_household_status_mapping": (
      CBDB_HOUSEHOLD_MAPPING_COLUMNS,
      cbdb_household_mapping_rows,
      ["cbdb_household_status_code"],
    ),
    "settlement_archetype_definition": (ARCHETYPE_COLUMNS, archetypes, ["settlement_type_code"]),
    "institution_poi_definition": (POI_COLUMNS, poi_definitions, ["poi_type_code"]),
    "county_education_profile": (COUNTY_EDUCATION_COLUMNS, education_rows, ["county_id"]),
    "county_social_structure_baseline": (COUNTY_SOCIAL_COLUMNS, social_rows, ["county_id"]),
    "county_occupation_quota": (COUNTY_OCCUPATION_COLUMNS, occupation_rows, ["county_id", "occupation_code"]),
    "county_education_occupation_class_overview": (COUNTY_OVERVIEW_COLUMNS, overview_rows, ["county_id"]),
    "historical_social_manual_anchors": (list(anchors[0].keys()) if anchors else ["anchor_id"], anchors, ["anchor_id"]),
    "social_source_manifest": (manifest_columns, manifest_rows, ["source_id"]),
  }
  target = install_small_tables(database_path, args.source_database, definitions)
  generated_temporary = output_dir / ".generated_v0.6.tmp"
  if generated_temporary.exists():
    shutil.rmtree(generated_temporary)
  generated_temporary.mkdir(parents=True)
  print("[v0.6] repartitioning population and building all settlement scenes", flush=True)
  settlement_result = build_settlements(source, target, generated_temporary, county_sector_counts)
  install_indexes_and_views(target)
  validation = validate_build(target, source, settlement_result)
  if validation["status"] != "pass":
    target.close()
    source.close()
    raise RuntimeError("v0.6 validation failed: " + "; ".join(validation["errors"]))
  target.execute("VACUUM")
  target.close()
  source.close()
  temporary_database = database_path.with_suffix(database_path.suffix + ".tmp")
  if not temporary_database.exists():
    raise RuntimeError("temporary v0.6 database is missing")
  if database_path.exists():
    database_path.unlink()
  temporary_database.replace(database_path)
  generated_dir = output_dir / "generated"
  if generated_dir.exists():
    shutil.rmtree(generated_dir)
  generated_temporary.replace(generated_dir)
  elapsed = time.perf_counter() - start
  validation["ruleset_version"] = RULESET_VERSION
  validation["elapsed_seconds"] = round(elapsed, 3)
  validation["database_sha256"] = file_sha256(database_path)
  validation["generated_file_hashes"] = {
    path.name: file_sha256(path) for path in sorted(generated_dir.glob("*.csv"))
  }
  validation["tracked_csv_hashes"] = {
    path.name: file_sha256(path)
    for path in sorted(output_dir.glob("*.csv"))
    if path.name != "county_occupation_quota_v0.6.csv"
  }
  build_fingerprint_payload = {
    "ruleset_version": RULESET_VERSION,
    "database_sha256": validation["database_sha256"],
    "generated_file_hashes": validation["generated_file_hashes"],
    "tracked_csv_hashes": validation["tracked_csv_hashes"],
  }
  validation["build_fingerprint"] = hashlib.sha256(
    json.dumps(build_fingerprint_payload, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
  ).hexdigest()
  write_json_atomic(output_dir / "social_v0.6_validation_report.json", validation)
  print(json.dumps({
    "status": validation["status"], "elapsed_seconds": validation["elapsed_seconds"],
    "database": str(database_path), "database_sha256": validation["database_sha256"],
    "settlements": validation["checks"]["counts"].get("settlements"),
    "zones": validation["checks"]["counts"].get("zones"),
    "pois": validation["checks"]["counts"].get("pois"),
  }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
  main()
