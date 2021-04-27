@setlocal
@echo off
rem
rem The test targets files redirect projects to use the targets deployed
rem to the Experimental hive. By installing these targets, you can test
rem modifications easily without having to manually redeploy files to
rem your MSBuild directory.
rem
rem This script should be run as an administrator.
rem

echo This script should be run as an administrator and from the VS developer command prompt.

set D=%~dp0

rem Guess some directories for Visual Studio 16
call :docopy "%VSINSTALLDIR%\MSBuild\Microsoft\VisualStudio\v16.0"
if errorlevel 1 call :docopy "%SystemDrive%\VS\MSBuild\Microsoft\VisualStudio\v15.0"
if errorlevel 1 call :docopy "%ProgramFiles(x86)%\MSBuild\Microsoft\VisualStudio\v15.0"

call :docopy "%ProgramFiles(x86)%\MSBuild\Microsoft\VisualStudio\v14.0"

pause
exit /B 0

:docopy

if not exist "%~1" exit /B 1

set TARGET=%~1\Python Tools\

pushd "%D%..\Product\BuildTasks\TestTargets"
echo.
echo Copying:
echo     from %CD%
echo     to %TARGET%
if not exist "%TARGET%" mkdir "%TARGET%"
copy /Y "*.targets" "%TARGET%"
popd

exit /B 0
