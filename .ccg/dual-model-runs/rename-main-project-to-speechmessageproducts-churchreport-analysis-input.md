# Task
Analyze a scoped project rename in the SpeechMessageProducts repository.

# User request
Rename the main web project from `ChurchReport` to `SpeechMessageProducts.ChurchReport`.

# Intended scope
- Rename folder: `ChurchReport` -> `SpeechMessageProducts.ChurchReport`
- Rename project file: `ChurchReport.csproj` -> `SpeechMessageProducts.ChurchReport.csproj`
- Update `SpeechMessageProducts.sln` project display name and path
- Update main project assembly identity to `SpeechMessageProducts.ChurchReport`
- Update direct project references from test projects and related projects if they reference the old csproj path
- Build `SpeechMessageProducts.sln`

# Non-goals
- Do not bulk replace all namespaces or types containing `ChurchReport`
- Do not rename test projects in this slice
- Do not change cookie/auth/session, Dataverse, LINE, payment, deployment, or runtime identifiers

# Current known context
- Repo path: D:\音訊科技產品\系統平台\SpeechMessageProducts
- Current branch: main
- Remote: https://github.com/MengSung/SpeechMessageProducts.git
- Solution: SpeechMessageProducts.sln
- Current main project: ChurchReport\ChurchReport.csproj
- Current main assembly name in csproj: ChurchReport

# Required output
Give implementation risks and a concrete checklist. Focus on references likely to break after folder/csproj rename and validation commands. Do not modify files.
