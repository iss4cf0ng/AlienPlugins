@echo off
setlocal

where csc >nul 2>&1
if %errorlevel% neq 0 (
    echo [-] Error: csc.exe not found or not in PATH.
    exit /b 1
) else (
    echo [+] csc.exe detected. Starting recursive scan...
)

for /d /r %%d in (*) do (
    if exist "%%d\*.cs" (
        echo.
        echo [*] Entering directory: %%d
        pushd "%%d"
        
        csc /target:library *.cs
        
        popd
    )
)

echo.
echo [+] Done.
endlocal