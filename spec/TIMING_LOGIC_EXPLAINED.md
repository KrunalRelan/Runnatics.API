# How Race Timing Works — A Plain-English Walkthrough

This explains, step by step, how the system turns chip detections at timing mats into each
runner's final result. No code knowledge needed. (A separate, technical version with exact file
references lives in `TIMING_LOGIC_SPEC.md`.)

---

## The big picture in one paragraph

A runner wears a chip. Timing mats on the course detect that chip and record the exact moment each
time it passes. The system collects all those detections, figures out **which mat = which point on
the course** (Start, a split, the Finish), picks **the one correct detection** at each point, and
then does simple subtraction to get the runner's times. Finally it decides whether they **Finished**,
and ranks the finishers fastest-first.

Think of it as three buckets that never get mixed up:

| Bucket | What it is | Can it change? |
|---|---|---|
| **Raw detections** | The mats' permanent record of every chip sighting | **Never edited.** This is the truth. |
| **Manual corrections** | A human override when the automatic read is wrong | Stays until a human removes it |
| **Results** | Everything calculated (times, splits, ranks, status) | Thrown away and rebuilt every time we reprocess |

The golden rule: **we never edit the raw truth.** If something's wrong, we either fix how we
*interpret* the raw data, or a human adds a correction in a separate place. The results are always
rebuilt from scratch.

---

## Follow one detection from mat to result

Let's follow runner **#2133** in the 5K race. Their gun (official start) is **6:29 AM**.

### Step 1 — The chip is detected
Each pass over a mat creates a **raw detection**: which chip, which mat, and the exact timestamp.
These are stored in UTC time and never changed. If a single detection accidentally reports two
chips at once, it's set aside and ignored (it's unreliable).

> *Real-world wrinkle:* because the race is in India (UTC+5:30) and starts before 5:30 AM, some
> timestamps land on the "previous day" in UTC. That looks odd but is correct — the system accounts
> for it.

### Step 2 — Which mat is which point?
Every mat is linked to one or more points on the course. The system matches each detection to a
course point:

- A mat used for **only one point** in this race (say, the 2.5 km split) → easy, the detection is
  that point.
- A mat used for **several points** — for example a **shared Start/Finish arch** where runners pass
  at the start *and* again at the finish — needs more care (Step 3).
- A detection from a mat that isn't part of *this* race → left unmatched and ignored. (At a
  multi-race event, one runner's chip can be seen by mats belonging to other races; those are
  filtered out.)
- A very weak signal (a faint, unreliable read) → discarded.

### Step 3 — Picking the **start** detection (the tricky part)
Here's the problem this system is specifically built to solve.

At a big event, the 21K, 10K, and 5K races often **share the same start arch** but start at
**different times** (a "staggered start"): 5:29, 6:02, and 6:29 AM. A 5K runner standing near the
arch at 5:29 (when the 21K gun fires) gets their chip detected then — even though their own race
doesn't start until 6:29.

Runner #2133 was detected **twice** at the shared arch: once at **5:29** (just standing there during
the 21K start) and once at **6:29** (their real 5K start).

A naive system would take the **earliest** detection (5:29) as the start — which is wrong, and
produces nonsense times. Instead, the system uses a **gun window**:

> **The start is the detection closest to the runner's own gun, within a window from
> 5 minutes before to 15 minutes after the gun.**

- The window for the 5K (gun 6:29) is **6:24 to 6:44**.
- The 5:29 detection is far outside that window → **excluded**.
- The 6:29 detection is inside → **chosen as the start.** ✅

Why those numbers? The three guns are at least 27 minutes apart, so a window of "5 minutes before /
15 after" comfortably separates them — it can never reach back to another race's gun. The small
"5 minutes before" allowance exists because a few runners legitimately cross the line a moment before
the horn.

### Step 4 — Picking detections at the other points
- At a **mid-race split**, if the mat fired several times, the system keeps the **first** detection
  (the moment the runner reached it).
- At the **finish**, same idea — the finish crossing.
- At the **start**, as above, the detection nearest the gun.

After this step, runner #2133 has a clean set of crossings: **Start 6:29, 2.5 km 6:42, Finish 7:31.**

### Step 5 — Calculating the times
Now it's simple subtraction:

- **Gun time** = Finish − Gun = 7:31 − 6:29 = **1 hour 2 minutes.** (Your time from the official horn.)
- **Net time** = Finish − *your own* start crossing. (Your time from when *you* crossed the line.)
  For most runners gun and net are nearly the same.
