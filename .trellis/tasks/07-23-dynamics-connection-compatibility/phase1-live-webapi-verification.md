# Phase 1 live Web API verification

Date: 2026-07-25

## Done in this step
- Fixed garbled UTF-8 comments in `SpeechMessage.Dynamics.Gateway/Program.cs`.
- Implemented no-SDK live HTTP execution:
  - `ApprovedWebApiRootFactory` (https-only, no user-info/query/fragment, v8.2/v9.1)
  - `DynamicsHttpTransport` (long-lived SocketsHttpHandler + HttpClient; no cookies; no proxy; no per-user session)
  - `ISecretResolver` + environment/dictionary resolvers
  - Auth modes: Windows HostIdentity / Windows SecretReference / AdfsOAuth bearer-from-secret-ref
  - Package 0/1 server-owned templates and FetchXML value encoding
  - Live `DynamicsWebApiClient` for WhoAmI + fee/lesson FetchXML reads + option-set OData route binding
- Unit tests expanded to 21 passing cases, including fake-handler WhoAmI and fee FetchXML encoding.
- Smoke placeholder still env-gated and passing by default.

## Verification
- `dotnet test SpeechMessage.Dynamics.Tests` => 21 passed
- `dotnet test SpeechMessage.Dynamics.SmokeTests --no-restore` => 1 passed
- New `SpeechMessage.Dynamics.*` projects: 0 no-SDK scanner hits
- Repo-wide scanner still report-only with legacy findings outside new projects

## Not done yet
- Consumer migration (ChurchReport)
- Organization admission / capacity runtime
- Delete `PowerPlatform.Dataverse.Client`
- Real CE 8.2/9.1 live smoke against lab VMs