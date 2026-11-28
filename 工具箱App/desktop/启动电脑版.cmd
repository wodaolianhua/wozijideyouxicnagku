@echo off
chcp 65001 > nul
title 文件工具箱
cd /d "%~dp0"

rem ============================================================
rem  文件工具箱 · 电脑版启动器
rem  完全离线运行，不需要网络。文件不会离开你的电脑。
rem ============================================================

echo.
echo   文件工具箱 · 电脑版
echo   ------------------------------
echo   正在启动本地服务...
echo.

rem 优先用 Python（大多数 Windows 都自带/装了）
where python >nul 2>nul
if %errorlevel%==0 (
    start "" http://127.0.0.1:8799/
    python -m http.server 8799 --bind 127.0.0.1
    goto :eof
)

rem 没有 python 就试 py
where py >nul 2>nul
if %errorlevel%==0 (
    start "" http://127.0.0.1:8799/
    py -m http.server 8799 --bind 127.0.0.1
    goto :eof
)

rem 再试 node
where node >nul 2>nul
if %errorlevel%==0 (
    start "" http://127.0.0.1:8799/
    node "%~dp0serve.js"
    goto :eof
)

echo   没找到 Python 或 Node。
echo.
echo   没关系 —— 直接双击 index.html 也能用。
echo   （Chrome / Edge 打开即可，功能完全一样）
echo.
pause
start "" "%~dp0index.html"
