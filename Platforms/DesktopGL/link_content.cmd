@echo off
rem Windows (DesktopGL build): link the game's Content folder next to the exe.  link_content.cmd C:\path\to\Content
if "%~1"=="" (echo usage: %0 ^<path to Content folder^> & exit /b 1)
if exist "%~dp0Content" rmdir "%~dp0Content"
mklink /J "%~dp0Content" "%~1"
