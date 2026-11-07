# P2-03 Channel Token 加密與寫入工具

## 目標與來源

依正式計畫03的P2-03及附錄A.3／A.5／C.2，建立net462與net10可互通的
Channel Token封包，以及受白名單保護的Deployment寫入入口。P2整體目標不變；
本 WP 依 3.4 不需要實機，寫入實測集中於 P2-04 前置與 P2-12；仍須驗證 Deployment 離線契約。

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
- [x] 本包實機不適用，依 3.4 移至 P2-04 前置、P2-12。
- [ ] 繁中文件、UTF-8無BOM／CRLF、I18n、duplicate與受影響整合通過；既有專案不另設coverage門檻。
- [ ] 回報、同張P2摘要、任務紀錄與本地提交完成。

沒有必要憑證或仍有既有離線限制時，明列缺口而不標整包完成；先繼續其他獨立項目。

## 3.4 先寫的測試（03 P2-03）
RoundTrip_Net462AndNet10_Compatible、Tampered_Ciphertext_Throws、Tampered_Mac_Throws、WrongKey_Throws、UnknownVersion_Throws、Exception_NeverContainsSecret、Mac_CompareIsConstantTime。兩個目標同名通過；protect-token／assign-channel 的白名單、stdin、Upsert 以假服務驗證。

## 完成定義（00 §7.1，不得加碼）
- [x] 1. 本 WP 段落列出的測試全部同名存在並通過（雙目標專案兩個目標都過）
- [ ] 2. 00 §8.1 全部命令在乾淨複本（00 §8.2）通過；提交待驗證
- [x] 3. 沒有新專案，不適用；既有覆蓋率證據保留
- [x] 4. 實機不適用
- [ ] 5. 一頁回報已寫、進度頁已更新

## 時間盒
開始：2026-11-07 21:10 Asia/Taipei；3.4 生效起半天或 15 個提交。

## 3.4 收尾現況
四個指定名稱已補為Fact，原theory案例全部保留；雙目標各33個加密案例通過。本包提交後乾淨複本守門仍待執行，不以先前基線冒充本次驗證。
