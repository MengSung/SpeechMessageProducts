# P5-04 國際化 Plugin 驗收

2026-11-15 10:00 Asia/Taipei (UTC+08:00)：依05A修訂2完成三外殼、純規則與共用RichMenuAlias、MemberRichMenuService。沒有舊入口，basis=new；固定六個情境共用 scenarios.json／expected.json，沒有legacy.json。文字變更更新前後教會UTC整秒；語言換綁、四項語系設定驗證與冪等待辦經規則及外殼驗證。

驗證：Core Release net48／net10.0各201/201，Plugins347/347（含部分信任），Deployment768/768；Localization核定名稱45/45。FileVersion=1.0.0.5，DLL SHA256 BEF5402520AF31051A576FE23B58FFF4EC04BDE60462AB3BF97EACA657D035CB，打包檢查通過。52b49678 在固定C:/smv/guard八項全通過，guard-52b49678-full.log SHA256 79b2cdae10e9a61c3b41d50ec60dab79a10b917f5703edd8591d594a01b35ef0。

實機：goodshepherdback plan→apply→verify=0。D3659.1建立步驟固定初始state0，原Create傳status2遭拒；已用同ID兩項原子交易建立後停用，故障回歸確認第二項失敗整筆回滾。六個新步驟皆停用、四映像、無secure；export-legacy確認舊步驟0啟用、舊流程0啟用。只寫部署metadata，沒有業務seed／推播殘留。依D5-19 live=deferred、differences=null，沒有宣稱實機影子通過。

證據：artifacts/d365/goodshepherdback/20261115-015634-3922414-9ea9a79f1e354cebb0768e2511d2fa4a/plan.json SHA256 987ad3da03a6197ec56c18e5077d6886015f56b511a218f1875210d241f7560a；artifacts/d365/goodshepherdback/20261115-015930-1366659-4213b349b5cb411989ad37c7c5fc70b6/export.json SHA256 a1380c9b28c11dfa6f5fd70f79911c9b8a960633dbf1eb04975949fca7a94025。本機另有 p5-04-apply-final.log、p5-04-verify-final.log、p5-04-legacy-final.log；原故障證據 p5-04-step-fault.log，均不進版控。

限制／後續：alias唯讀盤點符合0／不符4，未改資料，已登錄P5-PROP-003。RichMenuGenerate只驗正確冪等待辦及Shadow，尚無正式worker scope接線的端到端消費證據。更正早期09:03誤述：Deployment36失敗來自本包清單格式，95d58ec9已修復。下一包P5-05；無需使用者操作。
