@echo off
REM Native Windows IL2CPP player.
REM
REM IL2CPP auto-selects the NEWEST Visual Studio it can find, and VS 2026's
REM only MSVC toolset (14.51.36231) is missing vcruntime.h -- the C++ phase
REM dies with fatal error C1083 about 30 s in. Launching Unity from a VS 2022
REM Build Tools environment pins the complete 14.44 toolset instead, because
REM IL2CPP honours the toolchain it inherits.
REM
REM The builder exits the editor itself, so do NOT add -quit.

setlocal
set "PROJ=%~dp0.."
set "UNITY=C:\Program Files\Unity\Hub\Editor\6000.5.0f1\Editor\Unity.exe"
set "VCVARS=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"

if not exist "%VCVARS%" (
  echo [build_windows] VS 2022 Build Tools not found at "%VCVARS%".
  exit /b 1
)
call "%VCVARS%" >nul || exit /b 1

echo [build_windows] toolset: %VCToolsVersion%
"%UNITY%" -batchmode -projectPath "%PROJ%" -buildTarget Win64 ^
  -executeMethod WindowsBuilder.BuildBatch ^
  -logFile "%PROJ%\Logs\windows_build.log"
set ERR=%ERRORLEVEL%
echo [build_windows] Unity exit code %ERR%
exit /b %ERR%
