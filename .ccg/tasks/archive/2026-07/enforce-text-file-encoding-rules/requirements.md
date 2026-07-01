# Requirements

## User Report

The user reported that files modified by the assistant often trigger Visual Studio warnings like inconsistent line endings or file format problems.

## Root Cause

- `.editorconfig` only covered `*.cs` files.
- `.editorconfig` also requested `utf-8-bom`, while recent cleanup work expected UTF-8 without BOM.
- Frequently edited files such as `.cshtml`, `.css`, `.json`, `.md`, and `.ps1` were not covered by an editor rule.
- `.gitattributes` used only `text=auto`, so checkout behavior could depend on local Git settings.

## Acceptance Criteria

- Main project text files declare UTF-8 without BOM in `.editorconfig`.
- Main project text files declare CRLF checkout behavior in `.gitattributes`.
- Modified text files pass byte-level checks for `UTF8BOM=False` and `LFOnly=0`.
- `git diff --check` returns no whitespace or line-ending errors.
