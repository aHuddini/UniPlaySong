@echo off
rem SPIKE A: builds vt_producer.exe (x64) and vt_consumer32.dll (x86) into obj\.
setlocal
cd /d "%~dp0"
if not exist obj mkdir obj
set VS=C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build
cmd /c ""%VS%\vcvars64.bat" >nul 2>&1 && cl /nologo /EHsc /O2 /W3 /Fo:obj\ /Fe:obj\vt_producer.exe vt_producer.cpp" || exit /b 1
cmd /c ""%VS%\vcvars32.bat" >nul 2>&1 && cl /nologo /EHsc /O2 /W3 /LD /Fo:obj\ /Fe:obj\vt_consumer32.dll vt_consumer32.cpp" || exit /b 1
rem Spike B renderer: needs projectM 4.1.8 + GLEW 2.2.0 built for x64 into %PROJECTM_PREFIX% (default C:\Projects\projectm-src\install).
if "%PROJECTM_PREFIX%"=="" set PROJECTM_PREFIX=C:\Projects\projectm-src\install
if exist "%PROJECTM_PREFIX%\include\projectM-4" (
  cmd /c ""%VS%\vcvars64.bat" >nul 2>&1 && cl /nologo /EHsc /O2 /W3 /std:c++17 /I"%PROJECTM_PREFIX%\include" /Fo:obj\ /Fe:obj\vt_milkdrop.exe vt_milkdrop.cpp /link /LIBPATH:"%PROJECTM_PREFIX%\lib"" || exit /b 1
  copy /y "%PROJECTM_PREFIX%\bin\*.dll" obj\ >nul
)
echo built obj\vt_producer.exe (x64), obj\vt_consumer32.dll (x86), obj\vt_milkdrop.exe (x64, if projectM found)
