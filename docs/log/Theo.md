# Theo's log

- 2026-10-18 18:55 UTC · Theo · Claimed by the Token Budget Manager (nathaniel3): queue #583, note 855, branch `theo-voice-583`.
- 2026-10-18 19:16 UTC · Theo · #583 (note 855): the host's send is the same as a joiner's (its own player is a loopback ClientSession). Cause: no mic levelling (the mix is made for a -15 dBFS mic; a Windows headset is ~-35), heard through the cab's wall, under the outdoor bed's wind, which voice never ducked. Fix: `VoiceLeveller` on capture (player.json `voices.leveller`) and voice ducks tier 6 as well as 5 (mix.json, systems-spec A.3). `dt voice bench` gets --speed, --listener-cab/-car/-z, --mic, --out. Tests: AudioTests, VoiceTests, VoiceLevellerTests. Branch `theo-voice-583`.
