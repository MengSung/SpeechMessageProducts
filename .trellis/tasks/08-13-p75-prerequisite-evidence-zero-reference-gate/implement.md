# P7.5 前置證據與零參照閘門實施計畫

1. [x] 以 immutable matrix 和 production source 先寫 fail-first tests：comment/literal-only、active
       token、raw string、invalid UTF-8、path escape/symlink、excluded Logs、JSON value redaction、report
       tamper、stable order、preprocessor/JSONC escape 與現況 enforce no-go。
2. [x] 新增 `build_p75_prerequisite_evidence.py`：固定 root discovery、bounded regular file enumeration、
       conservative C# lexer、XML/JSON metadata scan、matrix aggregate、strict report validator 和 P7.5 gate。
3. [x] 寫 task-owned report，執行 `--validate`；執行 `--enforce-p75`，預期 nonzero/no-go 並記錄為
       release-gate result，不能稱為 tool failure/P7.5 complete。
4. [x] 更新 parent docs/metadata，使 P7.5 prerequisite report 成為 P7.4 下一個排程來源，並保留
       P7.5 removal/P8 immutable handoff gate。
5. [x] 執行 focused Python tests、task validation、JSON parser、UTF-8 no-BOM/CRLF/final-CRLF、diff check、
       full solution Release test/build 與 CCG review；僅提交 task/parent scope，archive child。

## 預期檔案

- 新增：`build_p75_prerequisite_evidence.py`、`test_p75_prerequisite_evidence.py`、
  `p75-prerequisite-evidence-report.json`。
- 修改：本 task artifacts、`08-05-gateway-purpose-and-positioning` parent docs/metadata，及既有 P7.4
  parent-child link 的 CRLF normalization。

不得修改 archive matrix、ChurchReport runtime/config/project reference、ToolUtility、CRM SDK 或
`.ccg/dual-model-runs/**`。

## 驗證命令

```powershell
python .\.trellis\tasks\08-13-p75-prerequisite-evidence-zero-reference-gate\test_p75_prerequisite_evidence.py -v
python .\.trellis\tasks\08-13-p75-prerequisite-evidence-zero-reference-gate\build_p75_prerequisite_evidence.py --report .\.trellis\tasks\08-13-p75-prerequisite-evidence-zero-reference-gate\p75-prerequisite-evidence-report.json
python .\.trellis\tasks\08-13-p75-prerequisite-evidence-zero-reference-gate\build_p75_prerequisite_evidence.py --validate .\.trellis\tasks\08-13-p75-prerequisite-evidence-zero-reference-gate\p75-prerequisite-evidence-report.json
python .\.trellis\tasks\08-13-p75-prerequisite-evidence-zero-reference-gate\build_p75_prerequisite_evidence.py --enforce-p75
```

最後一個命令目前預期 nonzero，且只能輸出 fixed, sanitized no-go classification。若未來所有靜態
前置條件通過，它只能回傳 `prerequisite-ready`，不能被視為 P7.5 removal、CE 或 P8 authorization。
