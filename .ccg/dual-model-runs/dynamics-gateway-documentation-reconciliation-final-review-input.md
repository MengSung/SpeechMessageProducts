# Dynamics Gateway 文件／SPEC 審查結果整合終審

## 角色與限制

請以唯讀 reviewer 身分審查本輪文件與 SPEC 增量，不得修改檔案。不得輸出或轉述任何帳號、Windows identity、Password、Credential、Token、Secret Reference、Session marker、Client ID、Callback 實值、完整 CRM／AD FS 私密 endpoint 或私有網路資訊。

## 本輪修改範圍

- `.ccg/tasks/dynamics-connection-compatibility/requirements.md`
- `.ccg/tasks/dynamics-connection-compatibility/review.md`
- `.ccg/tasks/dynamics-connection-compatibility/task.json`
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-local-central-boundary-verification.md`
- `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md`
- `.ccg/dual-model-runs/churchreport-local-gateway-documentation-lifecycle-final-review-input.md`
- `.ccg/dual-model-runs/20260730-024616-churchreport-local-gateway-documentation-lifecycle-final-review-reviewer/`

請同時對照實際 Production／Test／Config，但本輪不得要求為了文件終審擴張修改無關程式。

## 必須驗證的內容

1. 文件清楚記錄 Central Gateway 正式目標、Local Gateway Development 路徑、Embedded deferred、Data8 與 `PowerPlatform.Dataverse.Client` retained。
2. `Package01FeeReadsEnabled=false` 持續保持，沒有把 Local Gateway／Browser fail-closed smoke 誤寫成真實 CE 或 Phase 5 完成。
3. Development LocalDB、Gateway 401／403／controlled 400、ChurchReport Browser、AD FS 唯讀 marker、retired probe、host/listener cleanup 證據描述與既有測試／設定相符。
4. 真實 CE 8.2／9.1、OData annotation projection、cross-process capacity、coordinator fault、fault／soak／performance、Phase 5 單一 workflow、Phase 6 removal 仍明確 open。
5. Development `WorkloadBindings` index merge 被保留為 Warning，沒有誤宣稱已修正。
6. 完整雙模型 run `20260730-024616-...` 的整合必須誠實：Claude PASS；Gemini 的唯一 Critical 是 mojibake 判斷；18 個被指名檔案已用 strict byte-level UTF-8／BOM／CRLF／final CRLF／mojibake pattern 重新驗證，結果為有效，因此只能記為 reviewer 解碼誤判，不能假裝兩個模型都原始 PASS。
7. 其他 legacy Session cache manager 的根因描述必須正確：manager 本身非 `IDisposable`，多數只引用同一 process-wide ToolUtility singleton；eviction 不可擅自 Dispose shared singleton。非原子 `Get`→`Set` 是 correctness／performance debt；真正未完成的是 legacy singleton 的 Production host-shutdown owner／Phase 6 removal gate。
8. SPEC 必須保留可執行的 owner、validation matrix、good/base/bad、tests、wrong/correct 契約，不能只寫原則。
9. 所有本輪檔案必須為 UTF-8 without BOM、CRLF、final CRLF；Markdown fence 必須成對，JSON 必須可解析，`git diff --check` 必須通過。
10. 新 run artifacts 中 provider Session marker 與 local Windows identity 已移除，scan 必須為 0；不得在輸出中重新揭露其值。

## 已有本地驗證

```text
BYTE_ENCODING_OK
FENCES_OK
ADDED_SENSITIVE_OR_LOCAL_IDENTITY_MATCHES=0
RUN_LOCAL_IDENTITY_MATCHES=0
RUN_SENSITIVE_ASSIGNMENT_MATCHES=0
git diff --check = pass
task.json parse = pass
```

Production／Test 的先前 fresh evidence：ChurchReport 367 passed；Dynamics 230 passed、1 ordinary environment skip，該 LocalDB live contract 另行通過；Release build 0 warning／0 error。這些數字只需檢查文件是否一致，不需要在本次唯讀 review 重跑測試。

## 輸出格式

1. 第一行 `PASS` 或 `FAIL`。
2. `Critical`／`Warning`／`Info` 分組，每項附檔案／行號與具體矛盾或失敗時序。
3. 明確回答文件／SPEC 是否可作為後續 Phase 4～6 的權威解釋說明。
4. 明確確認 consumer flag false，以及 Embedded／Data8／`PowerPlatform.Dataverse.Client` retained。

任何敏感值殘留、Phase 完成度誇大、錯誤 owner 指導、把 shared singleton 從 Session eviction Dispose、或把 reviewer 解碼誤判寫成真實檔案損壞，均應判定 FAIL。
