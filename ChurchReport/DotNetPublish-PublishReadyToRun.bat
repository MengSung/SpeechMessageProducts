dotnet publish -c Release -r win-x64 --self-contained false ^
  /p:PublishSingleFile=false ^
  /p:IncludeNativeLibrariesForSelfExtract=false ^
  /p:PublishReadyToRun=true ^
  /p:PublishTrimmed=false ^
  /p:PublishAot=false ^
  /p:DebugType=None ^
  -o "./bin/Output-Release-PublishReadyToRun"

pause