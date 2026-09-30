@echo off
rem Builds publish\ReceiptTracker.exe: one self-contained file with the API *and* the web app,
rem so it runs on a PC without .NET or Node installed. See "Run it as a desktop app" in README.md.
setlocal
cd /d "%~dp0"

echo === Building the web app (web\dist) ===
pushd web
rem npm install, not npm ci: ci wipes node_modules, which fails while the Vite dev server has it open.
call npm install --no-audit --no-fund || goto :failed
call npm run build || goto :failed
popd

echo === Copying the web app into the API's wwwroot ===
rem /MIR mirrors, so files from an older build don't linger. robocopy exit codes below 8 mean success.
robocopy web\dist src\ReceiptExtractor.Api\wwwroot /MIR /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 goto :failed

echo === Publishing publish\ReceiptTracker.exe ===
rem IncludeAllContentForSelfExtract (with BundleContentIntoExe in the csproj) puts wwwroot and appsettings.json
rem inside the exe. They're unpacked to a temp folder on start, which is why Program.cs points the content root
rem at AppContext.BaseDirectory. IncludeNativeLibrariesForSelfExtract does the same for SkiaSharp's native library
rem (it turns sideways phone photos upright, Extraction/PhotoOrientation.cs). The old output is removed first so no stale loose files are left beside the exe.
if exist publish\ReceiptTracker.exe del publish\ReceiptTracker.exe
if exist publish\ReceiptTracker.exe echo Close ReceiptTracker.exe first. & goto :failed
if exist publish\wwwroot rmdir /s /q publish\wwwroot
del /q publish\appsettings*.json publish\web.config publish\*.staticwebassets.endpoints.json 2>nul
dotnet publish src\ReceiptExtractor.Api -c Release -r win-x64 -o publish ^
  -p:AssemblyName=ReceiptTracker ^
  -p:SelfContained=true ^
  -p:PublishSingleFile=true ^
  -p:IncludeAllContentForSelfExtract=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:IsTransformWebConfigDisabled=true ^
  -p:DebugType=none || goto :failed

echo.
echo Done: publish\ReceiptTracker.exe
echo Start Ollama, then double-click it. Your data goes in publish\data.
exit /b 0

:failed
popd 2>nul
echo.
echo Publish failed, see the errors above.
exit /b 1
