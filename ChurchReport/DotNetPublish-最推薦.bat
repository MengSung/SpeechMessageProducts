Bash

dotnet publish -c Release -r win-x64 --self-contained true ^
  /p:PublishSingleFile=true ^
  /p:IncludeNativeLibrariesForSelfExtract=true ^
  /p:PublishReadyToRun=true ^
  /p:PublishTrimmed=false ^
  /p:PublishAot=true  ^
  /p:DebugType=None ^
  -o "./bin/Output-最推薦"
pause