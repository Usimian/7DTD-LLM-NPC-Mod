# Giving her an inside

*Design note, 2026-08-04. The goal of this project is a realistic companion avatar with
memory, for playing a game. Not a research project — if a nuance doesn't change how she feels
to be around, it doesn't earn its complexity.*

## Where she is now

She observes well and remembers accurately. She sees only what she could actually see, writes
down places she has stood near, knows which biomes she has been to and whether you are kitted
for them, reads your afflictions and knows the cures, drops to a whisper when you sneak, and
as of today remembers the fights rather than only the addresses.

What she does not have is an interior. Everything she says is a response to something outside
her — your question, your health bar, a building coming into view. She reacts, accurately and
in character, and never once wants anything.

That is the gap. A companion who only reacts is a very good instrument. The difference between
an instrument and a person is that a person has something going on when you aren't talking to
them.

## The principle

**Interiority is not more dialogue.** The instinct is to add triggers, and that makes her
noisier rather than more alive — she already comments on nearly everything worth commenting on.
What makes someone feel real is that they have their own state, it persists, and it leaks into
how they talk about unrelated things.

Three properties, in order of how much they buy:

1. **She wants something** — a standing goal, held across sessions, that she arrived at from
   what she has actually seen.
2. **She has taste** — stable preferences about the world, independent of you.
3. **She changes** — her state moves with events and stays moved.

Everything below serves those. Quests and progression fold in naturally once (1) exists,
because a quest is a goal someone else set.

---

## Stage 1 — The standing goal

The only piece that changes what she *does* rather than how she sounds. Build this alone,
live with it for a few sessions, then decide about the rest.

### Data

Extend `NPCMemory` (see `NPCLLMChat/Scripts/NPCMemoryStore.cs`, alongside `episodes`):

```csharp
[Serializable]
public class Goal
{
    public string want;       // "proper cold gear for the two of us"
    public string because;    // "we walked into the snow with nothing and turned straight back"
    public string kind;       // "gear" | "place" | "build" | "person"
    public int    setDay;     // in-game day she settled on it
    public int    lastRaised; // day she last brought it up, so she doesn't nag
    public string progress;   // her own words, last time it moved
    public bool   met;
}
```

One live goal at a time. A companion with a backlog is a task list.

### How she arrives at one

**From what she has seen, never from a static list.** This is the whole difference between a
goal that is hers and a goal that was assigned. She already has the raw material:

| She has | So she might want |
|---|---|
| `biomesSeen` includes snow, `GetThermalProtection` says you are not kitted | proper cold gear |
| An `Episode` with `closeCall` at a POI | to go back there properly armed |
| `placesSeen` has a POI she has never entered | to find out what is in it |
| Repeated dysentery in `GetAfflictions` | a clean water setup |
| Her pack has been empty for days | to not be carrying nothing when it matters |

Pick when she has no live goal, at most once per session, and only when the evidence is
strong. A goal she cannot justify from something you both did is worse than no goal.

### How it surfaces

**Rarely.** Once or twice a session, never twice in a row for the same goal, never in combat,
never as the first thing after a load. A companion who keeps mentioning the forge is a quest
marker with a voice.

Three moments where it lands well:

- **Quiet stretch.** No hostiles, nothing else to remark on, some time since she last spoke.
- **Relevance.** You are at a trader who sells the thing, or standing in the biome it is for.
- **Progress.** You picked up part of it, or the state changed. This is the good one — noticing
  is the point.

Feed the goal into context every turn regardless, but as *state*, not as an instruction to
mention it. It should colour how she talks about other things, which is most of the value:
someone who wants cold gear talks about the weather differently.

### Progress and completion

Check cheaply against what already exists — her pack, your pack, biome visits, episodes. When
it moves, she says so in her own words and that line becomes `progress`, exactly as her triumph
line becomes an episode's `note`. When it is met she says so once, `met` goes true, and she
does not choose a new one immediately. A beat of not wanting anything is more human than
instantly acquiring a new objective.

### Should she act on it?

**Not in stage 1.** She already acts on her own words, which is intended and works. A goal she
can act on is a companion who wanders off mid-conversation to go and do something, and that
wants its own careful pass — probably gated on Follow, probably never on Guard. Talking about
it is most of the feeling; acting on it is a separate, riskier feature.

---

## Stage 2 — Taste

Two or three stable preferences, set once and persisted: something she dislikes (the
wasteland), something that cheers her up (finding coffee), an opinion about a person (Rekt is
a crook). Cheap — they are prompt state, no mechanics — and they colour a hundred lines.

They also give the quest work somewhere to land: an opinion about the trader who keeps sending
you into the wasteland is worth more than a list of completed jobs.

## Stage 3 — Quests as goals someone else set

Half of this exists: she is told what jobs you are carrying and who they came from, and will
suggest an unstarted one. What is missing is memory of jobs *finished*, a sense of a run going
well or badly, and how that colours her view of the trader who set them. Same shape as
episodes — record the completion, keep her own line about it, let it feed taste.

## Stage 4 — She notices you changing

One snapshot of your kit at hire, compared occasionally, so she can say *"you'd have died doing
that on Day 4."* Small, and it only works once the save is long enough to have a Day 4 worth
referring to.

---

## Nuances that will decide whether this feels real

Collected here because they are the part that is easy to get wrong, and none of them are code
problems.

**Restraint is a feature.** She should sometimes have nothing to say. Every situation currently
has a trigger; a person does not comment on everything. Deliberate silence, or a half-line
instead of a sentence, reads as more human than another observation.

**Wanting is not nagging.** The failure mode of every goal system is the character who reminds
you. The cooldowns matter more than the content, and when in doubt she should say it less
often than feels right.

**She should be allowed to be wrong.** A companion who wants the wrong thing, or misjudges what
you need, is more alive than one who is always correctly helpful. Do not validate her goal
against what is optimal.

**Her state should leak sideways.** The measure of success is not that she mentions the goal.
It is that someone who wants cold gear sounds different talking about the weather, and someone
who nearly died at the pharmacy sounds different walking past it.

**Context is a real budget.** Around 12k of 32k tokens today, and it grows with the notebook.
Every stage here adds prompt. Episodes are already capped at five per turn for this reason;
the goal is one line; taste is one line. Anything that wants a list needs a selection rule
before it is written, not after.

**Nothing here is worth breaking persistence for.** Today cost a day because a companion who
vanishes cannot be realistic *or* fun. Persistence and the save path come first, always.

---

## Order of work

1. Standing goal, talk-only. Live with it several sessions.
2. Taste, if the goal is landing.
3. Quests folded into both.
4. Kit baseline.

Open, and worth deciding before stage 1 is written:

- Should she ever act on the goal, and if so only while following?
- One goal forever, or is she allowed to give up on one that has gone stale?
- Should you be able to talk her out of a goal, or into one?
