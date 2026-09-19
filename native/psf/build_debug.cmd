@echo off
rem Debug build of the harness (symbols, no optimisation) into obj\debug\ for running under cdb.
rem   build_debug.cmd [path-to-ares-checkout]
setlocal
cd /d "%~dp0"
if not "%~1"=="" set ARES_DIR=%~1
if "%ARES_DIR%"=="" set ARES_DIR=C:\Projects\ares
call "C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars32.bat" >nul 2>&1
if not exist obj\debug mkdir obj\debug
set CXX=cl.exe /nologo /c /std:c++20 /EHsc /Od /Zi /W1 /Iinclude /Iglue /I"%ARES_DIR%\nall" /I"%ARES_DIR%\ares" /Fdobj\debug\harness.pdb
set CC=cl.exe /nologo /c /Od /Zi /W1 /DWIN32 /DLSB_FIRST=1 /DEMU_COMPILE /DEMU_LITTLE_ENDIAN /Iglue /Fdobj\debug\harness.pdb
%CXX% /Fo:obj\debug\spu.obj spu\spu.cpp || exit /b 1
%CXX% /Fo:obj\debug\spu_bridge.obj spu_bridge.cpp || exit /b 1
%CXX% /Fo:obj\debug\cpu.obj cpu\cpu.cpp || exit /b 1
%CXX% /Fo:obj\debug\cpu_bridge.obj cpu_bridge.cpp || exit /b 1
%CC% /Fo:obj\debug\ctx.obj cpu\ctx.c || exit /b 1
%CC% /FIstdarg.h /Fo:obj\debug\ glue\psx_hw.c glue\eng_psf.c glue\psf2_stubs.c || exit /b 1
%CXX% /Fo:obj\debug\harness.obj harness.cpp || exit /b 1
link /nologo /DEBUG /OUT:obj\debug\harness.exe obj\debug\harness.obj obj\debug\spu.obj obj\debug\spu_bridge.obj obj\debug\cpu.obj obj\debug\cpu_bridge.obj obj\debug\ctx.obj obj\debug\psx_hw.obj obj\debug\eng_psf.obj obj\debug\psf2_stubs.obj || exit /b 1
echo debug harness built: obj\debug\harness.exe
