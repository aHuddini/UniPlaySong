@echo off
rem Builds psf.dll (x86, static CRT) into src\Audio\Native\RetroChiptune\ and the test harness
rem into obj\. Needs an ares checkout at the pinned commit for its headers (nall, ps1\spu\spu.hpp,
rem ps1\cpu\cpu.hpp, types.hpp); see docs\dev_docs\features\PSF_ENGINE_BUILD.md.
rem
rem   build.cmd [path-to-ares-checkout]      default: %ARES_DIR%, else C:\Projects\ares
setlocal
cd /d "%~dp0"
if not "%~1"=="" set ARES_DIR=%~1
if "%ARES_DIR%"=="" set ARES_DIR=C:\Projects\ares
if not exist "%ARES_DIR%\nall\nall\nall.hpp" (
  echo ares checkout not found at %ARES_DIR% - see PSF_ENGINE_BUILD.md
  exit /b 1
)
call "C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars32.bat" >nul 2>&1
if not exist obj mkdir obj
set CXX=cl.exe /nologo /c /std:c++20 /EHsc /O2 /W1 /Iinclude /Iglue /I"%ARES_DIR%\nall" /I"%ARES_DIR%\ares"
set CC=cl.exe /nologo /c /O2 /W1 /DWIN32 /DLSB_FIRST=1 /DEMU_COMPILE /DEMU_LITTLE_ENDIAN /Iglue
%CXX% /Fo:obj\spu.obj spu\spu.cpp || exit /b 1
%CXX% /Fo:obj\spu_bridge.obj spu_bridge.cpp || exit /b 1
%CXX% /Fo:obj\cpu.obj cpu\cpu.cpp || exit /b 1
%CXX% /Fo:obj\cpu_bridge.obj cpu_bridge.cpp || exit /b 1
%CC% /Fo:obj\ctx.obj cpu\ctx.c || exit /b 1
%CC% /Fo:obj\ glue\psx_hw.c glue\eng_psf.c glue\psf2_stubs.c || exit /b 1
set OBJS=obj\spu.obj obj\spu_bridge.obj obj\cpu.obj obj\cpu_bridge.obj obj\ctx.obj obj\psx_hw.obj obj\eng_psf.obj obj\psf2_stubs.obj
link /nologo /Brepro /DLL /DEF:psf.def /OUT:..\..\src\Audio\Native\RetroChiptune\psf.dll /MACHINE:X86 %OBJS% || exit /b 1
cl.exe /nologo /std:c++20 /EHsc /O2 /W1 /Iglue /I"%ARES_DIR%\nall" /Fo:obj\ /Fe:obj\harness.exe harness.cpp %OBJS% || exit /b 1
del /q ..\..\src\Audio\Native\RetroChiptune\psf.exp ..\..\src\Audio\Native\RetroChiptune\psf.lib 2>nul
echo psf.dll built into src\Audio\Native\RetroChiptune\
