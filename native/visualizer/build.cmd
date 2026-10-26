@echo off
rem Builds the visualizer into src\Audio\Native\Visualizer\:
rem   UniPlaySongVisualizer.exe        x64 renderer (projectM, offscreen OpenGL, shared D3D11 textures)
rem   UniPlaySongVisualizerBridge.dll  x86, loaded into Playnite's 32-bit process to show the frames
rem   projectM-4.dll, glew32.dll       the renderer's libraries, from build_deps.cmd
rem Static MSVC runtime (/MT) everywhere, so nothing needs the Visual C++ redistributable.
rem See docs\dev_docs\features\VISUALIZER_ENGINE_BUILD.md.
setlocal
cd /d "%~dp0"
set PREFIX=%~dp0obj\deps\install
set OUT=%~dp0..\..\src\Audio\Native\Visualizer
if not exist "%PREFIX%\include\projectM-4\projectM.h" (
  echo projectM not built - run build_deps.cmd first
  exit /b 1
)
set VS=C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build
if not exist obj mkdir obj
if not exist "%OUT%" mkdir "%OUT%"

rem A version resource on both: a tiny unsigned binary with none is what antivirus heuristics flag.
cmd /c ""%VS%\vcvars64.bat" >nul 2>&1 && rc /nologo /fo obj\renderer.res version.rc && cl /nologo /EHsc /O2 /W3 /MT /std:c++17 /I"%PREFIX%\include" /Fo:obj\ /Fe:"%OUT%\UniPlaySongVisualizer.exe" renderer.cpp obj\renderer.res /link /Brepro /LIBPATH:"%PREFIX%\lib"" || exit /b 1
cmd /c ""%VS%\vcvars32.bat" >nul 2>&1 && rc /nologo /DVIS_BRIDGE /fo obj\bridge.res version.rc && cl /nologo /EHsc /O2 /W3 /MT /LD /Fo:obj\ /Fe:"%OUT%\UniPlaySongVisualizerBridge.dll" bridge.cpp obj\bridge.res /link /Brepro" || exit /b 1
copy /y "%PREFIX%\bin\projectM-4.dll" "%OUT%\" >nul || exit /b 1
copy /y "%PREFIX%\bin\glew32.dll" "%OUT%\" >nul || exit /b 1
del /q "%OUT%\*.exp" "%OUT%\*.lib" 2>nul

rem The shipped styles: two MilkDrop presets (make_presets.py makes six), the Jukebox Fountain's style file
rem (bench\tools\export_t8.mjs) and the music player's spectrum strip (UPS - Seek Spectrum.vis).
if not exist "%OUT%\presets" mkdir "%OUT%\presets"
copy /y "presets\UPS - Glow Bars.milk" "%OUT%\presets\" >nul || exit /b 1
copy /y "presets\UPS - Fountain.milk" "%OUT%\presets\" >nul || exit /b 1
copy /y "presets\UPS - Jukebox Fountain.vis" "%OUT%\presets\" >nul || exit /b 1
copy /y "presets\UPS - Seek Spectrum.vis" "%OUT%\presets\" >nul || exit /b 1
echo built into src\Audio\Native\Visualizer\
