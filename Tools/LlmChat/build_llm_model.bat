@echo off
setlocal

REM One-click build of the LLM decode graphs this project loads at runtime.
REM
REM   1. Ensures the exporter venv exists (skipped entirely when it already does)
REM   2. Exports the chosen checkpoint(s) to Tools\ExportedModel\*.onnx
REM
REM Option [4] builds the tiny random-weight test fixtures instead. 
REM They are not runtime models - the EditMode tests that drive the real Sentis decode loop
REM skip themselves when the .sentis files are missing.
REM
REM The ONNX still has to be converted in the Unity Editor afterwards; the steps are printed at the end.
REM
REM Hardware note: the 4B / 4096 graph needs a 16 GB-class GPU
REM (a full-window session measures ~17.5 GB). Below 15.5 GiB usable VRAM,
REM LlmSystemSettings auto-selects the 1.7B profile and never loads the 4B artifact on the GPU.

REM Where the Hugging Face checkpoints live. Defaults to Tools\ImportedModel next to this script,
REM resolved from %~dp0 so nothing is tied to one machine or drive.
REM Override by setting a MODEL_ROOT environment variable before launching.
if not defined MODEL_ROOT for %%I in ("%~dp0..\ImportedModel") do set "MODEL_ROOT=%%~fI"

REM Set to --verify-onnxruntime to sanity-check each graph in ONNX Runtime
REM before converting (costs an extra fp32 load of the model).
set "VERIFY="

REM Repo root is two levels above Tools\LlmChat.
cd /d "%~dp0..\.."
set "VENV_PYTHON=Tools\LlmChat\.venv\Scripts\python.exe"


REM ---------------------------------------------------------------- venv setup

if exist "%VENV_PYTHON%" (
    echo Exporter environment already present - skipping setup.
    goto :venv_ready
)

echo Setting up the exporter environment in Tools\LlmChat\.venv
echo.

where python >nul 2>&1
if errorlevel 1 (
    echo ERROR: python was not found on PATH. Install Python 3.12 and retry.
    goto :done
)
python --version

REM --system-site-packages lets a system torch install serve the venv.
python -m venv --system-site-packages "Tools\LlmChat\.venv"
if errorlevel 1 (
    echo ERROR: venv creation failed.
    goto :done
)

echo Installing pinned dependencies...
"%VENV_PYTHON%" -m pip install -r "Tools\LlmChat\requirements.txt"
if errorlevel 1 (
    echo ERROR: dependency installation failed.
    goto :done
)
echo Environment ready.

:venv_ready
echo.


REM -------------------------------------------------------------------- choice

echo Which graph do you want to build?
echo.
echo   [1] Qwen3-1.7B  / 2048 context  -^> Llm_Decode_2048.onnx
echo       Needs ~7.5 GiB usable VRAM. The default profile on most machines.
echo.
echo   [2] Qwen3-4B    / 4096 context  -^> Llm_Decode_4096.onnx
echo       Needs ~15.5 GiB usable VRAM or the much slower CPU backend.
echo       ~10 minute export, ~32 GB transient disk.
echo.
echo   [3] Both of the above
echo.
echo   [4] Tiny test fixtures / 128 context -^> Llm_Tiny_Text_128.onnx
echo                                        -^> Llm_Tiny_Decode_128.onnx
echo       Random weights, 4 layers, 512-token vocabulary. Seconds to export,
echo       no VRAM needed, editor may stay open. Test-only - see the note
echo       printed afterwards before you ship a build.
echo.
choice /C 1234 /N /M "Select [1/2/3/4]: "
echo.

if errorlevel 4 goto :tiny
if errorlevel 3 goto :both
if errorlevel 2 goto :only_4b

call :check_editor || goto :done
call :export "Qwen3-1.7B" 2048 "Tools\ExportedModel\Llm_Decode_2048.onnx" || goto :done
goto :finished

:only_4b
call :check_editor || goto :done
call :export "Qwen3-4B-Instruct-2507" 4096 "Tools\ExportedModel\Llm_Decode_4096.onnx" || goto :done
goto :finished

:both
call :check_editor || goto :done
call :export "Qwen3-1.7B" 2048 "Tools\ExportedModel\Llm_Decode_2048.onnx" || goto :done
call :export "Qwen3-4B-Instruct-2507" 4096 "Tools\ExportedModel\Llm_Decode_4096.onnx" || goto :done
goto :finished

REM The tiny graph takes its architecture - but none of its weights - from a real
REM checkpoint's config.json, so only the shape-defining fields
REM the exporter overrides matter and either Qwen3 produces the same graph. 
REM Docs\Llm-Chat-Runtime.md uses the 4B; fall back to the 1.7B when only that one is downloaded.
:tiny
set "TINY_CHECKPOINT=Qwen3-4B-Instruct-2507"
if not exist "%MODEL_ROOT%\%TINY_CHECKPOINT%\config.json" set "TINY_CHECKPOINT=Qwen3-1.7B"

call :export_tiny "Tools\ExportedModel\Llm_Tiny_Text_128.onnx" "" || goto :done
call :export_tiny "Tools\ExportedModel\Llm_Tiny_Decode_128.onnx" "--decode-only" || goto :done

