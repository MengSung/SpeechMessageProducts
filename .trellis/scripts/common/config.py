#!/usr/bin/env python3
"""
Trellis configuration reader.

Reads settings from .trellis/config.yaml with sensible defaults.
"""

from __future__ import annotations

import sys
from pathlib import Path

from .paths import DIR_WORKFLOW, get_repo_root


# =============================================================================
# YAML Simple Parser (no dependencies)
# =============================================================================


def _unquote(s: str) -> str:
    """Remove exactly one layer of matching surrounding quotes.

    Unlike str.strip('"'), this only removes the outermost pair,
    preserving any nested quotes inside the value.

    Examples:
        _unquote('"hello"')        -> 'hello'
        _unquote("'hello'")        -> 'hello'
        _unquote('"echo \\'hi\\'"')  -> "echo 'hi'"
        _unquote('hello')          -> 'hello'
        _unquote('"hello\\'')       -> '"hello\\''  (mismatched, unchanged)
    """
    if len(s) >= 2 and s[0] == s[-1] and s[0] in ('"', "'"):
        return s[1:-1]
    return s


def _strip_inline_comment(value: str) -> str:
    """Strip ` # …` inline comments while preserving `#` inside quoted strings.

    YAML treats ` #` (space-hash) as a comment opener; bare `#` inside a token
    is part of the value. Quoted strings are immune.

    Mirrors :func:`common.trellis_config._strip_inline_comment` so both
    parsers handle ``key: value  # comment`` identically.
    """
    in_quote: str | None = None
    for idx, ch in enumerate(value):
        if in_quote:
            if ch == in_quote:
                in_quote = None
            continue
        if ch in ('"', "'"):
            in_quote = ch
            continue
        if ch == "#" and (idx == 0 or value[idx - 1].isspace()):
            return value[:idx]
    return value


def parse_simple_yaml(content: str) -> dict:
    """Parse simple YAML with nested dict support (no dependencies).

    Supports:
        - key: value (string)
        - key: (followed by list items)
            - item1
            - item2
        - key: (followed by nested dict)
            nested_key: value
            nested_key2:
              - item

    Uses indentation to detect nesting (2+ spaces deeper = child).

    Args:
        content: YAML content string.

    Returns:
        Parsed dict (values can be str, list[str], or dict).
    """
    lines = content.splitlines()
    first_index, first_line = _next_content_line(lines, 0)
    if first_index >= len(lines):
        return {}

    first_indent = len(first_line) - len(first_line.lstrip())
    parsed, _ = _parse_yaml_node(lines, first_index, first_indent)
    return parsed if isinstance(parsed, dict) else {}


def _parse_yaml_node(lines: list[str], start: int, indent: int) -> tuple[object, int]:
    """Parse one supported YAML mapping or list block."""
    _, line = _next_content_line(lines, start)
    if line.strip().startswith("- "):
        return _parse_yaml_list(lines, start, indent)
    return _parse_yaml_mapping(lines, start, indent)


def _parse_yaml_mapping(
    lines: list[str], start: int, indent: int
) -> tuple[dict, int]:
    """Parse mapping entries at one indentation level."""
    result: dict = {}
    i = start
    while True:
        i, line = _next_content_line(lines, i)
        if i >= len(lines):
            return result, i

        current_indent = len(line) - len(line.lstrip())
        stripped = line.strip()
        if current_indent != indent or stripped.startswith("- ") or ":" not in stripped:
            return result, i

        key, _, raw_value = stripped.partition(":")
        value = _unquote(_strip_inline_comment(raw_value).strip())
        if value:
            result[key.strip()] = value
            i += 1
            continue

        next_i, next_line = _next_content_line(lines, i + 1)
        if next_i >= len(lines):
            result[key.strip()] = {}
            return result, next_i
        next_indent = len(next_line) - len(next_line.lstrip())
        if next_indent <= current_indent:
            result[key.strip()] = {}
            i += 1
            continue

        child, i = _parse_yaml_node(lines, next_i, next_indent)
        result[key.strip()] = child


