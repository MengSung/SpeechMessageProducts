## Analysis：818 個 .cs 檔案批次加註（繁體中文、UTF-8 無 BOM）

### 現況（已實測，非臆測）

| 項目 | 實測結果 |
|---|---|
| 追蹤中的 .cs 檔案 | 818 個，共 ~38,914 行（平均 48 行/檔，屬小型檔案為主） |
| 產生／設計器檔案 | 僅 3 個：`NSspi/Properties/AssemblyInfo.cs`、`SpeechMessage.Payments/Properties/AssemblyInfo.cs`、`Trace/AssemblyInfo.cs`（無 `.Designer.cs`、無 `.g.cs`、無 Migrations） |
| 編碼現況 | **混合**：多數檔案是 `UTF-8 with BOM`（如 `AuthenticationController.LineBinding.cs`、`FaultReader.cs`），少數已是無 BOM UTF-8。`.editorconfig` 已宣告 `charset = utf-8`（即無 BOM）且 `end_of_line = crlf`，但實際檔案違反此規則 |
| XML 文件產生 | 主專案 `ChurchReport.csproj` **未**開啟 `GenerateDocumentationFile`（一般 `//` 或 `///` 註解都不會觸發 CS1591/CS1570） |
| 高風險專案 | 3 個子專案開了 `GenerateDocumentationFile=true`：`Trace.csproj`／`Trace_Fixed.csproj`／`Trace_Net10.csproj`（已 `NoWarn` 內含 `CS1591`，安全）；`PowerPlatform.Dataverse.Client.csproj`（`NoWarn` 含 `CS1591`，安全）；**`NSspi.csproj`（GenerateDocumentationFile=true，但完全沒有 `NoWarn`，也沒有壓制 CS1591/CS1570）——這是唯一真正暴露的專案** |
| TreatWarningsAsErrors | 全倉庫皆未設定，故就算觸發文件註解警告也不會讓 build 失敗，只是噪音 |
| 前次嘗試 | `04fb51fa RICHMENU加註解`／`907ae8b0` 兩個提交僅落地了 CCG 雙模型分析的 artifacts（prompt/log/summary），**未對任何 .cs 檔案做實際加註**，因此本次是從零開始，沒有既有的加註風格可延續 |

### 建議實作策略

1. **一律使用 `//` 行內／區塊註解，不要用 `///` XML 文件註解**，除非該檔案所屬專案本來就大量使用 `///`（NSspi、Dataverse.Client、Trace）且你確認會同步補上格式正確的 `<summary>`。這樣可完全繞開 CS1570/CS1591 的風險面，且更貼近「詳細說明」而非「API 文件」的需求。
2. **分批＋分類處理**，而非一次性 818 檔全改：
   - 依專案分組（ChurchReport 主站 / Line.Messaging / LineMessagingProcessor* / PowerPlatform.Dataverse.Client / SpeechMessage.Payments* / Trace / *.Tests），每批跑完立即 `dotnet build` + 執行對應測試專案，隔離錯誤範圍。
   - 90 個測試檔案（`*Tests.cs`）可以視為低優先或簡化處理（測試意圖通常方法名已自解釋），避免為了「完整覆蓋」而在斷言堆裡塞入大量低價值註解。
3. **BOM 正規化與加註同批處理**：加註同時把檔案重新以 UTF-8 no-BOM + CRLF 寫回，一次到位，避免「先加註、再轉碼」造成二次 diff 及二次觸碰全部 818 檔的 git churn。
4. **不要用單一腳本無腦生成註解**：818 個檔案語意差異大（Controller / Service / DTO / 第三方 vendored 程式碼），機械式套版註解（例如「這是一個方法」）沒有價值且會被使用者視為噪音。建議按語意角色（Controller action、Service 商業邏輯、DTO/Model 欄位、Middleware、第三方 vendored code）給不同深度的註解密度。
5. **Vendored 第三方碼降低優先權**：`Line.Messaging`、`PowerPlatform.Dataverse.Client`、`NSspi` 屬於已知的 vendored 第三方碼（見既有記憶 `nowarn-audit-vendored-vs-own`），為其加中文註解的 ROI 低於主站 `ChurchReport/` 的業務邏輯，且改動這些檔案會讓未來要同步上游程式碼時 diff 噪音變大——建議與使用者確認是否要排除或降低這幾個子專案的加註深度。

### 高風險檔案類別（易造成編譯警告）

