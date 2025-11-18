# ContactId 緊急修復腳本
# 此腳本會自動在 DownloadIntegrateData.cs 中添加 ContactId

Write-Host "===== ContactId 緊急修復腳本 =====" -ForegroundColor Cyan
Write-Host ""

$filePath = "ChurchReport\WebServiceConnector\DownloadIntegrateData.cs"

Write-Host "檢查檔案是否存在..." -ForegroundColor Yellow
if (-not (Test-Path $filePath)) {
    Write-Host "錯誤: 找不到檔案 $filePath" -ForegroundColor Red
    exit 1
}

Write-Host "? 檔案存在" -ForegroundColor Green
Write-Host ""

# 備份檔案
$backupPath = "$filePath.backup_contactid_" + (Get-Date -Format "yyyyMMdd_HHmmss")
Write-Host "建立備份: $backupPath" -ForegroundColor Yellow
Copy-Item $filePath $backupPath
Write-Host "? 備份完成" -ForegroundColor Green
Write-Host ""

# 讀取檔案內容
$content = Get-Content $filePath -Raw

Write-Host "開始修改檔案..." -ForegroundColor Yellow
Write-Host ""

$modified = $false

# 修改 1: GetAllMemberDataFromPresentRecord 方法 - 取得 ContactId
Write-Host "1. 修改 GetAllMemberDataFromPresentRecord - 取得 ContactId..." -ForegroundColor Cyan
$pattern1 = '(\s+#region// 出席紀錄組員的全名\r?\n\s+String FullName = "";\r?\n\s+EntityReference aFullNameEntityReference = new EntityReference\(\);\r?\n\s+if \(PresentRecordEntity\.Attributes\.Contains\("new_contact_new_present_record"\)\)\r?\n\s+\{\r?\n\s+aFullNameEntityReference = \(EntityReference\)PresentRecordEntity\.Attributes\["new_contact_new_present_record"\];\r?\n\r?\n\s+FullName = \(string\)aFullNameEntityReference\.Name;)'
$replacement1 = @'
$1
                            ContactId = aFullNameEntityReference.Id.ToString(); // 取得 ContactId
'@

if ($content -match $pattern1) {
    # 先嘗試添加 ContactId 變數宣告
    $varPattern = '(\s+#region// 出席紀錄組員的全名\r?\n\s+String FullName = "";)'
    $varReplacement = @'
$1
                        String ContactId = ""; // 聯絡人 ID
'@
    
    if ($content -match $varPattern -and $content -notmatch 'String ContactId = ""') {
        $content = $content -replace $varPattern, $varReplacement
        Write-Host "  ? 添加 ContactId 變數宣告" -ForegroundColor Green
        $modified = $true
    }
    
    # 添加 ContactId 賦值
    if ($content -notmatch 'ContactId = aFullNameEntityReference\.Id\.ToString\(\)') {
        $content = $content -replace $pattern1, $replacement1
        Write-Host "  ? 添加 ContactId 取值" -ForegroundColor Green
        $modified = $true
    } else {
        Write-Host "  - ContactId 取值已存在" -ForegroundColor Gray
    }
} else {
    Write-Host "  ! 無法匹配模式，手動添加..." -ForegroundColor Yellow
    
    # 使用簡單替換
    $simplePattern = 'FullName = \(string\)aFullNameEntityReference\.Name;'
    $simpleReplacement = @'
FullName = (string)aFullNameEntityReference.Name;
                            ContactId = aFullNameEntityReference.Id.ToString(); // 取得 ContactId
'@
    
    if ($content -match $simplePattern -and $content -notmatch 'ContactId = aFullNameEntityReference\.Id\.ToString\(\)') {
        $content = $content -replace $simplePattern, $simpleReplacement
        
        # 添加變數宣告
        $varSimplePattern = 'String FullName = "";'
        $varSimpleReplacement = @'
String FullName = "";
                        String ContactId = ""; // 聯絡人 ID
'@
        if ($content -notmatch 'String ContactId = ""') {
            $content = $content -replace $varSimplePattern, $varSimpleReplacement
        }
        
        Write-Host "  ? 使用簡單替換添加 ContactId" -ForegroundColor Green
        $modified = $true
    } else {
        Write-Host "  - 已存在或無需修改" -ForegroundColor Gray
    }
}
Write-Host ""

# 修改 2: GetAllMemberDataFromPresentRecord 方法 - 設置 ContactId 到 Member
Write-Host "2. 修改 GetAllMemberDataFromPresentRecord - 設置 ContactId..." -ForegroundColor Cyan
$pattern2 = 'PresentRecordId = PresentRecordEntity\.Id\.ToString\(\),\r?\n\s+Group = GroupName,'
$replacement2 = @'
PresentRecordId = PresentRecordEntity.Id.ToString(),
                                    ContactId = ContactId, // 聯絡人 ID
                                    Group = GroupName,
