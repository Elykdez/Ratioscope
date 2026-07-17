@echo off
setlocal EnableDelayedExpansion
REM ---------------------------------------------------------------------------
REM Transfer a Sentis LLM model to a connected Android device.
REM
REM The app reads models from persistentDataPath/Models, so a model pushed there
REM is picked up without rebuilding the APK. See LlmModelCatalog.ResolvePath.
REM
REM Usage:
REM   Tools\push-model.bat [model-file]
REM     model-file   Name of a .sentis file in Assets\StreamingAssets\Sentis
REM                  (default: Llm_Decode_2048.sentis)
REM
REM Overridable via environment:
REM   PACKAGE          Android application id (default: com.Elykdez.Ratioscope)
REM   ADB              Path to adb.exe (otherwise auto-detected)
REM   ANDROID_SERIAL   Target device serial when several are attached
REM
REM Reading app logs:
REM   Unity mirrors logs to Android's logcat under the "Unity" tag, but two
REM   prerequisites must both hold or you will see nothing:
REM     1. The APK is a Development Build. A release build does not forward
REM        Debug.Log to logcat.
REM     2. ENABLE_LOG is in the Android scripting define symbols. The app logs
REM        through LogHelper, which is [Conditional("ENABLE_LOG")] and is
REM        stripped from the build otherwise (UNITY_EDITOR only covers the
REM        Editor, not the device).
REM   With both in place, using the adb this script prints on its "adb:" line:
REM     adb logcat -c                    (clear the backlog first, optional)
REM     adb logcat -s Unity:V            (stream only Unity logs; Ctrl-C to stop)
REM     adb logcat -s Unity:V > app.log  (or capture to a file)
REM   For a native crash, widen it: adb logcat -s Unity:V DEBUG:V AndroidRuntime:V
REM ---------------------------------------------------------------------------

if not defined PACKAGE set "PACKAGE=com.Elykdez.Ratioscope"
set "MODEL=%~1"
if not defined MODEL set "MODEL=Llm_Decode_2048.sentis"

for %%I in ("%~dp0..") do set "REPO_ROOT=%%~fI"
set "SRC=%REPO_ROOT%\Assets\StreamingAssets\Sentis\%MODEL%"
set "DEST_DIR=/storage/emulated/0/Android/data/%PACKAGE%/files/Models"
set "DEST=%DEST_DIR%/%MODEL%"

call :find_adb || exit /b 1

if not exist "%SRC%" (
    echo error: model not found: %SRC%>&2
    exit /b 1
)
echo adb:     %ADB%
echo model:   %MODEL%

"%ADB%" start-server >nul 2>&1

REM Pick the target device: an explicit serial, otherwise the sole authorized one.
if defined ANDROID_SERIAL (
    set "SERIAL=%ANDROID_SERIAL%"
) else (
    set "SERIAL="
    set "DEVCOUNT=0"
    for /f "skip=1 tokens=1,2" %%A in ('"%ADB%" devices') do (
        if "%%B"=="device" (
            set /a DEVCOUNT+=1
            set "SERIAL=%%A"
        )
    )
    if !DEVCOUNT! EQU 0 (
        "%ADB%" devices
        echo error: no authorized device. Enable USB debugging, replug, and tap 'Allow' on the phone.>&2
        exit /b 1
    )
    if !DEVCOUNT! GTR 1 (
        echo error: multiple devices attached; set ANDROID_SERIAL to one of them.>&2
        "%ADB%" devices
        exit /b 1
    )
)
echo device:  %SERIAL%

for %%F in ("%SRC%") do set "LOCAL_SIZE=%%~zF"
echo size:    %LOCAL_SIZE% bytes

"%ADB%" -s %SERIAL% shell mkdir -p "%DEST_DIR%"
echo pushing to %DEST% ...
"%ADB%" -s %SERIAL% push "%SRC%" "%DEST%" || (
    echo error: push failed.>&2
    exit /b 1
)

REM Read the on-device size via a temp file: for /f mis-parses a backtick command
REM whose first token is a quoted path, and reading a file also strips the CR.
set "REMOTE_SIZE="
set "_RSIZE=%TEMP%\push-model-rsize.txt"
"%ADB%" -s %SERIAL% shell stat -c %%s "%DEST%" > "%_RSIZE%" 2>nul
for /f "usebackq delims=" %%A in ("%_RSIZE%") do set "REMOTE_SIZE=%%A"
del "%_RSIZE%" 2>nul

if "%REMOTE_SIZE%"=="%LOCAL_SIZE%" (
    echo verified: %REMOTE_SIZE% bytes match. Restart the app to load it.
    exit /b 0
) else (
    echo error: size mismatch: local %LOCAL_SIZE% vs device %REMOTE_SIZE%. Re-run the transfer.>&2
    exit /b 1
)

:find_adb
REM Resolve adb.exe: ADB env, then PATH, then Android SDK env, then Unity editors.
if defined ADB if exist "%ADB%" goto :eof
for /f "delims=" %%A in ('where adb 2^>nul') do ( set "ADB=%%A" & goto :eof )
if defined ANDROID_SDK_ROOT if exist "%ANDROID_SDK_ROOT%\platform-tools\adb.exe" (
    set "ADB=%ANDROID_SDK_ROOT%\platform-tools\adb.exe" & goto :eof
)
if defined ANDROID_HOME if exist "%ANDROID_HOME%\platform-tools\adb.exe" (
    set "ADB=%ANDROID_HOME%\platform-tools\adb.exe" & goto :eof
)
for %%R in (
    "C:\Program Files\Unity\Editor" "D:\Unity\Editor"
) do (
    if exist %%R (
        for /f "delims=" %%A in ('dir /b /s "%%~R\adb.exe" 2^>nul ^| findstr /i /l /c:"AndroidPlayer\SDK\platform-tools\adb.exe"') do (
            set "ADB=%%A" & goto :eof
        )
    )
)
echo error: adb not found. Set ADB=path\to\adb.exe or add it to PATH.>&2
exit /b 1
