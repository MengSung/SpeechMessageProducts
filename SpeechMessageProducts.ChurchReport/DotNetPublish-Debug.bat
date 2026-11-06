@rem 檔案整體說明
@rem 
@rem 目的與定位：提供DotNetPublish-Debug選定的ChurchReport發佈組態，透過dotnet publish及既有步驟產生部署輸出；執行前須核對腳本中固定路徑、Debug／Release及發佈模式，不因名稱含Production就自動取得正式部署授權。
@rem 
@rem 主要功能與流程：
@rem 依下方原生宣告執行本檔所述工作；入口、條件及順序須與實際呼叫端一致，不能把示範或預覽視為已套用。
@rem 
@rem 使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
@rem 涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
dotnet publish -c Debug --output bin/Jesus-Output-Debug

pause
