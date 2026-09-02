# Block File — what the racket still needs

Brief for the "Game HUD Redesign 3D" design project. Screen: ALL BLOCKS IN THE CITY → block card, and WORD FROM THE BLOCKS. Written 2026-09-02 against the current draft (the one with Kearny St. Docks open on the right).

Two mechanics are being built under this page, and the page has to carry them:

1. **Collectors on duty.** The player marks a few hoods in a lieutenant's crew as collectors. Every block a lieutenant answers for gets a collection weekday. On that day a collector, with one escort, walks the block's paying doors on his own, carries the take and banks it at the front. The player never sends a round by hand unless he wants to override the schedule.
2. **Shake down the block.** One order sends a crew door to door through every shop on the block that does not pay yet, demanding at each. The crew's policy (Lenient / Normal / Strict / Brutal) decides what happens on a refusal.

The player's job becomes: assign the block, mark the collectors, set the policy, then read what came back and react. This page is the reaction surface, so every state it shows must lead to an action, and the copy keeps the voice already on the page ("on our paper", "nobody leans on it", "Held, but thin").

## 1. WHAT YOU CAN DO

**Now:** PUT A MAN ON IT · CHANGE WHO ANSWERS · MARK IT ON THE MAP. Nothing about the racket.

**Missing:**

