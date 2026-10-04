@echo off
setlocal
set "RESULT=1"

pushd "%~dp0"
if errorlevel 1 (
    echo ERROR: Cannot open the server directory.
    goto finish
)

where docker >nul 2>&1
if errorlevel 1 (
    echo ERROR: Docker is not installed or is not on PATH.
    echo Install Docker Desktop and enable Linux containers.
    goto cleanup
)

docker info >nul 2>&1
if errorlevel 1 (
    echo ERROR: Docker is not running.
    echo Start Docker Desktop, wait for the Linux engine to be ready, then try again.
    goto cleanup
)

docker compose version >nul 2>&1
if errorlevel 1 (
    echo ERROR: Docker Compose is not available. Update Docker Desktop.
    goto cleanup
)

echo Building and starting ECO Server. The first start may take several minutes.
docker compose up --build -d --wait --wait-timeout 600
if errorlevel 1 (
    echo ERROR: ECO Server failed to start or become healthy.
    docker compose ps -a
    docker compose logs --tail 80 db eco
    goto cleanup
)

docker compose ps
if errorlevel 1 goto cleanup

echo.
echo ECO Server is running on this PC:
echo   World: 127.0.0.1:17831
echo   Login: 127.0.0.1:17832
echo   Map:   127.0.0.1:17833
echo Test account: test / 111111
echo To stop without deleting data, run: docker compose stop
set "RESULT=0"

:cleanup
popd

:finish
if /i not "%~1"=="/no-pause" pause
exit /b %RESULT%
