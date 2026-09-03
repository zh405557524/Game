#!/usr/bin/env python3
"""Build a rough, auditable geography/resource model for Project Realm.

This is deliberately a game-planning model, not a claim that exact 1628
county boundaries, climate normals, or resource inventories are known.  It
combines:

* the project's reconstructed 1628 civil county hierarchy;
* CHGIS V6 county-seat points and 1820 prefecture areas;
* CHGIS 1820 river/lake geometry as a nearby historical proxy;
* explicit terrain, climate, resource, and weather-risk rules.

The generated CSV and SQLite outputs keep source/method/quality fields so that
better data can replace any estimate later without changing the game schema.

CHGIS V6 is licensed for academic/non-commercial research.  Outputs derived
from its coordinates are therefore marked ``commercial_release_ready = no``.
Replace or separately license those coordinates before a commercial release.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import math
import sqlite3
import struct
from collections import Counter, defaultdict
from pathlib import Path
from statistics import median
from typing import Any, Iterable


SNAPSHOT_YEAR = 1628
EARTH_KM_PER_DEGREE = 111.2

DEFAULT_DATA_ROOT = Path("docs/90_资料与归档/01_崇祯元年历史资料/data/1628")
DEFAULT_CHAPTER3_DIR = DEFAULT_DATA_ROOT / "3.疆域与人口"
DEFAULT_CHAPTER4_DIR = DEFAULT_DATA_ROOT / "4.地理地貌资源与天气"

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

REGION_QING_PROVINCES = {
    "北直隶（京师）": {"直隶"},
    "南直隶（南京）": {"江苏", "安徽"},
    "山东": {"山东"},
    "山西": {"山西"},
    "河南": {"河南"},
    "陕西": {"陕西", "甘肃"},
    "四川": {"四川", "贵州"},  # 遵义在明代属四川、清代属贵州
    "江西": {"江西"},
    "湖广": {"湖北", "湖南"},
    "浙江": {"浙江"},
    "福建": {"福建"},
    "广东": {"广东"},
    "广西": {"广西"},
    "云南": {"云南"},
    "贵州": {"贵州"},
}

REGION_MODERN_PREFIXES = {
    "北直隶（京师）": ("北京", "天津", "河北"),
    "南直隶（南京）": ("江苏", "安徽", "上海"),
    "山东": ("山东",),
    "山西": ("山西",),
    "河南": ("河南",),
    "陕西": ("陕西", "甘肃", "宁夏", "青海"),
    "四川": ("四川", "重庆", "贵州"),
    "江西": ("江西",),
    "湖广": ("湖北", "湖南"),
    "浙江": ("浙江",),
    "福建": ("福建",),
    "广东": ("广东", "海南"),
    "广西": ("广西",),
    "云南": ("云南",),
    "贵州": ("贵州",),
}

REGION_BBOX = {
    # 明代大名府南境跨至今河南滑县、长垣一带，不能以现代河北省界作为南界。
    "北直隶（京师）": (113.0, 35.0, 121.0, 42.5),
    "南直隶（南京）": (115.0, 28.5, 123.0, 35.5),
    "山东": (114.0, 34.0, 123.0, 39.5),
    "山西": (109.0, 34.0, 116.0, 41.5),
    "河南": (109.0, 30.5, 118.0, 37.5),
    "陕西": (99.0, 30.0, 112.5, 40.5),
    "四川": (97.0, 25.0, 111.0, 35.0),
    "江西": (112.5, 23.5, 119.5, 32.0),
    "湖广": (107.0, 23.0, 117.5, 34.5),
    "浙江": (117.5, 26.5, 123.0, 32.0),
    "福建": (115.5, 22.5, 121.5, 29.0),
    "广东": (107.0, 17.5, 118.5, 26.5),
    "广西": (103.5, 20.0, 114.0, 27.5),
    "云南": (96.0, 20.5, 107.0, 30.0),
    "贵州": (103.0, 23.5, 110.5, 30.0),
}

REGION_CENTRE = {
    region: ((bbox[0] + bbox[2]) / 2, (bbox[1] + bbox[3]) / 2)
    for region, bbox in REGION_BBOX.items()
}

# Only used when neither the CHGIS time series nor the 1820 time slice contains
# the exact county name.  Values are county-seat approximations, not borders.
MANUAL_COORDINATES = {
    "三泊县": (102.60, 24.60, "昆阳州附近推定"),
    "于潜县": (119.39, 30.18, "今杭州临安於潜镇"),
    "亦佐县": (104.10, 25.60, "曲靖府东部推定"),
    "大兴县": (116.40, 39.90, "北京城附郭"),
    "庄浪县": (106.05, 35.20, "今甘肃庄浪附近"),
    "感恩县": (108.65, 18.90, "今海南东方市境内"),
    "慈溪县": (121.45, 29.99, "今宁波慈城附近"),
    "扶凤县": (107.90, 34.38, "今陕西扶风附近"),
    "柷嘉县": (101.50, 24.70, "楚雄府东南部推定"),
    "漷县": (116.77, 39.65, "今北京通州漷县镇附近"),
    "灵璧县": (117.55, 33.55, "今安徽灵璧附近"),
    "瓯宁县": (118.32, 27.04, "建宁府附郭、今建瓯附近"),
    "真宁县": (108.36, 35.49, "今甘肃正宁附近"),
    "荥经县": (102.85, 29.79, "今四川荥经附近"),
    "阳宗县": (103.14, 24.89, "今云南阳宗镇附近"),
    "青涧县": (110.12, 37.09, "今陕西清涧附近"),
}

# Same-name counties cannot always be disambiguated from the CHGIS point layer
# alone.  These hierarchy-qualified overrides are applied before name search.
HIERARCHY_COORDINATES = {
    ("北直隶（京师）", "顺天府", "大兴县"): (116.40, 39.90, "北京城附郭"),
    ("北直隶（京师）", "延庆州", "永宁县"): (116.16, 40.53, "今北京延庆永宁镇附近"),
    ("南直隶（南京）", "池州府", "建德县"): (117.03, 30.12, "今安徽东至尧渡附近"),
    ("山西", "太原府", "孟县"): (113.41, 38.09, "今山西盂县附近；史料作孟县"),
    ("山西", "平阳府", "大宁县"): (110.75, 36.47, "今山西大宁附近"),
    ("陕西", "延安府", "安定县"): (109.67, 37.14, "今陕西子长附近"),
    ("江西", "吉安府", "永丰县"): (115.44, 27.32, "今江西永丰附近"),
    ("广东", "琼州府", "安定县"): (110.36, 19.68, "今海南定安附近；史料作安定县"),
    ("云南", "云南府", "归化县"): (102.80, 24.80, "晋宁州东北、滇池东南岸推定"),
}

PREFECTURE_ALIASES = {
    "临洮府": "兰州府",
    "兴安州": "兴安府",
    "南雄府": "南雄州",
    "嘉定州": "嘉定府",
    "姚安军民府": "姚安府",
    "平越军民府": "平越州",
    "应天府": "江宁府",
    "延庆州": "宣化府",
    "徐州": "徐州府",
    "思恩军民府": "思恩府",
    "承天府": "安陆府",
    "永昌军民府": "永昌府",
    "泽州": "泽州府",
    "潼川州": "潼川府",
    "真定府": "正定府",
    "福宁州": "福宁府",
    "贵阳军民府": "贵阳府",
    "遵义军民府": "遵义府",
    "雅州": "雅州府",
}

DEFAULT_AREA_PER_COUNTY = {
    "北直隶（京师）": 1800,
    "南直隶（南京）": 1900,
    "山东": 1700,
    "山西": 2000,
    "河南": 1700,
    "陕西": 3800,
    "四川": 3300,
    "江西": 2100,
    "湖广": 3000,
    "浙江": 1350,
    "福建": 1800,
    "广东": 2300,
    "广西": 3500,
    "云南": 4800,
    "贵州": 4200,
}

MAX_AREA_PER_COUNTY = {
    "北直隶（京师）": 6000,
    "南直隶（南京）": 6000,
    "山东": 6000,
    "山西": 7000,
    "河南": 6000,
    "陕西": 12000,
    "四川": 10000,
    "江西": 8000,
    "湖广": 9000,
    "浙江": 6000,
    "福建": 7000,
    "广东": 9000,
    "广西": 13000,
    "云南": 15000,
    "贵州": 13000,
}

REGION_DEFAULT_ZONE = {
    "北直隶（京师）": "north_china_plain",
    "南直隶（南京）": "jianghuai_plain",
    "山东": "shandong_hills",
    "山西": "shanxi_basin_plateau",
    "河南": "north_china_plain",
    "陕西": "loess_plateau",
    "四川": "sichuan_basin",
    "江西": "southeast_hills",
    "湖广": "south_central_hills",
    "浙江": "southeast_hills",
    "福建": "minzhe_coastal_hills",
    "广东": "lingnan_coastal_hills",
    "广西": "south_china_karst",
    "云南": "yunnan_basin_plateau",
    "贵州": "south_china_karst",
}


def unit_map(groups: dict[str, str]) -> dict[str, str]:
    output: dict[str, str] = {}
    for units, zone in groups.items():
        for unit in units.split():
            output[unit] = zone
    return output


ZONE_BY_UPPER = unit_map(
    {
        "永平府": "yanshan_coastal_hills",
        "延庆州": "northwest_steppe_plateau",
        "河间府": "north_china_wet_plain",
        "苏州府 松江府 常州府 镇江府": "yangtze_delta",
        "应天府 扬州府": "lower_yangtze_plain",
        "安庆府 太平府 池州府": "yangtze_hill_corridor",
        "宁国府 徽州府 广德州": "southeast_mountains",
        "青州府": "shandong_hills",
        "莱州府 登州府": "shandong_coastal_hills",
        "东昌府": "north_china_plain",
        "太原府 平阳府 汾州府": "shanxi_basin_plateau",
        "潞安府 泽州 沁州 辽州": "taihang_mountains",
        "大同府": "northwest_steppe_plateau",
        "河南府 怀庆府 汝州": "yellow_river_mountain_valley",
        "南阳府": "nanyang_basin",
        "西安府 凤翔府": "guanzhong_plain",
        "汉中府": "hanzhong_basin",
        "兴安州": "qinba_mountains",
        "延安府 庆阳府": "loess_plateau",
        "平凉府 巩昌府 临洮府": "loess_mountain_valley",
        "成都府": "chengdu_plain",
        "夔州府": "yangtze_gorges",
        "龙安府 马湖府 雅州": "qinba_hengduan_mountains",
        "遵义军民府": "south_china_karst",
        "嘉定州 邛州": "sichuan_foothills",
        "南昌府 九江府 南康府 饶州府": "poyang_lake_plain",
        "瑞州府 临江府": "gan_river_valley",
        "赣州府 南安府 广信府 建昌府 吉安府 袁州府": "southeast_mountains",
        "武昌府 汉阳府 承天府 德安府 荆州府": "jianghan_plain",
        "岳州府 常德府": "dongting_lake_plain",
        "襄阳府": "han_river_valley",
        "郧阳府": "qinba_mountains",
        "长沙府": "xiang_river_hills_plain",
        "衡州府 永州府 宝庆府 郴州": "nanling_hills",
        "辰州府 靖州": "wuling_xuefeng_mountains",
        "杭州府 嘉兴府 湖州府 绍兴府": "yangtze_delta",
        "宁波府 台州府 温州府": "minzhe_coastal_hills",
        "严州府 金华府 衢州府 处州府": "southeast_mountains",
        "福州府 兴化府 泉州府 漳州府 福宁州": "minzhe_coastal_hills",
        "建宁府 延平府 汀州府 邵武府": "southeast_mountains",
        "广州府": "pearl_river_delta",
        "韶州府 南雄府": "nanling_hills",
        "潮州府 惠州府": "lingnan_coastal_hills",
        "雷州府": "leizhou_coastal_plain",
        "琼州府": "hainan_island",
        "桂林府 平乐府 柳州府 庆远府 思恩军民府 太平府 镇安府": "south_china_karst",
        "浔州府 南宁府": "guangxi_river_basin",
        "大理府 永昌军民府": "hengduan_plateau_basin",
        "云南府 澂江府": "yunnan_lake_basin",
        "曲靖府 武定府": "yungui_plateau",
        "临安府 楚雄府 姚安军民府": "yunnan_basin_plateau",
        "黎平府 思南府 镇远府 铜仁府": "wuling_miaoling_mountains",
        "贵阳军民府 都匀府 平越军民府 石阡府": "south_china_karst",
    }
)

# Percent fields always sum to 100.  Climate numbers are rounded game
# baselines broadly representative of the zone, not reconstructed observations.
ZONE_TEMPLATES: dict[str, dict[str, Any]] = {
    "north_china_plain": dict(terrain=(72, 8, 4, 0, 5, 2, 0, 9, 0), feature="华北平原", climate="暖温带季风半湿润", temp=12.5, rain=600, frost=205, agri=4.3, soil=4.2, water=3.0, transport=4.0, drought=4, flood=3, cold=3, heat=3, typhoon=1, landslide=1),
    "north_china_wet_plain": dict(terrain=(62, 5, 2, 0, 4, 1, 0, 26, 0), feature="海河下游平原与湖沼", climate="暖温带季风半湿润", temp=12.5, rain=600, frost=205, agri=4.2, soil=4.1, water=4.0, transport=4.0, drought=3, flood=4, cold=3, heat=3, typhoon=1, landslide=1),
    "yanshan_coastal_hills": dict(terrain=(25, 30, 27, 3, 4, 4, 0, 3, 4), feature="燕山与渤海沿岸", climate="暖温带季风", temp=10.5, rain=600, frost=185, agri=2.8, soil=3.0, water=3.0, transport=3.0, drought=3, flood=2, cold=4, heat=2, typhoon=1, landslide=3),
    "jianghuai_plain": dict(terrain=(62, 12, 5, 0, 4, 0, 0, 17, 0), feature="江淮平原", climate="北亚热带季风湿润", temp=15.5, rain=950, frost=245, agri=4.3, soil=4.1, water=4.0, transport=4.0, drought=3, flood=4, cold=2, heat=3, typhoon=1, landslide=1),
    "lower_yangtze_plain": dict(terrain=(58, 8, 4, 0, 3, 0, 0, 22, 5), feature="长江下游平原", climate="北亚热带季风湿润", temp=16.0, rain=1050, frost=255, agri=4.6, soil=4.4, water=4.7, transport=4.8, drought=2, flood=4, cold=2, heat=3, typhoon=2, landslide=1),
    "yangtze_delta": dict(terrain=(52, 5, 2, 0, 1, 0, 0, 27, 13), feature="长江三角洲与太湖平原", climate="亚热带季风湿润", temp=16.5, rain=1150, frost=265, agri=5.0, soil=4.8, water=5.0, transport=5.0, drought=2, flood=4, cold=2, heat=3, typhoon=3, landslide=1),
    "yangtze_hill_corridor": dict(terrain=(22, 35, 27, 0, 5, 0, 0, 9, 2), feature="皖南丘陵与长江河谷", climate="亚热带季风湿润", temp=16.0, rain=1250, frost=255, agri=3.4, soil=3.6, water=4.2, transport=3.8, drought=2, flood=3, cold=2, heat=3, typhoon=1, landslide=3),
    "southeast_hills": dict(terrain=(12, 48, 33, 0, 3, 0, 0, 4, 0), feature="东南丘陵", climate="亚热带季风湿润", temp=17.0, rain=1450, frost=275, agri=3.0, soil=3.1, water=3.8, transport=2.5, drought=2, flood=2, cold=2, heat=3, typhoon=2, landslide=4),
    "southeast_mountains": dict(terrain=(7, 35, 50, 0, 4, 0, 0, 4, 0), feature="东南山地", climate="亚热带山地季风湿润", temp=15.5, rain=1550, frost=250, agri=2.5, soil=2.8, water=4.0, transport=2.0, drought=2, flood=2, cold=2, heat=2, typhoon=2, landslide=5),
    "shandong_hills": dict(terrain=(38, 34, 20, 0, 2, 1, 0, 3, 2), feature="山东丘陵与鲁西平原", climate="暖温带季风半湿润", temp=12.0, rain=650, frost=205, agri=3.6, soil=3.6, water=2.8, transport=3.4, drought=4, flood=2, cold=3, heat=3, typhoon=1, landslide=2),
    "shandong_coastal_hills": dict(terrain=(25, 37, 23, 0, 2, 1, 0, 3, 9), feature="胶东丘陵与海岸", climate="暖温带海洋季风", temp=11.5, rain=700, frost=200, agri=3.0, soil=3.2, water=3.0, transport=3.5, drought=3, flood=2, cold=3, heat=2, typhoon=2, landslide=2),
    "shanxi_basin_plateau": dict(terrain=(15, 25, 24, 20, 11, 5, 0, 0, 0), feature="黄土高原与汾河盆地", climate="温带半干旱季风", temp=9.5, rain=450, frost=175, agri=2.8, soil=3.0, water=2.0, transport=2.6, drought=5, flood=1, cold=4, heat=2, typhoon=1, landslide=3),
    "taihang_mountains": dict(terrain=(7, 28, 48, 10, 5, 2, 0, 0, 0), feature="太行山地", climate="温带山地季风", temp=9.0, rain=550, frost=170, agri=2.1, soil=2.5, water=2.5, transport=1.8, drought=4, flood=1, cold=4, heat=2, typhoon=1, landslide=4),
    "northwest_steppe_plateau": dict(terrain=(7, 15, 15, 25, 5, 27, 6, 0, 0), feature="晋北—燕北高原草原", climate="温带半干旱大陆性", temp=7.0, rain=350, frost=145, agri=1.6, soil=2.0, water=1.5, transport=2.0, drought=5, flood=1, cold=5, heat=1, typhoon=1, landslide=2),
    "yellow_river_mountain_valley": dict(terrain=(18, 27, 32, 8, 12, 3, 0, 0, 0), feature="黄河中游山地河谷", climate="暖温带季风半湿润", temp=12.0, rain=600, frost=205, agri=3.1, soil=3.2, water=3.0, transport=2.7, drought=4, flood=2, cold=3, heat=3, typhoon=1, landslide=3),
    "nanyang_basin": dict(terrain=(44, 16, 24, 0, 14, 0, 0, 2, 0), feature="南阳盆地", climate="北亚热带季风", temp=15.0, rain=850, frost=235, agri=4.2, soil=4.0, water=3.5, transport=3.5, drought=3, flood=2, cold=2, heat=3, typhoon=1, landslide=2),
    "loess_plateau": dict(terrain=(5, 31, 15, 39, 5, 5, 0, 0, 0), feature="黄土高原", climate="温带半干旱", temp=9.0, rain=400, frost=170, agri=2.0, soil=2.7, water=1.5, transport=2.0, drought=5, flood=1, cold=4, heat=2, typhoon=1, landslide=4),
    "loess_mountain_valley": dict(terrain=(7, 26, 29, 22, 10, 5, 1, 0, 0), feature="陇中黄土山地河谷", climate="温带半干旱", temp=8.0, rain=400, frost=160, agri=1.9, soil=2.5, water=1.8, transport=1.8, drought=5, flood=1, cold=4, heat=1, typhoon=1, landslide=4),
    "guanzhong_plain": dict(terrain=(52, 10, 20, 9, 7, 2, 0, 0, 0), feature="关中平原与秦岭北麓", climate="暖温带半湿润", temp=12.5, rain=600, frost=210, agri=4.3, soil=4.2, water=3.5, transport=4.0, drought=4, flood=2, cold=3, heat=3, typhoon=1, landslide=2),
    "hanzhong_basin": dict(terrain=(31, 20, 35, 0, 11, 0, 0, 3, 0), feature="汉中盆地与秦巴山地", climate="北亚热带湿润", temp=14.5, rain=900, frost=230, agri=3.8, soil=3.8, water=4.0, transport=2.6, drought=2, flood=3, cold=2, heat=2, typhoon=1, landslide=4),
    "qinba_mountains": dict(terrain=(5, 24, 59, 0, 9, 0, 0, 3, 0), feature="秦岭—大巴山", climate="山地季风湿润", temp=12.0, rain=900, frost=205, agri=2.2, soil=2.8, water=4.0, transport=1.5, drought=2, flood=2, cold=3, heat=1, typhoon=1, landslide=5),
    "sichuan_basin": dict(terrain=(17, 43, 14, 0, 23, 0, 0, 3, 0), feature="四川盆地", climate="亚热带湿润盆地", temp=17.0, rain=1050, frost=290, agri=4.0, soil=4.0, water=4.0, transport=3.2, drought=2, flood=2, cold=1, heat=3, typhoon=1, landslide=3),
    "chengdu_plain": dict(terrain=(59, 14, 14, 0, 10, 0, 0, 3, 0), feature="成都平原", climate="亚热带湿润盆地", temp=16.5, rain=1000, frost=285, agri=5.0, soil=4.8, water=5.0, transport=4.4, drought=1, flood=2, cold=1, heat=2, typhoon=1, landslide=2),
    "sichuan_foothills": dict(terrain=(14, 37, 35, 0, 11, 0, 0, 3, 0), feature="四川盆地西缘山麓", climate="亚热带山地湿润", temp=15.5, rain=1200, frost=260, agri=3.2, soil=3.5, water=4.3, transport=2.4, drought=1, flood=2, cold=2, heat=2, typhoon=1, landslide=4),
    "yangtze_gorges": dict(terrain=(5, 25, 55, 0, 9, 0, 0, 6, 0), feature="巫山与长江三峡", climate="亚热带山地湿润", temp=16.0, rain=1150, frost=270, agri=2.4, soil=2.8, water=4.5, transport=2.6, drought=2, flood=4, cold=2, heat=3, typhoon=1, landslide=5),
    "qinba_hengduan_mountains": dict(terrain=(4, 21, 60, 6, 7, 1, 0, 1, 0), feature="盆周山地与横断山北缘", climate="山地季风", temp=12.0, rain=1000, frost=210, agri=1.9, soil=2.5, water=4.0, transport=1.2, drought=2, flood=2, cold=3, heat=1, typhoon=1, landslide=5),
    "poyang_lake_plain": dict(terrain=(43, 14, 10, 0, 3, 0, 0, 30, 0), feature="鄱阳湖平原", climate="亚热带季风湿润", temp=17.5, rain=1500, frost=285, agri=4.7, soil=4.4, water=5.0, transport=4.3, drought=2, flood=5, cold=1, heat=4, typhoon=1, landslide=1),
    "gan_river_valley": dict(terrain=(25, 38, 25, 0, 7, 0, 0, 5, 0), feature="赣江河谷与丘陵", climate="亚热带季风湿润", temp=17.5, rain=1500, frost=285, agri=3.7, soil=3.6, water=4.2, transport=3.4, drought=2, flood=3, cold=1, heat=4, typhoon=1, landslide=3),
    "jianghan_plain": dict(terrain=(48, 9, 6, 0, 3, 0, 0, 34, 0), feature="江汉平原与湖群", climate="亚热带季风湿润", temp=17.0, rain=1200, frost=275, agri=4.7, soil=4.4, water=5.0, transport=4.4, drought=2, flood=5, cold=2, heat=4, typhoon=1, landslide=1),
    "dongting_lake_plain": dict(terrain=(43, 10, 8, 0, 3, 0, 0, 36, 0), feature="洞庭湖平原", climate="亚热带季风湿润", temp=17.5, rain=1350, frost=285, agri=4.7, soil=4.4, water=5.0, transport=4.3, drought=2, flood=5, cold=1, heat=4, typhoon=1, landslide=1),
    "han_river_valley": dict(terrain=(32, 25, 25, 0, 12, 0, 0, 6, 0), feature="汉江河谷", climate="北亚热带季风", temp=15.5, rain=900, frost=245, agri=3.8, soil=3.8, water=4.4, transport=3.5, drought=3, flood=3, cold=2, heat=3, typhoon=1, landslide=3),
    "south_central_hills": dict(terrain=(17, 43, 30, 0, 5, 0, 0, 5, 0), feature="江南丘陵", climate="亚热带季风湿润", temp=17.5, rain=1350, frost=285, agri=3.3, soil=3.4, water=4.0, transport=2.7, drought=2, flood=2, cold=1, heat=4, typhoon=1, landslide=4),
    "xiang_river_hills_plain": dict(terrain=(30, 36, 20, 0, 7, 0, 0, 7, 0), feature="湘江河谷与丘陵", climate="亚热带季风湿润", temp=17.5, rain=1350, frost=285, agri=4.0, soil=3.8, water=4.4, transport=3.6, drought=2, flood=3, cold=1, heat=4, typhoon=1, landslide=3),
    "nanling_hills": dict(terrain=(8, 34, 50, 0, 5, 0, 0, 3, 0), feature="南岭山地", climate="亚热带山地湿润", temp=17.0, rain=1500, frost=275, agri=2.6, soil=3.0, water=4.2, transport=1.8, drought=2, flood=2, cold=1, heat=3, typhoon=2, landslide=5),
    "wuling_xuefeng_mountains": dict(terrain=(6, 31, 54, 2, 5, 0, 0, 2, 0), feature="武陵—雪峰山地", climate="亚热带山地湿润", temp=16.0, rain=1400, frost=260, agri=2.5, soil=2.9, water=4.2, transport=1.5, drought=2, flood=2, cold=2, heat=2, typhoon=1, landslide=5),
    "minzhe_coastal_hills": dict(terrain=(19, 35, 25, 0, 3, 0, 0, 7, 11), feature="闽浙沿海丘陵与小平原", climate="亚热带海洋季风", temp=18.5, rain=1550, frost=300, agri=3.4, soil=3.4, water=4.1, transport=3.7, drought=2, flood=3, cold=1, heat=3, typhoon=5, landslide=4),
    "lingnan_coastal_hills": dict(terrain=(20, 35, 23, 0, 4, 0, 0, 7, 11), feature="岭南沿海丘陵", climate="南亚热带季风湿润", temp=21.5, rain=1650, frost=335, agri=3.6, soil=3.3, water=4.0, transport=3.5, drought=2, flood=3, cold=1, heat=4, typhoon=5, landslide=4),
    "pearl_river_delta": dict(terrain=(35, 11, 7, 0, 2, 0, 0, 22, 23), feature="珠江三角洲", climate="南亚热带季风湿润", temp=22.0, rain=1750, frost=345, agri=4.7, soil=4.2, water=5.0, transport=5.0, drought=2, flood=5, cold=1, heat=4, typhoon=5, landslide=1),
    "leizhou_coastal_plain": dict(terrain=(44, 13, 7, 0, 2, 0, 0, 10, 24), feature="雷州半岛沿海平原", climate="热带季风", temp=23.0, rain=1550, frost=355, agri=3.7, soil=3.1, water=3.2, transport=3.2, drought=3, flood=3, cold=1, heat=5, typhoon=5, landslide=1),
    "hainan_island": dict(terrain=(21, 29, 28, 0, 4, 0, 0, 5, 13), feature="海南岛山地与沿海平原", climate="热带季风", temp=23.5, rain=1750, frost=365, agri=3.8, soil=3.3, water=4.0, transport=2.8, drought=2, flood=3, cold=1, heat=5, typhoon=5, landslide=4),
    "south_china_karst": dict(terrain=(10, 39, 26, 15, 9, 0, 0, 1, 0), feature="华南喀斯特丘陵高原", climate="亚热带季风湿润", temp=18.0, rain=1350, frost=290, agri=2.6, soil=2.7, water=2.8, transport=1.8, drought=3, flood=2, cold=1, heat=3, typhoon=2, landslide=5),
    "guangxi_river_basin": dict(terrain=(25, 34, 18, 7, 11, 0, 0, 5, 0), feature="桂中南河谷盆地", climate="南亚热带季风湿润", temp=21.0, rain=1400, frost=325, agri=3.8, soil=3.5, water=4.3, transport=3.2, drought=2, flood=3, cold=1, heat=4, typhoon=3, landslide=3),
    "yungui_plateau": dict(terrain=(7, 25, 25, 33, 9, 1, 0, 0, 0), feature="云贵高原", climate="亚热带高原季风", temp=14.5, rain=1000, frost=245, agri=2.5, soil=2.8, water=3.0, transport=1.5, drought=3, flood=1, cold=2, heat=2, typhoon=1, landslide=4),
    "yunnan_basin_plateau": dict(terrain=(9, 21, 25, 25, 16, 2, 0, 2, 0), feature="滇中高原坝区", climate="亚热带高原季风", temp=15.5, rain=1000, frost=270, agri=3.1, soil=3.2, water=3.3, transport=1.8, drought=3, flood=1, cold=2, heat=2, typhoon=1, landslide=4),
    "yunnan_lake_basin": dict(terrain=(16, 18, 20, 20, 17, 0, 0, 9, 0), feature="滇中湖盆坝区", climate="亚热带高原季风", temp=15.5, rain=1000, frost=270, agri=3.7, soil=3.5, water=4.4, transport=2.6, drought=3, flood=2, cold=2, heat=2, typhoon=1, landslide=3),
    "hengduan_plateau_basin": dict(terrain=(5, 18, 48, 13, 12, 3, 0, 1, 0), feature="横断山地与高原盆地", climate="高原山地季风", temp=13.5, rain=900, frost=230, agri=2.2, soil=2.7, water=3.8, transport=1.0, drought=3, flood=1, cold=3, heat=1, typhoon=1, landslide=5),
    "wuling_miaoling_mountains": dict(terrain=(5, 28, 52, 8, 6, 0, 0, 1, 0), feature="武陵—苗岭山地", climate="亚热带山地湿润", temp=15.5, rain=1250, frost=255, agri=2.2, soil=2.7, water=3.8, transport=1.2, drought=2, flood=2, cold=2, heat=2, typhoon=1, landslide=5),
}

TERRAIN_COLUMNS = (
    "plain_pct",
    "hill_pct",
    "mountain_pct",
    "plateau_pct",
    "basin_valley_pct",
    "grassland_pct",
    "desert_pct",
    "wetland_lake_pct",
    "coast_island_pct",
)

RIVER_DEFINITIONS = [
    dict(name="长江", aliases=("大江", "长江"), basin="长江流域", units="叙州府 泸州 重庆府 夔州府 荆州府 岳州府 武昌府 汉阳府 黄州府 九江府 南康府 安庆府 池州府 太平府 应天府 镇江府 常州府 扬州府", exclude_pairs=(("广西", "太平府"),), benefit="灌溉、渔业、跨区航运、冲积土", risk="洪水、堤防溃决、峡江航运风险"),
    dict(name="黄河", aliases=("黄河",), basin="黄河流域", units="临洮府 巩昌府 西安府 延安府 平阳府 怀庆府 河南府 开封府 归德府 徐州 淮安府", benefit="灌溉、冲积平原、河运与渡口", risk="1628年下游偏南行经徐淮；决口、改道、泥沙淤积"),
    dict(name="淮河", aliases=("淮河",), basin="淮河流域", units="汝宁府 归德府 凤阳府 庐州府 淮安府", benefit="稻麦农业、湖泊湿地、区域运输", risk="黄淮顶托、洪涝、湖沼扩张"),
    dict(name="汉江", aliases=("汉水",), basin="长江流域", units="汉中府 兴安州 郧阳府 襄阳府 汉阳府", benefit="汉中—湖广航运、灌溉、渔业", risk="山洪、滩险、汛期洪水"),
    dict(name="京杭大运河", aliases=("运河",), basin="人工运河—海河/黄河/淮河/长江/钱塘江", units="顺天府 河间府 东昌府 济南府 兖州府 徐州 淮安府 扬州府 镇江府 苏州府 嘉兴府 湖州府 杭州府", benefit="漕运、粮食调拨、城市与手工业", risk="淤塞、决堤、维护成本、军事节点"),
    dict(name="渭河", aliases=("渭水",), basin="黄河流域", units="巩昌府 凤翔府 西安府", benefit="关中灌溉、农田、东西交通", risk="洪涝、泥沙与旱季水量不足"),
    dict(name="汾河", aliases=("汾水",), basin="黄河流域", units="太原府 汾州府 平阳府", benefit="盆地灌溉、农田与聚落走廊", risk="干旱年份水量不足、山洪"),
    dict(name="赣江", aliases=("赣江",), basin="长江—鄱阳湖流域", units="赣州府 吉安府 临江府 南昌府", benefit="江西南北运输、稻作、木材下运", risk="洪水、滩险"),
    dict(name="湘江", aliases=("湘江",), basin="长江—洞庭湖流域", units="永州府 衡州府 长沙府 岳州府", benefit="稻作、矿木运输、南北走廊", risk="季节洪水与滩险"),
    dict(name="沅江", aliases=("沅江",), basin="长江—洞庭湖流域", units="黎平府 镇远府 辰州府 常德府", benefit="黔湘木材、药材与山货运输", risk="峡谷急流、山洪"),
    dict(name="资江", aliases=("资江",), basin="长江—洞庭湖流域", units="宝庆府 长沙府 常德府", benefit="灌溉、木材与区域运输", risk="山洪、滩险"),
    dict(name="澧水", aliases=("澧水", "澧河"), basin="长江—洞庭湖流域", units="辰州府 常德府", benefit="山货木材下运、灌溉", risk="山洪"),
    dict(name="岷江", aliases=("岷江",), basin="长江流域", units="成都府 嘉定州 叙州府", benefit="都江堰灌溉、成都平原粮食、航运", risk="山洪、河道摆动"),
    dict(name="嘉陵江", aliases=(), basin="长江流域", units="保宁府 顺庆府 重庆府", benefit="川北—重庆运输、渔业、灌溉", risk="峡谷洪水、航运滩险"),
    dict(name="涪江", aliases=("涪江",), basin="长江—嘉陵江流域", units="龙安府 潼川州 重庆府", benefit="盆地运输与灌溉", risk="山洪"),
    dict(name="乌江", aliases=("乌江",), basin="长江流域", units="遵义军民府 贵阳军民府 思南府 重庆府", benefit="黔北水运、盐与山货交换", risk="峡谷急流、通航困难"),
    dict(name="钱塘江", aliases=("衢江",), basin="钱塘江流域", units="衢州府 金华府 严州府 杭州府", benefit="浙西木材、粮食与城市运输", risk="山洪、钱塘潮"),
    dict(name="瓯江", aliases=("瓯江",), basin="浙南沿海诸河", units="处州府 温州府", benefit="山货、木材与沿海贸易", risk="山洪、台风暴雨"),
    dict(name="闽江", aliases=("闽江", "建溪"), basin="闽江流域", units="建宁府 邵武府 延平府 福州府", benefit="木材、茶叶、纸业与福州港运输", risk="山洪、台风暴雨"),
    dict(name="九龙江", aliases=(), basin="闽南沿海诸河", units="漳州府", benefit="农业灌溉、漳州港与海贸", risk="台风、洪水、潮汐"),
    dict(name="西江", aliases=("西江",), basin="珠江流域", units="浔州府 梧州府 肇庆府 广州府", benefit="两广干线航运、稻作、渔业", risk="洪水、峡谷与滩险"),
    dict(name="北江", aliases=("北江",), basin="珠江流域", units="韶州府 广州府", benefit="南岭—广州运输与灌溉", risk="山洪"),
    dict(name="东江", aliases=("东江",), basin="珠江流域", units="惠州府 广州府", benefit="灌溉、渔业、珠江三角洲供水", risk="洪水与台风暴雨"),
    dict(name="韩江", aliases=("韩江",), basin="粤东沿海诸河", units="潮州府", benefit="潮汕平原农业、内河航运", risk="洪水、台风与潮灾"),
    dict(name="漓江—桂江", aliases=("漓江", "桂江"), basin="珠江—西江流域", units="桂林府 平乐府 梧州府", benefit="桂东北交通、灌溉与山货运输", risk="喀斯特洪水、滩险"),
    dict(name="郁江", aliases=("郁江",), basin="珠江—西江流域", units="南宁府 浔州府", benefit="桂中南农业与航运", risk="洪水"),
    dict(name="滦河", aliases=("滦河",), basin="渤海沿海水系", units="永平府", benefit="灌溉、木材与边地运输", risk="山洪与河道摆动"),
    dict(name="怒江", aliases=("怒江",), basin="怒江—萨尔温江流域", units="永昌军民府", benefit="河谷农业与边贸通道", risk="深切峡谷、山洪、通航极弱"),
    dict(name="澜沧江", aliases=(), basin="澜沧江—湄公河流域", units="大理府 永昌军民府", benefit="河谷农业与西南边贸", risk="峡谷阻隔、山洪"),
]

LAKE_DEFINITIONS = [
    dict(name="太湖", aliases=("太湖",), units="苏州府 常州府 松江府 湖州府 嘉兴府", benefit="稻作灌溉、渔业、湖运、芦苇", risk="洪涝、水位波动、圩田维护"),
    dict(name="鄱阳湖", aliases=("鄱阳湖",), units="南昌府 九江府 南康府 饶州府", benefit="渔业、湖运、蓄洪、冲积农田", risk="季节涨落、洪涝与疫病环境"),
    dict(name="洞庭湖", aliases=("洞庭湖",), units="岳州府 常德府 长沙府 荆州府", benefit="渔业、蓄洪、湖运、稻作", risk="洪涝、洲滩迁移与堤防维护"),
    dict(name="洪泽湖", aliases=("洪泽湖",), units="淮安府 凤阳府", benefit="蓄洪、渔业、运河与淮河调节", risk="黄淮顶托、堤坝溃决、大片淹没"),
    dict(name="巢湖", aliases=("巢湖",), units="庐州府 和州", benefit="渔业、灌溉、区域湖运", risk="洪涝与水位波动"),
    dict(name="滇池", aliases=("滇池",), units="云南府 澂江府", benefit="坝区农业、渔业、灌溉", risk="洪涝、湖滨疫病与水位变化"),
    dict(name="洱海", aliases=("洱海",), units="大理府", benefit="坝区农业、渔业、区域运输", risk="湖滨洪涝与山洪"),
    dict(name="抚仙湖", aliases=("抚仙湖",), units="澂江府", benefit="渔业、灌溉与湖滨农业", risk="岸陡、可耕湖滨有限"),
    dict(name="南四湖", aliases=("南旺湖", "蜀山湖"), units="兖州府 东昌府 徐州", benefit="运河水源、渔业、芦苇、蓄洪", risk="湖面变动、运河淤塞与洪涝"),
    dict(name="江汉湖群", aliases=("洪湖", "沔阳湖", "长湖"), units="荆州府 汉阳府 承天府", benefit="渔业、稻作、蓄洪与水运", risk="洪涝、沼泽疾病、圩垸维护"),
    dict(name="白洋淀", aliases=(), units="保定府 顺天府 河间府", benefit="渔业、芦苇、蓄洪与区域水运", risk="旱年萎缩、涝年泛滥"),
    dict(name="运城盐池", aliases=("盐池", "女盐池"), units="平阳府", benefit="池盐与财政专卖", risk="淡水不足、盐业制度冲突"),
    dict(name="高邮湖", aliases=(), units="扬州府", benefit="渔业、运河调蓄、湖运", risk="洪涝与堤防风险"),
    dict(name="骆马湖", aliases=("骆马湖",), units="徐州 淮安府", benefit="渔业、蓄洪与运河水源", risk="黄淮洪水、湖面变动"),
]

LAND_FEATURE_DEFINITIONS = [
    dict(type="平原", name="华北平原", units="顺天府 保定府 河间府 真定府 顺德府 广平府 大名府 济南府 兖州府 东昌府 开封府 归德府 卫辉府 彰德府 徐州 凤阳府", benefit="麦、粟、豆、棉；道路与大规模军队机动", risk="旱灾、河患、土地盐碱化"),
    dict(type="平原", name="江淮平原", units="凤阳府 淮安府 扬州府 庐州府 滁州 和州", benefit="稻麦复合、湖泊渔业、运河交通", risk="黄淮洪涝、低洼积水"),
    dict(type="平原/三角洲", name="长江三角洲—太湖平原", units="苏州府 松江府 常州府 镇江府 应天府 嘉兴府 湖州府 杭州府 绍兴府", benefit="高产稻作、桑蚕、棉纺、密集水运与城市", risk="洪涝、海潮、圩田维护成本"),
    dict(type="平原/湖区", name="江汉平原", units="武昌府 汉阳府 承天府 德安府 荆州府", benefit="稻作、渔业、芦苇、长江汉江航运", risk="洪涝、沼泽与疫病"),
    dict(type="平原/湖区", name="洞庭湖平原", units="岳州府 常德府 长沙府 荆州府", benefit="稻作、渔业、木材集散、水运", risk="洪涝与湖岸迁移"),
    dict(type="平原/湖区", name="鄱阳湖平原", units="南昌府 九江府 南康府 饶州府", benefit="稻作、渔业、湖运、木材集散", risk="洪水与季节水位变化"),
    dict(type="平原", name="成都平原", units="成都府", benefit="都江堰灌溉、高产稻麦、手工业与密集人口", risk="水利维护、上游山洪"),
    dict(type="平原", name="关中平原", units="西安府 凤翔府", benefit="麦粟农业、灌溉、东西交通与政治中心", risk="旱灾、水土流失与泾渭洪水"),
    dict(type="盆地", name="汉中盆地", units="汉中府", benefit="稻麦农业、汉江通道、秦巴山货", risk="山地阻隔、洪水与滑坡"),
    dict(type="盆地", name="南阳盆地", units="南阳府", benefit="麦稻农业、南北交通、林产品", risk="旱涝交替"),
    dict(type="平原/三角洲", name="珠江三角洲", units="广州府 肇庆府", benefit="稻作、桑蚕、鱼塘、海贸与密集水运", risk="洪水、台风、海潮与疫病"),
    dict(type="高原", name="黄土高原", units="延安府 庆阳府 平凉府 巩昌府 临洮府 太原府 汾州府", benefit="旱作麦粟、牧草、煤铁盐等潜力", risk="干旱、水土流失、沟壑交通困难"),
    dict(type="高原", name="云贵高原", units="云南府 曲靖府 临安府 澂江府 楚雄府 姚安军民府 武定府 大理府 永昌军民府 贵阳军民府 都匀府 平越军民府", benefit="坝区农业、马帮贸易、金属与药材潜力", risk="喀斯特缺水、山地阻隔、滑坡"),
    dict(type="草原", name="晋北—燕北草原", units="大同府 延庆州", benefit="马、牛、羊、皮毛与边贸", risk="寒潮、干旱、草场退化与边防压力"),
    dict(type="草原/荒漠", name="陕甘宁半干旱草原", units="延安府 庆阳府 平凉府 临洮府", benefit="马羊、皮毛、盐与边地贸易", risk="干旱、沙尘、草场波动"),
    dict(type="沙地/荒漠", name="毛乌素—宁夏荒漠边缘", units="延安府 庆阳府 平凉府", benefit="有限牧业、盐碱资源与边防通道", risk="风沙、缺水、耕地承载力低"),
    dict(type="山脉", name="太行山", units="保定府 真定府 顺德府 彰德府 怀庆府 太原府 辽州 潞安府 泽州", benefit="煤铁、石材、木材与山口防御", risk="交通瓶颈、滑坡、山洪"),
    dict(type="山脉", name="燕山", units="顺天府 永平府 延庆州", benefit="林木、矿石、关隘与防御", risk="山地交通、寒潮、山洪"),
    dict(type="山脉", name="吕梁山", units="太原府 汾州府 平阳府", benefit="煤铁、林牧与黄河屏障", risk="水土流失、交通困难"),
    dict(type="山脉", name="秦岭", units="西安府 凤翔府 汉中府 兴安州 河南府 南阳府", benefit="木材、药材、矿产潜力、水源与关隘", risk="南北交通瓶颈、滑坡与山洪"),
    dict(type="山脉", name="大巴山", units="汉中府 兴安州 保宁府 夔州府", benefit="木材、药材、矿产潜力与水源", risk="交通困难、山洪、滑坡"),
    dict(type="山脉", name="大别山", units="安庆府 庐州府 汝宁府 黄州府", benefit="木材、茶、药材与战略屏障", risk="山地交通与洪水汇流"),
    dict(type="山脉", name="巫山", units="夔州府 荆州府", benefit="峡江关隘、木材、药材", risk="峡江航运、滑坡、山洪"),
    dict(type="山脉", name="武陵山", units="重庆府 遵义军民府 辰州府 常德府 思南府 铜仁府", benefit="木材、药材、矿产潜力与山货", risk="交通阻隔、滑坡与地方控制困难"),
    dict(type="山脉", name="雪峰山", units="宝庆府 辰州府 靖州", benefit="木材、药材、水源", risk="交通阻隔、滑坡"),
    dict(type="山脉", name="南岭", units="衡州府 永州府 郴州 赣州府 南安府 韶州府 南雄府 桂林府", benefit="木材、金属矿潜力、南北关隘", risk="交通瓶颈、山洪与瘴疫环境"),
    dict(type="山脉", name="武夷山", units="广信府 建昌府 建宁府 邵武府 汀州府", benefit="茶、木材、纸材、药材与矿产潜力", risk="交通阻隔、台风暴雨后的山洪"),
    dict(type="山脉", name="罗霄山", units="吉安府 袁州府 赣州府 长沙府 郴州", benefit="木材、竹、药材、矿产潜力", risk="交通阻隔与山洪"),
    dict(type="山脉", name="天目—黄山山地", units="杭州府 湖州府 宁国府 徽州府 广德州", benefit="茶、木材、竹、药材与水源", risk="山洪、滑坡、交通困难"),
    dict(type="山脉", name="横断山", units="雅州 大理府 永昌军民府", benefit="木材、药材、金属矿潜力与马帮通道", risk="高差巨大、滑坡、地震与交通极难"),
    dict(type="山脉", name="乌蒙—苗岭山地", units="云南府 曲靖府 武定府 贵阳军民府 都匀府 黎平府 镇远府", benefit="森林、药材、金属矿潜力与高地牧草", risk="喀斯特缺水、滑坡、交通阻隔"),
]

WEATHER_RULES = [
    dict(event="干旱", trigger="半干旱、黄土高原、北方平原；连续少雨", effects="粮食产量-25%~-65%；河运水量下降；牲畜饮水压力", followups="饥荒、逃户、地价下跌、治安恶化", duration="一季至多年"),
    dict(event="洪涝", trigger="大河湖区、低洼平原；梅雨或上游暴雨", effects="当季粮食-20%~-70%；道路中断；渔业短期上升后波动", followups="堤防工程、疫病、流民、河道改迁", duration="数周至一季"),
    dict(event="寒潮/早霜", trigger="北方、高原、山地；晚明冷背景下概率上调", effects="冬麦、果木和牲畜受损；燃料需求增加", followups="粮价上涨、木柴消耗、军队冻伤", duration="数日至一月"),
    dict(event="酷热", trigger="南方盆地、长江中下游与华南", effects="劳作效率下降；水稻和牲畜受热；用水增加", followups="旱情、疫病与火灾概率上升", duration="数日至数周"),
    dict(event="台风/风暴潮", trigger="闽浙粤琼沿海；夏秋", effects="稻田、盐场、渔船、港口和房屋受损", followups="海堤修复、盐价与海贸波动", duration="数日，恢复数月"),
    dict(event="连阴雨/渍涝", trigger="江南、四川盆地、湖区", effects="谷物霉变、晒盐受阻、道路泥泞", followups="仓储损失、疫病与运输延误", duration="数周"),
    dict(event="暴雪", trigger="北方、高原和山口", effects="道路封闭、牧草掩埋、军需运输下降", followups="牲畜死亡、边防补给危机", duration="数日至一月"),
    dict(event="冰雹", trigger="高原、山地与强对流季节", effects="局地作物、果园与屋瓦严重受损", followups="单县歉收与赈济需求", duration="数小时，损失一季"),
    dict(event="沙尘/风蚀", trigger="西北半干旱草原、沙地与裸露黄土", effects="苗期受损、能见度和行军效率下降", followups="耕地退化、居民迁移", duration="数日至季节性"),
    dict(event="山洪/滑坡", trigger="山地、峡谷；短时强降雨", effects="道路、桥梁、矿山、村落受损", followups="交通断绝、工程与救援需求", duration="突发，恢复数月"),
    dict(event="蝗灾", trigger="旱涝交替、河滩湖滩与温暖季节", effects="粮食和牧草-30%~-90%", followups="饥荒、迁徙、军粮危机", duration="一季，可跨区传播"),
    dict(event="疫病环境", trigger="洪后、湿热低地、人口密集与营养不良", effects="劳动力和军队有效人数下降", followups="税源减少、恐慌、迁徙", duration="数月；不是单纯天气事件"),
]


def clamp(value: float, low: float, high: float) -> float:
    return max(low, min(high, value))


def stable_factor(key: str, low: float = 0.9, high: float = 1.1) -> float:
    digest = hashlib.sha256(key.encode("utf-8")).digest()
    unit = int.from_bytes(digest[:8], "big") / (2**64 - 1)
    return low + (high - low) * unit


def read_dbf(path: Path) -> list[dict[str, str]]:
    data = path.read_bytes()
    record_count, header_length, record_length = struct.unpack_from(
        "<xxxxIHH", data, 0
    )
    fields: list[tuple[str, int]] = []
    offset = 32
    while data[offset] != 0x0D:
        descriptor = data[offset : offset + 32]
        name = descriptor[:11].split(b"\0", 1)[0].decode("ascii")
        fields.append((name, descriptor[16]))
        offset += 32

    rows: list[dict[str, str]] = []
    for index in range(record_count):
        record = data[
            header_length + index * record_length :
            header_length + (index + 1) * record_length
        ]
        if not record or record[0:1] == b"*":
            continue
        cursor = 1
        row: dict[str, str] = {}
        for name, length in fields:
            raw = record[cursor : cursor + length]
            cursor += length
            row[name] = raw.decode("utf-8", "replace").strip()
        rows.append(row)
    return rows


def read_poly_shapes(path: Path) -> list[list[list[tuple[float, float]]]]:
    """Read PolyLine/Polygon parts from a basic 2D ESRI shapefile."""

    data = path.read_bytes()
    output: list[list[list[tuple[float, float]]]] = []
    offset = 100
    while offset + 8 <= len(data):
        _, content_words = struct.unpack_from(">ii", data, offset)
        offset += 8
        content = data[offset : offset + content_words * 2]
        offset += content_words * 2
        if len(content) < 4:
            output.append([])
            continue
        shape_type = struct.unpack_from("<i", content, 0)[0]
        if shape_type == 0:
            output.append([])
            continue
        if shape_type not in {3, 5, 13, 15}:
            output.append([])
            continue
        part_count, point_count = struct.unpack_from("<ii", content, 36)
        part_indices = list(
            struct.unpack_from(f"<{part_count}i", content, 44)
        )
        points_offset = 44 + 4 * part_count
        points = [
            struct.unpack_from("<dd", content, points_offset + 16 * index)
            for index in range(point_count)
        ]
        part_indices.append(point_count)
        output.append(
            [
                points[part_indices[index] : part_indices[index + 1]]
                for index in range(part_count)
            ]
        )
    return output


def haversine_km(a: tuple[float, float], b: tuple[float, float]) -> float:
    lon1, lat1 = map(math.radians, a)
    lon2, lat2 = map(math.radians, b)
    dlon = lon2 - lon1
    dlat = lat2 - lat1
    value = (
        math.sin(dlat / 2) ** 2
        + math.cos(lat1) * math.cos(lat2) * math.sin(dlon / 2) ** 2
    )
    return 6371.0088 * 2 * math.asin(math.sqrt(value))


def point_segment_distance_km(
    point: tuple[float, float], start: tuple[float, float], end: tuple[float, float]
) -> float:
    lon, lat = point
    cosine = math.cos(math.radians(lat))
    px, py = lon * cosine * EARTH_KM_PER_DEGREE, lat * EARTH_KM_PER_DEGREE
    ax, ay = start[0] * cosine * EARTH_KM_PER_DEGREE, start[1] * EARTH_KM_PER_DEGREE
    bx, by = end[0] * cosine * EARTH_KM_PER_DEGREE, end[1] * EARTH_KM_PER_DEGREE
    dx, dy = bx - ax, by - ay
    if dx == 0 and dy == 0:
        return math.hypot(px - ax, py - ay)
    ratio = clamp(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy), 0, 1)
    return math.hypot(px - (ax + ratio * dx), py - (ay + ratio * dy))


def distance_to_parts_km(
    point: tuple[float, float], parts: Iterable[list[tuple[float, float]]]
) -> float:
    best = float("inf")
    for part in parts:
        for start, end in zip(part, part[1:]):
            if (
                point[0] < min(start[0], end[0]) - 1.0
                or point[0] > max(start[0], end[0]) + 1.0
                or point[1] < min(start[1], end[1]) - 1.0
                or point[1] > max(start[1], end[1]) + 1.0
            ):
                continue
            best = min(best, point_segment_distance_km(point, start, end))
    return best


def normalize_intermediate(name: str) -> str:
    if "州" in name:
        return name.split("州", 1)[0] + "州"
    return name


def coordinate_score(
    candidate: dict[str, str], county: dict[str, str], source: str
) -> float:
    region = county["region"]
    # Prefer a point explicitly valid in 1628.  Province/parent metadata in
    # the complete 1820 slice is still useful for disambiguation, but must not
    # outweigh an otherwise plausible same-period record.
    score = {"active_1628": 250, "slice_1820": 78, "other_time": 55}[source]
    if candidate.get("LEV1_CH") in REGION_QING_PROVINCES[region]:
        score += 45
    present = candidate.get("PRES_LOC", "")
    if any(prefix in present for prefix in REGION_MODERN_PREFIXES[region]):
        score += 40
    candidate_parent = candidate.get("LEV2_CH", "")
    expected_parent = PREFECTURE_ALIASES.get(county["upper_unit"], county["upper_unit"])
    if candidate_parent in {county["upper_unit"], expected_parent}:
        score += 35
    lon = float(candidate.get("X_COOR") or candidate.get("X_COORD") or 0)
    lat = float(candidate.get("Y_COOR") or candidate.get("Y_COORD") or 0)
    min_lon, min_lat, max_lon, max_lat = REGION_BBOX[region]
    if min_lon <= lon <= max_lon and min_lat <= lat <= max_lat:
        score += 20
    else:
        score -= 40
    centre = REGION_CENTRE[region]
    score -= haversine_km((lon, lat), centre) / 500
    return score


def build_coordinate_index(
    time_rows: list[dict[str, str]], slice_rows: list[dict[str, str]]
) -> tuple[dict[str, list[dict[str, str]]], dict[str, list[dict[str, str]]], dict[str, list[dict[str, str]]]]:
    active: dict[str, list[dict[str, str]]] = defaultdict(list)
    all_time: dict[str, list[dict[str, str]]] = defaultdict(list)
    slice_1820: dict[str, list[dict[str, str]]] = defaultdict(list)
    for row in time_rows:
        if row.get("TYPE_CH") != "县":
            continue
        all_time[row["NAME_CH"]].append(row)
        begin = int(row.get("BEG_YR") or -9999)
        end = int(row.get("END_YR") or 9999)
        if begin <= SNAPSHOT_YEAR <= end:
            active[row["NAME_CH"]].append(row)
    for row in slice_rows:
        if row.get("TYPE_CH") == "县":
            slice_1820[row["NAME_CH"]].append(row)
    return active, slice_1820, all_time


def geocode_counties(
    counties: list[dict[str, str]],
    active: dict[str, list[dict[str, str]]],
    slice_1820: dict[str, list[dict[str, str]]],
    all_time: dict[str, list[dict[str, str]]],
) -> None:
    for county in counties:
        name = county["county"]
        hierarchy_key = (county["region"], county["upper_unit"], name)
        if hierarchy_key in HIERARCHY_COORDINATES:
            lon, lat, note = HIERARCHY_COORDINATES[hierarchy_key]
            county["longitude"] = lon
            county["latitude"] = lat
            county["coordinate_method"] = "hierarchy_disambiguation_override"
            county["coordinate_quality"] = "C"
            county["coordinate_note"] = note
            county["coordinate_source"] = "Project Realm manual disambiguation"
            continue
        candidates: list[tuple[float, str, dict[str, str]]] = []
        seen: set[tuple[str, str]] = set()
        for source, index in (
            ("active_1628", active),
            ("slice_1820", slice_1820),
            ("other_time", all_time),
        ):
            for candidate in index.get(name, []):
                key = (source, candidate.get("SYS_ID", ""))
                if key in seen:
                    continue
                seen.add(key)
                candidates.append((coordinate_score(candidate, county, source), source, candidate))
        if candidates:
            _, source, candidate = max(candidates, key=lambda item: item[0])
            county["longitude"] = round(float(candidate.get("X_COOR") or candidate.get("X_COORD")), 5)
            county["latitude"] = round(float(candidate.get("Y_COOR") or candidate.get("Y_COORD")), 5)
            county["coordinate_method"] = source
            county["coordinate_quality"] = (
                "A" if source == "active_1628" else "B" if source == "slice_1820" else "C"
            )
            county["coordinate_note"] = candidate.get("PRES_LOC", "")
            county["coordinate_source"] = "CHGIS V6"
            continue
        if name in MANUAL_COORDINATES:
            lon, lat, note = MANUAL_COORDINATES[name]
            county["longitude"] = lon
            county["latitude"] = lat
            county["coordinate_method"] = "manual_historical_place_approximation"
            county["coordinate_quality"] = "C"
            county["coordinate_note"] = note
            county["coordinate_source"] = "Project Realm manual approximation"
            continue
        centre = REGION_CENTRE[county["region"]]
        jitter = stable_factor(name, -0.4, 0.4)
        county["longitude"] = round(centre[0] + jitter, 5)
        county["latitude"] = round(centre[1] - jitter / 2, 5)
        county["coordinate_method"] = "region_centroid_fallback"
        county["coordinate_quality"] = "D"
        county["coordinate_note"] = "仅用于占位，需后续核对"
        county["coordinate_source"] = "Project Realm fallback"


def zone_for(county: dict[str, str]) -> str:
    return ZONE_BY_UPPER.get(
        county["upper_unit"], REGION_DEFAULT_ZONE[county["region"]]
    )


def direction_fields(lon: float, lat: float) -> tuple[str, str, str]:
    boundary = 33.0 if lon >= 112 else 33.5
    difference = lat - boundary
    north_south = "北方" if difference > 0.6 else "南方" if difference < -0.6 else "秦岭—淮河过渡带"
    east_west = "东部" if lon >= 115 else "中部" if lon >= 108.5 else "西部"
    if north_south.startswith("秦岭"):
        quadrant = f"{east_west}过渡区"
    else:
        prefix = {"东部": "东", "中部": "中", "西部": "西"}[east_west]
        quadrant = f"{prefix}{'北' if north_south == '北方' else '南'}部"
    return north_south, east_west, quadrant


def default_basin(county: dict[str, str]) -> str:
    region, upper = county["region"], county["upper_unit"]
    if region == "北直隶（京师）":
        return "滦河流域" if upper == "永平府" else "海河流域"
    if region == "南直隶（南京）":
        if upper in {"凤阳府", "淮安府", "徐州", "庐州府"}:
            return "淮河流域"
        if upper in {"徽州府", "宁国府", "广德州"}:
            return "长江—钱塘江分水区"
        return "长江下游流域"
    if region == "山东":
        return "淮河—山东沿海诸河"
    if region == "山西":
        return "黄河流域"
    if region == "河南":
        if upper in {"汝宁府", "归德府"}:
            return "淮河流域"
        if upper == "南阳府":
            return "汉江流域"
        return "黄河流域"
    if region == "陕西":
        return "汉江流域" if upper in {"汉中府", "兴安州"} else "黄河流域"
    if region == "四川":
        return "长江上游流域"
    if region == "江西":
        return "长江—鄱阳湖流域"
    if region == "湖广":
        return "长江中游流域"
    if region == "浙江":
        return "钱塘江—浙东南沿海诸河"
    if region == "福建":
        return "闽江—福建沿海诸河"
    if region == "广东":
        return "海南岛诸河" if upper == "琼州府" else "珠江—广东沿海诸河"
    if region == "广西":
        return "珠江—西江流域"
    if region == "云南":
        if upper == "临安府":
            return "红河流域"
        if upper in {"大理府", "永昌军民府"}:
            return "澜沧江—怒江流域"
        return "长江—珠江上游分水区"
    if upper in {"都匀府", "贵阳军民府", "平越军民府"}:
        return "长江—珠江分水区"
    return "长江上游流域"


def prepare_water_geometries(
    dbf_path: Path,
    shp_path: Path,
    definitions: list[dict[str, Any]],
) -> dict[str, list[list[tuple[float, float]]]]:
    records = read_dbf(dbf_path)
    shapes = read_poly_shapes(shp_path)
    aliases = {
        alias: definition["name"]
        for definition in definitions
        for alias in definition.get("aliases", ())
    }
    output: dict[str, list[list[tuple[float, float]]]] = defaultdict(list)
    for record, parts in zip(records, shapes):
        canonical = aliases.get(record.get("NAME_CH", ""))
        if canonical and parts:
            output[canonical].extend(parts)
    return output


def definition_matches_county(
    definition: dict[str, Any], county: dict[str, Any]
) -> bool:
    units = set(definition["units"].split())
    excluded = set(definition.get("exclude_pairs", ()))
    return (
        county["upper_unit"] in units
        and (county["region"], county["upper_unit"]) not in excluded
    )


def assign_terrain_climate_and_water(
    counties: list[dict[str, Any]],
    river_geometries: dict[str, list[list[tuple[float, float]]]],
    lake_geometries: dict[str, list[list[tuple[float, float]]]],
) -> None:
    river_by_name = {definition["name"]: definition for definition in RIVER_DEFINITIONS}
    lake_by_name = {definition["name"]: definition for definition in LAKE_DEFINITIONS}
    for county in counties:
        zone = zone_for(county)
        template = ZONE_TEMPLATES[zone]
        county["geographic_zone"] = zone
        terrain = template["terrain"]
        for column, value in zip(TERRAIN_COLUMNS, terrain):
            county[column] = value
        ranked = sorted(zip(terrain, TERRAIN_COLUMNS), reverse=True)
        labels = {
            "plain_pct": "平原",
            "hill_pct": "丘陵",
            "mountain_pct": "山地",
            "plateau_pct": "高原",
            "basin_valley_pct": "盆地/河谷",
            "grassland_pct": "草原",
            "desert_pct": "沙地/荒漠",
            "wetland_lake_pct": "湖泊/湿地",
            "coast_island_pct": "海岸/岛屿",
        }
        county["primary_landform"] = labels[ranked[0][1]]
        county["secondary_landform"] = labels[ranked[1][1]]
        county["named_landform"] = template["feature"]
        north_south, east_west, quadrant = direction_fields(
            county["longitude"], county["latitude"]
        )
        county["north_south_zone"] = north_south
        county["east_west_zone"] = east_west
        county["macro_quadrant"] = quadrant
        county["river_basin"] = default_basin(county)

        point = (county["longitude"], county["latitude"])
        river_distances = {
            name: distance_to_parts_km(point, parts)
            for name, parts in river_geometries.items()
        }
        lake_distances = {
            name: distance_to_parts_km(point, parts)
            for name, parts in lake_geometries.items()
        }
        relevant_rivers = {
            name for name, definition in river_by_name.items()
            if definition_matches_county(definition, county)
        }
        relevant_rivers.update(
            name for name, distance in river_distances.items() if distance <= 35
        )
        relevant_lakes = {
            name for name, definition in lake_by_name.items()
            if definition_matches_county(definition, county)
        }
        relevant_lakes.update(
            name for name, distance in lake_distances.items() if distance <= 30
        )
        county["major_river_systems"] = ";".join(sorted(relevant_rivers))
        county["major_lakes_wetlands"] = ";".join(sorted(relevant_lakes))
        if river_distances:
            nearest_river, distance = min(river_distances.items(), key=lambda item: item[1])
            if distance <= 100:
                county["nearest_mapped_major_river"] = nearest_river
                county["major_river_distance_km_est"] = round(distance, 1)
            else:
                county["nearest_mapped_major_river"] = ""
                county["major_river_distance_km_est"] = ""
        if lake_distances:
            nearest_lake, distance = min(lake_distances.items(), key=lambda item: item[1])
            if distance <= 100:
                county["nearest_mapped_major_lake"] = nearest_lake
                county["major_lake_distance_km_est"] = round(distance, 1)
            else:
                county["nearest_mapped_major_lake"] = ""
                county["major_lake_distance_km_est"] = ""

        county["climate_zone"] = template["climate"]
        county["annual_mean_temp_c_est"] = template["temp"]
        county["annual_precip_mm_est"] = int(round(template["rain"] / 50) * 50)
        county["frost_free_days_est"] = int(round(template["frost"] / 10) * 10)
        near_water = any(distance <= 35 for distance in river_distances.values()) or any(
            distance <= 30 for distance in lake_distances.values()
        )
        county["drought_risk_1_5"] = template["drought"]
        county["flood_risk_1_5"] = int(clamp(template["flood"] + (1 if near_water else 0), 1, 5))
        county["cold_risk_1_5"] = template["cold"]
        county["heat_risk_1_5"] = template["heat"]
        county["typhoon_risk_1_5"] = template["typhoon"]
        county["landslide_risk_1_5"] = template["landslide"]
        county["climate_method"] = "physical-zone game baseline; not observed 1628 weather"


def prefecture_area_estimates(
    counties: list[dict[str, Any]], pref_rows: list[dict[str, str]]
) -> dict[tuple[str, str], tuple[float, str]]:
    grouped: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    for county in counties:
        grouped[(county["region"], county["upper_unit"])].append(county)
    pref_index: dict[str, list[dict[str, str]]] = defaultdict(list)
    for row in pref_rows:
        pref_index[row.get("NAME_CH", "")].append(row)
    output: dict[tuple[str, str], tuple[float, str]] = {}
    for key, members in grouped.items():
        region, upper = key
        target = PREFECTURE_ALIASES.get(upper, upper)
        candidates = [
            row
            for row in pref_index.get(target, [])
            if row.get("LEV1_CH") in REGION_QING_PROVINCES[region]
        ]
        if candidates:
            raw_area = float(candidates[0]["AREA"])
            per_county = raw_area / len(members)
            capped = clamp(
                per_county,
                250,
                MAX_AREA_PER_COUNTY[region],
            )
            method = "CHGIS 1820 prefecture area divided among 1628 counties"
            if abs(capped - per_county) > 1:
                method += "; frontier/outlier cap applied"
            output[key] = (capped * len(members), method)
        else:
            output[key] = (
                DEFAULT_AREA_PER_COUNTY[region] * len(members),
                "regional county-area fallback",
            )
    return output


def assign_areas(
    counties: list[dict[str, Any]], pref_rows: list[dict[str, str]]
) -> None:
    upper_areas = prefecture_area_estimates(counties, pref_rows)
    grouped: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    for county in counties:
        grouped[(county["region"], county["upper_unit"])].append(county)
    for key, members in grouped.items():
        total_area, method = upper_areas[key]
        distances: list[float] = []
        for county in members:
            point = (county["longitude"], county["latitude"])
            other_distances = [
                haversine_km(point, (other["longitude"], other["latitude"]))
                for other in members
                if other is not county
                and (other["longitude"], other["latitude"]) != point
            ]
            distances.append(min(other_distances) if other_distances else 50.0)
        typical_distance = median([distance for distance in distances if distance > 0]) if distances else 50
        weights: list[float] = []
        for county, distance in zip(members, distances):
            terrain_factor = {
                "平原": 0.85,
                "丘陵": 1.0,
                "山地": 1.25,
                "高原": 1.3,
                "盆地/河谷": 0.95,
                "草原": 1.45,
                "沙地/荒漠": 1.8,
                "湖泊/湿地": 0.9,
                "海岸/岛屿": 1.0,
            }[county["primary_landform"]]
            spacing = clamp(distance / typical_distance, 0.75, 1.35)
            weights.append(
                terrain_factor
                * spacing
                * stable_factor(
                    f"{county['region']}|{county['upper_unit']}|{county['county']}",
                    0.92,
                    1.08,
                )
            )
        scale = total_area / sum(weights)
        rounded = [max(50, int(round(weight * scale))) for weight in weights]
        difference = int(round(total_area)) - sum(rounded)
        if rounded:
            rounded[0] += difference
        for county, area in zip(members, rounded):
            county["area_km2_est"] = area
            county["area_method"] = method
            county["area_quality"] = "C"


def resource_names(county: dict[str, Any]) -> list[str]:
    region = county["region"]
    resources: list[str] = []
    if county["north_south_zone"] == "北方":
        resources.extend(["麦类", "粟黍", "豆类"])
    else:
        resources.extend(["水稻", "豆类"])
    if county["agriculture_potential_1_5"] >= 4:
        resources.append("高产农田")
    if county["forest_potential_1_5"] >= 4:
        resources.extend(["木材", "竹材", "药材"])
    if county["pasture_potential_1_5"] >= 4:
        resources.extend(["马", "牛羊", "皮毛"])
    if county["fishery_potential_1_5"] >= 4:
        resources.extend(["淡水鱼", "芦苇"])
    if county["coast_island_pct"] >= 8:
        resources.extend(["海鱼", "海盐", "贝类"])
    if region in {"南直隶（南京）", "浙江", "四川", "江西", "湖广"}:
        resources.append("蚕桑")
    if region in {"南直隶（南京）", "山东", "河南", "北直隶（京师）", "浙江"}:
        resources.append("棉麻")
    if region in {"浙江", "福建", "江西", "湖广", "四川", "云南", "广东"} and county["hill_pct"] + county["mountain_pct"] >= 25:
        resources.append("茶")
    if region in {"福建", "广东", "广西"}:
        resources.append("甘蔗")
    if region == "山西":
        resources.extend(["煤", "铁", "石材"])
    elif region == "云南":
        resources.extend(["铜矿潜力", "锡铅银矿潜力", "马帮贸易"])
    elif region in {"陕西", "河南", "山东", "北直隶（京师）"} and county["mountain_pct"] + county["plateau_pct"] >= 20:
        resources.extend(["煤铁矿潜力", "石材"])
    elif region in {"广西", "广东", "贵州", "江西", "福建"} and county["mountain_pct"] >= 20:
        resources.extend(["有色金属潜力", "石材"])
    if region == "四川":
        resources.extend(["井盐潜力", "铁器原料"])
    if county["upper_unit"] == "平阳府":
        resources.append("池盐")
    if region == "江西" and county["upper_unit"] == "饶州府":
        resources.extend(["瓷土", "陶瓷燃料"])
    if region in {"福建", "广东", "浙江", "江西"}:
        resources.append("陶土")
    return list(dict.fromkeys(resources))[:12]


def assign_resources(counties: list[dict[str, Any]]) -> None:
    for county in counties:
        template = ZONE_TEMPLATES[county["geographic_zone"]]
        river_distance = county.get("major_river_distance_km_est", "")
        lake_distance = county.get("major_lake_distance_km_est", "")
        near_water = (
            (river_distance != "" and float(river_distance) <= 35)
            or (lake_distance != "" and float(lake_distance) <= 30)
            or county["wetland_lake_pct"] >= 10
        )
        plain = county["plain_pct"]
        hills_mountains = county["hill_pct"] + county["mountain_pct"]
        wet = county["wetland_lake_pct"]
        coast = county["coast_island_pct"]
        arable = (
            plain * 0.67
            + county["hill_pct"] * 0.24
            + county["mountain_pct"] * 0.07
            + county["plateau_pct"] * 0.16
            + county["basin_valley_pct"] * 0.52
            + county["grassland_pct"] * 0.08
            + county["desert_pct"] * 0.02
            + wet * 0.22
            + coast * 0.18
        )
        county["arable_land_pct_est"] = int(round(clamp(arable, 3, 68)))
        forest = hills_mountains * (0.58 if county["north_south_zone"] != "北方" else 0.38)
        county["forest_land_pct_est"] = int(round(clamp(forest, 3, 65)))
        pasture = county["grassland_pct"] * 0.8 + county["plateau_pct"] * 0.22 + county["hill_pct"] * 0.08
        county["pasture_land_pct_est"] = int(round(clamp(pasture, 1, 55)))
        county["freshwater_index_1_5"] = int(clamp(round(template["water"] + (0.6 if near_water else 0)), 1, 5))
        county["soil_fertility_index_1_5"] = int(clamp(round(template["soil"]), 1, 5))
        county["agriculture_potential_1_5"] = int(clamp(round(template["agri"] + (0.4 if near_water else 0)), 1, 5))
        county["forest_potential_1_5"] = int(clamp(round(1 + hills_mountains / 25 + (0.7 if county["north_south_zone"] != "北方" else 0)), 1, 5))
        county["pasture_potential_1_5"] = int(clamp(round(1 + county["grassland_pct"] / 10 + county["plateau_pct"] / 20), 1, 5))
        county["fishery_potential_1_5"] = int(clamp(round(1 + wet / 8 + coast / 8 + (0.8 if near_water else 0)), 1, 5))
        salt = 1
        if coast >= 8:
            salt = 4
        if county["region"] == "四川":
            salt = max(salt, 3)
        if county["upper_unit"] == "平阳府":
            salt = 5
        county["salt_potential_1_5"] = salt
        mineral_base = {
            "山西": 5,
            "云南": 5,
            "陕西": 4,
            "贵州": 4,
            "江西": 3,
            "广西": 4,
            "广东": 3,
            "福建": 3,
            "河南": 3,
            "山东": 3,
            "北直隶（京师）": 3,
        }.get(county["region"], 2)
        if hills_mountains < 15:
            mineral_base -= 1
        county["mineral_potential_1_5"] = int(clamp(mineral_base, 1, 5))
        county["transport_index_1_5"] = int(clamp(round(template["transport"] + (0.5 if near_water else 0)), 1, 5))
        county["primary_resources"] = ";".join(resource_names(county))
        county["resource_method"] = "terrain/climate/hydrology rules; mineral values are potential only"


def allocate_integer(total: int, weights: list[float]) -> list[int]:
    scale = total / sum(weights)
    raw = [weight * scale for weight in weights]
    values = [int(value) for value in raw]
    remainder = total - sum(values)
    order = sorted(range(len(raw)), key=lambda index: raw[index] - values[index], reverse=True)
    for index in order[:remainder]:
        values[index] += 1
    return values


def assign_population_capacity(
    counties: list[dict[str, Any]], population_rows: list[dict[str, str]]
) -> None:
    population_by_region = {
        row["region"]: int(row["estimated_population_1630_cao"])
        for row in population_rows
    }
    headroom = {
        "北直隶（京师）": 1.15,
        "南直隶（南京）": 1.10,
        "山东": 1.15,
        "山西": 1.12,
        "河南": 1.15,
        "陕西": 1.08,
        "四川": 1.35,
        "江西": 1.18,
        "湖广": 1.30,
        "浙江": 1.08,
        "福建": 1.18,
        "广东": 1.28,
        "广西": 1.40,
        "云南": 1.45,
        "贵州": 1.50,
    }
    grouped: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for county in counties:
        grouped[county["region"]].append(county)
    for region, members in grouped.items():
        population_weights: list[float] = []
        capacity_weights: list[float] = []
        for county in members:
            seat_bonus = 1.18 if not county["intermediate_unit"] else 1.0
            water_bonus = 1 + 0.05 * (county["freshwater_index_1_5"] - 3)
            transport_bonus = 1 + 0.04 * (county["transport_index_1_5"] - 3)
            productive_area = county["area_km2_est"] * (
                0.30
                + county["agriculture_potential_1_5"] * 0.17
                + county["fishery_potential_1_5"] * 0.025
            )
            population_weights.append(productive_area * seat_bonus * water_bonus * transport_bonus)
            resilience = 1 - 0.025 * (
                county["drought_risk_1_5"] + county["flood_risk_1_5"] - 2
            )
            capacity_weights.append(productive_area * water_bonus * max(0.65, resilience))
        populations = allocate_integer(population_by_region[region], population_weights)
        capacity_total = int(round(population_by_region[region] * headroom[region]))
        capacities = allocate_integer(capacity_total, capacity_weights)
        for county, population, capacity in zip(members, populations, capacities):
            bad_year_factor = clamp(
                0.94
                - 0.035 * county["drought_risk_1_5"]
                - 0.035 * county["flood_risk_1_5"]
                - 0.01 * county["cold_risk_1_5"],
                0.55,
                0.85,
            )
            county["population_1630_est_allocated"] = population
            county["carrying_capacity_normal_year_est"] = capacity
            county["carrying_capacity_bad_year_est"] = int(round(capacity * bad_year_factor))
            county["population_pressure_pct"] = round(population / capacity * 100, 1) if capacity else 0
            county["population_capacity_method"] = "regional 1630 estimate allocated by area, terrain, water and transport"


def feature_rows(counties: list[dict[str, Any]]) -> list[dict[str, Any]]:
    output: list[dict[str, Any]] = []
    definitions: list[tuple[str, dict[str, Any]]] = []
    definitions.extend(("河流/运河", definition) for definition in RIVER_DEFINITIONS)
    definitions.extend(("湖泊/湖区", definition) for definition in LAKE_DEFINITIONS)
    definitions.extend((definition["type"], definition) for definition in LAND_FEATURE_DEFINITIONS)
    for index, (feature_type, definition) in enumerate(definitions, 1):
        affected = [
            county for county in counties if definition_matches_county(definition, county)
        ]
        regions = [region for region in REGION_ORDER if any(c["region"] == region for c in affected)]
        prefectures = sorted({county["upper_unit"] for county in affected})
        representative = [county["county"] for county in affected[:12]]
        output.append(
            {
                "feature_id": f"NF{index:03d}",
                "feature_type": feature_type,
                "feature_name": definition["name"],
                "river_basin_or_system": definition.get("basin", ""),
                "regions": ";".join(regions),
                "prefectures_1628": ";".join(prefectures),
                "associated_county_count": len(affected),
                "representative_counties": ";".join(representative),
                "resource_benefits": definition["benefit"],
                "constraints_and_hazards": definition["risk"],
                "association_method": "prefecture-level rough association; river/lake proximity also stored in county table",
                "evidence_grade": "documented_feature_reconstructed_extent",
            }
        )
    return output


def region_summary_rows(counties: list[dict[str, Any]]) -> list[dict[str, Any]]:
    grouped: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for county in counties:
        grouped[county["region"]].append(county)
    output: list[dict[str, Any]] = []
    for region in REGION_ORDER:
        members = grouped[region]
        landforms = Counter(county["primary_landform"] for county in members)
        zones = Counter(county["named_landform"] for county in members)
        resources = Counter(
            resource
            for county in members
            for resource in county["primary_resources"].split(";")
            if resource
        )
        output.append(
            {
                "region": region,
                "county_count": len(members),
                "county_service_area_km2_est": sum(county["area_km2_est"] for county in members),
                "population_1630_est_allocated": sum(county["population_1630_est_allocated"] for county in members),
                "carrying_capacity_normal_year_est": sum(county["carrying_capacity_normal_year_est"] for county in members),
                "carrying_capacity_bad_year_est": sum(county["carrying_capacity_bad_year_est"] for county in members),
                "dominant_primary_landform": landforms.most_common(1)[0][0],
                "major_named_landforms": ";".join(name for name, _ in zones.most_common(4)),
                "top_resource_potentials": ";".join(name for name, _ in resources.most_common(8)),
                "avg_agriculture_potential_1_5": round(sum(c["agriculture_potential_1_5"] for c in members) / len(members), 2),
                "avg_freshwater_index_1_5": round(sum(c["freshwater_index_1_5"] for c in members) / len(members), 2),
                "avg_transport_index_1_5": round(sum(c["transport_index_1_5"] for c in members) / len(members), 2),
                "avg_drought_risk_1_5": round(sum(c["drought_risk_1_5"] for c in members) / len(members), 2),
                "avg_flood_risk_1_5": round(sum(c["flood_risk_1_5"] for c in members) / len(members), 2),
                "data_quality": "game_model_v0.1",
            }
        )
    return output


def terrain_rule_rows() -> list[dict[str, Any]]:
    output = []
    for code, template in ZONE_TEMPLATES.items():
        row = {
            "geographic_zone": code,
            "display_name": template["feature"],
            "climate_zone": template["climate"],
            "annual_mean_temp_c_baseline": template["temp"],
            "annual_precip_mm_baseline": template["rain"],
            "frost_free_days_baseline": template["frost"],
            "agriculture_base_1_5": template["agri"],
            "soil_base_1_5": template["soil"],
            "water_base_1_5": template["water"],
            "transport_base_1_5": template["transport"],
        }
        row.update(dict(zip(TERRAIN_COLUMNS, template["terrain"])))
        output.append(row)
    return output


def write_csv(path: Path, rows: list[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if not rows:
        raise RuntimeError(f"Refusing to write empty CSV: {path}")
    with path.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def sqlite_type(values: Iterable[Any]) -> str:
    values = [value for value in values if value not in (None, "")]
    if values and all(isinstance(value, int) and not isinstance(value, bool) for value in values):
        return "INTEGER"
    if values and all(isinstance(value, (int, float)) and not isinstance(value, bool) for value in values):
        return "REAL"
    return "TEXT"


def replace_sqlite_table(
    connection: sqlite3.Connection, table: str, rows: list[dict[str, Any]]
) -> None:
    columns = list(rows[0])
    types = {column: sqlite_type(row[column] for row in rows) for column in columns}
    connection.execute(f'DROP TABLE IF EXISTS "{table}"')
    definition = ", ".join(f'"{column}" {types[column]}' for column in columns)
    connection.execute(f'CREATE TABLE "{table}" ({definition})')
    placeholders = ",".join("?" for _ in columns)
    connection.executemany(
        f'INSERT INTO "{table}" VALUES ({placeholders})',
        [[row[column] for column in columns] for row in rows],
    )


def write_sqlite(
    path: Path,
    counties: list[dict[str, Any]],
    features: list[dict[str, Any]],
    regions: list[dict[str, Any]],
    terrain_rules: list[dict[str, Any]],
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    connection = sqlite3.connect(path)
    try:
        replace_sqlite_table(connection, "county_geography_resources", counties)
        replace_sqlite_table(connection, "natural_features", features)
        replace_sqlite_table(connection, "region_summary", regions)
        replace_sqlite_table(connection, "terrain_resource_rules", terrain_rules)
        replace_sqlite_table(connection, "weather_event_rules", WEATHER_RULES)
        connection.execute(
            "CREATE INDEX IF NOT EXISTS idx_county_region ON county_geography_resources(region)"
        )
        connection.execute(
            "CREATE INDEX IF NOT EXISTS idx_county_upper ON county_geography_resources(upper_unit)"
        )
        connection.execute(
            "CREATE INDEX IF NOT EXISTS idx_county_name ON county_geography_resources(county)"
        )
        connection.commit()
    finally:
        connection.close()


def validate(
    counties: list[dict[str, Any]],
    population_rows: list[dict[str, str]],
    features: list[dict[str, Any]],
) -> None:
    if len(counties) != 1168:
        raise RuntimeError(f"Expected 1168 counties, found {len(counties)}")
    if len({county["county_id"] for county in counties}) != len(counties):
        raise RuntimeError("Duplicate county_id")
    if any(sum(county[column] for column in TERRAIN_COLUMNS) != 100 for county in counties):
        raise RuntimeError("Terrain percentages must sum to 100 for every county")
    expected_population = sum(
        int(row["estimated_population_1630_cao"]) for row in population_rows
    )
    actual_population = sum(county["population_1630_est_allocated"] for county in counties)
    if actual_population != expected_population:
        raise RuntimeError(
            f"Population allocation mismatch: {actual_population} != {expected_population}"
        )
    if not features:
        raise RuntimeError("Natural feature table is empty")
    if any(not county.get("latitude") or not county.get("longitude") for county in counties):
        raise RuntimeError("Every county must have a coordinate")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--county-csv", type=Path, default=DEFAULT_CHAPTER3_DIR / "county_hierarchy_1628.csv")
    parser.add_argument("--population-csv", type=Path, default=DEFAULT_CHAPTER3_DIR / "region_population_baseline.csv")
    parser.add_argument("--chgis-time-dbf", type=Path, default=Path("tmp/research/chgis_counties/extracted/v6_time_cnty_pts_utf_wgs84.dbf"))
    parser.add_argument("--chgis-1820-county-dbf", type=Path, default=Path("tmp/research/chgis_1820/extracted/v6_1820_cnty_pts_utf.dbf"))
    parser.add_argument("--chgis-1820-prefecture-dbf", type=Path, default=Path("tmp/research/chgis_1820/extracted/v6_1820_pref_pgn_utf.dbf"))
    parser.add_argument("--chgis-river-dbf", type=Path, default=Path("tmp/research/chgis_1820/extracted/v6_1820_coded_rvr_lin_utf.dbf"))
    parser.add_argument("--chgis-river-shp", type=Path, default=Path("tmp/research/chgis_1820/extracted/v6_1820_coded_rvr_lin_utf.shp"))
    parser.add_argument("--chgis-lake-dbf", type=Path, default=Path("tmp/research/chgis_1820/extracted/v6_1820_lks_pgn_utf.dbf"))
    parser.add_argument("--chgis-lake-shp", type=Path, default=Path("tmp/research/chgis_1820/extracted/v6_1820_lks_pgn_utf.shp"))
    parser.add_argument("--output-dir", type=Path, default=DEFAULT_CHAPTER4_DIR)
    args = parser.parse_args()

    required = [
        args.county_csv,
        args.population_csv,
        args.chgis_time_dbf,
        args.chgis_1820_county_dbf,
        args.chgis_1820_prefecture_dbf,
        args.chgis_river_dbf,
        args.chgis_river_shp,
        args.chgis_lake_dbf,
        args.chgis_lake_shp,
    ]
    missing = [str(path) for path in required if not path.exists()]
    if missing:
        raise SystemExit("Missing input files:\n- " + "\n- ".join(missing))

    with args.county_csv.open(encoding="utf-8-sig", newline="") as stream:
        source_counties = list(csv.DictReader(stream))
    with args.population_csv.open(encoding="utf-8-sig", newline="") as stream:
        population_rows = list(csv.DictReader(stream))

    counties: list[dict[str, Any]] = []
    for index, source in enumerate(source_counties, 1):
        counties.append(
            {
                "county_id": f"MING1628-{index:04d}",
                "snapshot_year": SNAPSHOT_YEAR,
                "region": source["region"],
                "upper_unit": source["upper_unit"],
                "upper_unit_type": source["upper_unit_type"],
                "intermediate_unit": normalize_intermediate(source["intermediate_unit"]),
                "county": source["county"],
            }
        )

    active, slice_1820, all_time = build_coordinate_index(
        read_dbf(args.chgis_time_dbf), read_dbf(args.chgis_1820_county_dbf)
    )
    geocode_counties(counties, active, slice_1820, all_time)
    river_geometries = prepare_water_geometries(
        args.chgis_river_dbf, args.chgis_river_shp, RIVER_DEFINITIONS
    )
    lake_geometries = prepare_water_geometries(
        args.chgis_lake_dbf, args.chgis_lake_shp, LAKE_DEFINITIONS
    )
    assign_terrain_climate_and_water(counties, river_geometries, lake_geometries)
    assign_areas(counties, read_dbf(args.chgis_1820_prefecture_dbf))
    assign_resources(counties)
    assign_population_capacity(counties, population_rows)

    for county in counties:
        county["data_quality"] = "historical_hierarchy_plus_game_estimate_v0.1"
        county["commercial_release_ready"] = "no - replace or license CHGIS-derived coordinates"

    features = feature_rows(counties)
    regions = region_summary_rows(counties)
    terrain_rules = terrain_rule_rows()
    validate(counties, population_rows, features)

    write_csv(args.output_dir / "county_geography_resources_v0.1.csv", counties)
    write_csv(args.output_dir / "major_natural_features_v0.1.csv", features)
    write_csv(args.output_dir / "region_geography_summary_v0.1.csv", regions)
    write_csv(args.output_dir / "terrain_resource_rules_v0.1.csv", terrain_rules)
    write_csv(args.output_dir / "weather_event_rules_v0.1.csv", WEATHER_RULES)
    write_sqlite(
        args.output_dir / "game_world_1628_geography_v0.1.sqlite",
        counties,
        features,
        regions,
        terrain_rules,
    )
    quality = Counter(county["coordinate_quality"] for county in counties)
    print(
        f"Wrote {len(counties)} counties, {len(features)} natural features, "
        f"and {len(regions)} region summaries to {args.output_dir}"
    )
    print(f"Coordinate quality: {dict(sorted(quality.items()))}")


if __name__ == "__main__":
    main()
