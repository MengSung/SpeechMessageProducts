# X04B Security Analysis

Module: X04B
Mode: DIAGNOSIS_ONLY

## Security Findings

### Package Source Reproducibility And Private Path Exposure

`SpeechMessageProducts.ChurchReport/NuGet.config` points package source `devextreme-controls-netcore` at `C:\Program Files (x86)\DevExpress 19.1\DevExtreme\System\DevExtreme\Bin\AspNetCore`. `NuGet.config.bak` points the same key at the 18.2 path.

Security impact:

- The repository exposes private workstation/package layout assumptions.
- Restore behavior depends on a local absolute path instead of an authenticated, documented package feed.
- The active file and backup file disagree, making accidental downgrade or stale package-source use plausible.

No credentials or token values were found in these NuGet source files during targeted text inspection. The risk is package provenance and local-path exposure rather than immediate secret leakage.

### Development Launch Settings Are Publish Content

`SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj:55` includes `Properties/launchSettings.json` as content. The launch settings file contains Development profiles, local HTTP URLs, IIS Express settings, anonymous authentication, and `sslPort: 0`.

Security impact:

- Development-only runtime hints can be copied to publish output because they are explicitly included as content.
- The file is not a production secret, but it is unsafe packaged metadata because it normalizes Development environment settings next to release output.
- It increases the chance that deployment operators inspect or copy the wrong environment assumptions.

### Publish Scripts Mention Secret-Backed Configuration In Manual Checklists

`SpeechMessageProducts.ChurchReport/DotNetPublish-Release-Deploy-Official-Production.bat` prints deployment checklist items for Dynamics 365 connection strings, Line Notify token, web.config Production environment, CRM URL, account/password, and firewall settings.

Security impact:

- The script does not embed those values.
- The checklist correctly avoids literal secrets, but it does not perform an automated artifact audit to prove secrets are absent from publish output.
- X04A remains the owner of runtime secret values; X04B should own the release artifact audit that validates no packaged file contains forbidden secret-like keys or development config.

## Secret Scan Scope

Targeted search covered X04B-owned launch settings, NuGet config files, publish scripts, `.pubxml` candidates, and `verify-release-noperf.ps1` using patterns for password, token, secret, API key, connection string, package source, and debug/development hints.

Confirmed:

- No literal token/password/API key values were identified in X04B package-source files.
- Local absolute package source paths were identified.
- Development launch settings are included in project content.
- No automated release artifact allowlist/denylist was found in X04B-owned files.

## Security Issue Candidates

Retained:

- X04B-SEC-001: make package sources reproducible and remove stale local-path backup ambiguity.
- X04B-SEC-002: exclude or gate development launch settings from publish artifacts.
- X04B-SEC-003: add automated publish artifact audit for secret-like keys, debug config, development launch metadata, private paths, and overbroad content.

Rejected:

- Literal secret leakage from X04B-owned files. Evidence did not show committed token or password values in this scope.
