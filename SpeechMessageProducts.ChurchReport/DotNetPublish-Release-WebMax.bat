dotnet publish -c Release -r win-x64 --self-contained false ^
 /p:PublishReadyToRun=true ^
 /p:ReadyToRunUseCrossgen2=true ^
 /p:PublishReadyToRunComposite=false ^
 /p:TieredCompilation=true ^
 /p:TieredCompilationQuickJit=true ^
 /p:TieredPGO=true ^
 /p:OptimizationPreference=Speed ^
 /p:IlcOptimizationPreference=Speed ^
 /p:IlcOptimizationData=true ^
 /p:PublishTrimmed=false ^
 /p:DebugType=None ^
 /p:DebugSymbols=false ^
 /p:PublishSingleFile=false ^
 /p:IncludeNativeLibrariesForSelfExtract=false ^
 -o "./bin/Output-Release-WebMax"
pause

