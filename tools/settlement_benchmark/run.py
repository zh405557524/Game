#!/usr/bin/env python3
"""Read-only fixture export, offline C# build/run, and reproducible diagnostic report."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import sqlite3
import statistics
import subprocess
import sys
from datetime import datetime


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_DB = ROOT / "docs/90_资料与归档/01_崇祯元年历史资料/data/1628/13.模拟基础规则/game_world_1628_v1.0.sqlite"
DEFAULT_SDK = Path("/Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet")


def file_hash(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(4 * 1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def command_output(*args):
    return subprocess.check_output(args, text=True).strip()


def export_fixture(database, output):
    before = file_hash(database)
    with sqlite3.connect(database.resolve().as_uri() + "?mode=ro", uri=True) as connection:
        connection.execute("PRAGMA query_only=ON")
        counties = connection.execute(
            "SELECT county_id, county, population_est_1628 FROM county_economy_baseline ORDER BY county_id"
        ).fetchall()
        indices = {county_id: index for index, (county_id, _, _) in enumerate(counties)}
        divisions = [
            {"Id": identity, "County": indices[county_id], "Population": population}
            for identity, county_id, population in connection.execute(
                "SELECT division_id, county_id, resident_population_est FROM local_division_definition ORDER BY division_id"
            )
        ]
        totals = [0] * len(counties)
        for division in divisions:
            totals[division["County"]] += division["Population"]
        assert totals == [population for _, _, population in counties], "County/division population mismatch"
        fixture = {"SourceSha256": before, "CountyIds": [c[0] for c in counties],
                   "CountyNames": [c[1] for c in counties], "Divisions": divisions}
        source = {
            "database": str(database), "sha256_before": before,
            "user_version": connection.execute("PRAGMA user_version").fetchone()[0],
            "county_count": len(counties), "division_count": len(divisions),
            "population_estimate": sum(totals),
            "settlement_memberships": connection.execute("SELECT COUNT(*) FROM settlement_local_division").fetchone()[0],
            "person_catalog_status_counts": dict(connection.execute(
                "SELECT alive_status_1628, COUNT(*) FROM historical_person_catalog GROUP BY alive_status_1628"
            )),
            "county_division_population_match": True,
        }
    output.write_text(json.dumps(fixture, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    return source


def report_markdown(data, metadata, output):
    source = metadata["source"]
    hardware = metadata["hardware"]
    lines = [
        "# 全国分时月结合成负载测试", "",
        f"生成时间：{metadata['completed_local']}。这是独立诊断工具，不是正式月结实现，也不是完整游戏性能验收。", "",
        "## 运行环境与输入", "",
        f"- 机器：{hardware['cpu']}，{hardware['memory_gib']:.0f} GiB 内存，{hardware['logical_cpus']} 逻辑核；macOS {platform.mac_ver()[0]}。",
        f"- 运行时：{data['Runtime']} / {data['Architecture']}，Unity 自带 SDK，Release，无 Unity 画面、无 IL2CPP。",
        f"- 压测进程峰值 RSS：{metadata['benchmark_peak_rss_mib']:.2f} MiB，由操作系统 wait4 统计，包含两档预热、复测与检查点测试，不是单个世界常驻内存。",
        "- 单计算线程；不使用多机或并行任务。关闭 .NET 分层 JIT，先运行同规模预热，再分别采样 3 次同步和分片运行。",
        f"- 只读基础库：{source['county_count']:,} 县、{source['division_count']:,} 计算区、{source['settlement_memberships']:,} 聚落成员关系，模型人口 {source['population_estimate']:,}。",
        "- 每县与所属计算区的人口之和逐县核验一致。稳定 ID 和归属来自基础库；经济数值、人物及关系、决策规则全部为压力测试合成数据。",
        f"- 人物史料目录状态：{json.dumps(source['person_catalog_status_counts'], ensure_ascii=False)}；并未把已故人物当成开局人物。",
        f"- 基础库前后 SHA-256 一致：`{source['sha256_before']}`。", "",
        "## 负载构成", "",
        "| 项目 | 常规压力档 | 加重压力档 |", "|---|---:|---:|",
    ]
    first, second = data["Scenarios"]
    for label, key in [("县", "Counties"), ("计算区", "Divisions"), ("合成人口群体", "Cohorts"),
                       ("合成官员", "Officials"), ("其他合成人物", "OtherPeople"), ("资源状态槽", "ResourceSlots"),
                       ("每人物候选数", "CandidatesPerActor"), ("每候选关系读取数", "RelationsPerCandidate"),
                       ("跨县双边转移", "Transfers")]:
        lines.append(f"| {label} | {first[key]:,} | {second[key]:,} |")
    lines += [
        "", "每计算区覆盖 29 个合成资源槽；普通人口按群体更新，不逐人运行 AI。官员与人物使用同一个有界合成评分器，不代表已实现正式官员 AI。",
        "生产阶段直接调用仓库现有 `ProductionSettlementService.Settle`；常规每区 1 个外力驱动，加重档每区 4 个。其他人口、消耗、损耗、税与关系公式均为合成。",
        "", "## 测量结果", "",
        "同步值为 3 次中位数；分片统计包含 3 次不等待执行和 1 次约 60 Hz 的无画面节拍运行。2ms 是协作预算，不是硬实时保证。", "",
        "| 指标 | 常规压力档 | 加重压力档 |", "|---|---:|---:|",
    ]
    summaries = []
    for scenario in data["Scenarios"]:
        sync = [r for r in scenario["Runs"] if r["Mode"] == "synchronous"]
        sliced = [r for r in scenario["Runs"] if r["Mode"] != "synchronous"]
        unpaced = [r for r in sliced if r["Mode"] == "sliced_2ms_unpaced"]
        paced = next(r for r in sliced if r["Mode"] == "sliced_2ms_paced_60hz")
        summaries.append({
            "sync": statistics.median(r["WorkMs"] for r in sync),
            "slice_work": statistics.median(r["WorkMs"] for r in unpaced),
            "p95": max(r["P95SliceMs"] for r in sliced),
            "max": max(r["MaxSliceMs"] for r in sliced),
            "over_frame": sum(r["Over16_67MsSlices"] for r in sliced),
            "paced": paced["WallMs"] / 1000,
            "frames": paced["Slices"],
            "prepare": max(r["PreparationMs"] for r in scenario["Runs"]),
            "audit": max(r["AuditMs"] for r in scenario["Runs"]),
            "hash": max(r["HashMs"] for r in scenario["Runs"]),
            "allocated": max(r["AllocatedMiB"] for r in scenario["Runs"]),
            "checkpoint": max(scenario["CheckpointRoundtripMs"]),
        })
    for label, key, unit in [
        ("同步连续计算中位数", "sync", "ms"), ("分片累计计算中位数", "slice_work", "ms"),
        ("各次分片 p95 的最大值", "p95", "ms"), ("观测到最慢分片", "max", "ms"),
        ("超过 16.67ms 的分片次数", "over_frame", ""),
        ("约 60Hz 分片实际完成时间", "paced", "s"), ("约 60Hz 分片节拍数", "frames", ""),
        ("月结草稿分配最大耗时（另计）", "prepare", "ms"),
        ("完整审计最大耗时（另计）", "audit", "ms"), ("完整 SHA-256 最大耗时（另计）", "hash", "ms"),
        ("单次月结托管分配最大值", "allocated", "MiB"),
        ("检查点 JSON 序列化与反序列化最大耗时（另计）", "checkpoint", "ms"),
    ]:
        lines.append(f"| {label} | {summaries[0][key]:.2f} {unit} | {summaries[1][key]:.2f} {unit} |")
    lines += ["", "累计分片计算可能比同步更长，这是检查预算和调度的额外开销；其收益是让出执行时间。实际节拍时间并非游戏内 1～2 日，换算取决于游戏倍速和每游戏日现实时间。", "",
              "月结草稿分配、完整审计、完整摘要、检查点 JSON 测试均单独测量，未塞进 2ms 分片数字。正式接入时这些步骤也需放到后台或分批执行，不能在界面线程集中运行。", "",
              "## 阶段耗时", "", "下表来自实际节拍运行，包含被操作系统抢占的时间。", "",
              "| 阶段 | 常规压力档 ms | 加重压力档 ms |", "|---|---:|---:|"]
    paced_runs = [next(r for r in s["Runs"] if r["Mode"] == "sliced_2ms_paced_60hz") for s in data["Scenarios"]]
    for phase in paced_runs[0]["StageWorkMs"]:
        lines.append(f"| {phase} | {paced_runs[0]['StageWorkMs'][phase]:.2f} | {paced_runs[1]['StageWorkMs'][phase]:.2f} |")
    lines += ["", "## 一致性与异常检查", ""]
    for scenario in data["Scenarios"]:
        lines += [f"### {scenario['Name']}", "", f"最终状态 SHA-256：`{scenario['ResultSha256']}`。", ""]
        lines += [f"- PASS `{check}`" for check in scenario["Checks"]]
        lines += [f"- 检查点大小：{scenario['CheckpointMiB']:.2f} MiB。", ""]
    sample = first["SampleCounty"]
    lines += [
        "## 一条可以追溯的县级账", "",
        f"常规档：{sample['CountyName']}（`{sample['CountyId']}`），资源 `{sample['Resource']}`，合成单位，不是历史粮食估算。", "",
        "| 期初 | 本月产出 | 本月消耗 | 本月损耗 | 跨县净流入 | 期末 |",
        "|---:|---:|---:|---:|---:|---:|",
        "| " + " | ".join(f"{sample[key]:,}" for key in ["Opening", "Produced", "Consumed", "Lost", "NetTransfer", "Closing"]) + " |", "",
        "检查恒等式：期初 + 产出 - 消耗 - 损耗 + 跨县净流入 = 期末。每县每资源都核对，不能只查全国总和。", "",
    ]
    lines += [
        f"现有分层年度探针额外执行成功：{data['ExistingYearProbeMonths']} 个月。不是完整测试套件，也不是全国 12 个月连续经济模拟。", "",
        "## 防止数据混乱的具体约束", "",
        "1. 每个计算区只有一个县父级；县账只是汇总同一轮分区草稿，不重新生产或扣除第二次。",
        "2. 一个阶段完成才进入依赖它的阶段；只有彼此独立的项目可改执行顺序。跨县转移保持固定顺序并成对记账。",
        "3. 固定月份与输入快照；随机数由实体 ID/固定种子推导，不取决于先算谁。",
        "4. 未关闭的月结不发布；已发布月份再次提交不改变余额。新月命令在独立预留层，不被旧月结果覆盖。",
        "5. 人口、资源、资金守恒；对不平衡报错，不通过截断或归零掩盖。故意多加 1 单位资源会被拒绝。", "",
        "## 解释边界与下一步", "",
        "- 本测试证明的是：这组明确负载下，可以分片执行并保持结果一致；不能证明全量游戏永不卡顿。",
        "- 未包含 Unity 渲染、UI 刷新、寻路、完整战争/贸易撮合/人物 AI、生产存档数据库事务、崩溃恢复、后台线程同步争用。",
        "- 检查点测试是内存中 JSON 序列化恢复，不是磁盘存档或断电一致性测试。发布是内存引用切换，不是 SQLite 入账测试。",
        "- 当前月结预留仅模拟已知安全余额；正式游戏必须预留真实待结义务，不能照搬测试里的固定下界。",
        "- 资源单位、人口变化、税额、候选与关系算法没有历史真实性或完整玩法语义保证。",
        "- 2ms 是目标预算。GC、操作系统抢占、单个不可拆任务会造成超时；需要同时看最慢分片，不能只看平均数。",
        "- 下一步应把相同负载接入 Unity 实际场景，测主线程最长帧、GC、存档及批量 UI 更新；再确定次月 1～2 日窗口对应的真实时间和倍速上限。", "",
        "## 复现", "", "```bash", "python3 tools/settlement_benchmark/run.py", "```", "",
        f"完整原始结果：`{output / 'results.json'}`；输入与环境：`{output / 'metadata.json'}`。", "",
    ]
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--database", type=Path, default=DEFAULT_DB)
    parser.add_argument("--dotnet", type=Path, default=DEFAULT_SDK)
    parser.add_argument("--output", type=Path, default=ROOT / "builds/settlement-benchmark")
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    if not args.dotnet.is_file():
        raise SystemExit(".NET SDK not found; pass --dotnet. This tool does not install software.")
    source = export_fixture(args.database, output / "fixture.json")
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1",
               DOTNET_TieredCompilation="0")
    project = Path(__file__).parent
    subprocess.run([str(args.dotnet), "build", str(project / "SettlementBenchmark.csproj"), "-c", "Release",
                    "-o", str(output / "bin"), f"-p:BaseIntermediateOutputPath={output / 'obj'}/",
                    "--configfile", str(project / "NuGet.Config")], env=env, check=True, cwd=ROOT)
    process = subprocess.Popen([str(args.dotnet), str(output / "bin/SettlementBenchmark.dll"),
                                str(output / "fixture.json"), str(output / "results.json")], env=env, cwd=ROOT)
    _, status, usage = os.wait4(process.pid, 0)
    process.returncode = os.waitstatus_to_exitcode(status)
    if process.returncode:
        raise subprocess.CalledProcessError(process.returncode, process.args)
    source["sha256_after"] = file_hash(args.database)
    assert source["sha256_before"] == source["sha256_after"], "Source database changed during test"
    metadata = {
        "source": source,
        "hardware": {
            "cpu": command_output("sysctl", "-n", "machdep.cpu.brand_string"),
            "model": command_output("sysctl", "-n", "hw.model"),
            "memory_gib": int(command_output("sysctl", "-n", "hw.memsize")) / 1024**3,
            "logical_cpus": int(command_output("sysctl", "-n", "hw.logicalcpu")),
        },
        "completed_local": datetime.now().astimezone().isoformat(),
        "benchmark_peak_rss_mib": usage.ru_maxrss / (1024**2 if sys.platform == "darwin" else 1024),
        "sdk": str(args.dotnet), "source_code_sha256": file_hash(project / "Program.cs"),
        "domain_sources_sha256": {
            p.name: file_hash(p) for p in sorted((ROOT / "game/Packages/com.projectrealm.simulation/Runtime/Domain").glob("*.cs"))
        },
    }
    (output / "metadata.json").write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8")
    data = json.loads((output / "results.json").read_text(encoding="utf-8"))
    report = output / "report.md"
    report.write_text(report_markdown(data, metadata, output), encoding="utf-8")
    print(f"Report: {report}", flush=True)


if __name__ == "__main__":
    main()
