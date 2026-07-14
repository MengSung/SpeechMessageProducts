# X04A Extraction Analysis

## Cohesive Extraction Candidate: Runtime Config Contract

Candidate module:

- `RuntimeConfigurationSecrets` or `ChurchReport.ConfigurationValidation`

Responsibility:

- Define config schema for LINE, CRM, payment providers, security flags, callback URLs, and diagnostics.
- Validate environment-specific requirements at startup.
- Classify keys as secret, public metadata, endpoint, or operational flag.
- Provide secret-source policy without owning business decisions.

Input contract:

- Effective `IConfiguration`.
- Host environment name.
- Validation mode: local development, test, production, deployment smoke.

Output contract:

- Validation result with severity, key path, owner module, environment, and remediation.
- Redacted diagnostic summary for logs and CI.
- Machine-readable list of required secret names for deployment.

## Automation Candidate: Secret And Drift Scanner

Batchable scanner rules:

- Reject committed values for keys matching `*Secret`, `*Password`, `*Token`, `*Key`, `*IV`, `A1`, `A2`, `B1`, `B2`, `XKeyID`, and `Credentials:*`.
- Allow explicit non-secret metadata keys such as `ChannelId`, public callback URLs, and public LIFF IDs.
- Reject known placeholders such as `YOUR_*`, `your_store_key`, and `your_store_iv` in production effective configuration.
- Compare base and environment override files to ensure Production overrides every required environment-sensitive key.
- Detect product runtime paths that construct ad hoc `ConfigurationBuilder` instances and read `appsettings.json` outside host configuration.
- Emit only key names and hashes, never raw values.

## Extraction Boundaries

X04A should not extract:

- Payment provider protocol behavior, signature construction, callback handling, or order workflow. Those belong to F08/F09/B05.
- LINE messaging SDK behavior. That belongs to F04-F07/B07.
- Auth/session runtime behavior. That belongs to B01.
- Deployment scripts and publish packaging. Those belong to X04B.

## Test Seam

The strongest seam is pure config validation:

- Given a JSON config fixture and environment, validation returns deterministic findings.
- No CRM, LINE, payment gateway, HTTP, or database calls are needed.
- CI can run the scanner without building deployment artifacts.

## Loop Leverage

This extraction is high leverage because every downstream module consumes X04A configuration. A single config contract can prevent repeated secret leakage, placeholder drift, direct `appsettings.json` reads, and environment mismatch across B01, B05, B07, F08/F09, and X01 startup.
