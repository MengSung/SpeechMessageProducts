確認完畢，僅使用標準函式庫且無網路/子行程/環境變數存取。以下是最終審查報告。

---

# P7.5 Prerequisite Evidence Final Review Report

## 審查範圍
`.trellis/tasks/08-13-p75-prerequisite-evidence-zero-reference-gate/`（uncommitted，尚未 `git add`）下的 `build_p75_prerequisite_evidence.py`、`test_p75_prerequisite_evidence.py`、`p75-prerequisite-evidence-report.json`、`design.md`、`prd.md`、`implement.md`、`task.json`。

**重要流程觀察**：審查期間該目錄下 `build_p75_prerequisite_evidence.py`／`test_p75_prerequisite_evidence.py` 在約 09:23–09:30 之間被外部程序連續修改四次（657→744→753 行；14→15 個測試），期間曾短暫出現 `AttributeError`（測試引用尚未定義的 `scan_settings_key_names_from_jsonc`）與一個 `ScannerInputError not raised`（value escape 未驗證）的失敗狀態。這些中間狀態均已在後續寫入中修復。本報告以**最後一次觀察到的穩定狀態**（09:30:13 之後檔案無再變動、`python -m unittest` 15/15 通過、`--validate`／`--enforce-p75` 皆符合預期）為準。若送審時另有並行寫入程序仍在運作，建議在檔案穩定後重新觸發一次 review run。

## 1. 審查摘要
- **Offline / Bounded**：`import` 僅有 `argparse, json, re, sys, xml.etree.ElementTree, collections, pathlib, typing`，無 `socket/urllib/requests/subprocess/os.environ`。單檔 4MB 上限（`MAX_SOURCE_BYTES`），固定 `PRODUCTION_ROOT`/`MATRIX_FILE`/`SETTINGS_FILES`，無可注入的掃描根目錄或 CLI 覆寫參數。
- **Sanitized**：`strip_csharp_noncode` 遮罩註解與字面量、保留插值運算式中的真實 code；settings 改用新的 `JsoncKeyOnlyScanner`（純遞迴下降 parser）取代先前「先剝註解、再 `json.loads` 整份文件」的作法——value 完全不解碼、不 materialize，只做語法跳過，key-only evidence 邊界更嚴謹。
- **Fail-closed**：所有已知不確定路徑（`unclosed-*`、`invalid-utf8`、`path-escape`、`settings-json-invalid` 等）皆拋出 `ScannerInputError`，`main()` 統一分類為 `invalid-input`／exit code 2，不以部分結果代替零參照結論。`validate_report` 對竄改後的 report（`temporaryLegacyCount=0` + `state="prerequisite-ready"`）會判定 `invalid`，`evaluate_p75_gate` 回傳 `invalid-report`，不會被拿去偽造 removal 授權（`test_report_tamper_cannot_produce_a_ready_p75_gate`）。
- **報告狀態無不實聲明**：`p75-prerequisite-evidence-report.json` 的 `readiness.state = "no-go"`，`noGoReasons` 列出 8 項具體阻擋原因；`design.md:51` 與 `prd.md:10,40` 明確聲明本 task 完成「不等於 ToolUtility removal / P8 evidence」。掃描全文未見任何聲稱 CE evidence 完成、流量切換或 P8 就緒的字句。
- **Gate 結果正確性**：`--enforce-p75` 目前回傳 `outcome:"no-go"` 且 exit code = 1；`--validate p75-prerequisite-evidence-report.json` 回傳 `outcome:"valid"` 且 exit code = 0。這與程式邏輯（`main()` 僅在 `prerequisite-ready` 才回傳 0）一致，屬預期、正確的 gate 攔截結果，**不應被誤判為腳本錯誤**。

## 2. 具體發現

### Critical
無。截至最後穩定觀測狀態，未發現安全邊界破口、敏感資料外洩路徑，或會讓 no-go 被誤判為 ready 的邏輯缺陷。

