#!/usr/bin/env python3
"""
Task utility functions.

Provides:
    is_safe_task_path   - Validate task path is safe to operate on
    find_task_by_name   - Find task directory by name
    resolve_task_dir    - Resolve task directory from name, relative, or absolute path
    archive_task_dir    - Archive task to monthly directory
    run_task_hooks      - Run lifecycle hooks for task events
"""

from __future__ import annotations

import shutil
import sys
from datetime import datetime
from pathlib import Path

from .paths import get_repo_root, get_tasks_dir


# =============================================================================
# Path Safety
# =============================================================================

def is_safe_task_path(task_path: str, repo_root: Path | None = None) -> bool:
    """Check if a relative task path is safe to operate on.

    Args:
        task_path: Task path (relative to repo_root).
        repo_root: Repository root path. Defaults to auto-detected.

    Returns:
        True if safe, False if dangerous.
    """
    if repo_root is None:
        repo_root = get_repo_root()

    normalized = task_path.replace("\\", "/")

    # Check empty or null
    if not normalized or normalized == "null":
        print("Error: empty or null task path", file=sys.stderr)
        return False

    # Reject absolute paths
    if Path(task_path).is_absolute():
        print(f"Error: absolute path not allowed: {task_path}", file=sys.stderr)
        return False

    # Reject ".", "..", paths starting with "./" or "../", or containing ".."
    if normalized in (".", "..") or normalized.startswith("./") or normalized.startswith("../") or ".." in normalized:
        print(f"Error: path traversal not allowed: {task_path}", file=sys.stderr)
        return False

    # Final check: ensure resolved path is not the repo root
    abs_path = repo_root / Path(normalized)
    if abs_path.exists():
        try:
            resolved = abs_path.resolve()
            root_resolved = repo_root.resolve()
            if resolved == root_resolved:
                print(f"Error: path resolves to repo root: {task_path}", file=sys.stderr)
                return False
        except (OSError, IOError):
            pass

    return True


# =============================================================================
# Task Lookup
# =============================================================================

def find_task_by_name(task_name: str, tasks_dir: Path) -> Path | None:
    """Find task directory by name (exact or suffix match).

    Args:
        task_name: Task name to find.
        tasks_dir: Tasks directory path.

    Returns:
        Absolute path to task directory, or None if not found.
    """
    if not task_name or not tasks_dir or not tasks_dir.is_dir():
        return None

    # Try exact match first
    exact_match = tasks_dir / task_name
    if exact_match.is_dir():
        return exact_match

    # Try suffix match (e.g., "my-task" matches "01-21-my-task")
    for d in tasks_dir.iterdir():
        if d.is_dir() and d.name.endswith(f"-{task_name}"):
            return d

    return None


# =============================================================================
# Archive Operations
# =============================================================================

def archive_task_dir(task_dir_abs: Path, repo_root: Path | None = None) -> Path | None:
    """Archive a task directory to archive/{YYYY-MM}/.

    Args:
        task_dir_abs: Absolute path to task directory.
        repo_root: Repository root path. Defaults to auto-detected.

    Returns:
        Path to archived directory, or None on error.
    """
    if not task_dir_abs.is_dir():
        print(f"Error: task directory not found: {task_dir_abs}", file=sys.stderr)
        return None

    # Get tasks directory (parent of the task)
    tasks_dir = task_dir_abs.parent
    archive_dir = tasks_dir / "archive"
    year_month = datetime.now().strftime("%Y-%m")
    month_dir = archive_dir / year_month

    # Create archive directory
    try:
        month_dir.mkdir(parents=True, exist_ok=True)
    except (OSError, IOError) as e:
        print(f"Error: Failed to create archive directory: {e}", file=sys.stderr)
        return None

    # Move task to archive
    task_name = task_dir_abs.name
    dest = month_dir / task_name

    try:
        shutil.move(str(task_dir_abs), str(dest))
    except (OSError, IOError, shutil.Error) as e:
        print(f"Error: Failed to move task to archive: {e}", file=sys.stderr)
        return None

    return dest


def archive_task_complete(
    task_dir_abs: Path,
    repo_root: Path | None = None
) -> dict[str, str]:
    """Complete archive workflow: archive directory.

    Args:
        task_dir_abs: Absolute path to task directory.
        repo_root: Repository root path. Defaults to auto-detected.

    Returns:
        Dict with archive result info.
    """
    if not task_dir_abs.is_dir():
        print(f"Error: task directory not found: {task_dir_abs}", file=sys.stderr)
        return {}

    archive_dest = archive_task_dir(task_dir_abs, repo_root)
    if archive_dest:
        return {"archived_to": str(archive_dest)}

    return {}


# =============================================================================
# Task Directory Resolution
# =============================================================================