echo.
echo Next, in the Unity Editor, once per exported graph:
echo   1. Tools ^> Hypocycloid ^> Sentis ^> ONNX to Sentis
echo   2. Browse to the .onnx in Tools\ExportedModel
echo   3. Uncheck Skip graph optimization - these graphs are small enough for
echo      the normal Sentis optimizer and its validation
echo   4. Convert - writes the .sentis file to Assets\StreamingAssets\Sentis
echo.
echo Then run the EditMode group Hypocycloid.Editor.LlmChatTests. The Tiny*
echo tests stop skipping once both .sentis files exist.
echo.
echo ============================================================
echo   THESE ARTIFACTS SHIP IN THE NEXT PLAYER BUILD
echo ============================================================
echo.
echo StreamingAssets is copied wholesale into the build, so a converted tiny
echo model makes the Tiny option selectable in the config panel. It cannot
echo answer anything - its 512-token vocabulary does not match the shipped
echo tokenizer, and ChatService refuses the prompt with START FAILED. Delete
echo Assets\StreamingAssets\Sentis\Llm_Tiny_*.sentis before a release build.
goto :done

:finished
echo.
echo Next, in the Unity Editor, once per exported graph:
echo   1. Tools ^> Hypocycloid ^> Sentis ^> ONNX to Sentis
echo   2. Browse to the .onnx in Tools\ExportedModel
echo   3. Keep Weight quantization = Uint8 and Skip graph optimization enabled
echo   4. Convert - writes the .sentis file to Assets\StreamingAssets\Sentis

:done
echo.
pause
endlocal
exit /b


REM ------------------------------------------------------------- :check_editor

REM Only the production exports care: they need roughly twice the fp32 weight size
REM in free commit, which a running editor can deny. The tiny export does not.

:check_editor
tasklist /FI "IMAGENAME eq Unity.exe" 2>nul | find /I "Unity.exe" >nul
if errorlevel 1 exit /b 0

echo ============================================================
echo   THE UNITY EDITOR IS RUNNING - CLOSE IT BEFORE EXPORTING
echo ============================================================
echo.
echo Each export needs roughly twice the fp32 weight size in free Windows
echo commit ^(~14 GB for 1.7B, ~32 GB for 4B^). A running editor can pin
echo tens of GB of leaked native memory after loading a large model, and
echo the export then dies with access violation 0xC0000005.
echo See Docs\Llm-Chat-Runtime.md.
echo.
choice /C YN /N /M "Continue anyway? [Y/N] "
if errorlevel 2 exit /b 1
echo.
exit /b 0


REM -------------------------------------------- :export_tiny <out> [extra flags]

:export_tiny
set "OUTPUT=%~1"
set "TINY_FLAGS=%~2"

if exist "%OUTPUT%" (
    echo Skipping %OUTPUT%: already exists.
    echo Delete it to force a re-export.
    exit /b 0
)

if not exist "%MODEL_ROOT%\%TINY_CHECKPOINT%\config.json" (
    echo ERROR: no checkpoint at %MODEL_ROOT%\%TINY_CHECKPOINT%
    echo The tiny graph still needs one real config.json for its architecture.
    exit /b 1
)

echo ------------------------------------------------------------
echo Exporting %OUTPUT%
echo Architecture from %TINY_CHECKPOINT%, random weights, 128 context.
echo ------------------------------------------------------------

"%VENV_PYTHON%" Tools\LlmChat\export_llm_sentis.py ^
  --model-dir "%MODEL_ROOT%\%TINY_CHECKPOINT%" ^
  --output "%OUTPUT%" ^
  --tiny --context-length 128 %TINY_FLAGS% %VERIFY%

if errorlevel 1 (
    echo.
    echo ERROR: tiny export failed.
    exit /b 1
)

echo.
echo Export complete: %OUTPUT%
exit /b 0


REM ------------------------------------------------------- :export <ckpt> <ctx> <out>

:export
set "CHECKPOINT=%~1"
set "CONTEXT_LENGTH=%~2"
set "OUTPUT=%~3"

if exist "%OUTPUT%" (
    echo Skipping %CHECKPOINT%: %OUTPUT% already exists.
    echo Delete it ^(and its .weights folder^) to force a re-export.
    exit /b 0
)

if not exist "%MODEL_ROOT%\%CHECKPOINT%\config.json" (
    echo ERROR: no checkpoint at %MODEL_ROOT%\%CHECKPOINT%
    echo Edit MODEL_ROOT at the top of this script, or download the checkpoint
    echo as described in README.md.
    exit /b 1
)

echo ------------------------------------------------------------
echo Exporting %CHECKPOINT% at context length %CONTEXT_LENGTH%.
echo This runs for several minutes with no progress output - torch.onnx.export
echo stays silent until it finishes. Do not close this window.
echo ------------------------------------------------------------
echo.

"%VENV_PYTHON%" Tools\LlmChat\export_llm_sentis.py ^
  --model-dir "%MODEL_ROOT%\%CHECKPOINT%" ^
  --output "%OUTPUT%" ^
  --decode-only --context-length %CONTEXT_LENGTH% %VERIFY%

if errorlevel 1 (
    echo.
    echo ERROR: export of %CHECKPOINT% failed. If it stopped with 0xC0000005,
    echo free up memory ^(close Unity and other heavy processes^) and retry.
    exit /b 1
)

echo.
echo Export complete: %OUTPUT%
exit /b 0
