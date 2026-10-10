# ERRORGATE world faults

ent-ErrorgateAnomalyHeat = heat fault
    .desc = THERMAL RUNAWAY. NO SOURCE FOUND. THE AIR OVER IT BENDS.

ent-ErrorgateAnomalyArc = arc fault
    .desc = DISCHARGE WITHOUT A CIRCUIT. IT REACHES FOR WHATEVER MOVES.

ent-ErrorgateAnomalyCollapse = collapse fault
    .desc = GRAVITY ERROR. EVERYTHING FALLS TOWARD IT.

# Admin commands

cmd-spawnanomalies-desc = Clears and places the world faults again, on every anomaly field or on one grid or map. A number sets how many faults are wanted in total.
cmd-spawnanomalies-help = spawnanomalies [grid or map uid] [count]
    A single number that is also the uid of a grid or a map is taken as the uid: give both to be sure.
cmd-spawnanomalies-invalid-args = SPAWNANOMALIES TAKES A GRID OR MAP UID AND A COUNT.
cmd-spawnanomalies-no-entity = NOT A GRID, A MAP OR A COUNT: { $entity }.
cmd-spawnanomalies-no-field = NO ANOMALY FIELD LOADED. GIVE A GRID OR MAP UID TO ADD ONE.
cmd-spawnanomalies-done = { $entity }: { $count } FAULTS PLACED IN { $packs } PACKS.

cmd-listanomalies-desc = Lists every world fault with its position, grouped by pack.
cmd-listanomalies-help = listanomalies
cmd-listanomalies-pack = >>> { $field } PACK { $index }: { $count } x { $proto } AROUND { $x }, { $y } RADIUS { $radius } { $passable }
cmd-listanomalies-line = { $kind } { $entity } MAP { $map } AT { $x }, { $y }
cmd-listanomalies-none = NO FAULTS LOADED.
