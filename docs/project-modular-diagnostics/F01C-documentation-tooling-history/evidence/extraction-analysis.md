# F01C Extraction Analysis

Status: COMPLETE
Mode: STATIC_READ_ONLY

## Confirmed Finding

### F01C-EXT-001 DOCX Generation Is Duplicated And Bound To One Workstation

- Confirmed: true
- Primary owner: F01C
- Owning files:
  - `tools/generate_vs2026_git_guide.py:9`
  - `tools/generate_vs2026_git_guide.py:13`
  - `tools/generate_vs2026_git_guide.py:49`
  - `tools/generate_vs2026_git_guide.py:245`
  - `tools/generate_vs2026_ide_steps_doc.py:9`
  - `tools/generate_vs2026_ide_steps_doc.py:13`
  - `tools/generate_vs2026_ide_steps_doc.py:49`
  - `tools/generate_vs2026_ide_steps_doc.py:183`
  - `tools/merge_vs2026_client_version_docs.py:11`
  - `tools/merge_vs2026_client_version_docs.py:12`
  - `tools/merge_vs2026_client_version_docs.py:18`
  - `tools/merge_vs2026_client_version_docs.py:358`
- Cohesive responsibility: Create styled Traditional-Chinese Word documents,
  add text/list/code primitives, merge source documents, and save a declared
  artifact.
- Duplication evidence: Lines 13 through 61 are identical in both generator
  scripts: 49 of 49 lines at the same positions. They independently define the
  same font, paragraph, bullet, numbered-list, code-block, and base-style
  behavior. A style correction must be copied to both files and the merge
  script contains another related style implementation.
- Machine binding: The merge script fixes all inputs and output beneath a
  literal `E:\電子書籍\改善 GitHub 多客戶版本上線追蹤` path. The two
  generators derive repository output paths but execute document construction
  and `doc.save(...)` at import/module level rather than through a callable CLI.
- Consumer boundary: The three scripts and tracked tutorial DOCX outputs are
  consumers. Future tutorial sources should provide content/configuration, not
  private copies of rendering mechanics.
- Dependency boundary: `python-docx`, filesystem inputs, font/style policy, and
  output manifest. The helper should not own tutorial prose.
- Proposed clean module:
  - `docx_rendering` library: style setup, paragraph/list/code helpers, merge
    primitives, deterministic metadata, and validation hooks.
  - CLI adapter: explicit `--input`, `--output`, and optional manifest/config
    paths; no write on import.
  - Content modules: tutorial-specific prose and source order.
  - Manifest: source hashes, output path, generator version, generated date,
    and expected packaged artifacts.
- Why extraction is necessary: The shared behavior is already copied across
  executable owners, while one workflow cannot run outside the original drive.
  This is a real responsibility and portability boundary, not file movement.
- Validation: Importing each module creates no file. Fixture content rendered
  through the shared library has expected styles and section order. CLI paths
  work from a temporary directory without drive-specific assumptions. Manifest
  hashes explain whether checked-in DOCX outputs are current.
- Rollback boundary: Introduce the helper and CLI behind existing script names;
  migrate one generator at a time; retain previous generated documents until
  visual comparison is accepted.

## Candidate Seam Map

| Input | Shared contract | Output | Consumers |
|---|---|---|---|
| tutorial content/config | paragraph/list/code/style API | `Document` | two generator scripts |
| ordered DOCX paths | merge API preserving sections/styles | merged `Document` | merge script |
| CLI arguments + manifest | validated path/output contract | DOCX + provenance record | operators and packaged tutorials |

## Counter-Evidence And Rejected Candidates

- The two full scripts are not 98% identical. Whole-file, same-position
  comparison is only 72 matching lines because their tutorial prose and lengths
  differ. The retained extraction is based on the exact 49-line shared helper
  block and repeated style responsibility.
- Separate tutorial content is appropriate and should remain separate.
- A general repository-wide document platform is not justified. The proposed
  boundary is limited to the three F01C scripts and their tutorial artifacts.
- The `.ccg/tasks/**/docx-generator/**` exception uses another technology and
  is not automatically included in this extraction without a later contract
  comparison.
