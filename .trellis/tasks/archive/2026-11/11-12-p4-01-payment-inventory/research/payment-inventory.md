# P4-01 金流副本盤點

依據：04A §1 F4-F6、F18 及 §5 P4-01；主方案以本工作樹為準，舊來源僅唯讀核對。欄位比較限收費單，不把聯絡人、課程或通知副作用混入。

| 能力 | 主方案 `SpeechMessage.Payments*` | ChurchReport `Payments/` 與回呼 Controller | 舊 `Payment.Infrastructure` | 舊 `Tools/QPayWebhook.*.cs` | 舊 `AllotPlugIn` | 舊 `AllotWorkflow` | 舊 `AllotTlsWorkflow` |
|---|---|---|---|---|---|---|---|
| 建單 | `Providers/Sinopac/SinopacPaymentProvider.SendOrderAsync`；Nonce→AES→Sign，主方案 provider | `DonationPaymentCreateGatewayAdapter` 與 `MyPayController`／`TSPGController` 呼叫共用 Gateway；產品流程仍在 ChurchReport | `Providers/QPay/QPayGateway.cs`、`Processors/QPayProcessor.*.cs`、`Domain/QPay/*` 為舊付款副本，另有 MyPay／LinePay；不屬目前 Gateway | ATM／信用卡回呼處理，不是建單端 | 無建單責任；處理撥款 | `AllotProcessor.cs`／`QPayToolkit - 複製.cs` 是舊撥款端，不是目前建單 provider | 不直接建永豐訂單 |
| 付款查詢 | `SinopacApiService` 有 `OrderPayQuery = 5`，由共用 provider 執行 | `PaymentReturnController` 等入口使用共用流程，產品路由仍由 ChurchReport 管理 | `QPayProcessor.Sinopac.cs` 與 QPay models 保留舊處理器／DTO；與主方案契約分離 | 回呼依 PayToken 處理舊交易結果，不是目前共用查詢 API | 無付款查詢 | 舊工具含永豐交易協定副本 | 透過自家中繼站查詢 |
| 撥款查詢 | 目前無 `AllotQuery` API | 無共用產品掛勾；現有 ChurchReport 回寫不等於永豐撥款查詢 | 不承載 04A 指定的撥款工作流程 | 無撥款查詢責任 | `AllotProcessor.cs` 直接處理撥款比對並寫入收費單 | `AllotProcessor.cs`／`QPayToolkit - 複製.cs` 含舊直接 API 與模型 | `AllotApiClient.cs` 呼叫 `/Allot/AllotQuery` 中繼站；`AllotTlsProcessor.cs` 執行舊比對 |
| 回呼驗證／回寫 | 主方案已有簽章、AES 純協定 helper，`Payments.AspNetCore` 尚無端點 | `PaymentReturnController`、`MyPayController`、`TSPGController`；`PaymentCrmService` 保留產品欄位更新與通知 | 舊基礎設施副本，不是目前回呼入口 | `QPayWebhook.Atm.cs`、`QPayWebhook.Card.cs` 各自驗證並直接執行舊回寫 | 無付款 callback | 無付款 callback | 無付款 callback；只處理撥款 |
| HashCode／簽章 | `SinopacCrypto`、`SinopacSigner`、`SinopacRequestMapper` 在共用 provider；P4-01 固定向量鎖相容 | 使用共用 Gateway，不保留此處理器副本 | `Providers/QPay/QPayCommon.cs`、`QPayToolkit.cs` 等是舊協定副本，不作目前測試 oracle | 舊 callback 協定處理，沒有主方案共用邊界 | 撥款端使用舊協定副本 | `QPayToolkit - 複製.cs` 有舊 API／簽章支援 | 撥款由中繼站路徑處理 |

**收費單 callback 寫入欄位（成功路徑）：** 舊 ATM：`new_pay_date`、`new_fee_really_paid`、`new_big_chinese_number`、`new_pay_status`、`new_pay_way`、`new_description`。舊信用卡：上述付款狀態／金額／日期／描述欄位，另更新 `new_payment_records`、`new_q_paid_card_order_no`。ChurchReport `PaymentCrmService.UpdateFeeEntityWithPaymentResult`：`new_pay_status`、`new_fee_really_paid`、`new_difference_fee_paid`、`new_pay_date`、`new_pay_way`、`new_description`；失敗路徑只附加 `new_description`。信用卡舊流程另改課程報名狀態、把 token 寫入聯絡人並通知付款人，這些不是收費單欄位。

**舊撥款共同規則與差異：** 付款方式 100000001 依訂單編號是否以 `C` 開頭查 `C`／`M`，100000002 查 `A`，100000007 查 `M`；日期為收費日減 1 天至加 15 天並以今天為上限，收費日空白時舊起日為 `09991231`、迄日為今天；明細依類型比對對應訂單欄位，多筆符合時最後一筆勝出。回寫 `new_allot_amount = (Amount - Fee) / 100`、`new_allot_fee = Fee / 100`、`new_allot_date = AllotDate`、`new_allot_no = AllotNo`。案例與合成對照在 `SpeechMessage.Payments.Tests/Golden/Sinopac/allot-legacy-cases.json`；新規則的空白收費日起日為今天減 16 天，且空白訂單編號不比對，兩者均刻意不同於舊邏輯。所有樣本均為合成資料；獨立 Node.js 腳本計算密碼學向量，未編譯或執行舊程式。