def _parse_yaml_list(
    lines: list[str], start: int, indent: int
) -> tuple[list, int]:
    """Parse scalar and mapping list entries at one indentation level."""
    result: list = []
    i = start
    while True:
        i, line = _next_content_line(lines, i)
        if i >= len(lines):
            return result, i

        current_indent = len(line) - len(line.lstrip())
        stripped = line.strip()
        if current_indent != indent or not stripped.startswith("- "):
            return result, i

        item_text = _strip_inline_comment(stripped[2:]).strip()
        if ":" not in item_text or item_text.startswith(("'", '\"')):
            result.append(_unquote(item_text))
            i += 1
            continue

        key, _, raw_value = item_text.partition(":")
        item: dict = {}
        value = _unquote(_strip_inline_comment(raw_value).strip())
        i += 1
        if value:
            item[key.strip()] = value
        else:
            next_i, next_line = _next_content_line(lines, i)
            if next_i < len(lines):
                next_indent = len(next_line) - len(next_line.lstrip())
                if next_indent > current_indent:
                    child, i = _parse_yaml_node(lines, next_i, next_indent)
                    item[key.strip()] = child
                else:
                    item[key.strip()] = {}
            else:
                item[key.strip()] = {}

        continuation_indent = current_indent + 2
        next_i, next_line = _next_content_line(lines, i)
        if next_i < len(lines):
            next_indent = len(next_line) - len(next_line.lstrip())
            if next_indent == continuation_indent and not next_line.strip().startswith("- "):
                continuation, i = _parse_yaml_mapping(lines, next_i, continuation_indent)
                item.update(continuation)
        result.append(item)


def _next_content_line(lines: list[str], start: int) -> tuple[int, str]:
    """Find the next non-empty, non-comment line."""
    i = start
    while i < len(lines):
        stripped = lines[i].strip()
        if stripped and not stripped.startswith("#"):
            return i, lines[i]
        i += 1
    return i, ""


# Defaults
DEFAULT_SESSION_COMMIT_MESSAGE = "chore: record journal"
DEFAULT_MAX_JOURNAL_LINES = 2000
DEFAULT_SESSION_AUTO_COMMIT = True
DEFAULT_HOOK_TIMEOUT_SECONDS = 30.0
DEFAULT_HOOK_FAILURE_POLICY = "warn"

CONFIG_FILE = "config.yaml"


def _is_true_config_value(value: object) -> bool:
    """Return True when a config value represents an enabled flag."""
    if isinstance(value, bool):
        return value
    if isinstance(value, str):
        return value.strip().lower() == "true"
    return False


def _get_config_path(repo_root: Path | None = None) -> Path:
    """Get path to config.yaml."""
    root = repo_root or get_repo_root()
    return root / DIR_WORKFLOW / CONFIG_FILE


def _load_config(repo_root: Path | None = None) -> dict:
    """Load and parse config.yaml. Returns empty dict on any error."""
    config_file = _get_config_path(repo_root)
    try:
        content = config_file.read_text(encoding="utf-8")
        return parse_simple_yaml(content)
    except (OSError, IOError):
        return {}


def get_session_commit_message(repo_root: Path | None = None) -> str:
    """Get the commit message for auto-committing session records."""
    config = _load_config(repo_root)
    return config.get("session_commit_message", DEFAULT_SESSION_COMMIT_MESSAGE)


def get_max_journal_lines(repo_root: Path | None = None) -> int:
    """Get the maximum lines per journal file."""
    config = _load_config(repo_root)
    value = config.get("max_journal_lines", DEFAULT_MAX_JOURNAL_LINES)
    try:
        return int(value)
    except (ValueError, TypeError):
        return DEFAULT_MAX_JOURNAL_LINES


