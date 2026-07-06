dotnet publish -c Release -r win-x64 --self-contained true ^
  /p:PublishAot=true ^
  /p:DebugType=None ^
  -o "./bin/Output-SelfContained-Release-AOT"

pause