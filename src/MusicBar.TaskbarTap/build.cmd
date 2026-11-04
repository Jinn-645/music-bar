@echo off
rem Builds MusicBar.TaskbarTap.dll (x64) with the MSVC toolset. Usage: build.cmd <output folder>
setlocal
set "OUT=%~1"
if "%OUT%"=="" set "OUT=%~dp0bin"
if not exist "%OUT%" mkdir "%OUT%"

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
set "VCVARS="
rem "call" keeps cmd from stripping the quotes around a path with parentheses.
for /f "usebackq tokens=*" %%i in (`call "%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VCVARS=%%i\VC\Auxiliary\Build\vcvars64.bat"
if not defined VCVARS goto :notools
if not exist "%VCVARS%" goto :notools
rem vcvars looks for vswhere on PATH and complains harmlessly when it is not there.
call "%VCVARS%" >nul 2>nul || exit /b 1

cl /nologo /std:c++17 /EHsc /O2 /W3 /MT /LD /DUNICODE /D_UNICODE "%~dp0TaskbarTap.cpp" ^
   /Fo"%OUT%\\" /Fe"%OUT%\MusicBar.TaskbarTap.dll" ^
   /link /DEF:"%~dp0TaskbarTap.def" ole32.lib oleaut32.lib user32.lib || exit /b 1
del "%OUT%\MusicBar.TaskbarTap.exp" "%OUT%\MusicBar.TaskbarTap.lib" "%OUT%\TaskbarTap.obj" 2>nul
exit /b 0

:notools
echo error TAP0001: MSVC x64 tools not found; install the "Desktop development with C++" workload.
exit /b 1
