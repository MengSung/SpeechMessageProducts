# ========================================
# Trace 專案升級到 .NET 10 自動化腳本
# ========================================

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Trace 專案升級到 .NET 10" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 設定路徑
$SolutionRoot = "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport"
$TraceProject = Join-Path $SolutionRoot "Trace"
$TraceCsproj = Join-Path $TraceProject "Trace.csproj"
$BackupFile = Join-Path $TraceProject "Trace.csproj.backup"

# 檢查專案目錄是否存在
if (-not (Test-Path $TraceProject)) {
    Write-Host "? 錯誤: 找不到 Trace 專案目錄" -ForegroundColor Red
    Write-Host "   路徑: $TraceProject" -ForegroundColor Yellow
    exit 1
}

Write-Host "? 找到 Trace 專案目錄" -ForegroundColor Green
Write-Host "   路徑: $TraceProject" -ForegroundColor Gray
Write-Host ""

# 備份原始專案檔案
Write-Host "[步驟 1/5] 備份原始專案檔案..." -ForegroundColor Cyan
if (Test-Path $TraceCsproj) {
    Copy-Item $TraceCsproj $BackupFile -Force
    Write-Host "? 已備份至: $BackupFile" -ForegroundColor Green
} else {
    Write-Host "??  找不到原始專案檔案: $TraceCsproj" -ForegroundColor Yellow
}
Write-Host ""

# 創建新的 SDK-Style 專案檔案
Write-Host "[步驟 2/5] 創建新的 SDK-Style 專案檔案..." -ForegroundColor Cyan