- **SHAKE DOWN THE BLOCK** — needs a crew choice (which lieutenant's men go) before it fires. Disabled state with a reason: "every door here has answered us".
- **SEND THE ROUND NOW** — the manual override of the schedule. Disabled: "nothing owed yet" / "a round is already out".
- **LEAN ON THE HOLDOUTS** — threaten every door marked REFUSED or WAVERING in one walk. Disabled: "nobody is holding out".
- **POLICY** for the crew that answers here: Lenient / Normal / Strict / Brutal. A segmented control, the same component as the clock's speed bar.

**Every key carries a one-line note under its label** saying what it does, the way the door menu already does ("empty the till · a one-night take, not a round"). Mono, muted, lower case, " · " between clauses:

- SHAKE DOWN THE BLOCK — every door that does not pay yet · the crew asks at each · a no is handled by the crew's policy
- SEND THE ROUND NOW — collect what the paying doors owe · the take walks home to the front · skips the schedule
- LEAN ON THE HOLDOUTS — threaten every door that refused or is wavering · fear up, heat up
- PUT A MAN ON IT — one more of ours stands on this block · presence, not paper
- CHANGE WHO ANSWERS — name the lieutenant whose paper this block is on · he collects and answers for it
- MARK IT ON THE MAP — find this block on the turf map
- POLICY — how his crew handles a short or a no

**States a key needs:** idle · disabled with reason (the reason replaces the note) · out (the order was taken and the men are walking; the key turns into a status line until they are back).

## 2. RESPONSIBLE · PAPER

**Now:** a name.

**Missing:**

- His standing arrangement in one line: "Artie Byrne · collects Thursdays · policy STRICT", or "PAPER ONLY — nobody of ours stands here".
- No collector in his crew: "NOBODY ON THE BAG — $640 a day goes uncollected", red, with the fix one click away (jumps to WHO STANDS HERE).
- A round in progress: "ROUND OUT · 3 of 7 doors · $410 in the bag · Dutch Kaminski", with a thin meter (doors done of doors), the same meter family as MEN STANDING ON IT.
- The last round: "LAST ROUND Thu · banked $1,240 · 1 short".

## 3. WHO STANDS HERE

**Now:** name, blurb, PULL.

**Missing:**

- A duty mark on the row: COLLECTOR (later MUSCLE, DRIVER, GUARD). A small caps tag from the same family as the tenure words.
- A way to set it from here: a per-row menu, or a second key beside PULL ("ON THE BAG" / "OFF THE BAG"). Only hoods carry; the lieutenant never does.
- The count in the head: "WHO STANDS HERE · 3 MEN · 1 COLLECTOR".
- A man out on the round reads as away: "on the round · Kearny St." replaces his blurb, greyed.

## 4. WHAT TRADES HERE

**Now:** name · trade · tenure phrase · $ a day · OURS / PAYS US / OPEN. Every row looks the same whether the owner pays on time or spat in our face.

**Missing — the second line becomes the door's standing, one of:**

- refused us · 5 Jan — red
- wavering · not visited since 4 Jan — amber
- owes $400 · 3 days late — red
- short last round · "a bad week" — amber
- pays us · $120 owed · collects Thu — green
- nobody has been to see him — muted
- Castellano holds it · their man comes Thu — purple
- shut · reopens 9 Jan — muted

**Also:**

- Sort: red, amber, then the rest. The head gets a count on the right: "3 DOORS NEED AN ANSWER".
- The row is a control. Clicking it opens the door menu beside it (DEMAND / THREATEN / COLLECT / SMASH …). Show the affordance (a chevron or a hover state); today it reads as a list.
- The coloured street mark doubles as severity: red square for refused or late, amber for short or wavering.

## 5. The block card figures

**Now:** MEN STANDING ON IT · TAKE A DAY "counted into the books at midnight" · HEAT ON THIS GROUND · ON THE BOOKS SINCE · WAGES STANDING HERE · NET OFF THIS BLOCK.

**Problem:** money does not go into the books at midnight. It walks. It exists only when a collector carries it home, and it can be robbed on the way. The card has to say so.

**Missing:**

- Replace TAKE A DAY's caption with the truth and add three figures: OWED (what the doors owe right now) · IN THE BAG (carried by a round out now) · BANKED THIS WEEK. NET OFF THIS BLOCK then reads banked minus wages.
- A fact line COLLECTS Thu, or NOBODY ON THE BAG in red.
- The italic summary line gains the racket: "Held, but thin — 1 man short. Two doors owe us and nobody has collected since Tuesday."

## 6. WORD FROM THE BLOCKS

**Now:** tags OPEN · THIN · PUSHED ON · HEAT · THEIRS · PAPER ONLY, and lines like "Nobody has been to see Patrick Kaminski at River Garage yet".

**Problem:** half the lines are states, not events. A state repeats every morning ("nobody has been to see X yet") and the feed reads as saying things twice. States belong on the door row (section 4). The feed is for things that happened, once, with a time.

**Missing tags, all events:** AT THE DOOR · HE PAYS · WAVERING · REFUSED · LEANED ON · SHORT · MISSED · ROUND BANKED · ROUND LOST · WRECKED · ASSAULT · LAPSED · LOST THE DOOR, and on the collector's side ROBBED · BEATEN. Colour: red for money lost, refusal and blood; amber for short and wavering; green for paid and banked; plain for the rest.

**Sample lines:**

- 06 JAN 09:34 · THE OWNER OF HALLORAN'S BAR REFUSED US · REFUSED
- 06 JAN 11:02 · PELLEGRINI GROCER CAME UP SHORT — $60 OF $120 · "A BAD WEEK" · SHORT
- 06 JAN 13:40 · THE ROUND ON KEARNY ST. DOCKS BANKED $520 · 4 DOORS, 1 SHORT · BANKED
- 06 JAN 15:16 · THE ROUND ON LITTLE SICILY IS GONE — $410 LOST WITH THE MEN · LOST

**Also:**

- Filter THIS BLOCK / ALL when a block is picked (segmented, right of the head).
- A line is a link. Clicking a door line picks that door in the block file and opens its menu.
- Keep the time stamp "06 JAN 09:34". The game prints DAY N only today; the design's hour is right and will be adopted.
- The footer "Nothing here is filed until you file it" contradicts the telex strip at the top, which prints the same news the minute it happens. Pick one. Recommended: the feed is the wire, events as they happen, and the footer goes. Otherwise the feed is the morning digest and the strip says so.

## 7. The telex strip (top)

A rule to write into the design: one slip per event, the newest four regardless of source (a door, an incident, a round), and never a door slip and an incident slip about the same visit. Hour stamp on the right, tag beside it, as now.

## 8. The personnel page (one control)

Marking collectors happens on the man's card too: a key MAKE HIM A COLLECTOR / TAKE HIM OFF THE BAG beside PROMOTE, and a COLLECTOR mark on his row in the roster list. Same tag as section 3.

## 9. Two rules to settle before drawing

- **Money walks.** Nothing on the page may say money arrives on its own.
- **Event vs state.** The feed carries events with a time; the rows carry states. A state never appears in the feed, and an event appears on a row only as "last: refused 5 Jan".
