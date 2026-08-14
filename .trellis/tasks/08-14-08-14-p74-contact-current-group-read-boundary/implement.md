# ORG-CALL-00052 執行計畫

1. [x] 讀取 AGENTS.md、Trellis workflow、backend isolation/gateway specs 與 P7 parent
       artifacts。
2. [x] 讀取 authoritative matrix row，定位 `GetContactCurrentGroup` 及所有 production caller。
3. [x] 稽核 caller 是否包含 membership、attendance、contact、Owner 或 notification write adjacency。
4. [x] 建立 `source-audit.md`，記錄固定分類、證據位置、決定與 recovery conditions。
5. [x] 以限時 CCG analysis 交叉核對 no-go，並建立 `check.md` 與 CCG review record；不得建立 runtime code。
6. [x] 執行 JSON/Trellis manifest validation、UTF-8/no-BOM/CRLF/final-CRLF（適用檔案）、
       `git diff --check` 與 scope scan。
7. [ ] 更新 P7.4 parent checkpoint，完成 scope-only commit，然後 archive 本 child。

## 禁止事項

- 不得 CE、fixture、CRM mutation、feature gate、traffic、P7.5、P8 或重試歷史 Slice C。
- 不得掃描或猜選 Owner，不得修改週報、共享資料、舊資料或正式資料。
- 不得將 local design、registry、unit test 或 no-go 誤稱為 CE、consumer、host 或 release evidence。