'@

if ($content -match $pattern2 -and $content -notmatch 'PresentRecordId = PresentRecordEntity\.Id\.ToString\(\),\r?\n\s+ContactId = ContactId') {
    $content = $content -replace $pattern2, $replacement2
    Write-Host "  ? 添加 ContactId 到 Member 對象" -ForegroundColor Green
    $modified = $true
} else {
    Write-Host "  - ContactId 已存在於 Member 對象" -ForegroundColor Gray
}
Write-Host ""

# 修改 3: GetAllMemberDataFromList 方法 - 取得並設置 ContactId
Write-Host "3. 修改 GetAllMemberDataFromList - 添加 ContactId..." -ForegroundColor Cyan
$pattern3 = '(// 組員的全名\r?\n\s+String FullName = "";\r?\n\s+if \(ContactEntity\.Attributes\.Contains\("fullname"\)\)\r?\n\s+\{\r?\n\s+FullName = \(string\)ContactEntity\.Attributes\["fullname"\];\r?\n\s+\})'
$replacement3 = @'
$1
                        // 組員的 ContactId
                        String ContactId = ContactEntity.Id.ToString();
'@

if ($content -match $pattern3 -and $content -notmatch '// 組員的 ContactId\r?\n\s+String ContactId = ContactEntity\.Id\.ToString\(\)') {
    $content = $content -replace $pattern3, $replacement3
    Write-Host "  ? 添加 ContactId 變數" -ForegroundColor Green
    $modified = $true
} else {
    Write-Host "  - 嘗試簡單替換..." -ForegroundColor Gray
    $simplePattern3 = 'if \(ContactEntity\.Attributes\.Contains\("fullname"\)\)\s+\{\s+FullName = \(string\)ContactEntity\.Attributes\["fullname"\];\s+\}'
    $simpleReplacement3 = @'
if (ContactEntity.Attributes.Contains("fullname"))
                        {
                            FullName = (string)ContactEntity.Attributes["fullname"];
                        }
                        // 組員的 ContactId
                        String ContactId = ContactEntity.Id.ToString();
'@
    if ($content -match $simplePattern3 -and $content -notmatch 'String ContactId = ContactEntity\.Id\.ToString\(\)') {
        $content = $content -replace $simplePattern3, $simpleReplacement3
        Write-Host "  ? 使用簡單替換添加 ContactId" -ForegroundColor Green
        $modified = $true
    } else {
        Write-Host "  - 已存在或無法修改" -ForegroundColor Gray
    }
}

# 在 GetAllMemberDataFromList 的 Member 對象中添加 ContactId
$pattern3b = '(PresentRecordId = PresentRecordIdCounter\+\+\.ToString\(\),)\r?\n(\s+Group = GroupName,)'
$replacement3b = @'
$1
$2ContactId = ContactId, // 聯絡人 ID
                                    $3
'@

if ($content -match 'PresentRecordId = PresentRecordIdCounter\+\+\.ToString\(\),\r?\n\s+Group = GroupName,' -and $content -notmatch 'PresentRecordId = PresentRecordIdCounter\+\+\.ToString\(\),\r?\n\s+ContactId = ContactId') {
    $content = $content -replace 'PresentRecordId = PresentRecordIdCounter\+\+\.ToString\(\),', 'PresentRecordId = PresentRecordIdCounter++.ToString(),
                                    ContactId = ContactId, // 聯絡人 ID'
    Write-Host "  ? 添加 ContactId 到 Member 對象 (List方法)" -ForegroundColor Green
    $modified = $true
} else {
    Write-Host "  - ContactId 已存在於 Member 對象 (List方法)" -ForegroundColor Gray
}
Write-Host ""

# 修改 4: SetAllMemberDataByPersonalReport 方法
Write-Host "4. 修改 SetAllMemberDataByPersonalReport - 添加 ContactId..." -ForegroundColor Cyan
$pattern4 = '(#region// 依據紀錄組員的全名，找到手機號碼、家裡電話、地址、生日、職業及專長\r?\n\s+// 組員的全名\r?\n\s+String FullName = "";\r?\n\s+if \( this\.m_ContactEntity\.Attributes\.Contains\("fullname"\)\))'
$replacement4 = @'
$1
            // 組員的 ContactId
            String ContactId = m_ContactEntity.Id.ToString();
'@

