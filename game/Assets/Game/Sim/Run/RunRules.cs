using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>
    /// The run (unity-game.md, The Run; native's engine/freeze.rs): its start, the run clock,
    /// freezes, the music stopping, the finale and the ends.
    ///
    /// - <b>Start</b> (<see cref="SimConfig.Start"/>): on the first committed beat after
    ///   <see cref="RunControl.Start"/>, or after <see cref="ClearStartS"/> of reliable beat. Until
    ///   then the world is held. A <see cref="SimEventKind.RunStart"/> marks it.
    /// - <b>The run clock</b> counts song time while the run plays and the world isn't frozen,
    ///   exactly: a freeze stops it at its own time, not at a tick's end.
    /// - <b>Freezes</b> (<see cref="FreezeReason"/>) hold the world while any reason holds; each
    ///   reason's start and the resume emit <see cref="SimEventKind.Freeze"/>. A reason let go
    ///   (<see cref="Thaw"/>: a resume, a card confirmed) resumes play on the next committed beat,
    ///   or on the next stride step when no beat is known ahead.
    /// - <b>The music stops</b> after <see cref="QuietS"/> of ticks at or below
    ///   <see cref="SilentEnergy"/> (native's), after <see cref="EndQuietS"/> of them around a
    ///   confirmed end, or on <see cref="RunControl.Watchdog"/>: the world freezes on the first
    ///   stride step after that is known. It resumes on the second downbeat after the beat locks
    ///   again: the first reliable beat after the quiet, with one whole bar to hear the tempo.
    ///   Quiet again before then restarts the lock.
    /// - <b>Victory</b>: when the run clock reaches the schedule's duration the run's
    ///   <see cref="RunPhase.Finale"/> begins; the finale's blast fires on the next downbeat the
    ///   world plays (every enemy and structure within its radius of the hero, a
    ///   <see cref="SimEventKind.Finale"/>), the hero holds, and the victory card's
    ///   <see cref="SimEventKind.RunEnd"/> follows <see cref="Finale.VictoryCardS"/> later.
    /// - <b>Ends</b>: victory, the hero's fall, <see cref="RunControl.Quit"/> and
    ///   <see cref="RunControl.DeviceChanged"/> each end the run with a
    ///   <see cref="SimEventKind.RunEnd"/>; the run clock stops there.
    ///
    /// The beat's systems call in at fixed times (a beat, a stride step, a revealed entry, a tick's
    /// end), so the run is a function of the stream's known-at times and the recorded controls.
    /// </summary>
    internal sealed class RunRules
    {
        public const double ClearStartS = 2;
        public const double SilentEnergy = 0.01;
        public const double QuietS = 5;
        public const double EndQuietS = 3;
        /// <summary>How far the finale's blast throws a structure's shards, metres: twice the heavy strike's.</summary>
        public const double BlastForceM = 9.26;

        readonly SimState state;
        readonly BeliefLog log;
        readonly RunStart start;
        readonly double duration;
        readonly Finale finale;
        double account;
        double startRequested = double.NaN;
        double reliableSince = double.NaN;
        int held, released;
        int ticksSeen;
        double quietFrom = double.NaN, clearFrom = double.NaN;
        bool musicPending;
        double musicSince;
        int lockBeat = -1, downbeatsSinceLock;
        double cardAt = double.PositiveInfinity;

        public RunRules(SimConfig config, GameContent content, BeliefLog log, SimState state)
        {
            this.state = state;
            this.log = log;
            start = config.Start;
            duration = content?.Schedule.DurationS ?? double.PositiveInfinity;
            finale = content?.Schedule.Finale;
            account = state.Now;
            if (start == RunStart.Immediately)
            {
                state.Phase = RunPhase.Playing;
                state.StartedAt = state.Now;
            }
            else state.Frozen = true;
        }

        /// <summary>The world may play: the run started, and hasn't ended or blasted its finale.</summary>
        bool Plays => state.Phase == RunPhase.Playing || (state.Phase == RunPhase.Finale && double.IsInfinity(cardAt));

        /// <summary>A recorded run control, at the sim's time <paramref name="now"/>.</summary>
        public void Control(Simulation sim, RunControl control, double now)
        {
            switch (control)
            {
                case RunControl.Start:
                    if (state.Phase == RunPhase.Waiting && double.IsNaN(startRequested)) startRequested = now;
                    break;
                case RunControl.Pause:
                    Freeze(sim, now, FreezeReason.Pause);
                    break;
                case RunControl.Resume:
                    Thaw(FreezeReason.Pause);
                    break;
                case RunControl.Quit:
                    End(sim, now, RunEnd.Quit);
                    break;
                case RunControl.DeviceChanged:
                    End(sim, now, RunEnd.DeviceChanged);
                    break;
                case RunControl.Watchdog:
                    if (Plays && !Holds(FreezeReason.MusicStopped)) musicPending = true;
                    break;
            }
        }

        static int Bit(FreezeReason reason) => 1 << (int)reason;

        bool Holds(FreezeReason reason) => (held & Bit(reason)) != 0;

        /// <summary>The world freezes at <paramref name="t"/> for <paramref name="reason"/>, while the run plays.</summary>
        public void Freeze(Simulation sim, double t, FreezeReason reason)
        {
            if (!Plays || Holds(reason)) return;
            Account(t);
            held |= Bit(reason);
            released &= ~Bit(reason);
            state.FreezeReasons = held;
            state.Frozen = true;
            sim.Emit(SimEvent.Freeze(t, reason, true));
        }

        /// <summary>
        /// <paramref name="reason"/> no longer holds the world: once none does, play resumes on the
        /// next committed beat (or stride step with no beat known ahead).
        /// </summary>
        public void Thaw(FreezeReason reason)
        {
            if (!Holds(reason)) return;
            held &= ~Bit(reason);
            released |= Bit(reason);
            state.FreezeReasons = held;
        }

        void Resume(Simulation sim, double t)
        {
            state.Frozen = false;
            account = t;
            for (int r = 0; released != 0; r++)
            {
                if ((released & (1 << r)) == 0) continue;
                released &= ~(1 << r);
                sim.Emit(SimEvent.Freeze(t, (FreezeReason)r, false));
            }
        }

        bool ResumeDue => held == 0 && released != 0;

        /// <summary>The run clock counts the song time from the last account to <paramref name="t"/>, if the run plays unfrozen.</summary>
        void Account(double t)
        {
            if (state.Phase == RunPhase.Playing && !state.Frozen)
                state.RunClock = Math.Min(duration, state.RunClock + (t - account));
            account = t;
        }

        /// <summary>The run ends at <paramref name="t"/>; the run clock stops there.</summary>
        public void End(Simulation sim, double t, RunEnd why)
        {
            if (state.Phase == RunPhase.Ended) return;
            Account(t);
            if (state.Phase == RunPhase.Waiting) state.Frozen = false;
            state.Phase = RunPhase.Ended;
            state.End = why;
            state.EndedAt = t;
            sim.Emit(SimEvent.RunEnd(t, why));
        }

        /// <summary>The beat's state changed at <paramref name="t"/>.</summary>
        public void BeatState(double t, Reliability now)
        {
            if (now != Reliability.Reliable) reliableSince = double.NaN;
            else if (double.IsNaN(reliableSince)) reliableSince = t;
        }

        /// <summary>Takes in the newly revealed ticks: quiet, clear, and the music stopping.</summary>
        public void Reveal()
        {
            var ticks = log.Ticks;
            for (; ticksSeen < ticks.Length; ticksSeen++)
            {
                ref readonly var tick = ref ticks[ticksSeen];
                if (tick.V1 > SilentEnergy)
                {
                    quietFrom = double.NaN;
                    if (double.IsNaN(clearFrom)) clearFrom = tick.T;
                    continue;
                }
                clearFrom = double.NaN;
                lockBeat = -1;
                if (double.IsNaN(quietFrom)) quietFrom = tick.T;
                bool stopped = tick.T >= quietFrom + QuietS - 1e-9 || (tick.T >= quietFrom + EndQuietS - 1e-9 && EndAround(tick.T));
                if (stopped && Plays && !Holds(FreezeReason.MusicStopped)) musicPending = true;
            }
        }

        /// <summary>Is a confirmed end revealed within the quiet, or up to <see cref="EndQuietS"/> before it?</summary>
        bool EndAround(double t)
        {
            foreach (ref readonly var e in log.Revealed(EntryKind.End))
                if (e.T >= quietFrom - EndQuietS - 1e-9 && e.T <= t + 1e-9) return true;
            return false;
        }

        /// <summary>The next song time the run itself acts at: the run clock reaching the duration, or the victory card.</summary>
        public double NextTime()
        {
            if (state.Phase == RunPhase.Playing && !state.Frozen && !double.IsInfinity(duration))
                return account + (duration - state.RunClock);
            return state.Phase == RunPhase.Finale ? cardAt : double.PositiveInfinity;
        }

        public void Fire(Simulation sim, double t)
        {
            if (state.Phase == RunPhase.Playing)
            {
                account = t;
                state.RunClock = duration;
                state.Phase = RunPhase.Finale;
            }
            else End(sim, t, RunEnd.Victory);
        }

        /// <summary>
        /// Beat <paramref name="i"/> fires at <paramref name="t"/> (its bar and the beat's state
        /// already in <see cref="SimState"/>), before the systems that act on it: the start, the
        /// music's lock and resume, a resume, and the finale's blast land here.
        /// </summary>
        public void Beat(Simulation sim, double t, int i)
        {
            if (state.Phase == RunPhase.Waiting && StartDue(t))
            {
                state.Phase = RunPhase.Playing;
                state.Frozen = false;
                state.StartedAt = t;
                account = t;
                sim.Emit(SimEvent.RunStart(t));
                return;
            }
            if (Holds(FreezeReason.MusicStopped))
            {
                if (lockBeat < 0)
                {
                    if (state.Beat == Reliability.Reliable && t >= musicSince - 1e-9 && double.IsNaN(quietFrom)
                        && !double.IsNaN(clearFrom) && t >= clearFrom - 1e-9)
                    {
                        lockBeat = i;
                        downbeatsSinceLock = 0;
                    }
                }
                else if (state.Downbeat && ++downbeatsSinceLock == 2)
                {
                    lockBeat = -1;
                    Thaw(FreezeReason.MusicStopped);
                }
            }
            if (ResumeDue) Resume(sim, t);
            if (state.Phase == RunPhase.Finale && Plays && !state.Frozen && state.Downbeat) Blast(sim, t);
        }

        bool StartDue(double t)
        {
            if (start == RunStart.OnKey) return t > startRequested + 1e-9;
            return start == RunStart.AfterClearBeat && state.Beat == Reliability.Reliable
                   && t >= reliableSince + ClearStartS - 1e-9;
        }

        /// <summary>A stride step at <paramref name="t"/>, before the hero's move on it: the music stopping, or a resume with no beat ahead.</summary>
        public void Step(Simulation sim, double t)
        {
            if (musicPending)
            {
                musicPending = false;
                if (!Plays) return;
                Freeze(sim, t, FreezeReason.MusicStopped);
                musicSince = t;
                lockBeat = -1;
            }
            else if (ResumeDue && state.Beat == Reliability.NoBeatAhead) Resume(sim, t);
        }

        /// <summary>The tick's end: the run clock.</summary>
        public void Tick(double end) => Account(end);

        void Blast(Simulation sim, double t)
        {
            state.Hero.PositionAt(t, out double x, out double y);
            sim.Horde?.Blast(sim, t, x, y, finale.RadiusM, finale.Damage);
            sim.Structures.Strike(sim, t, x, y, finale.RadiusM, StructureHit.FinaleBlast, BlastForceM);
            sim.Emit(SimEvent.Finale(t, x, y, finale.RadiusM));
            // The hero holds its pose: the world stays still through the card.
            state.Frozen = true;
            cardAt = t + finale.VictoryCardS;
        }
    }
}
