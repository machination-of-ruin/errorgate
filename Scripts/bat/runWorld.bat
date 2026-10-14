@echo off
cd ../../

rem >>> THE WORLD: server and client, the round runs in the web of generated locations (docs/design/world.md) <<<
call dotnet build -c Debug --no-restore

start "ERRORGATE server" dotnet run --project Content.Server --no-build -- --cvar game.map=World --cvar errorgate.world.enabled=true
start "ERRORGATE client" dotnet run --project Content.Client --no-build
