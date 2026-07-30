#Requires -Version 5.1

<#
.SYNOPSIS
RETIRED：此舊 ADFS token probe 已永久停用，僅保留檔名以阻止舊文件、捷徑或人工命令誤執行危險流程。

.DESCRIPTION
舊工具曾接受互動式帳密、讀取產品 appsettings，並直接向身分與 CRM 邊界送出要求；這會讓 credential、
identity、endpoint 與結果資料跨越不必要的檔案、console 及程序生命週期，也不符合目前核准的 Microsoft
Public Client Authorization Code 設計。此退役入口不接受任何參數、不讀設定或環境、不建立 HTTP／socket／
token cache、不建立結果檔，也不啟動背景工作；執行後只會立即以固定 ASCII 訊息 fail closed。

本機開發若要驗證互動式 ADFS，必須先啟動 ChurchReport，再由 /diagnostics/adfs-authorize 進入既有 Public Client
流程。該流程的 callback 只在單一 request 內完成 code exchange 與受控連線驗證，token 不落盤、不進 Session 或共享 cache，
所有逾時與 cleanup 由該 request 的確定性生命週期負責；本腳本不再複製第二套 owner。
在 Local Gateway、AD FS、CE 8.2／9.1、operation matrix、browser E2E 與 soak gate 全部通過前，
Package01FeeReadsEnabled=false 必須保持不變，且不得因診斷成功自動修改任何產品設定。

.NOTES
檔案必須維持 UTF-8 without BOM、CRLF 與 final CRLF。繁體中文說明刻意放在獨立 block comment，
避免 Windows PowerShell 5.1 對 UTF-8 without BOM 的歷史解碼差異改變後續 ASCII token 的 parser 邊界。
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

throw 'Invoke-AdfsTokenProbe.ps1 is RETIRED. Start ChurchReport and use /diagnostics/adfs-authorize. Keep Package01FeeReadsEnabled=false until every rollout gate passes.'
