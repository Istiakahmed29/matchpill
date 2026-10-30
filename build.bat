@echo off
rem ===========================================================================
rem  MatchPill build - uses only the C# compiler that ships with Windows.
rem  No .NET SDK, no NuGet, no Visual Studio, no administrator rights.
rem ===========================================================================
setlocal

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if not exist "%CSC%" (
    echo ERROR: the in-box C# compiler was not found at:
    echo        %CSC%
    echo MatchPill needs Windows 10/11 x64 with .NET Framework 4.8.
    exit /b 1
)

"%CSC%" /target:winexe /optimize+ /out:MatchPill.exe MatchPill.cs

if errorlevel 1 (
    echo.
    echo Build FAILED.
    exit /b 1
)

rem Optional: embed the DPI manifest instead of setting per-monitor v2 at runtime.
rem "%CSC%" /target:winexe /optimize+ /win32manifest:app.manifest /out:MatchPill.exe MatchPill.cs

echo.
echo Built MatchPill.exe
echo Run it with:  MatchPill.exe
echo Self test:    MatchPill.exe --selftest   (writes selftest.log, no window)
exit /b 0