$NewCsprojContent = @"
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <!-- ========================================
         專案基本設定
         ======================================== -->
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>TraceNameSpace</RootNamespace>
    <AssemblyName>Trace</AssemblyName>
    <LangVersion>14.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    
    <!-- ========================================
         輸出設定
         ======================================== -->
    <OutputType>Library</OutputType>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    
    <!-- ========================================
         簽章設定 (Strong Name)
         ======================================== -->
    <SignAssembly>true</SignAssembly>
    <AssemblyOriginatorKeyFile>SpeechMessageCrmKey.snk</AssemblyOriginatorKeyFile>
    <DelaySign>false</DelaySign>
    
    <!-- ========================================
         文件設定
         ======================================== -->
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <DocumentationFile>`$(OutputPath)`$(AssemblyName).xml</DocumentationFile>
    
    <!-- ========================================
         警告和錯誤設定
         ======================================== -->
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <WarningLevel>4</WarningLevel>
    <NoWarn>`$(NoWarn);CS1591</NoWarn>
    
    <!-- ========================================
         版本資訊
         ======================================== -->
    <Version>2.0.0</Version>
    <AssemblyVersion>2.0.0.0</AssemblyVersion>
    <FileVersion>2.0.0.0</FileVersion>
    <Copyright>Copyright ? 1997-2025 John Robbins &amp; SpeechMessage -- All rights reserved.</Copyright>
    <Company>SpeechMessage</Company>
    <Product>ChurchReport Trace Library</Product>
    <Description>Enhanced tracing and debugging utilities for .NET 10 applications</Description>
  </PropertyGroup>

  <PropertyGroup Condition="'`$(Configuration)|`$(Platform)'=='Debug|AnyCPU'">
    <!-- ========================================
         Debug 設定
         ======================================== -->
    <DefineConstants>`$(DefineConstants);DEBUG;TRACE</DefineConstants>
    <DebugType>full</DebugType>
    <DebugSymbols>true</DebugSymbols>
    <Optimize>false</Optimize>
    <CheckForOverflowUnderflow>false</CheckForOverflowUnderflow>
  </PropertyGroup>

  <PropertyGroup Condition="'`$(Configuration)|`$(Platform)'=='Release|AnyCPU'">
    <!-- ========================================
         Release 設定
         ======================================== -->
    <DefineConstants>`$(DefineConstants);TRACE</DefineConstants>
    <DebugType>pdbonly</DebugType>
    <DebugSymbols>false</DebugSymbols>
    <Optimize>true</Optimize>
    <CheckForOverflowUnderflow>false</CheckForOverflowUnderflow>
  </PropertyGroup>

  <ItemGroup>
    <!-- ========================================
         編譯檔案
         ======================================== -->
    <Compile Include="AssemblyInfo.cs" />
    <Compile Include="BSUStackTrace.cs" />
    <Compile Include="BSUTextWriterTraceListener.cs" />
  </ItemGroup>

  <ItemGroup>
    <!-- ========================================
         其他檔案
         ======================================== -->
    <None Include="SpeechMessageCrmKey.snk" />
  </ItemGroup>

</Project>
"@

Set-Content -Path $TraceCsproj -Value $NewCsprojContent -Encoding UTF8
Write-Host "? 已創建新的專案檔案" -ForegroundColor Green
Write-Host ""

# 編譯專案
Write-Host "[步驟 3/5] 編譯 Trace 專案..." -ForegroundColor Cyan
Push-Location $TraceProject

$buildResult = dotnet build 2>&1
$buildExitCode = $LASTEXITCODE

if ($buildExitCode -eq 0) {
    Write-Host "? 編譯成功!" -ForegroundColor Green
} else {
    Write-Host "? 編譯失敗!" -ForegroundColor Red
    Write-Host "錯誤訊息:" -ForegroundColor Yellow
    Write-Host $buildResult -ForegroundColor Yellow
    Pop-Location
    exit 1
}

Pop-Location
Write-Host ""

# 檢查輸出檔案
Write-Host "[步驟 4/5] 檢查輸出檔案..." -ForegroundColor Cyan
$OutputDir = Join-Path $TraceProject "bin\Debug\net10.0"
$TraceDll = Join-Path $OutputDir "Trace.dll"
$TracePdb = Join-Path $OutputDir "Trace.pdb"
$TraceXml = Join-Path $OutputDir "Trace.xml"

if (Test-Path $TraceDll) {
    Write-Host "? Trace.dll 已產生" -ForegroundColor Green
    $fileInfo = Get-Item $TraceDll
    Write-Host "   大小: $($fileInfo.Length) bytes" -ForegroundColor Gray
} else {
    Write-Host "? Trace.dll 未找到" -ForegroundColor Red
}

if (Test-Path $TracePdb) {
    Write-Host "? Trace.pdb 已產生" -ForegroundColor Green
} else {
    Write-Host "??  Trace.pdb 未找到" -ForegroundColor Yellow
}

if (Test-Path $TraceXml) {
    Write-Host "? Trace.xml 已產生" -ForegroundColor Green
} else {
    Write-Host "??  Trace.xml 未找到" -ForegroundColor Yellow
}
Write-Host ""

# 驗證強式名稱
Write-Host "[步驟 5/5] 驗證強式名稱簽章..." -ForegroundColor Cyan
if (Test-Path $TraceDll) {
    $snResult = sn -v $TraceDll 2>&1
    if ($snResult -match "is valid") {
        Write-Host "? 強式名稱簽章有效" -ForegroundColor Green
    } else {
        Write-Host "??  強式名稱簽章驗證失敗" -ForegroundColor Yellow
        Write-Host "   結果: $snResult" -ForegroundColor Gray
    }
} else {
    Write-Host "??  跳過簽章驗證（找不到 Trace.dll）" -ForegroundColor Yellow
}
Write-Host ""

# 完成
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "? Trace 專案升級完成!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "下一步:" -ForegroundColor Cyan
Write-Host "1. 在 Visual Studio 中重新載入 Trace 專案" -ForegroundColor White
Write-Host "2. 編譯整個解決方案: dotnet build ChurchReport.sln" -ForegroundColor White
Write-Host "3. 執行測試: dotnet test ChurchReport.Tests\ChurchReport.Tests.csproj" -ForegroundColor White
Write-Host ""
Write-Host "備份檔案位置: $BackupFile" -ForegroundColor Gray
Write-Host ""

# 詢問是否編譯整個解決方案
$response = Read-Host "是否要編譯整個解決方案? (Y/N)"
if ($response -eq "Y" -or $response -eq "y") {
    Write-Host ""
    Write-Host "編譯整個解決方案..." -ForegroundColor Cyan
    Push-Location $SolutionRoot
    dotnet build ChurchReport.sln
    Pop-Location
}

Write-Host ""
Write-Host "完成!" -ForegroundColor Green
