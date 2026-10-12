@echo off
rem Builds the visualizer renderer's third-party libraries - GLEW 2.2.0 and projectM 4.1.8, x64 Release - from the
rem source tarballs pinned in lib\source, into obj\deps\install. Run once before build.cmd.
rem
rem Static MSVC runtime throughout (/MT): the renderer must start on a PC without the Visual C++ redistributable,
rem which Playnite does not install. projectM's playlist library is not built - the renderer loads one preset file.
rem
rem GLEW stays a DLL on purpose: projectM never calls glewInit() and relies on the host having done it, which only
rem works when the renderer and projectM share one glew32.dll. A static GLEW in each binary leaves projectM's copy
rem uninitialised and crashes on its first GL call.
rem
rem Needs Visual Studio 2022 (C++ workload) and CMake on PATH. Uses Windows' own tar (System32, Windows 10+): Git's GNU tar,
rem if it comes first on PATH, reads "C:\..." as a remote host.
rem See docs\dev_docs\features\VISUALIZER_ENGINE_BUILD.md.
setlocal
cd /d "%~dp0"
set SRC=%~dp0..\..\lib\source
set DEPS=%~dp0obj\deps
set PREFIX=%DEPS%\install
if exist "%DEPS%" rmdir /s /q "%DEPS%"
mkdir "%DEPS%\src" || exit /b 1

"%SystemRoot%\System32\tar.exe" -xzf "%SRC%\glew-source-2.2.0.tgz" -C "%DEPS%\src" || exit /b 1
"%SystemRoot%\System32\tar.exe" -xzf "%SRC%\projectm-source-4.1.8.tar.gz" -C "%DEPS%\src" || exit /b 1

rem GLEW's CMake predates the MSVC_RUNTIME_LIBRARY policy, so its runtime is set through the release flags.
cmake -S "%DEPS%\src\glew-2.2.0\build\cmake" -B "%DEPS%\build\glew" -G "Visual Studio 17 2022" -A x64 ^
  -DCMAKE_POLICY_VERSION_MINIMUM=3.5 -DBUILD_UTILS=OFF -DCMAKE_INSTALL_PREFIX="%PREFIX%" ^
  -DCMAKE_C_FLAGS_RELEASE="/MT /O2 /Ob2 /DNDEBUG" || exit /b 1
cmake --build "%DEPS%\build\glew" --config Release --target install || exit /b 1

cmake -S "%DEPS%\src\projectm-4.1.8" -B "%DEPS%\build\projectm" -G "Visual Studio 17 2022" -A x64 ^
  -DCMAKE_PREFIX_PATH="%PREFIX%" -DCMAKE_INSTALL_PREFIX="%PREFIX%" -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded ^
  -DBUILD_SHARED_LIBS=ON -DENABLE_PLAYLIST=OFF -DENABLE_SDL_UI=OFF -DBUILD_TESTING=OFF -DBUILD_DOCS=OFF ^
  -DENABLE_CXX_INTERFACE=OFF -DENABLE_GLES=OFF || exit /b 1
cmake --build "%DEPS%\build\projectm" --config Release --target install || exit /b 1

echo GLEW + projectM built into %PREFIX%