- **Splits** = how long to reach each checkpoint (e.g. time to 2.5 km), and the **segment** time
  between consecutive checkpoints.
- **Pace** (minutes per km) and **speed** (km/h) come from those.

A built-in safety check: any crossing that would produce a **negative or impossible time** (like a
"finish" that lands before the gun) is rejected rather than used, so one bad detection can't corrupt
a runner's result.

### Step 6 — Did they finish?
- **Finished** — detected at **every required point**, including the finish.
- **DNF (Did Not Finish)** — they started but **missed a required point**.
- **DNS (Did Not Start)** — **never detected at the start** at all.

(*Note:* there is currently **no automatic disqualification** — that's a human/manual concern, not
something the timing logic decides.)

### Step 7 — Ranking
Only **finishers** are ranked. They're sorted fastest-first by time. The same sorting then produces
the separate **gender** and **age-category** standings. Non-finishers get no rank.

---

## When a human needs to correct something

Sometimes the automatic result is wrong (a missed read, a bad detection). A timing official can fix
it in two ways, and **both are durable** — they survive every future reprocess until explicitly undone:

1. **Type a corrected time** for a checkpoint, or
2. **Pick the right detection** from the list of raw sightings at that checkpoint (when the right
   read exists but the system chose the wrong one).

These corrections are stored **separately** from the raw data and the results. Every time the race is
reprocessed, the system rebuilds results from the raw detections and then **re-applies the human
corrections on top.** That's why a manual fix never gets "lost" when data is recalculated.

To undo a correction, the official removes it — that's the only way it disappears. If a correction
was the *only* time at a checkpoint, removing it can flip a runner from Finished to DNF (the system
warns about this).

There's a strict rule: **one correction per runner per checkpoint** — you can't have two competing
manual times at the same point.

---

## Moving a runner to a different race

If a runner is registered in the wrong race (say they ran the 5K but were entered in the 21K), an
official can move them. When that happens:

- **Their raw detections are kept** (the truth never changes — they physically made those crossings).
- **All their calculated results are wiped** and rebuilt against the **new race's** start time and
  checkpoints.
- **Any manual corrections are cancelled** (they were tied to the old race's checkpoints, which mean
  nothing in the new race).

Because the gun-window logic (Step 3) anchors the start on the *new* race's gun, a runner carrying
old detections from another race won't accidentally get the wrong start.

---

## Recalculating a race ("clear and reprocess")

When mappings or times change, an official can clear a race and rebuild it:

- **Calculated data** (results, splits, normalized crossings) is **deleted** and rebuilt.
- **Raw detections** are normally **kept** (just reset so they get reprocessed). There's also a
  stronger option that deletes the raw uploads entirely — used only when starting completely fresh.
- **Manual corrections are always kept** and re-applied during the rebuild.

The rebuild always runs the same ordered steps: match detections to points → pick the right ones →
calculate times → apply manual corrections → build splits → compute finish status and ranks.

---

## The settings that tune the behavior

A few adjustable numbers control the edges:

| Setting | What it does | Current value |
|---|---|---|
| **Start window** | How close to the gun a start crossing must be | 5 min before → 15 min after |
| **Early-start cutoff** | How far before the gun the system bothers to load detections | usually 10 min (one race is set looser, ~60 min) |
| **Duplicate window** | Detections this close together count as one pass | 30 seconds |
| **Pass-gap threshold** | A gap this big means "a new lap / a new crossing" | 5 minutes |
| **Weak-signal cutoff** | Below this signal strength, a detection is ignored | −80 dBm |
| **Max sensible time** | A time longer than this is treated as invalid | 24 hours |

---

## Things worth double-checking with the client

These are places where the system's behavior might differ from what the client assumes — worth
confirming:

1. **Disqualification (DQ)** is **not automated** — there's a place to record it, but nothing sets it
   automatically.
2. **Tied times** currently get **different consecutive ranks** (e.g. two runners with identical times
   become rank 5 and rank 6, not joint-5th). Confirm whether ties should share a rank.
3. **"Did Not Start" (DNS)** is only assigned by the full race recalculation, not by the single-runner
   manual-edit path — so a one-off edit won't mark someone DNS.
4. **Pace** is shown two slightly different ways in two places (average-so-far vs this-segment-only) —
   confirm which the client wants displayed where.

---

*For exact methods, file locations, formulas, and line-level citations, see
`TIMING_LOGIC_SPEC.md` in this folder.*
