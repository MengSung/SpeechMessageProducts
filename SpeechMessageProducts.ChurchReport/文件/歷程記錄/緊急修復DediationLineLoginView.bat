@echo off
chcp 65001 >nul

color 0A
echo שÝששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש‗
echo שר    DediationLineLoginView ÷ע«ז­×´_¤u¨ד                        שר
echo שר    Emergency Fix for DediationLineLoginView                  שר
echo שדשששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששו
echo.

REM ÀË¬d÷Þ²z­ûÅv­­
net session >nul 2>&1
if %errorLevel% neq 0 (
    color 0C
    echo שÝששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש‗
    echo שר  ? ¿ש»~¡G»Ý­n÷Þ²z­ûÅv­­                                      שר
    echo שר  ½Ð¥H¡u¥H¨t²Î÷Þ²z­û¨­¤À°ץ¦ז¡v­«·s°ץ¦ז¦¹¸}¥»                   שר
    echo שדשששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששו
    pause
    exit /b 1
)

echo [¨BÆJ 1/6] °±¤מ IIS ×A°È...
echo ????????????????????????????????????????????????????????????
iisreset /stop
timeout /t 2 /nobreak >nul
echo ? IIS ×A°È¤w°±¤מ
echo.

echo [¨BÆJ 2/6] °±¤מÀ³¥Îµ{¦¡¦À...
echo ????????????????????????????????????????????????????????????
powershell -Command "Import-Module WebAdministration; Stop-WebAppPool 'ChurchReport' -ErrorAction SilentlyContinue"
timeout /t 2 /nobreak >nul
echo ? À³¥Îµ{¦¡¦À¤w°±¤מ
echo.

echo [¨BÆJ 3/6] ²M°£¼È¦sÀÉ®×...
echo ????????????????????????????????????????????????????????????
REM ²M°£ ASP.NET ¼È¦s
if exist "%TEMP%\Temporary ASP.NET Files" (
    rd /s /q "%TEMP%\Temporary ASP.NET Files" 2>nul
    echo ? ¤w²M°£ ASP.NET ¼È¦sÀÉ®×
) else (
    echo ??  µL»Ý²M°£¼È¦sÀÉ®×
)
echo.

echo [¨BÆJ 4/6] ±Ò°ÊÀ³¥Îµ{¦¡¦À...
echo ????????????????????????????????????????????????????????????
powershell -Command "Import-Module WebAdministration; Start-WebAppPool 'ChurchReport'"
timeout /t 2 /nobreak >nul
echo ? À³¥Îµ{¦¡¦À¤w±Ò°Ê
echo.

echo [¨BÆJ 5/6] ±Ò°Ê IIS ×A°È...
echo ????????????????????????????????????????????????????????????
iisreset /start
timeout /t 3 /nobreak >nul
echo ? IIS ×A°È¤w±Ò°Ê
echo.

echo [¨BÆJ 6/6] ÅחÃÒ×A°È×¬÷A...
echo ????????????????????????????????????????????????????????????

REM ÀË¬d IIS ×A°È
sc query W3SVC | findstr "RUNNING" >nul
if errorlevel 1 (
    color 0C
    echo ? IIS ×A°È¥¼¹B¦ז
    echo.
    echo ¹Á¸Õ¤ג°Ê±Ò°Ê...
    net start W3SVC
    timeout /t 2 /nobreak >nul
    sc query W3SVC | findstr "RUNNING" >nul
    if errorlevel 1 (
        echo ? µL×k±Ò°Ê IIS ×A°È¡I
        goto :error
    ) else (
        color 0A
        echo ? IIS ×A°È¤w¦¨¥\±Ò°Ê
    )
) else (
    echo ? IIS ×A°È¥¿¦b¹B¦ז
)

REM ÀË¬dÀ³¥Îµ{¦¡¦À
powershell -Command "Import-Module WebAdministration; $state = (Get-WebAppPoolState 'ChurchReport').Value; if ($state -eq 'Started') { exit 0 } else { exit 1 }" >nul 2>&1
if errorlevel 1 (
    echo ??  À³¥Îµ{¦¡¦À¥i¯א¥¼¥¿½T±Ò°Ê
) else (
    echo ? À³¥Îµ{¦¡¦À¥¿¦b¹B¦ז
)

REM ÀË¬d Port 479
netstat -ano | findstr ":479.*LISTENING" >nul
if errorlevel 1 (
    echo ??  Port 479 ¥¼³Q÷ÊÅ¥
    echo    ½ÐÀË¬d IIS ÷פ¯¸¸j©w
) else (
    echo ? Port 479 ¥¿¦b÷ÊÅ¥
)
echo.

color 0B
echo שÝששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש‗
echo שר  ? ÷ע«ז­×´_§¹¦¨¡I                                            שר
echo שדשששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששו
echo.

echo ¡i´ת¸Õ¨BÆJ¡j
echo ????????????????????????????????????????????????????????????
echo.
echo 1. ¥»¾ק´ת¸Õ¡]¦b¦ר×A¾¹¤W¡^:
echo    ¶}±ÒÂsÄ‎¾¹³X°Ý:
echo    https://localhost:479/Dedication/DediationLineLoginView/test
echo.
echo 2. ¹ך»Ú LIFF URL ´ת¸Õ¡]¦b LINE ¤¤¡^:
echo    https://jesusback.speechmessage.com.tw:479/Dedication/DediationLineLoginView/2007156647-OYnN8BKy
echo.
echo 3. ¶}±Ò Chrome DevTools (F12):
echo    - Console ¼ÐÅÒ: ¬d¬Ý JavaScript ¿ש»~
echo    - Network ¼ÐÅÒ: ¬d¬Ý½Ð¨D×¬÷A
echo.
echo 4. °O¿‎¥H¤U¸ך°T:
echo    ¡¼ ÂsÄ‎¾¹Åד¥Ü¤°»ע¡H (­¶­±/404/500/×Å¥Õ/µL×k³s½u)
echo    ¡¼ Status Code: _______
echo    ¡¼ Console ¬O§_¦³¿ש»~¡H
echo    ¡¼ Network ¤¤¬O§_¬Ý¨ל DediationLineLoginView ½Ð¨D¡H
echo.

echo ¡i¦p×G¤´µM¥¢±Ñ¡j
echo ????????????????????????????????????????????????????????????
echo.
echo ½Ð°ץ¦ז§¹¾ד¶EÂ_:
echo    ¶EÂ_DediationLineLoginView­¶­±¥¼Åד¥Ü.ps1
echo.
echo ©Î¬d¬Ý¤י»x:
echo    Logs\Trace.log
echo    Logs\stdout*.log
echo.

goto :end

:error
color 0C
echo.
echo שÝששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש‗
echo שר  ? ­×´_¥¢±Ñ¡I                                                שר
echo שדשששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששו
echo.
echo ¥i¯א×÷°ÝÃD:
echo  1. IIS ¥¼¦w¸Ë©Î°t¸m¤£¥¿½T
echo  2. ChurchReport À³¥Îµ{¦¡¦À¤£¦s¦b
echo  3. Åv­­¤£¨¬
echo  4. ¨ה¥L IIS °t¸m°ÝÃD
echo.
echo ½ÐÁpµ¸¨t²Î÷Þ²z­û©Î¬d¬Ý¨Æ¥ףÀËµר¾¹¤¤×÷¿ש»~¡C
echo.

:end
pause
