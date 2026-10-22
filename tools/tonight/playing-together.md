## Playing together tonight

Tested headless on this build's code (note 985): host and join by address, the LAN list, joining late at a stop, and a
crash and rejoin to the same place, with two real copies of the game. Best first:

**1. Same house, same network (easiest).**
1. Host: **HOST** on the title screen. Pick the tier, **CARS**, and **CREW** (bots, to fill the seats), then
   **OPEN THE LOBBY**. You wait in the yard with the train. The top left says who's aboard, and "(OR THEY TYPE
   192.168.x.x:27450)": that's your address on the network.
2. Friend: **JOIN**. Your run shows on the list within a few seconds ("NO GAMES YET" until it's heard). Press Enter on it.
   If it never shows, type the address from the host's top left into **ADDRESS**, then **JOIN**.
3. Windows asks "Allow access?" for the game the first time it hosts: tick both boxes and allow, or nobody can reach you.
4. Everyone's in: the host drives out of the yard.

**2. Over the internet, by address.**
1. On the host's router, forward **UDP port 27450** to the host's computer (the 192.168.x.x address in the yard's top
   left). Only that one port; 27451 is for the LAN list only.
2. Host: find your public address (search "what is my IP" in a browser) and send it to your friends.
3. Host: **HOST**, **OPEN THE LOBBY**, as above (allow the firewall prompt).
4. Friend: **JOIN**, **ADDRESS**, type the public address (`203.0.113.7`, or `203.0.113.7:27450`), then **JOIN**.
5. If it says nobody answered: the port isn't forwarded, the firewall said no, or the internet provider shares one
   address between houses (no forwarding is possible). Then use Steam (3) if this build can, or play on one network.

**3. Steam** (no ports to forward): only if "A Steam night" below says this build has Valve's library. Both of you need
Steam running and logged in; the game shows as Spacewar (Valve's test app 480) until it has its own app id. Steps are
in that section.

**4. Epic Online Services** (no ports, no Steam): not yet. It needs the Epic Developer Portal setup in the inbox
(`n1-N1-eos-portal`).

**What to try**
- [ ] Host with a bot or two (**CREW**), a friend joins in the yard: both see each other at the gate.
- [ ] A late join: the host drives out, a friend joins after. They come aboard, into a bot's seat if one's free (out of
      the yard, that can wait for the train's next stop).
- [ ] Drop and rejoin: a friend quits the game (or pulls the network cable), starts again and joins the same way within
      3 minutes. They're back in their own place on the train, with their kit.
- [ ] Voice: the mic is open by default (Settings, **VOICE**; push to talk is **V**), so you needn't hold V, whatever the
      first-nights card says. Talk aloud standing near each other; voices fade with distance.
- [ ] The radio: hold the right mouse button with a radio on your belt; the whole train hears.
- [ ] A full night to the terminus, then the next night together.
- [ ] Anything odd: **F8** to type a note, or hold **F9** to say one.