def resolve_task_dir(target_dir: str, repo_root: Path) -> Path:
    """Resolve task directory to absolute path.

    Supports:
    - Absolute path: /path/to/task
    - Relative path: .trellis/tasks/01-31-my-task
    - Task name: my-task (uses find_task_by_name for lookup)

    Args:
        target_dir: Task directory specification.
        repo_root: Repository root path.

    Returns:
        Resolved absolute path.
    """
    if not target_dir:
        return Path()

    normalized = target_dir.replace("\\", "/")
    while normalized.startswith("./"):
        normalized = normalized[2:]

    # Absolute path
    if Path(target_dir).is_absolute():
        return Path(target_dir)

    # Relative path (contains path separator or starts with .trellis)
    if "/" in normalized or normalized.startswith(".trellis"):
        return repo_root / Path(normalized)

    # Task name - try to find in tasks directory
    tasks_dir = get_tasks_dir(repo_root)
    found = find_task_by_name(target_dir, tasks_dir)
    if found:
        return found

    # Fallback to treating as relative path
    return repo_root / Path(normalized)


# =============================================================================
# Bounded Lifecycle Hooks
# =============================================================================

def _normalize_hook(hook: object) -> tuple[str | list[str], float, str]:
    if isinstance(hook, str):
        return hook, 30.0, "warn"
    if not isinstance(hook, dict):
        raise ValueError("Hook must be a command string or mapping")

    command = hook.get("command")
    if not isinstance(command, str) and not (
        isinstance(command, list) and all(isinstance(part, str) for part in command)
    ):
        raise ValueError("Hook command must be a string or argv list")
    timeout_seconds = float(hook.get("timeout_seconds", 30))
    if timeout_seconds < 0:
        raise ValueError("Hook timeout_seconds must not be negative")
    failure_policy = str(hook.get("failure_policy", "warn")).lower()
    if failure_policy not in {"warn", "block", "ignore"}:
        raise ValueError("Hook failure_policy must be warn, block, or ignore")
    return command, timeout_seconds, failure_policy


def _hook_command_name(command: str | list[str]) -> str:
    if isinstance(command, list):
        return Path(command[0]).name if command else "<empty>"
    return command.strip().split(maxsplit=1)[0] if command.strip() else "<empty>"


def _terminate_process_tree(process: object) -> None:
    import os
    import signal
    import subprocess

    if os.name == "nt":
        subprocess.run(
            ["taskkill", "/PID", str(process.pid), "/T", "/F"],
            capture_output=True,
            check=False,
        )
    else:
        os.killpg(process.pid, signal.SIGTERM)


def _handle_hook_failure(
    event: str,
    command_name: str,
    reason: str,
    failure_policy: str,
    colored: object,
    colors: object,
) -> None:
    message = f"Hook {command_name} for {event} {reason}."
    if failure_policy == "block":
        raise RuntimeError(message)
    if failure_policy == "warn":
        print(colored(f"[WARN] {message}", colors.YELLOW), file=sys.stderr)


def run_task_hooks(event: str, task_json_path: Path, repo_root: Path) -> None:
    """Run lifecycle hooks with an explicit bounded failure contract."""
    import os
    import subprocess

    from .config import get_hooks
    from .log import Colors, colored

    hooks = get_hooks(event, repo_root)
    if not hooks:
        return

    env = {**os.environ, "TASK_JSON_PATH": str(task_json_path)}
    for hook in hooks:
        command, timeout_seconds, failure_policy = _normalize_hook(hook)
        command_name = _hook_command_name(command)
        try:
            process = subprocess.Popen(
                command,
                shell=isinstance(command, str),
                cwd=repo_root,
                env=env,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
                encoding="utf-8",
                errors="replace",
                start_new_session=os.name != "nt",
                creationflags=(subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0),
            )
            try:
                process.communicate(timeout=None if timeout_seconds == 0 else timeout_seconds)
                if process.returncode == 0:
                    continue
                reason = f"exited with status {process.returncode}"
            except subprocess.TimeoutExpired:
                _terminate_process_tree(process)
                process.communicate()
                reason = "timed out"
        except OSError:
            reason = "could not start"

        _handle_hook_failure(event, command_name, reason, failure_policy, colored, Colors)


# =============================================================================
# Main Entry (for testing)
# =============================================================================

if __name__ == "__main__":
    repo = get_repo_root()
    tasks = get_tasks_dir(repo)

    print(f"Tasks dir: {tasks}")
    print(f"is_safe_task_path('.trellis/tasks/test'): {is_safe_task_path('.trellis/tasks/test', repo)}")
    print(f"is_safe_task_path('../test'): {is_safe_task_path('../test', repo)}")
