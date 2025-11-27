dotnet publish -c Release -r win-x64 ^
    /p:PublishTrimmed=true ^
    -o "./bin/Output-Trimmed-Release"

pause