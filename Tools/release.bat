@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM Publish one Ratioscope GitHub Release from prebuilt Windows and Android players.
REM The model stays outside both players and is uploaded as a separate release asset.
REM
REM Usage:
REM   Tools\release.bat v1.0.0
REM   Tools\release.bat v1.0.0 --dry-run
REM
REM Override the default build folder when needed:
REM   set "RELEASE_BUILD_DIR=D:\Ratioscope\Release"
REM   Tools\release.bat v1.0.0

set "RELEASE_TAG=%~1"
set "RELEASE_MODE=%~2"

if not defined RELEASE_TAG (
    echo error: release tag is required, for example v1.0.0.>&2
    exit /b 2
)

echo(%RELEASE_TAG%| findstr /R /X "v[0-9][0-9A-Za-z.-]*" >nul
if errorlevel 1 (
    echo error: invalid release tag "%RELEASE_TAG%". Use a tag such as v1.0.0.>&2
    exit /b 2
)

if defined RELEASE_MODE if /I not "%RELEASE_MODE%"=="--dry-run" (
    echo error: unknown option "%RELEASE_MODE%". Only --dry-run is supported.>&2
    exit /b 2
)

for %%I in ("%~dp0..") do set "RELEASE_REPO_ROOT=%%~fI"
if not defined RELEASE_BUILD_DIR set "RELEASE_BUILD_DIR=%RELEASE_REPO_ROOT%\Builds\Release"

set "RELEASE_WIN=%RELEASE_BUILD_DIR%\Ratioscope-Windows-x64-%RELEASE_TAG%.zip"
set "RELEASE_APK=%RELEASE_BUILD_DIR%\Ratioscope-Android-%RELEASE_TAG%.apk"
set "RELEASE_MODEL=%RELEASE_REPO_ROOT%\Assets\StreamingAssets\Sentis\Llm_Decode_2048.sentis"
set "RELEASE_LICENSE=%RELEASE_BUILD_DIR%\Qwen3-1.7B-LICENSE.txt"
set "RELEASE_CHECKSUMS=%RELEASE_BUILD_DIR%\SHA256SUMS.txt"
set "RELEASE_QWEN_LICENSE_URL=https://huggingface.co/Qwen/Qwen3-1.7B/resolve/main/LICENSE"

where git >nul 2>&1 || (
    echo error: git was not found on PATH.>&2
    exit /b 1
)
where gh >nul 2>&1 || (
    echo error: GitHub CLI was not found on PATH. Install gh and run gh auth login.>&2
    exit /b 1
)
where curl.exe >nul 2>&1 || (
    echo error: curl.exe was not found on PATH.>&2
    exit /b 1
)
where powershell.exe >nul 2>&1 || (
    echo error: powershell.exe was not found on PATH.>&2
    exit /b 1
)

for %%F in ("%RELEASE_WIN%" "%RELEASE_APK%" "%RELEASE_MODEL%") do if not exist "%%~fF" (
    echo error: required release asset not found: %%~fF>&2
    exit /b 1
)

powershell.exe -NoProfile -Command ^
  "$limit = 2GB; $files = @($env:RELEASE_WIN, $env:RELEASE_APK, $env:RELEASE_MODEL); $bad = @(Get-Item -LiteralPath $files | Where-Object Length -ge $limit); if ($bad.Count) { $bad | ForEach-Object { [Console]::Error.WriteLine('error: release asset must be under 2 GiB: {0} ({1} bytes)', $_.FullName, $_.Length) }; exit 1 }"
if errorlevel 1 exit /b 1

pushd "%RELEASE_REPO_ROOT%" || exit /b 1

gh auth status >nul 2>&1 || (
    echo error: GitHub CLI is not authenticated. Run gh auth login.>&2
    popd
    exit /b 1
)

for /f "usebackq delims=" %%R in (`gh repo view --json nameWithOwner --jq ".nameWithOwner" 2^>nul`) do set "RELEASE_REPO=%%R"
if not defined RELEASE_REPO (
    echo error: could not resolve the GitHub repository for %RELEASE_REPO_ROOT%.>&2
    popd
    exit /b 1
)

gh release view "%RELEASE_TAG%" --repo "%RELEASE_REPO%" >nul 2>&1
if not errorlevel 1 (
    echo error: GitHub Release %RELEASE_TAG% already exists in %RELEASE_REPO%.>&2
    popd
    exit /b 1
)

if /I not "%RELEASE_MODE%"=="--dry-run" (
    git fetch --quiet origin main || (
        echo error: could not fetch origin/main.>&2
        popd
        exit /b 1
    )

    for /f "delims=" %%H in ('git rev-parse HEAD') do set "RELEASE_HEAD=%%H"
    for /f "delims=" %%H in ('git rev-parse origin/main') do set "RELEASE_ORIGIN_HEAD=%%H"
    if /I not "!RELEASE_HEAD!"=="!RELEASE_ORIGIN_HEAD!" (
        echo error: HEAD does not match origin/main. Commit and push the intended release state first.>&2
        popd
        exit /b 1
    )

    for /f "delims=" %%S in ('git status --porcelain') do set "RELEASE_DIRTY=1"
    if defined RELEASE_DIRTY (
        echo error: the repository has uncommitted changes. Commit the intended release state first.>&2
        popd
        exit /b 1
    )
)

echo Fetching the Qwen3-1.7B Apache 2.0 license...
curl.exe -fL --retry 3 --output "%RELEASE_LICENSE%" "%RELEASE_QWEN_LICENSE_URL%"
if errorlevel 1 (
    echo error: failed to download the Qwen3-1.7B license.>&2
    popd
    exit /b 1
)

powershell.exe -NoProfile -Command ^
  "$files = @($env:RELEASE_WIN, $env:RELEASE_APK, $env:RELEASE_MODEL, $env:RELEASE_LICENSE); $lines = foreach ($file in $files) { $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash.ToLowerInvariant(); '{0}  {1}' -f $hash, [IO.Path]::GetFileName($file) }; [IO.File]::WriteAllLines($env:RELEASE_CHECKSUMS, $lines, [Text.UTF8Encoding]::new($false))"
if errorlevel 1 (
    echo error: failed to generate %RELEASE_CHECKSUMS%.>&2
    popd
    exit /b 1
)

echo.
echo Repository: %RELEASE_REPO%
echo Tag:        %RELEASE_TAG%
echo Windows:    %RELEASE_WIN%
echo Android:    %RELEASE_APK%
echo Model:      %RELEASE_MODEL%
echo Checksums:  %RELEASE_CHECKSUMS%
echo License:    %RELEASE_LICENSE%
echo.

if /I "%RELEASE_MODE%"=="--dry-run" (
    echo Dry run complete. No tag or GitHub Release was created.
    popd
    exit /b 0
)

gh release create "%RELEASE_TAG%" ^
  "%RELEASE_WIN%#Windows x64" ^
  "%RELEASE_APK%#Android APK" ^
  "%RELEASE_MODEL%#Qwen3-1.7B Sentis model" ^
  "%RELEASE_CHECKSUMS%#SHA-256 checksums" ^
  "%RELEASE_LICENSE%#Qwen3-1.7B Apache 2.0 license" ^
  --repo "%RELEASE_REPO%" ^
  --target main ^
  --title "Ratioscope %RELEASE_TAG%" ^
  --generate-notes ^
  --notes "Windows and Android builds do not contain model weights. Download Llm_Decode_2048.sentis separately or use Settings - Download Models. The model is derived from Qwen/Qwen3-1.7B and distributed under Apache 2.0."
if errorlevel 1 (
    echo error: GitHub Release creation failed. Inspect any draft or partial release before retrying.>&2
    popd
    exit /b 1
)

echo.
gh release view "%RELEASE_TAG%" --repo "%RELEASE_REPO%" --json url,tagName,isDraft,isPrerelease,assets ^
  --jq "{url: .url, tag: .tagName, draft: .isDraft, prerelease: .isPrerelease, assets: [.assets[].name]}"
if errorlevel 1 (
    echo error: release was created, but verification failed. Check it on GitHub.>&2
    popd
    exit /b 1
)

popd
exit /b 0
