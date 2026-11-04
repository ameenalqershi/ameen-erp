#!/usr/bin/env python3
"""Reproducible .NET 10 verification; reports only checks that actually ran."""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
BACKEND = ROOT / "backend"


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--dotnet", default="dotnet")
    p.add_argument("--sql", action="store_true")
    p.add_argument("--artifacts", type=Path, default=BACKEND / ".artifacts")
    args = p.parse_args()
    if args.sql and not os.environ.get("AMEEN_TEST_SQL_CONNECTION"):
        p.error("--sql requires AMEEN_TEST_SQL_CONNECTION for an isolated test server")
    args.artifacts = args.artifacts.resolve()
    args.artifacts.mkdir(parents=True, exist_ok=True)
    env = os.environ.copy()
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    env["AMEEN_TEST_ARTIFACTS"] = str(args.artifacts)
    sdk = subprocess.check_output([args.dotnet, "--version"], cwd=BACKEND, text=True).strip()
    if int(sdk.split(".")[0]) < 10:
        p.error("Use the .NET 10 SDK required by backend/global.json")
    report = {
        "startedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "sdk": sdk, "targetFramework": "net10.0", "configuration": "Release",
        "liveSqlRequested": args.sql, "steps": [], "result": "failed",
    }
    fingerprint = hashlib.sha256()
    for path in sorted(BACKEND.rglob("*")):
        if path.is_file() and (path.suffix in {".cs", ".csproj", ".props", ".sql"} or path.name in {"global.json", "packages.lock.json"}) and not any(x in path.parts for x in ("bin", "obj", ".artifacts")):
            fingerprint.update(str(path.relative_to(ROOT)).encode())
            fingerprint.update(path.read_bytes())
    report["sourceSha256"] = fingerprint.hexdigest()
    commands = [
        [sys.executable, str(BACKEND / "scripts/check_analysis_baseline.py")],
        [sys.executable, "-m", "unittest", "discover", "-s", str(BACKEND / "scripts/tests"), "-p", "test_*.py"],
        [sys.executable, str(BACKEND / "scripts/check_migration_versions.py")],
        [sys.executable, str(BACKEND / "scripts/traceability.py"), "--check"],
        [sys.executable, str(BACKEND / "scripts/audit_cash_diagram.py")],
        [args.dotnet, "restore", "Ameen.Erp.sln", "--locked-mode", "--disable-parallel", "-m:1"],
        [args.dotnet, "build", "Ameen.Erp.sln", "-c", "Release", "--no-restore", "-p:UseSharedCompilation=false", "-m:1"],
        [args.dotnet, "tests/Ameen.Erp.Tests/bin/Release/net10.0/Ameen.Erp.Tests.dll", *(["--sql"] if args.sql else [])],
    ]
    outputs = []
    code = 0
    for command in commands:
        process = subprocess.Popen(command, cwd=BACKEND, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        lines = []
        for line in process.stdout:
            print(line, end="", flush=True)
            lines.append(line)
        code = process.wait()
        output = "".join(lines)
        outputs.append(output)
        report["steps"].append({"command": command, "exitCode": code})
        if code:
            break
    combined = "\n".join(outputs)
    result = re.search(r"RESULT: (\d+)/(\d+) passed; (\d+) failed", combined)
    report["checks"] = [line for line in combined.splitlines() if line.startswith(("PASS ", "  SQL PASS ", "  HOST PASS "))]
    if result:
        report["passed"], report["total"], report["failed"] = map(int, result.groups())
    report["finishedAtUtc"] = datetime.datetime.now(datetime.timezone.utc).isoformat()
    if code == 0 and result and report["failed"] == 0:
        report["result"] = "passed"
    else:
        code = code or 1
    (args.artifacts / "verification.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n")
    (args.artifacts / "verification.log").write_text(combined)
    print("Verification report:", args.artifacts / "verification.json")
    return code


if __name__ == "__main__":
    raise SystemExit(main())
