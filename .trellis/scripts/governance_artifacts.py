#!/usr/bin/env python3
"""Enforce the local-only policy for generated CCG and Serena artifacts."""

from __future__ import annotations

import argparse
import json
import re
import subprocess
from dataclasses import dataclass
from pathlib import Path


CCG_RAW_PREFIX = ".ccg/dual-model-runs/"
SERENA_CACHE_PREFIX = ".serena/cache/"
BEARER_TOKEN_PATTERN = re.compile(r"Bearer\s+[A-Za-z0-9_-]{172}(?![A-Za-z0-9_-])")


@dataclass(frozen=True)
class IndexScanResult:
    """A path-minimized summary of disallowed Git-index entries."""

    ccg_raw_paths: int
    serena_cache_paths: int

    @property
    def is_clean(self) -> bool:
        return self.ccg_raw_paths == 0 and self.serena_cache_paths == 0

    def to_durable_record(self) -> dict[str, int | str]:
        return {
            "policy": "pass" if self.is_clean else "fail",
            "ccg_raw_paths": self.ccg_raw_paths,
            "serena_cache_paths": self.serena_cache_paths,
        }


def _indexed_paths(repo_root: Path) -> list[str]:
    """Return Git-index paths without reading or rendering their contents."""
    result = subprocess.run(
        ["git", "-C", str(repo_root), "ls-files", "-z"],
        capture_output=True,
        check=False,
    )
    if result.returncode != 0:
        raise RuntimeError("Unable to inspect the Git index.")
    return [
        path.replace("\\", "/")
        for path in result.stdout.decode("utf-8", errors="surrogateescape").split("\0")
        if path
    ]


def check_index(repo_root: Path) -> IndexScanResult:
    """Count prohibited generated-artifact classes in the Git index."""
    paths = _indexed_paths(repo_root)
    return IndexScanResult(
        ccg_raw_paths=sum(path.startswith(CCG_RAW_PREFIX) for path in paths),
        serena_cache_paths=sum(path.startswith(SERENA_CACHE_PREFIX) for path in paths),
    )


def build_fixture_report(fake_bearer: str | None = None) -> dict[str, int | str]:
    """Exercise bearer redaction without persisting or printing the fixture."""
    fixture = fake_bearer or f"Bearer {'A' * 172}"
    redacted = BEARER_TOKEN_PATTERN.sub("[REDACTED_BEARER_TOKEN]", fixture)
    durable_payload = json.dumps(
        {"fixture": "synthetic", "payload": redacted}, sort_keys=True
    )
    return {
        "policy": "pass",
        "synthetic_token_matches": len(BEARER_TOKEN_PATTERN.findall(fixture)),
        "durable_token_matches": len(BEARER_TOKEN_PATTERN.findall(durable_payload)),
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Check generated CCG/Serena artifacts without exposing payloads."
    )
    parser.add_argument("--repo-root", type=Path, default=Path.cwd())
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--check-index", action="store_true")
    mode.add_argument("--scan-fixture", action="store_true")
    args = parser.parse_args(argv)

    if args.scan_fixture:
        report = build_fixture_report()
        print(json.dumps(report, sort_keys=True))
        return 0 if report["durable_token_matches"] == 0 else 1

    result = check_index(args.repo_root)
    print(json.dumps(result.to_durable_record(), sort_keys=True))
    return 0 if result.is_clean else 1


if __name__ == "__main__":
    raise SystemExit(main())
