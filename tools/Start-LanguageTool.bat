@echo off
REM LanguageTool Server Launcher
REM Runs: java -jar languagetool-server.jar --port 8081 --allow-origin "*"

cd /d "%~dp0"

if not exist "languagetool-server.jar" (
    echo ERROR: languagetool-server.jar not found in %~dp0
    echo.
    echo Download from: https://languagetool.org/download/LanguageTool-stable.zip
    echo Extract languagetool-server.jar to this folder.
    echo.
    pause
    exit /b 1
)

echo Starting LanguageTool server on port 8081...
echo Press Ctrl+C to stop.
echo.

java -Dfile.encoding=utf-8 -Xms256m -Xmx512m -jar "languagetool-server.jar" --port 8081 --allow-origin "*"

echo.
echo Server stopped. Press any key to close...
pause