def get_session_auto_commit(repo_root: Path | None = None) -> bool:
    """Whether scripts should auto-stage + auto-commit session/task changes.

    Governs both ``add_session.py:_auto_commit_workspace`` and
    ``task_store.py:_auto_commit_archive``.

    Default: ``True`` (existing behavior — auto-stage + auto-commit).
    Set ``session_auto_commit: false`` in ``.trellis/config.yaml`` to skip
    auto-staging entirely; the journal/archive files are still written to
    disk, but the user manages ``git add`` / ``git commit`` themselves.

    Accepts native YAML booleans (``true`` / ``false``) and the string
    aliases ``true / false / yes / no / 1 / 0 / on / off`` (case-insensitive).
    Invalid values fall back to ``True`` with a stderr warning.
    """
    config = _load_config(repo_root)
    raw = config.get("session_auto_commit", DEFAULT_SESSION_AUTO_COMMIT)
    if isinstance(raw, bool):
        return raw
    s = str(raw).strip().lower()
    if s in ("true", "yes", "1", "on"):
        return True
    if s in ("false", "no", "0", "off"):
        return False
    print(
        f"[WARN] invalid session_auto_commit value: {raw!r}; using true (default)",
        file=sys.stderr,
    )
    return DEFAULT_SESSION_AUTO_COMMIT


def get_hooks(event: str, repo_root: Path | None = None) -> list[dict[str, object]]:
    """Get normalized hook specifications for a lifecycle event.

    Args:
        event: Event name (e.g. "after_create", "after_archive").
        repo_root: Repository root path.

    Returns:
        A list of ``command``, ``timeout_seconds``, and ``failure_policy``
        mappings, or an empty list if no hook is configured.
    """
    config = _load_config(repo_root)
    hooks = config.get("hooks")
    if not isinstance(hooks, dict):
        return []

    try:
        default_timeout = float(
            hooks.get("default_timeout_seconds", DEFAULT_HOOK_TIMEOUT_SECONDS)
        )
    except (TypeError, ValueError):
        default_timeout = DEFAULT_HOOK_TIMEOUT_SECONDS
    if default_timeout < 0:
        default_timeout = DEFAULT_HOOK_TIMEOUT_SECONDS

    default_policy = str(
        hooks.get("default_failure_policy", DEFAULT_HOOK_FAILURE_POLICY)
    ).lower()
    if default_policy not in {"warn", "block", "ignore"}:
        default_policy = DEFAULT_HOOK_FAILURE_POLICY

    configured = hooks.get(event)
    if isinstance(configured, dict):
        configured = [configured]
    if not isinstance(configured, list):
        return []

    normalized: list[dict[str, object]] = []
    for hook in configured:
        if isinstance(hook, str):
            normalized.append(
                {
                    "command": hook,
                    "timeout_seconds": default_timeout,
                    "failure_policy": DEFAULT_HOOK_FAILURE_POLICY,
                }
            )
            continue
        if not isinstance(hook, dict):
            continue

        command = hook.get("command")
        if not isinstance(command, str) and not (
            isinstance(command, list) and all(isinstance(part, str) for part in command)
        ):
            continue
        try:
            timeout_seconds = float(hook.get("timeout_seconds", default_timeout))
        except (TypeError, ValueError):
            timeout_seconds = default_timeout
        failure_policy = str(
            hook.get("failure_policy", default_policy)
        ).lower()
        normalized.append(
            {
                "command": command,
                "timeout_seconds": timeout_seconds,
                "failure_policy": failure_policy,
            }
        )
    return normalized


# =============================================================================
# Monorepo / Packages
# =============================================================================


def get_packages(repo_root: Path | None = None) -> dict[str, dict] | None:
    """Get monorepo package declarations.

    Returns:
        Dict mapping package name to its config (path, type, etc.),
        or None if not configured (single-repo mode).

    Example return:
        {"cli": {"path": "packages/cli"}, "docs-site": {"path": "docs-site", "type": "submodule"}}
    """
    config = _load_config(repo_root)
    packages = config.get("packages")
    if not isinstance(packages, dict):
        return None
    # Ensure each value is a dict (filter out scalar entries)
    filtered = {k: v for k, v in packages.items() if isinstance(v, dict)}
    if not filtered:
        return None
    return filtered