if ($content -match 'if \( this\.m_ContactEntity\.Attributes\.Contains\("fullname"\)\)' -and $content -notmatch 'String ContactId = m_ContactEntity\.Id\.ToString\(\)') {
    $insertPos = $content.IndexOf('if ( this.m_ContactEntity.Attributes.Contains("fullname"))')
    if ($insertPos -gt 0) {
        $beforeInsert = $content.Substring(0, $insertPos)
        $afterInsert = $content.Substring($insertPos)
        $content = $beforeInsert + "            // 組員的 ContactId`r`n            String ContactId = m_ContactEntity.Id.ToString();`r`n            " + $afterInsert
        Write-Host "  ? 添加 ContactId 變數 (PersonalReport方法)" -ForegroundColor Green
        $modified = $true
    }
} else {
    Write-Host "  - 已存在或無法修改" -ForegroundColor Gray
}

if ($content -match 'PresentRecordId = DateTime\.Now\.ToLongTimeString\(\)\.ToString\(\),\r?\n\s+Group = GroupName,' -and $content -notmatch 'PresentRecordId = DateTime\.Now\.ToLongTimeString\(\)\.ToString\(\),\r?\n\s+ContactId = ContactId') {
    $content = $content -replace 'PresentRecordId = DateTime\.Now\.ToLongTimeString\(\)\.ToString\(\),', 'PresentRecordId = DateTime.Now.ToLongTimeString().ToString(),
                        ContactId = ContactId, // 聯絡人 ID'
    Write-Host "  ? 添加 ContactId 到 Member 對象 (PersonalReport方法)" -ForegroundColor Green
    $modified = $true
} else {
    Write-Host "  - ContactId 已存在於 Member 對象 (PersonalReport方法)" -ForegroundColor Gray
}
Write-Host ""

# 修改 5: CreateMember 方法
Write-Host "5. 修改 CreateMember - 添加 ContactId..." -ForegroundColor Cyan
if ($content -match 'private Member CreateMember\(String GroupName\)' -and $content -notmatch 'ContactId = this\.m_ContactEntity\.Id\.ToString\(\)') {
    $content = $content -replace '(private Member CreateMember\(String GroupName\)\s+\{\s+return new Member\s+\{\s+PresentRecordId = "\.\.\.\.\.\.\.",)', @'
$1
                ContactId = this.m_ContactEntity.Id.ToString(), // 聯絡人 ID
'@
    Write-Host "  ? 添加 ContactId 到 CreateMember" -ForegroundColor Green
    $modified = $true
} else {
    Write-Host "  - 已存在或無法修改" -ForegroundColor Gray
}
Write-Host ""

# 寫入修改後的內容
if ($modified) {
    Write-Host "寫入修改後的檔案..." -ForegroundColor Yellow
    [System.IO.File]::WriteAllText((Resolve-Path $filePath).Path, $content, [System.Text.Encoding]::UTF8)
    Write-Host "? 檔案已更新" -ForegroundColor Green
    Write-Host ""
    
    Write-Host "===== 修改完成 =====" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "已完成的修改:" -ForegroundColor Yellow
    Write-Host "  ? GetAllMemberDataFromPresentRecord - 取得並設置 ContactId" -ForegroundColor Green
    Write-Host "  ? GetAllMemberDataFromList - 取得並設置 ContactId" -ForegroundColor Green
    Write-Host "  ? SetAllMemberDataByPersonalReport - 取得並設置 ContactId" -ForegroundColor Green
    Write-Host "  ? CreateMember - 設置 ContactId" -ForegroundColor Green
    Write-Host ""
    Write-Host "下一步:" -ForegroundColor Yellow
    Write-Host "  1. 編譯專案: dotnet build" -ForegroundColor White
    Write-Host "  2. 運行應用程式" -ForegroundColor White
    Write-Host "  3. 查看調試日誌確認 ContactId 不再為 null" -ForegroundColor White
    Write-Host ""
    Write-Host "如果發生問題，可以從備份還原:" -ForegroundColor Yellow
    Write-Host "  Copy-Item $backupPath $filePath" -ForegroundColor White
} else {
    Write-Host "===== 無需修改 =====" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "所有 ContactId 相關代碼已經存在。" -ForegroundColor Green
    Write-Host ""
    Write-Host "如果 member.ContactId 仍然是 null，請檢查:" -ForegroundColor Yellow
    Write-Host "  1. 是否已重新編譯專案" -ForegroundColor White
    Write-Host "  2. 是否已重啟應用程式" -ForegroundColor White
    Write-Host "  3. 是否已清除瀏覽器緩存" -ForegroundColor White
    Write-Host "  4. 檢查 InMemoryContext 緩存是否已更新" -ForegroundColor White
}

Write-Host ""
Write-Host "備份檔案: $backupPath" -ForegroundColor Cyan
Write-Host ""
