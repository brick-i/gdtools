@echo off
rem Build gd_currency.exe (CLI), gd_tool.exe (menu), gd_gui.exe (GUI).
rem NOTE: keep this file pure ASCII - cmd.exe reads it in the OEM codepage.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (echo csc.exe not found & exit /b 1)

"%CSC%" /nologo /target:exe /platform:anycpu /optimize+ /debug- ^
  /out:"%~dp0gd_currency.exe" ^
  /reference:System.IO.Compression.dll /reference:System.Web.Extensions.dll ^
  "%~dp0src\GdTool.cs"
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo OK -^> gd_currency.exe

"%CSC%" /nologo /target:exe /platform:anycpu /optimize+ /debug- ^
  /out:"%~dp0gd_tool.exe" /main:Launcher ^
  /reference:System.IO.Compression.dll /reference:System.Web.Extensions.dll ^
  "%~dp0src\GdTool.cs" "%~dp0src\Launcher.cs"
if errorlevel 1 (echo LAUNCHER BUILD FAILED & exit /b 1)
echo OK -^> gd_tool.exe

rem GUI may be running: Windows permits renaming a running exe, so move it aside
if exist "%~dp0gd_gui.exe" (
  del /f /q "%~dp0gd_gui_old.exe" >nul 2>&1
  ren "%~dp0gd_gui.exe" gd_gui_old.exe >nul 2>&1
)

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /debug- ^
  /out:"%~dp0gd_gui.exe" /main:GDGui ^
  /win32manifest:"%~dp0src\admin.manifest" ^
  /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ^
  /reference:System.IO.Compression.dll /reference:System.Web.Extensions.dll ^
  "%~dp0src\GdTool.cs" "%~dp0src\Gui.cs"
if errorlevel 1 (echo GUI BUILD FAILED & exit /b 1)
echo OK -^> gd_gui.exe

echo.
echo Done. Old GUI build (if any) is gd_gui_old.exe - close the old window and delete it.
