# 2>nul || goto :cmd_start
# polyglot dispatcher: sh sees the line above as a comment; cmd treats `#` as
# an unknown command (errorlevel != 0, stderr swallowed) and `||` jumps past
# the sh block to :cmd_start. invoked as `./arc.cmd <sub>` on unix and
# `./arc <sub>` on windows (PATHEXT resolves the missing extension).
set -e
script_dir=$(cd "$(dirname "$0")" && pwd)
sub=${1:-}
[ $# -gt 0 ] && shift
case "$sub" in
  build|start) exec "$script_dir/scripts/$sub.sh" "$@" ;;
  ""|-h|--help)
    echo "usage: ./arc.cmd <build|start> [args...]"
    [ -z "$sub" ] && exit 2 || exit 0
    ;;
  *)
    echo "error: unknown subcommand '$sub' (expected: build, start)" >&2
    exit 2
    ;;
esac
exit 0

:cmd_start
@echo off
setlocal
set "sub=%~1"
if "%sub%"=="" goto :arc_usage
if /i "%sub%"=="-h" goto :arc_usage
if /i "%sub%"=="--help" goto :arc_usage
if /i not "%sub%"=="build" if /i not "%sub%"=="start" (
  echo error: unknown subcommand '%sub%' ^(expected: build, start^) 1>&2
  exit /b 2
)
shift
set "args="
:arc_loop
if "%~1"=="" goto :arc_run
set "args=%args% %1"
shift
goto :arc_loop
:arc_run
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\%sub%.ps1"%args%
exit /b %ERRORLEVEL%
:arc_usage
echo usage: arc ^<build^|start^> [args...]
if "%sub%"=="" exit /b 2
exit /b 0
