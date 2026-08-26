@echo off
setlocal

if "%~1"=="" goto all
if /I "%~1"=="clean" goto clean
if /I "%~1"=="restore" goto restore
if /I "%~1"=="build" goto build
if /I "%~1"=="test" goto test
if /I "%~1"=="pack" goto pack

echo Invalid section: %~1
echo Usage: %~nx0 [clean^|restore^|build^|test^|pack]
exit /b 1

:all
call :do_clean || exit /b 1
call :do_restore || exit /b 1
call :do_build || exit /b 1
call :do_test || exit /b 1
call :do_pack || exit /b 1
exit /b 0

:clean
call :do_clean
exit /b %errorlevel%

:restore
call :do_restore
exit /b %errorlevel%

:build
call :do_build
exit /b %errorlevel%

:test
call :do_test
exit /b %errorlevel%

:pack
call :do_pack
exit /b %errorlevel%

:do_clean
echo.
echo === Clean ===
dotnet clean Icod.Host.sln -c Debug
exit /b %errorlevel%

:do_restore
echo.
echo === Restore ===
dotnet restore Icod.Host.sln
exit /b %errorlevel%

:do_build
echo.
echo === Build ===
dotnet build Icod.Host.sln -c Debug --no-restore
exit /b %errorlevel%

:do_test
echo.
echo === Test ===
dotnet test Icod.Host.sln -c Debug --no-build
exit /b %errorlevel%

:do_pack
echo.
echo === Pack ===
dotnet pack Icod.Host.csproj -c Debug --include-source --include-symbols --no-build
exit /b %errorlevel%
