@echo off
REM Log Analysis Tool - Windows Batch File
REM Automatically finds and analyzes Playnite extensions.log

echo ========================================
echo Log Analysis Tool for UniPlaySong
echo ========================================
echo.

REM Find Python
where python >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Python not found in PATH
    echo Please install Python 3.6+ or add it to your PATH
    pause
    exit /b 1
)

REM Get Playnite log file path
set "APPDATA_PATH=%APPDATA%"

echo Looking for log files in:
echo   %APPDATA_PATH%\Playnite\
echo   %APPDATA_PATH%\Playnite\Extensions\
echo.

REM Get the script directory
set "SCRIPT_DIR=%~dp0"
set "SCRIPT_PATH=%SCRIPT_DIR%analyze_logs.py"

if not exist "%SCRIPT_PATH%" (
    echo ERROR: analyze_logs.py not found at:
    echo %SCRIPT_PATH%
    echo.
    echo Please make sure the script is in the same directory as this batch file.
    pause
    exit /b 1
)

echo Running log analysis...
echo The script will automatically find all relevant log files.
echo.

REM Run the Python script (no arguments - it will auto-find log files)
python "%SCRIPT_PATH%"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR: Analysis failed!
    echo Check the error messages above.
    pause
    exit /b 1
)

echo.
echo ========================================
echo Analysis complete!
echo ========================================
echo.
echo Reports saved to:
echo %APPDATA_PATH%\Playnite\log_analysis\
echo.
echo The tool automatically searches for:
echo   - extensions.log
echo   - playnite.log
echo   - UniPlaySong.log (in extension folder)
echo   - PlayniteSound.log (in extension folder)
echo.
pause

