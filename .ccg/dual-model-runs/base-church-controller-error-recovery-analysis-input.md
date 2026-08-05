# BaseChurchController error-recovery analysis

Review the current `SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs` implementation and its MVC usage.

User-observed evidence:

- A real F5 request reached `BaseChurchController.HandleError` line 365.
- The original exception was masked by `System.NullReferenceException` on `TempData["ErrorMessage"] = exception.Message`.
- The file contains already-persisted mojibake comments (`?` replacement characters); the file bytes are valid UTF-8 without BOM and CRLF, so this is corrupted text content, not an editor encoding preference.

Constraints:

- Do not change Dynamics/CE, SQL, IIS, DNS, ADFS, IFD, CRMWeb, or Web API behavior.
- Error handling must never leak exception details, Session state, user identity, credential, token, connection, or other mutable data.
- Normal MVC non-AJAX requests should still redirect safely to `Home/DisplayErrorView` without embedding an unbounded error in route values.
- If TempData is unavailable, fail safely without masking the original exception. No process-wide/static fallback cache is acceptable.
- Newly/substantively modified C# must use deep Traditional Chinese documentation and UTF-8 without BOM, CRLF, final CRLF.
- Prefer TDD and minimal behavior change. Identify required tests and any risk to controller lifecycle/resource cleanup.

OUTPUT: Critical / Warning / Info findings, root-cause assessment, smallest safe fix design, and test plan.
