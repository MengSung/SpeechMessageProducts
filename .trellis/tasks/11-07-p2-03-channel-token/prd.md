# P2-03 Channel Token 加密與寫入工具

## 目標與來源

依正式計畫03的P2-03及附錄A.3／A.5／C.2，建立net462與net10可互通的
Channel Token封包，以及受白名單保護的Deployment寫入入口。P2整體目標不變；
本WP不依賴P2-04實機推播，但不能以純核心驗證取代Deployment與實機證據。

## 必要需求

- AES-256-CBC／PKCS7，新鮮RNG IV；64-byte金鑰前半AES、後半HMAC。
- `v1:base64(iv16):base64(ciphertext):base64(mac32)`；
  HMAC輸入為ASCII(v1)接IV接密文；固定時間比對成功後才能解密。
- 所有驗證失敗固定ChannelTokenProtectionException，不回顯任何輸入、不帶敏感inner。
- net10使用CryptographicOperations.FixedTimeEquals；
  net462逐byte XOR累加，長度不同也跑完。
- 兩目標共用合成固定向量；真正雙向交叉解密、篡改、錯key、版本、輸入邊界、
  例外保密、併行A/B與資源／緩衝區生命週期均需證據。
- protect-token只由stdin讀token與channel secret，金鑰由既有受控secret來源取得，
  以new_channelid替代鍵Upsert；assign-channel以資料庫ID設定account lookup。
- CLI沿用goodshepherdback白名單、同組織寫鎖、唯一lease、caller取消、共用例外入口，
  不讀Plugin DLL、不輸出明密文／key／secret／完整SDK錯誤；不得破壞既有命令。

## 驗收

- [ ] 雙目標加密核心與相容／安全／生命週期測試通過。
- [ ] protect-token、assign-channel離線正反例、最小欄位、Upsert身分、鎖與清理通過。
- [ ] 實機操作範圍、備份／回復與測試資料清理確認，必要外部驗證有實際證據。
- [ ] 繁中文件、UTF-8無BOM／CRLF、I18n、duplicate、coverage與受影響整合通過。
- [ ] 回報、同張P2摘要、任務紀錄與本地提交完成。

沒有必要憑證或仍有既有離線限制時，明列缺口而不標整包完成；先繼續其他獨立項目。
