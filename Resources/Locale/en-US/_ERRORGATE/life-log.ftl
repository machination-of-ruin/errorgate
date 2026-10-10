# The log shown to a player when they die. Short, cold, factual, and it never says what to do.
life-log-header = >>> LOG OF {$name} <<<

life-log-lifespan-short = ERRORS COUNTED: LESS THAN A MINUTE.
life-log-lifespan = ERRORS COUNTED: {$minutes} MINUTES.

life-log-harmed-player = LAST HARMED BY {$source}: {$damage} DAMAGE.
life-log-harmed-thing = LAST HARMED BY {$source}: {$damage} DAMAGE.
life-log-harmed-fault = LAST HARMED BY A {$source} FAULT: {$damage} DAMAGE.
life-log-harmed-self = LAST HARMED BY ITSELF: {$damage} DAMAGE.
life-log-harmed-environment = NO HAND WAS RAISED AGAINST IT. CAUSE: {$source}.
life-log-harmed-none = NO CAUSE RECORDED.

life-log-env-fire = FIRE
life-log-env-cold = COLD
life-log-env-air = NO AIR
life-log-env-blood = BLOOD LOSS
life-log-env-poison = POISON
life-log-env-radiation = RADIATION
life-log-env-unknown = UNKNOWN

life-log-fault-heat = HEAT
life-log-fault-arc = ARC
life-log-fault-collapse = COLLAPSE

life-log-hurt = IT HURT: {$names}.
life-log-killed = IT DELETED: {$names}.
life-log-hurt-none = IT HURT NO ONE.

life-log-spoke = IT SPOKE WITH {$names}: {$lines ->
    [one] 1 LINE
   *[other] {$lines} LINES
}.
life-log-spoke-none = IT SPOKE TO NO ONE.
life-log-spoken-to = SPOKEN TO BY {$names}.
life-log-last-words = LAST WORDS RECORDED: "{$words}"
life-log-last-words-none = NO LAST WORDS RECORDED.

life-log-footer = ERROR NOT CORRECTED.
