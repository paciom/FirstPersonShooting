@echo off
rem Batch-builds the Windows IL2CPP player headless.
rem vcvars64 from VS 2022 Build Tools (the 2026 toolset is broken for IL2CPP),
rem -nographics because batchmode crashed initializing D3D12 on the Quadro K600.
rem Editor must be CLOSED (project lock). Log: Logs\windows_audio_build.log
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1
"C:\Program Files\Unity\Hub\Editor\6000.5.0f1\Editor\Unity.exe" -batchmode -nographics -projectPath "D:\Claude\FirstPersongShooting" -buildTarget Win64 -executeMethod WindowsBuilder.BuildBatch -logFile "D:\Claude\FirstPersongShooting\Logs\windows_audio_build.log"
exit /b %errorlevel%
