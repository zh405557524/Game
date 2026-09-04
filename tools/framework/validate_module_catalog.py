#!/usr/bin/env python3
"""Verify that the runtime catalog covers every authoritative documented module."""

from __future__ import annotations

import re
import sqlite3
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
DOC_ROOTS = (
    REPO_ROOT / "docs/01_游戏底层架构/01_世界模拟框架与规则",
    REPO_ROOT / "docs/01_游戏底层架构/02_世界运行设计",
)
CATALOG_SOURCE = (
    REPO_ROOT
    / "game/Packages/com.projectrealm.framework/Runtime/World/FrameworkModuleCatalog.cs"
)
DEFINITION_DB = (
    REPO_ROOT
    / "game/Assets/ProjectRealm/Content/Definitions/Development/Resources/realm_definition_ming1628_dev_v1.sqlite"
)
EXCLUDED = {"DomainModule", "SolverModule", "PersonModule", "TaxModule"}


def documented_modules() -> set[str]:
    result: set[str] = set()
    for root in DOC_ROOTS:
        for path in root.rglob("*.md"):
            if "参考资料" in path.parts or "归档" in path.parts:
                continue
            result.update(re.findall(r"`([A-Z][A-Za-z0-9]*Module)`", path.read_text(encoding="utf-8")))
    return result - EXCLUDED


def source_modules() -> set[str]:
    text = CATALOG_SOURCE.read_text(encoding="utf-8")
    block = re.search(
        r"CanonicalSourceNames\s*=\s*\{(?P<body>.*?)\};", text, re.DOTALL
    )
    if block is None:
        raise SystemExit("CanonicalSourceNames was not found in FrameworkModuleCatalog.cs")
    return set(re.findall(r'"([A-Z][A-Za-z0-9]*Module)"', block.group("body")))


def database_modules() -> set[str]:
    if not DEFINITION_DB.is_file():
        raise SystemExit(
            "Generated Definition DB is missing. Run: python3 tools/framework/build_runtime_definition.py"
        )
    connection = sqlite3.connect(f"file:{DEFINITION_DB.resolve()}?mode=ro&immutable=1", uri=True)
    try:
        names = {row[0] for row in connection.execute("SELECT source_name FROM module_definition")}
        aliases = dict(
            connection.execute(
                "SELECT alias_name, canonical_definition_id FROM module_compatibility_alias"
            )
        )
        if set(aliases) != {"PersonModule", "TaxModule"}:
            raise SystemExit(f"Unexpected compatibility aliases: {sorted(aliases)}")
        authoritative_aliases = connection.execute(
            "SELECT COUNT(*) FROM module_compatibility_alias WHERE authoritative_provider<>0"
        ).fetchone()[0]
        if authoritative_aliases:
            raise SystemExit("Compatibility aliases must never be authoritative providers")
        return names
    finally:
        connection.close()


def compare(label: str, expected: set[str], actual: set[str]) -> None:
    missing = sorted(expected - actual)
    extra = sorted(actual - expected)
    if missing or extra:
        raise SystemExit(f"{label} mismatch; missing={missing}, extra={extra}")


def main() -> None:
    documented = documented_modules()
    source = source_modules()
    database = database_modules()
    compare("C# module catalog", documented, source)
    compare("Definition DB module catalog", documented, database)
    if len(documented) != 101:
        raise SystemExit(f"Expected 101 canonical modules, found {len(documented)}")
    print("Module catalog validation passed: 101 canonical modules, 2 non-authoritative aliases")


if __name__ == "__main__":
    main()