def get_default_package(repo_root: Path | None = None) -> str | None:
    """Get the default package name from config.

    Returns:
        Package name string, or None if not configured.
    """
    config = _load_config(repo_root)
    value = config.get("default_package")
    return str(value) if value else None


def get_submodule_packages(repo_root: Path | None = None) -> dict[str, str]:
    """Get packages that are git submodules.

    Returns:
        Dict mapping package name to its path for submodule-type packages.
        Empty dict if none configured.

    Example return:
        {"docs-site": "docs-site"}
    """
    packages = get_packages(repo_root)
    if packages is None:
        return {}
    return {
        name: cfg.get("path", name)
        for name, cfg in packages.items()
        if cfg.get("type") == "submodule"
    }


def get_git_packages(repo_root: Path | None = None) -> dict[str, str]:
    """Get packages that have their own independent git repository.

    These are sub-directories with their own .git (not submodules),
    marked with ``git: true`` in config.yaml.

    Returns:
        Dict mapping package name to its path for git-repo packages.
        Empty dict if none configured.

    Example config::

        packages:
          backend:
            path: iqs
            git: true

    Example return::

        {"backend": "iqs"}
    """
    packages = get_packages(repo_root)
    if packages is None:
        return {}
    return {
        name: cfg.get("path", name)
        for name, cfg in packages.items()
        if _is_true_config_value(cfg.get("git"))
    }


def is_monorepo(repo_root: Path | None = None) -> bool:
    """Check if the project is configured as a monorepo (has packages in config)."""
    return get_packages(repo_root) is not None


def get_spec_base(package: str | None = None, repo_root: Path | None = None) -> str:
    """Get the spec directory base path relative to .trellis/.

    Single-repo: returns "spec"
    Monorepo with package: returns "spec/<package>"
    Monorepo without package: returns "spec" (caller should specify package)
    """
    if package and is_monorepo(repo_root):
        return f"spec/{package}"
    return "spec"


def validate_package(package: str, repo_root: Path | None = None) -> bool:
    """Check if a package name is valid in this project.

    Single-repo (no packages configured): always returns True.
    Monorepo: returns True only if package exists in config.yaml packages.
    """
    packages = get_packages(repo_root)
    if packages is None:
        return True  # Single-repo, no validation needed
    return package in packages


def resolve_package(
    task_package: str | None = None,
    repo_root: Path | None = None,
) -> str | None:
    """Resolve package from inferred sources with validation.

    Checks in order: task_package → default_package.
    Invalid inferred values print a warning to stderr and are skipped.

    Returns:
        Resolved package name, or None if no valid package found.

    Note:
        CLI --package should be validated separately by the caller
        (fail-fast with available packages list on error).
    """
    packages = get_packages(repo_root)
    if packages is None:
        return None  # Single-repo, no package needed

    # Try task_package (guard against non-string values from malformed JSON)
    if task_package and isinstance(task_package, str):
        if task_package in packages:
            return task_package
        print(
            f"Warning: task.json package '{task_package}' not found in config, skipping",
            file=sys.stderr,
        )

    # Try default_package
    default = get_default_package(repo_root)
    if default:
        if default in packages:
            return default
        print(
            f"Warning: default_package '{default}' not found in config, skipping",
            file=sys.stderr,
        )

    return None


def get_spec_scope(repo_root: Path | None = None) -> list[str] | str | None:
    """Get session.spec_scope configuration.

    Returns:
        list[str]: Package names to include in spec scanning.
        str: "active_task" to use current task's package.
        None: No scope configured (scan all packages).
    """
    config = _load_config(repo_root)
    session = config.get("session")
    if not isinstance(session, dict):
        return None

    scope = session.get("spec_scope")
    if scope is None:
        return None
    if isinstance(scope, str):
        return scope  # e.g. "active_task"
    if isinstance(scope, list):
        return [str(s) for s in scope]
    return None
