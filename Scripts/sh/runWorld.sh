#!/usr/bin/env sh

# >>> THE WORLD: the round runs in the web of generated locations (docs/design/world.md) <<<
if [ "$(dirname $0)" != "." ]; then
    cd "$(dirname $0)"
fi

cd ../../
dotnet build -c Debug --no-restore
dotnet run --project Content.Server --no-build -- --cvar game.map=World --cvar errorgate.world.enabled=true &
dotnet run --project Content.Client --no-build
