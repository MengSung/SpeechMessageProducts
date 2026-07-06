# Rename Main Project To SpeechMessageProducts.ChurchReport

## Requirement

Rename the main web project from `ChurchReport` to `SpeechMessageProducts.ChurchReport` in the new `SpeechMessageProducts` repository.

## Scope

- Rename project folder: `ChurchReport` -> `SpeechMessageProducts.ChurchReport`
- Rename project file: `ChurchReport.csproj` -> `SpeechMessageProducts.ChurchReport.csproj`
- Update `SpeechMessageProducts.sln` project display name and project path
- Update main project assembly identity to `SpeechMessageProducts.ChurchReport`
- Build `SpeechMessageProducts.sln`

## Non-Goals

- Do not rename test projects in this slice.
- Do not bulk replace every namespace or type containing `ChurchReport`.
- Do not change cookie, auth, Dataverse, LINE, payment, or deployment identifiers.
