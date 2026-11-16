@echo off
setlocal
echo Compiling ColourPickerWidget.cs...
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /target:winexe /optimize+ /out:ColourPickerWidget_Portable.exe /win32icon:app.ico /resource:app.ico,app.ico ColourPickerWidget.cs
if %ERRORLEVEL% NEQ 0 (
    echo Compilation failed!
    pause
    exit /b %ERRORLEVEL%
)
echo Launching ColourPickerWidget_Portable.exe...
start "" ColourPickerWidget_Portable.exe
