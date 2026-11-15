# P5-06 出席盤點

2026-11-15 11:30 Asia/Taipei (UTC+08:00)：只讀PresentRecordLinePlugIn兩原檔、20261115-030842 export-legacy及既有結構。舊註冊唯一步驟891bf94a-f80d-f011-8143-00155d006608，assembly=PresentRecordLinePlugInDatabase、type=PresentRecordLinePlugIn.PresentRecordLinePlugIn；Update/new_present_record、40、同步mode0、rank1、未篩選、停用。PreImage名稱及alias=PresentRecordLineImage、未明列欄位。另一同名assembly無步驟，不猜配。

舊外層.cs:32-50：Depth>2且非Create返回，最外catch吞錯；Class.cs:192-250先讀前像、借user SDK再Retrieve。:257-269 Create推播為註解，無實際通知；:274-303 Update以C#字串差異比new_paster_reply，讀本人lookup後multicast一則；:319-337文字為lookup.Name＋空格＋代禱與CRLF牧者回應。:373-392只讀本人contact.new_lineid，沒有其他角色收件者。SetupSavedFlag未被呼叫，沒有業務寫入；外部效果只有LINE。兩同名部署組件沒有可供比較的另一份本地入口，不能宣稱相同。

Class.cs:205/230空token建構式走寫死正式token，:75常數未使用；本階段不執行舊入口。P5-02三小時工具修正已結案，沿用reading，沒有legacy.json。新通知改正式Channel＋NotifyComposedAsync及本人push；來源教會從出席list組長account鏈決定，再驗本人歸屬，錯配／未綁定正常拒絕，未知錯誤保留原例外重拋。新非同步40/mode1、篩選new_paster_reply、PreImage同欄，舊同步改非同步為05A指定。Create無副作用不新增註冊。

讀欄位：new_present_record.new_paster_reply／new_explanation為文字，new_contact_new_present_record→contact、new_list_new_present_record→list；list.new_contact_family_leader_list→contact；contact.parentcustomerid→account，new_lineid、new_preferredlanguage為文字；account語系／Channel lookup沿共用查詢。沒有日期欄位，無日期行為轉換。無觸發列以外業務寫入，不搬未呼叫SendLineReplyReport／SetupSavedFlag。六案：差異、同值、Create、Depth3、錯教會、未綁定。核定Created_NotifiesMemberOnce測試明確驗Create零、首次回應Update一則，避免按名稱增加業務行為。live=deferred，驗收以離線／CAS／打包／停用部署verify=0。
