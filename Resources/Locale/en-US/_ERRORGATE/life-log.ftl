# The log shown to a player when they die: the last things that happened to the character, as a technical record.
# Impersonal, no names of the dead, no advice. {$time} is minutes, seconds and hundredths before the end, like 04:52.37, the kind column
# is padded so the bars line up.
life-log-born = T-{$time} >  SUBJECT INSTANTIATED
life-log-end = T-{$time} >  SUBJECT TERMINATED

life-log-speech = T-{$time} >  SPEECH      "{$text}"
life-log-whisper = T-{$time} >  WHISPER     "{$text}"
life-log-heard = T-{$time} >  HEARD       {$subject}: "{$text}"
life-log-damage-in = T-{$time} >  DAMAGE IN   {$subject}: {$amount}
life-log-damage-out = T-{$time} >  DAMAGE OUT  {$subject}: {$amount}
life-log-deleted = T-{$time} >  KILLED      {$subject}

life-log-source-self = SELF
life-log-source-fault = {$kind} FAULT

life-log-env-fire = FIRE
life-log-env-cold = COLD
life-log-env-air = NO AIR
life-log-env-blood = BLOOD LOSS
life-log-env-starvation = STARVATION
life-log-env-poison = POISON
life-log-env-radiation = RADIATION
life-log-env-unknown = UNKNOWN

life-log-fault-heat = HEAT
life-log-fault-arc = ARC
life-log-fault-collapse = COLLAPSE
