# P5-04 國際化 Plugin

依05A修訂2 P5-04及各模組固定做法。沒有舊入口，basis=new；LocalizedTextChangedPlugin三訊息更新UTC整秒，換教會觸及兩者；MemberLanguageChangedPlugin綁定者換alias，未綁不呼叫；ChurchLocaleChangedPlugin同步驗證四欄與非同步冪等宮格待辦。只部署停用Shadow，live=deferred依D5-19，不啟用Live。

所有核定同名測試及隔離、失敗原樣拋出、部分信任與打包都須驗證。
