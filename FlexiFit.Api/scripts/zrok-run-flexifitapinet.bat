@echo off
title Zrok Tunnel Manager - FlexiFit API
color 0A
cls

echo ===================================================
echo             ZROK TUNNEL SETUP STARTED              
echo ===================================================
echo.

cd /d C:\zrok
if errorlevel 1 (
    echo [ERROR] Hindi mahanap ang C:\zrok folder!
    pause
    exit /b 1
)
echo [OK] Nasa C:\zrok folder na.
echo.

echo [CHECK] Tinitignan kung tumatakbo ang API sa localhost:8090...
curl -s -o nul -w "%%{http_code}" http://localhost:8090/swagger/v1/swagger.json > temp.txt
set /p status=<temp.txt
del temp.txt

if "%status%"=="200" (
    echo [OK] API is running on localhost:8090
) else (
    echo [WARNING] API not detected on localhost:8090!
    echo          Please start your API first (dotnet run)
    echo.
    choice /C YN /M "Continue anyway"
    if errorlevel 2 (
        echo Exiting...
        pause
        exit /b 0
    )
)
echo.

echo [STARTING] Sinisimulan ang zrok tunnel...
echo Public Link: https://flexifitapinet.shares.zrok.io
echo.
echo ===================================================
echo  ✅ TUNNEL ACTIVE: https://flexifitapinet.shares.zrok.io
echo  📌 PAALALA: Huwag isasara ang window na ito!
echo  🔄 API must be running on localhost:8090
echo ===================================================
echo.

.\zrok2 share public localhost:8090 -n public:flexifitapinet

pause