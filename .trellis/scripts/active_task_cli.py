#!/usr/bin/env python3
"""Expose the canonical Trellis active-task resolver as a JSON CLI."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from common.active_task import resolve_active_task


def _parse_platform_input(raw: str) -> dict[str, Any] | None:
    if not raw:
        return None
    parsed = json.loads(raw)
    if not isinstance(parsed, dict):
        raise ValueError("--platform-input-json must contain a JSON object")
    return parsed


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Resolve the active Trellis task without duplicating policy."
    )
    parser.add_argument("--repo-root", required=True)
    parser.add_argument("--platform")
    parser.add_argument("--platform-input-json", default="")
    parser.add_argument("--use-sole-session", action="store_true")
    arguments = parser.parse_args()

    try:
        platform_input = _parse_platform_input(arguments.platform_input_json)
    except ValueError as error:
        parser.error(str(error))

    active = resolve_active_task(
        Path(arguments.repo_root),
        platform_input=platform_input,
        platform=arguments.platform,
        allow_sole_session_fallback=arguments.use_sole_session,
    )
    json.dump(
        {
            "taskPath": active.task_path,
            "source": active.source,
            "stale": active.stale,
        },
        fp=sys.stdout,
        ensure_ascii=False,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
