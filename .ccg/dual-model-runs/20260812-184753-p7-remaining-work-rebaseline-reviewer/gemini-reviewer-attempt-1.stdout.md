# P7 剩餘功能重新基準化審查報告 (P7 Remaining Capability Rebaseline Review)

本報告針對 `.trellis/tasks/08-12-p7-remaining-work-rebaseline/` 的本地變更及父級規劃更新進行審查。

---

## 關鍵審查發現 (Review Findings)

### 1. 警告 (Warning) — 產出物換行格式不符合 CRLF 合約
* **檔案路徑**: `.trellis/tasks/08-12-p7-remaining-work-rebaseline/build_rebaseline.py` (第 353 行)
* **合理依據**: 任務規範要求產出的 artifacts 必須為 **UTF-8 no BOM, CRLF 且結尾為 CRLF**。然而，`build_rebaseline.py` 中的 `write_json` 函數目前實作為：
  ```python
  path.write_text(json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")
  ```
  此處指定 `newline="\n"` 會強制將輸出檔案寫入為 LF 換行符，這違反了 CRLF 的合約要求。
* **建議措施**: 將 `newline="\n"` 修改為 `newline="\r\n"`，並確保結尾附加的換行符亦為 `\r\n`。

### 2. 提示 (Info) — 工作區檔案換行符不一致
* **檔案路徑**: `.trellis/tasks/08-05-gateway-purpose-and-positioning/` (多個規劃檔案，包括 `prd.md`, `design.md`, `implement.md`, `roadmap-p5-p7.md`, `task.json`)
* **合理依據**: Git 狀態顯示警告 `LF will be replaced by CRLF the next time Git touches it`，這代表工作區中的某些父級規劃檔案目前使用了 LF 換行符。
* **建議措施**: 確保所有 `.md` 和 `.json` 檔案皆以 UTF-8 (無 BOM) 格式儲存，且換行符統一為 CRLF，以維持專案程式碼風格的一致性。

### 3. 提示 (Info) — 隔離狀態與 Package01/Package02 規則驗證通過
* **檔案路徑**: `.trellis/tasks/08-12-p7-remaining-work-rebaseline/build_rebaseline.py`
* **合理依據**:
  * **獨立狀態**: 註冊表、Data8 執行器、具型別 ProductClient、ChurchReport 消費端、CE 證據、主機證據及 Rollout 狀態皆保持獨立，無共享可變狀態。
  * **Package01 規則**: 三個明確的 ChurchReport 具型別用戶端路徑（`fee.dedication.retrieve.by.contact.date.range`、`lessons.stor.retrieve.by.contact`、`lessons.stor.retrieve.by.disciplelesson`）已正確對應至 `migrated-disabled`，而僅限用戶端的作業則保持為 `not-migrated`。
  * **D-H 本地專用列**: 已正確標記為 `local-only-rejected`、`not-migrated` 且無 CE 證據，符合 fail-closed 安全隔離。
  * **Package02 多行常數**: 正確透過 `re.MULTILINE` 與 `\s*` 匹配跨行宣告的 C# 常數（例如 `MemberInfoContactCountUngroupedCommitment`）。
  * **無敏感資訊洩漏**: 經測試驗證，產出物中未包含任何 secrets、endpoint、CRM ID 或 raw exceptions。

---

## 結論 (Conclusion)
除 **產出物換行格式 (LF vs CRLF)** 需進行微調外，其餘基準化矩陣的建置邏輯、測試覆蓋率、隔離邊界及父級規劃更新皆符合設計與安全規範。