- **Critical｜`PowerPlatform.Dataverse.Client/NSspi/**/*.cs`**：`NSspi.csproj` 開了 `GenerateDocumentationFile=true` 卻無任何 `NoWarn`。若在這些檔案內對 public 型別/成員新增或改動 `///` 註解，任何 XML 格式錯誤（未跳脫的 `<`、`>`、`&`，或中文全形符號誤用如「＜」「＞」混淆）會觸發 CS1570「XML 註解格式錯誤」，且既有 public 成員若原本沒有 `///` 也會冒出 CS1591。建議：此資料夾一律用 `//` 註解，不要新增/修改 `///`。
- **Warning｜`Trace/*.csproj` 系列與 `PowerPlatform.Dataverse.Client.csproj` 本體**：雖已 `NoWarn` 壓了 CS1591，但若既有 `///` 註解本身格式正確、你在裡面插入中文說明時不慎破壞 XML 結構（例如在 `<summary>` 內插入未跳脫的尖括號），CS1570 **未被壓制**，仍會冒出警告。改動這些檔案的既有 XML 註解時要格外小心保留合法 XML 結構。
- **Info｜`AssemblyInfo.cs`（3 個）**：屬性型檔案，內容多為 `[assembly: ...]`，加註意義有限，建議只加檔案層級一句說明即可，不需逐行加註。
- **Info｜測試專案（90 個 *Tests.cs）**：無 GenerateDocumentationFile 疑慮，但過度加註斷言區塊反而降低可讀性，建議僅在測試案例意圖不明顯時加註。

### 驗證指令（Windows / PowerShell，此 repo 慣用 PowerShell 工具鏈）

**1. UTF-8 無 BOM 檢查**
```powershell
git ls-files "*.cs" | ForEach-Object {
  $bytes = [System.IO.File]::ReadAllBytes($_)
  if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
    Write-Output $_
  }
} | Tee-Object -FilePath bom-violations.txt
```
（清單應為空；`.editorconfig` 已宣告此規則，可作為 CI gate）

**2. CRLF 檢查**（此 repo `.editorconfig` 要求 `end_of_line = crlf`）
```powershell
git ls-files "*.cs" | ForEach-Object {
  $t = Get-Content -Raw $_
  if ($t -match "(?<!\r)\n") { Write-Output $_ }
}
```

**3. 註解語言（是否含繁體中文）粗檢**——用於抽樣確認新增註解確實是中文而非留空/英文佔位：
```powershell
git diff --name-only main... -- "*.cs" | ForEach-Object {
  $hasCJK = Select-String -Path $_ -Pattern '[\u4e00-\u9fff]' -Quiet
  if (-not $hasCJK) { Write-Output "NO-CJK: $_" }
}
```

**4. XML 文件註解格式檢查**——直接靠編譯器最準確，不要自己寫正則判斷 XML 合法性：
```powershell
dotnet build PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj -warnaserror:CS1570,CS1571,CS1572,CS1573,CS1574,CS1580,CS1581,CS1587,CS1589,CS1590,CS1592
dotnet build Trace/Trace.csproj -warnaserror:CS1570,CS1571,CS1572,CS1573,CS1574
dotnet build "PowerPlatform.Dataverse.Client/NSspi/NSspi.csproj" -warnaserror:CS1570,CS1591
```
（暫時把這些 CS15xx 系列升級為 error，只在驗證階段用，讓有問題的檔案立刻炸出來，而非被 NoWarn 吞掉）

**5. 全量建置＋測試**（行為不應改變，測試必須全綠）
```powershell
dotnet build ChurchReport.sln -c Debug
dotnet test ChurchReport.sln -c Debug
```

**6. 確認只有註解/空白改動、無邏輯異動**——用 git diff 過濾掉純加註的行，抽查剩下的非空白 diff：
```powershell
git diff --ignore-blank-lines -- "*.cs" | Select-String -Pattern '^[+-](?!\s*(//|/\*|\*|\*/))' 
```
（此指令找出「非註解、非空白」的新增/刪除行；理論上除了 BOM/EOL 正規化造成的整檔重寫外，不應該有邏輯行變動——但因為 BOM 移除會讓 git diff 顯示整檔改變，建議搭配 `git diff --color-words` 或先跑一個獨立的「僅轉碼」commit，再跑「僅加註」commit，兩者分開才能讓這個驗證有意義）

### 建議行動項目

1. [ ] 與使用者確認：`Line.Messaging`／`PowerPlatform.Dataverse.Client`／`NSspi`／`LinePayCSharp` 等 vendored 第三方碼是否要排除在「全部加註」範圍外（降低不必要的 diff 與未來上游同步成本）
2. [ ] 先跑一個獨立的「BOM→無BOM + 統一CRLF」commit（zero 邏輯改動），再跑「加註」commit，兩者分開便於驗證與 review
3. [ ] `NSspi.csproj` 內檔案改動一律使用 `//`，不新增/修改 `///`
4. [ ] 依專案分批加註 + 每批建置驗證，而非一次性改 818 檔
5. [ ] 加註完成後跑上述驗證指令 1–6，並跑 `dotnet test` 全量測試確認行為未變

---
**結論**：818 檔規模不大（平均 48 行/檔），技術風險集中在**編碼不一致（BOM 混雜）**與**`NSspi.csproj` 缺少 CS1591/CS1570 抑制**這兩點，其餘專案風險可控。核心建議是「用 `//` 而非 `///`」+「轉碼與加註分兩個 commit」+「分批建置驗證」，可把 818 檔的行為風險壓到接近零。

---
SESSION_ID: c5749f02-10e7-4657-ae96-f5f9e4385381
