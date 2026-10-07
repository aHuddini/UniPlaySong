@echo off
rem SPIKE A: builds vt_producer.exe (x64) and vt_consumer32.dll (x86) into obj\.
setlocal
cd /d "%~dp0"
if not exist obj mkdir obj
set VS=C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build
cmd /c ""%VS%\vcvars64.bat" >nul 2>&1 && cl /nologo /EHsc /O2 /W3 /Fo:obj\ /Fe:obj\vt_producer.exe vt_producer.cpp" || exit /b 1
cmd /c ""%VS%\vcvars32.bat" >nul 2>&1 && cl /nologo /EHsc /O2 /W3 /LD /Fo:obj\ /Fe:obj\vt_consumer32.dll vt_consumer32.cpp" || exit /b 1
echo built obj\vt_producer.exe (x64) and obj\vt_consumer32.dll (x86)
