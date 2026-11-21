# 階段6 ChurchReport 頁面對照

本工具依06A §6A序列驗收P5與P6，**會建立當次測試資料並向測試Channel推播**。只允許goodshepherdback，不部署或啟停任何D365步驟／流程。產品與共用安全守門不改。

先建置現有Deployment與兩個ChurchReport Release輸出；P5工作樹必須位於P6同層、起點9b7ac0a0且來源乾淨。Node／本機Edge已安裝，playwright-core位於`%USERPROFILE%/.speechmessage/playwright/node_modules/`，不在repo安裝或下載瀏覽器。

```powershell
node tools/PageAcceptance/P6/Run-P6PageAcceptance.cjs
```

Deployment User Secrets提供TestLineUserId、ShadowTestChurchId、ShadowTestContactId及TestChannelToken；只在記憶體取用，不經命令列／輸出。隨機登入帳密只存記憶體，寫入當次新會員後隨資料刪除，不讀既有帳密。
依使用者本會話核定：Host User Secrets原始位元組先以Windows DPAPI CurrentUser保護，存repo外`%USERPROFILE%/.speechmessage/evidence/p6-secrets/`並解開驗證，再直接暫存測試token到指定User Secrets；finally還原並逐位元組驗證。受保護備份保留供復原；若輸出P6_CLEANUP_OR_SECRET_RESTORE_FAILED，不能宣稱殘留0，須立即回報並使用該備份復原。

Run-P6PageAcceptance.cjs負責原有情境與正規化，Edge Browser 建立委派給 PageAcceptance/Shared/EdgeBrowser.cjs；P6AcceptanceOwner.cjs擁有兩輪、Development Host 與記憶體設定；Host與User Secrets載入共用 DevelopmentHost.cjs；P6Fixtures.cjs只建立原有合成情境資料。共用SDK橋接位於PageAcceptance/Shared/FixtureBridge.cjs，將P6FixtureOwner.cs以本機SDK編譯至ignored artifacts後用JSON管線傳遞，不新增csproj。SDK owner驗證D34測試身分、只更新／刪除本輪確認未存在後建立的ID，所有lookup限測試教會／會友與本輪資料。

每輪四情境：課程、主日（底線／at各一次）、小組、帳密登入週報。比較所有回應欄位、明列寫入欄位、週報主要文字與資料載入回應；只允許ID代號、時間欄位是否有寫。result／onboardtype內只替換明確時間片段，保留結果文字。原始result.json不覆寫；若在完成操作後調整正規化，另存normalized-result.json，禁止改實際回應冒充相同。

產物在`artifacts/p6/page-acceptance-<UTC>/`；截圖可能含測試聯絡人姓名／照片，只存本機，不提交。兩輪各查測試LINE月用量差作推播數補證，並搭配使用者送達截圖；計費用量不是逐則送達回執。2026-11-17兩輪各+2，手機確認課程／小組各1則。未偽造LIFF：桌面QR頁顯示原未初始化訊息，簽到以同session原POST完成，不宣稱手機LIFF登入已驗收。實際結果／限制及SHA見階段頁面一頁回報。

EOF／finish時SDK逆序刪除並讀回確認；Host／Edge由各owner finally關閉。reset可在兩輪間刪除本輪資料而保留秘密租約；最終close才還原User Secrets。遇操作失敗不自動重送Create；兩次同一步失敗依06A §7回報。正常執行後殘留與秘密還原狀態必須明列，不能只靠程式exit 0。

階段審查的必要生命週期回歸：`node tools/PageAcceptance/P6/Tests/P6Lifecycle.cjs`，四案注入ready／cleanup逾時、cleanup失敗及context關閉例外，替換外部邊界但執行真正來源；不連線、不讀秘密。以`before`參數只讀3ea8a133可重現4紅，目前4綠。Bridge所有失敗先EOF及有界drain，超時才終止並將秘密狀態明列未確認，不以kill冒充還原成功。
