# ERRORGATE world faults

ent-ErrorgateAnomalyHeat = heat fault
    .desc = THERMAL RUNAWAY. NO SOURCE FOUND. THE AIR OVER IT BENDS.

ent-ErrorgateAnomalyArc = arc fault
    .desc = DISCHARGE WITHOUT A CIRCUIT. IT REACHES FOR WHATEVER MOVES.

ent-ErrorgateAnomalyCollapse = collapse fault
    .desc = GRAVITY ERROR. EVERYTHING FALLS TOWARD IT.

# Admin commands

cmd-spawnanomalies-desc = Clears and places the world faults again, on every anomaly field or on one grid or map.
cmd-spawnanomalies-help = spawnanomalies [grid or map uid]
cmd-spawnanomalies-invalid-args = SPAWNANOMALIES TAKES AT MOST ONE ARGUMENT: A GRID OR MAP UID.
cmd-spawnanomalies-no-entity = NO ENTITY { $entity }.
cmd-spawnanomalies-not-grid = { $entity } IS NEITHER A GRID NOR A MAP.
cmd-spawnanomalies-no-field = NO ANOMALY FIELD LOADED. GIVE A GRID OR MAP UID TO ADD ONE.
cmd-spawnanomalies-done = { $entity }: { $count } FAULTS PLACED.

cmd-listanomalies-desc = Lists every world fault with its position.
cmd-listanomalies-help = listanomalies
cmd-listanomalies-line = { $kind } { $entity } MAP { $map } AT { $x }, { $y }
cmd-listanomalies-none = NO FAULTS LOADED.
