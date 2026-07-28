ROLE: reviewer
TASK: Review the Phase 1 live no-SDK Dynamics Web API implementation changes.

Scope:
- SpeechMessage.Dynamics.WebApi live WhoAmI + Package 1 fee-read HTTP client
- Auth modes Windows/AdfsOAuth with secret references only
- ApprovedWebApiRoot validation
- Server-owned FetchXML templates
- Unit tests with fake HttpMessageHandler
- Program.cs UTF-8 comment repair

Constraints to enforce:
- No Microsoft CRM SDK / WCF / WS-Trust references
- No per-user CRM session pooling
- No free-form FetchXML from callers
- No plaintext secrets in JSON
- Products must not reference WebApi project

Please report Critical / Warning / Info findings only.
Focus on correctness, security, session/memory leak risk, and encoding safety.

Diffstat:


Key files:
- SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs
- SpeechMessage.Dynamics.WebApi/Runtime/DynamicsHttpTransport.cs
- SpeechMessage.Dynamics.WebApi/Runtime/Package01ServerOwnedTemplates.cs
- SpeechMessage.Dynamics.WebApi/Runtime/ApprovedWebApiRootFactory.cs
- SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs