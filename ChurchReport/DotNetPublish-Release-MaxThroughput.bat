dotnet publish -c Release -r win-x64 --self-contained false ^
    /p:PublishSingleFile=false ^
    /p:IncludeNativeLibrariesForSelfExtract=false ^
    /p:PublishReadyToRun=true ^
    /p:PublishReadyToRunComposite=false ^
    /p:PublishTrimmed=false ^
    /p:TieredCompilation=true ^
    /p:TieredCompilationQuickJit=true ^
    /p:TieredPGO=true ^
    /p:OptimizationPreference=Speed ^
    /p:DebugType=None ^
    /p:ReadyToRunUseCrossgen2=true ^
    /p:DebugSymbols=false ^
-o "./bin/Output-Release-MaxThroughput"

pause

