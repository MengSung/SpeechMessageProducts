dotnet publish -c Release -r win-x64 ^
  /p:PublishAot=true ^
  /p:DebugType=None ^
  -o "./bin/Output-SelfContained-Release-AOT"

pause