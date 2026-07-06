# ?? Trace 專案升級除錯指南

## ? 問題：NETSDK1022 重複項目錯誤

### 錯誤訊息
```
NETSDK1022: 包含 'Compile' 個重複的項目。根據預設，.NET SDK 會包含來自您專案目錄的 'Compile' 個項目。
重複的項目為: 'AssemblyInfo.cs'; 'BSUStackTrace.cs'; 'BSUTextWriterTraceListener.cs'
```

### ?? 問題原因

SDK-Style 專案格式會**自動包含**專案目錄中的所有 `.cs` 檔案，但原始的專案檔案中又手動指定了：

```xml
<ItemGroup>
  <Compile Include="AssemblyInfo.cs" />
  <Compile Include="BSUStackTrace.cs" />
  <Compile Include="BSUTextWriterTraceListener.cs" />
</ItemGroup>
```

這導致這些檔案被包含**兩次**！

---

## ? 解決方案

### 方法 1: 手動修正（推薦）

1. **開啟 `Trace\Trace.csproj`**

2. **找到這段程式碼並刪除：**
   ```xml
   <ItemGroup>
     <Compile Include="AssemblyInfo.cs" />
     <Compile Include="BSUStackTrace.cs" />
     <Compile Include="BSUTextWriterTraceListener.cs" />
   </ItemGroup>
   ```

3. **保留其他部分不變**

4. **儲存檔案**

5. **在 Visual Studio 中重新載入專案**
   - 右鍵點擊 **Trace** 專案
   - 點選 **「重新載入專案」**

6. **編譯驗證**
   ```powershell
   cd "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\Trace"
   dotnet build Trace.csproj
   ```

### 方法 2: 使用修正後的檔案

```powershell
# 1. 備份原始檔案
cd "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\Trace"
copy Trace.csproj Trace.csproj.with-error

# 2. 使用修正後的檔案
copy Trace_Fixed.csproj Trace.csproj

# 3. 清理 obj 目錄
Remove-Item -Recurse -Force obj

# 4. 重新編譯
dotnet build Trace.csproj
```

---

## ?? 正確的專案檔案內容

### ? 修正後的 Trace.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>TraceNameSpace</RootNamespace>
    <AssemblyName>Trace</AssemblyName>
    <LangVersion>14.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <OutputType>Library</OutputType>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    
    <SignAssembly>true</SignAssembly>
    <AssemblyOriginatorKeyFile>SpeechMessageCrmKey.snk</AssemblyOriginatorKeyFile>
    
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <DocumentationFile>$(OutputPath)$(AssemblyName).xml</DocumentationFile>
    
    <Version>2.0.0</Version>
    <AssemblyVersion>2.0.0.0</AssemblyVersion>
    <FileVersion>2.0.0.0</FileVersion>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Debug|AnyCPU'">
    <DefineConstants>$(DefineConstants);DEBUG;TRACE</DefineConstants>
    <DebugType>full</DebugType>
    <DebugSymbols>true</DebugSymbols>
    <Optimize>false</Optimize>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Release|AnyCPU'">
    <DefineConstants>$(DefineConstants);TRACE</DefineConstants>
    <DebugType>pdbonly</DebugType>
    <Optimize>true</Optimize>
  </PropertyGroup>

  <!-- ? SDK-Style 專案會自動包含所有 .cs 檔案 -->
  <!-- ? 不要手動添加 <Compile Include="..."> -->

  <ItemGroup>
    <None Include="SpeechMessageCrmKey.snk" />
  </ItemGroup>

</Project>
```

---

## ?? 關鍵要點

### SDK-Style 專案的自動包含規則

| 檔案類型 | 自動包含 | 說明 |
|---------|---------|------|
| `**/*.cs` | ? 是 | 所有 C# 原始檔案 |
| `**/*.resx` | ? 是 | 資源檔案 |
| `**/*.settings` | ? 是 | 設定檔案 |
| `*.snk` | ? 否 | 需要手動指定為 `<None>` |

### 何時需要手動指定？

只在以下情況需要手動 `<Compile Include>`：

1. **排除某些檔案**
   ```xml
   <ItemGroup>
     <Compile Remove="OldCode\*.cs" />
   </ItemGroup>
   ```

2. **包含專案目錄外的檔案**
   ```xml
   <ItemGroup>
     <Compile Include="..\Shared\Common.cs" />
   </ItemGroup>
   ```

3. **設定特殊屬性**
   ```xml
   <ItemGroup>
     <Compile Include="Generated.cs">
       <AutoGen>True</AutoGen>
     </Compile>
   </ItemGroup>
   ```

---

## ? 驗證步驟

### 1. 清理專案
```powershell
cd "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\Trace"
Remove-Item -Recurse -Force obj, bin
```

### 2. 重新編譯
```powershell
dotnet build Trace.csproj
```

### 3. 檢查輸出
應該看到：
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### 4. 確認產生的檔案
```powershell
dir bin\Debug\net10.0\Trace.dll
dir bin\Debug\net10.0\Trace.xml
```

---

## ?? 修正前後對比

| 項目 | 修正前 | 修正後 |
|------|--------|--------|
| **ItemGroup 數量** | 2 個 | 1 個 |
| **手動 Compile Include** | ? 3 個 | ? 0 個 |
| **專案檔案行數** | ~80 行 | ~70 行 |
| **編譯狀態** | ? 失敗 | ? 成功 |

---

## ?? 其他常見問題

### 問題 1: 仍然看到重複錯誤

**解決方案：**清理 obj 和 bin 目錄
```powershell
Remove-Item -Recurse -Force obj, bin
dotnet build
```

### 問題 2: 找不到 SpeechMessageCrmKey.snk

**解決方案：**確認檔案存在
```powershell
dir SpeechMessageCrmKey.snk
```

如果不存在，從備份復原或重新產生。

### 問題 3: Visual Studio 顯示錯誤但命令列編譯成功

**解決方案：**
1. 關閉 Visual Studio
2. 刪除 `.vs` 隱藏目錄
3. 重新開啟 Visual Studio

---

## ?? 需要更多協助？

查看其他文檔：
- `Trace/UPGRADE-QUICK-START.md` - 快速開始
- `ChurchReport/文件/升級Trace/README.md` - 完整指南
- `ChurchReport/文件/升級Trace/執行指南.md` - 詳細步驟

---

## ? 完成！

修正完成後，Trace 專案應該能夠正常編譯了！

**下一步：**
```powershell
# 編譯整個解決方案
cd "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport"
dotnet build ChurchReport.sln
```