### Warning
**1. 前次（Gemini）reviewer 對 `.ccg/tasks/p7-3-churchreport-special-resource-migrations/.turns.json` 的結論不準確，且該檔本不屬於本次 P7.5 審查範圍**
- 檔案：`.ccg/tasks/p7-3-churchreport-special-resource-migrations/.turns.json`
- 說明：`20260813-092431-p75-prerequisite-evidence-final-review-reviewer/gemini-reviewer-attempt-1.stdout.md` 聲稱此檔「被修改為僅包含單個字元 `[`，這是一個無效的 JSON 格式」。實際以 `git diff`／`cat -A` 檢視，該檔案是完整、合法的 JSON 陣列（10 筆 `{"phase":"review",...}` 記錄，`python -m json.tool` 可解析），並非只有 `[`。此外此檔屬於 P7.3 任務，git status 顯示為已追蹤檔案的一般修改，不在「當前未提交的 P7.5 task changes」範圍內。
- 建議：不要沿用該筆 Gemini 發現；若需要 dual-model 交叉比對，應以本報告的重新驗證結果覆蓋。

### Info
**1. 開發期間並行寫入造成暫時性測試失敗（已於本輪觀測期間自行修復，僅供留存）**
- 檔案：`build_p75_prerequisite_evidence.py`（`scan_settings_key_names_from_jsonc`、`JsoncKeyOnlyScanner._parse_string`）、`test_p75_prerequisite_evidence.py`（`test_key_only_jsonc_scanner_traverses_nested_values_without_materializing_them`、`test_key_only_jsonc_scanner_rejects_invalid_value_escape`）
- 說明：審查過程中兩度觀察到暫時性失敗（函式未定義；value 字串的 `\q` 等非法 escape 未被拒絕）。最終版本已補上 escape 白名單驗證（`"\\/bfnrt` 與 `\uXXXX`），15/15 測試皆通過。記錄此點是為了讓後續合併前有人確認：此 task 目錄目前是否仍有其他程序在編輯，避免「已審查版本」與「實際合併版本」不一致。
- 建議：合併前再跑一次 `python -m unittest test_p75_prerequisite_evidence.py` 與 `--enforce-p75` 作最終確認。

**2. `__pycache__/build_p75_prerequisite_evidence.cpython-314.pyc` 為未追蹤產物**
- 檔案：`.trellis/tasks/08-13-p75-prerequisite-evidence-zero-reference-gate/__pycache__/`
- 說明：已被根目錄 `.gitignore:260` 的 `__pycache__/` 規則排除，不會被提交，僅供留意。

## 3. 檢核清單
- [x] Offline（無網路/subprocess import）
- [x] Bounded（4MB 檔案上限、固定 root、無可注入路徑）
- [x] Sanitized（C# 註解/字面量遮罩、settings key-only scanner 不 materialize value）
- [x] Fail-closed（未知 lexical/JSON 狀態一律 `ScannerInputError` → exit 2；竄改 report → `invalid-report`）
- [x] C# 指令（`#region`/`#pragma`）與註解/字面量中的 legacy token 不會產生誤判（`test_comment_literal_and_character_only_legacy_tokens_are_not_source_references`、`test_preprocessor_directive_is_noncode_even_when_its_label_contains_quotes`）
- [x] JSONC 註解不會干擾 key 掃描或洩漏 value（`test_json_with_comments_settings_scanner_reads_keys_without_publishing_values`、`test_key_only_jsonc_scanner_traverses_nested_values_without_materializing_them`）
- [x] 報告未聲稱 ToolUtility removal／CE evidence／流量切換／P8 readiness
- [x] `--enforce-p75` 目前的 no-go（exit 1）被正確視為有效 gate 結果，非腳本錯誤

## 4. 結論
**PASS**（無 Critical）。建議事項：合併前重新確認目錄無並行寫入並重跑測試/gate（Info #1），並丟棄前次 Gemini 對 `.turns.json` 的錯誤結論（Warning #1）。

---
SESSION_ID: a7aff0a1-3c77-47b1-bab9-34fcbae28d09
