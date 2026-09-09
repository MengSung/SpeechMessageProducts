# 需求
所有影響運作／真正失敗／未處理的例外與 Error/Critical，須寫入 Exception.log 並通知既有 LINE 管理者。Debug、Release 都啟用。排除已正常取消、成功恢復的 retry。永久規則寫入 AGENTS.md 與技術契約。
不得保留或傳送原始使用者資料、憑證、HttpContext、Exception 物件圖。落檔與 LINE 使用相同事件 ID；落檔先完成，LINE 有界背景派送並可取消。原始失敗語意不改變。檔案輪替保留上限，關機解除全域事件／靜態 owner，停止、drain、Dispose。
