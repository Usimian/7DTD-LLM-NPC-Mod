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

## What must survive, and what currently does not

This is a prerequisite for all four stages, not a detail of any of them. A goal she forgets
you agreed to is worse than no goal at all — it is the same wound as the vanishing, one layer
up.

### Durable today

Structured fields on `NPCMemory`, written atomically with a `.bak` since `a8ffb80`:
`placesVisited`, `placesSeen`, `biomesSeen`, `markedPlaces`, `cargoSnapshots`, `rapport`,
`persona`, `episodes`. These are safe. Whatever is in them will be there next session, and
nothing rewrites them behind your back.

### Not durable

**Anything said in conversation.** `TrimHistory` moves expired messages into `pendingSummary`,
which is compressed into `longTermMemory` — capped at 150 words of prose and **rewritten from
scratch on every batch**. The summarizer is asked to preserve "promises made, shared events,
plans", and mostly does, but each rewrite is a fresh chance to drop something, and there is no
second copy. `pendingSummary` itself is also capped at 60 and discards the oldest.

So an agreement reached in chat decays. She will remember it for a while and then quietly not.

### The rule

**If it matters, it must be structured. The summarizer is a nicety, never the only copy.**

Prose memory is right for texture — how you two get on, what she has picked up about you. It
is wrong for anything with a truth value: a goal you agreed, a promise she made, a place you
told her to avoid.

### The mechanism

Promote it at the moment it is agreed, not afterwards from the transcript. There is already a
pass that reads her replies and extracts intent — `NPCLLMChat/Scripts/Actions/ActionParser.cs`,
which is how she acts on her own words. The same pass can recognise an agreement and write it
to a structured field.

Worth capturing this way:

| Said | Lands in |
|---|---|
| She proposes a goal and you agree | `Goal` (stage 1) |
| You ask her to remember a place | `markedPlaces` — already works |
| You tell her to avoid somewhere | a new avoid list |
| She promises something | a `promise` field on the goal, with the day |
| You tell her a fact about yourself | prose is fine — this is texture |

The test for whether something needs structure: *if she got it wrong three weeks of game time
later, would it feel like a bug or like a person misremembering?* Bugs need fields. People are
allowed prose.

## Who she is allowed to be

Honest, constructive, mostly agreeable — but not always. The disagreement is load-bearing: a
companion who agrees with everything is furniture, and her agreement means nothing if it was
never in doubt. She can think a plan is a bad idea and say so, once, and then come along.

**She is never deceptive or self-interested.** Not for realism reasons — because the moment
she is managing you, nothing she says can be taken at face value, and trust is the whole
product. A companion you have to second-guess is not more real, she is worse company.

The line runs between states and agendas:

| Safe | The line |
|---|---|
| Tired at 3am, cold, hungry, rattled after a bad fight | Wanting something she does not tell you about |
| Wanting cold gear, and saying so | Steering you toward it without saying so |
| Thinking you are wrong, and saying so | Agreeing out loud and not meaning it |

Physical states make her a body in the world alongside you. Wants are a step up and still safe
as long as they are declared. Hidden anything is the boundary.

### The pattern that already works

`NPCChatComponent` around line 659 does this correctly for time of day:

```csharp
mood   = "It is the middle of the night and you are tired.";
manner = "Low and slow, minimal words, half asleep.";
```

She is never told to *mention* being tired. She is told she *is* tired, and the model works out
what that sounds like. That is why it reads as a person rather than a line, and it is the shape
every item in this plan should take. Not "bring up the cold gear" — "you want proper cold gear
and you are not sure he is serious about it", and let it come out sideways.

## On method

This design is felt, not specified. Nobody knows exactly what makes a companion feel real, and
the parts that have landed best so far — the tiredness, the whisper when sneaking, her own
words on an episode — were small and got kept because they felt right in play, not because they
were argued for on paper.

So: build one small thing, live with it for several sessions, keep it if it changes how she
feels to be around and delete it if it does not. Resist batching. A feature that cannot be
judged in play is a feature that will accumulate.

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

0. Promote agreements out of prose into structured memory. Prerequisite — a goal she forgets
   you agreed to is worse than no goal.
1. Standing goal, talk-only. Live with it several sessions.
2. Taste, if the goal is landing.
3. Quests folded into both.
4. Kit baseline.

Open, and worth deciding before stage 1 is written:

- Should she ever act on the goal, and if so only while following?
- One goal forever, or is she allowed to give up on one that has gone stale?
- Should you be able to talk her out of a goal, or into one?
