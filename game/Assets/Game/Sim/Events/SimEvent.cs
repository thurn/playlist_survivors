using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>
    /// What the sim tells presentation (unity-game.md, "The Sim's Contract"). The sim emits beats,
    /// stride steps, beat-state changes, the hero's steps and attacks, the enemies' steps, knocks,
    /// lunges and spawns, hits, deaths, cleaves and drops, structure glints, breaks and regrowths,
    /// zone changes, freezes, the run's start, its finale and its end, gems' flights and pickups,
    /// level-ups and their cards, the music-driven events and the enemies that leave with them, the
    /// tiles a trail weapon burns, the weapons' projectiles and orbits (ProjectileEvents.cs), and the
    /// lightning's bolts; the rest are declared, with their fields,
    /// for the rule beads to emit and the client to build against.
    /// </summary>
    public enum SimEventKind
    {
        None = 0,
        Beat,
        StrideStep,
        BeatState,
        Attack,
        EnemyStep,
        Hit,
        Death,
        StructureBreak,
        Regrowth,
        Spawn,
        Pickup,
        LevelUp,
        Chest,
        Freeze,
        SoundCue,
        HeroStep,
        EnemyKnock,
        Cleave,
        Drop,
        Glint,
        ZoneChange,
        RunStart,
        Finale,
        RunEnd,
        GemFlight,
        Card,
        SetPiece,
        Leave,
        Burn,
        Projectile,
        Orbit,
        Bolt,
        HeroGlide,
        Finisher,
        HeroDash,
    }

    /// <summary>The beat's three states (unity-game.md, "Beats, Bars and Reliability").</summary>
    public enum Reliability
    {
        /// <summary>No committed beat ahead: the hero strides on a free pulse, enemies hold.</summary>
        NoBeatAhead = 0,
        /// <summary>Beats known ahead, but below the gate or the music unclear: enemies hold.</summary>
        BelowGate = 1,
        /// <summary>Reliable beats: enemies step on main beats.</summary>
        Reliable = 2,
    }

    public enum FreezeReason { LevelUp, Chest, Pause, MusicStopped }

    /// <summary>
    /// A chest's moments (<see cref="SimEventKind.Chest"/>): the hero takes it and its roulette opens,
    /// freezing the world; it ticks on eight eighths; it lands on the downbeat after them, then each
    /// reward follows at the landing's time.
    /// </summary>
    public enum ChestPhase { Open = 0, Tick = 1, Land = 2, Reward = 3 }

    /// <summary>
    /// A finisher ring's moments (<see cref="SimEventKind.Finisher"/>, <see cref="Finishers"/>): it starts closing on the
    /// hero toward its phrase downbeat; its downbeat's prediction moves; a press in its window fires the slam; or it
    /// ends with no press in it, or is dropped by a hold.
    /// </summary>
    public enum FinisherPhase { Ring = 0, Retime = 1, Hit = 2, Miss = 3, Drop = 4 }

    /// <summary>
    /// One settled event: dense and blittable, so the log is a plain array and its hash a pass over
    /// fields. <see cref="T"/> is the song time it fired at; the slots' meaning depends on
    /// <see cref="Kind"/>, as each factory documents, and is read through the kind's view. Positions
    /// are ground-plane metres (x, y), ids index the sim's dense arrays, and -1 means none.
    /// </summary>
    public partial struct SimEvent
    {
        public SimEventKind Kind;
        public double T;
        public int I0, I1, I2;
        public double D0, D1, D2, D3, D4, D5;

        static SimEvent Of(SimEventKind kind, double t) => new SimEvent { Kind = kind, T = t };

        /// <summary>A committed beat reached: I0 beat index, I1 bar (-1 before any lock), I2 downbeat (0/1) | reliability &lt;&lt; 1; D0 period, D1 evidence.</summary>
        public static SimEvent Beat(double t, int index, int bar, bool downbeat, Reliability reliability, double period, double evidence)
        {
            var e = Of(SimEventKind.Beat, t);
            e.I0 = index;
            e.I1 = bar;
            e.I2 = (downbeat ? 1 : 0) | ((int)reliability << 1);
            e.D0 = period;
            e.D1 = evidence;
            return e;
        }

        /// <summary>A stride step: I0 step index, I1 halvings (-1 on the free pulse), I2 the segment's first beat, or the latest beat it continues (-1 free); D0 period to the next step.</summary>
        public static SimEvent StrideStep(double t, int index, int halvings, int beat, double period)
        {
            var e = Of(SimEventKind.StrideStep, t);
            e.I0 = index;
            e.I1 = halvings;
            e.I2 = beat;
            e.D0 = period;
            return e;
        }

        /// <summary>The beat's state changed: I0 the new state, I1 the previous.</summary>
        public static SimEvent BeatState(double t, Reliability now, Reliability before)
        {
            var e = Of(SimEventKind.BeatState, t);
            e.I0 = (int)now;
            e.I1 = (int)before;
            return e;
        }

        /// <summary>
        /// The hero's attack winds up: I0 -1, I1 the weapon's kind (the sword's: <see cref="HeroAttackKind"/>),
        /// I2 flags: 1 heavy | the weapon's shape (<see cref="ShapeKind"/>) &lt;&lt; 1 | its content index &lt;&lt; 4;
        /// D0 wind-up start, D1 impact time, D2/D3 direction, D4 the hit's reach around the hero.
        /// The sword, content's weapon 0 with shape 0, keeps those bits 0.
        /// </summary>
        public static SimEvent HeroAttack(double t, int weapon, ShapeKind shape, int kind, double windupStart, double impact,
            double dirX, double dirY, bool heavy, double reach)
        {
            var e = Of(SimEventKind.Attack, t);
            e.I0 = -1;
            e.I1 = kind;
            e.I2 = (heavy ? 1 : 0) | ((int)shape << AttackEvent.ShapeShift) | (weapon << AttackEvent.WeaponShift);
            e.D0 = windupStart;
            e.D1 = impact;
            e.D2 = dirX;
            e.D3 = dirY;
            e.D4 = reach;
            return e;
        }

        /// <summary>
        /// The hero's sword winds up a strike: <see cref="HeroAttack"/> with the sword's shape, and D5 a press's beat grade
        /// (<see cref="BeatGrade"/>, 0.5 to 1; 0 for a strike the beat planned).
        /// </summary>
        public static SimEvent SwordAttack(double t, int weapon, HeroAttackKind kind, double windupStart, double impact,
            double dirX, double dirY, bool heavy, double reach, double grade = 0)
        {
            var e = HeroAttack(t, weapon, ShapeKind.Sword, (int)kind, windupStart, impact, dirX, dirY, heavy, reach);
            e.D5 = grade;
            return e;
        }

        /// <summary>
        /// An enemy's attack winds up: I0 enemy, I1 <see cref="EnemyAttackKind"/>; D0 wind-up start, D1 impact time,
        /// D2/D3 direction, D4/D5 a slam's centre (where it lands: the boss, or the signature's mark).
        /// </summary>
        public static SimEvent EnemyAttack(double t, int enemy, EnemyAttackKind kind, double windupStart, double impact, double dirX, double dirY,
            double x = 0, double y = 0)
        {
            var e = Of(SimEventKind.Attack, t);
            e.I0 = enemy;
            e.I1 = (int)kind;
            e.D0 = windupStart;
            e.D1 = impact;
            e.D2 = dirX;
            e.D3 = dirY;
            e.D4 = x;
            e.D5 = y;
            return e;
        }

        /// <summary>An enemy step decided: I0 enemy; D0/D1 from, D2/D3 to, D4 move start, D5 footplant.</summary>
        public static SimEvent EnemyStep(double t, int enemy, double fromX, double fromY, double toX, double toY, double moveStart, double footplant)
        {
            var e = Of(SimEventKind.EnemyStep, t);
            e.I0 = enemy;
            e.D0 = fromX;
            e.D1 = fromY;
            e.D2 = toX;
            e.D3 = toY;
            e.D4 = moveStart;
            e.D5 = footplant;
            return e;
        }

        /// <summary>
        /// A hit on an enemy: I0 enemy, I1 how it landed (<see cref="WeaponHit"/>), I2 the weapon's
        /// content index (the sword's is 0; -1 for the finale's blast); D0 the damage dealt, D1/D2 the enemy's position, D3/D4 the
        /// direction it is struck in, D5 1 for a critical hit (Bloody Tear's).
        /// </summary>
        public static SimEvent EnemyHit(double t, int enemy, int weapon, WeaponHit how, double damage, double x, double y, double dirX, double dirY,
            bool critical = false)
        {
            var e = Of(SimEventKind.Hit, t);
            e.I0 = enemy;
            e.I1 = (int)how;
            e.I2 = weapon;
            e.D0 = damage;
            e.D1 = x;
            e.D2 = y;
            e.D3 = dirX;
            e.D4 = dirY;
            e.D5 = critical ? 1 : 0;
            return e;
        }

        /// <summary>
        /// A hit on the hero: I0 -1, I1 the enemy's attack (<see cref="EnemyAttackKind"/>); D0 the damage
        /// dealt (0 while it can't be hurt), D1/D2 the hero's position, D3/D4 the direction it is struck in.
        /// </summary>
        public static SimEvent HeroHit(double t, EnemyAttackKind kind, double damage, double x, double y, double dirX, double dirY)
        {
            var e = Of(SimEventKind.Hit, t);
            e.I0 = -1;
            e.I1 = (int)kind;
            e.D0 = damage;
            e.D1 = x;
            e.D2 = y;
            e.D3 = dirX;
            e.D4 = dirY;
            return e;
        }

        /// <summary>
        /// A death: I0 enemy (-1 the hero, who fell: "YOU FELL"), I1 role (-1 the hero); D0/D1 position,
        /// D2/D3 impact direction, D4 force: how far the death launch throws the body, in metres (0 for the hero).
        /// </summary>
        public static SimEvent Death(double t, int enemy, int role, double x, double y, double dirX, double dirY, double force)
        {
            var e = Of(SimEventKind.Death, t);
            e.I0 = enemy;
            e.I1 = role;
            e.D0 = x;
            e.D1 = y;
            e.D2 = dirX;
            e.D3 = dirY;
            e.D4 = force;
            return e;
        }

        /// <summary>
        /// A structure shatters, on the hit's own frame: I0 structure (the level data's index), I1 what
        /// broke it (<see cref="StructureHit"/>); D0/D1 the strike point (the structure's nearest point
        /// to the hit's centre), D2/D3 the direction from the hit's centre through it, D4 force: how far
        /// the hit throws, in metres, as a death's.
        /// </summary>
        public static SimEvent StructureBreak(double t, int structure, StructureHit source, double x, double y,
            double dirX, double dirY, double force)
        {
            var e = Of(SimEventKind.StructureBreak, t);
            e.I0 = structure;
            e.I1 = (int)source;
            e.D0 = x;
            e.D1 = y;
            e.D2 = dirX;
            e.D3 = dirY;
            e.D4 = force;
            return e;
        }

        /// <summary>
        /// A hit on a standing structure that doesn't break it: I0 structure, I1 the hit
        /// (<see cref="StructureHit"/>), I2 the breaking hits it has taken toward its break (0 when
        /// this hit can't break it); D0/D1 the strike point, D2/D3 the direction, as a break's.
        /// </summary>
        public static SimEvent Glint(double t, int structure, StructureHit source, int hits, double x, double y, double dirX, double dirY)
        {
            var e = Of(SimEventKind.Glint, t);
            e.I0 = structure;
            e.I1 = (int)source;
            e.I2 = hits;
            e.D0 = x;
            e.D1 = y;
            e.D2 = dirX;
            e.D3 = dirY;
            return e;
        }

        /// <summary>A broken structure grows back at its site, on a downbeat: I0 structure; D0 start (it blocks its tiles from then), D1 end, a beat later.</summary>
        public static SimEvent Regrowth(double t, int structure, double start, double end)
        {
            var e = Of(SimEventKind.Regrowth, t);
            e.I0 = structure;
            e.D0 = start;
            e.D1 = end;
            return e;
        }

        /// <summary>
        /// The arena moves to the next zone, on a downbeat: I0 the new zone (schedule.json's zone
        /// index), I1 the previous, I2 the cause (<see cref="ZoneCause"/>); D0 the cause's song time
        /// (a fallback's: when it fell due), D1 when it was known.
        /// </summary>
        public static SimEvent ZoneChange(double t, int zone, int previous, ZoneCause cause, double causeT, double knownAt)
        {
            var e = Of(SimEventKind.ZoneChange, t);
            e.I0 = zone;
            e.I1 = previous;
            e.I2 = (int)cause;
            e.D0 = causeT;
            e.D1 = knownAt;
            return e;
        }

        /// <summary>
        /// An enemy spawns: I0 enemy, I1 role, I2 flags: 1 a recycled enemy re-entering (its old place
        /// is left at once), 2 a scheduled boss; D0/D1 position.
        /// </summary>
        public static SimEvent Spawn(double t, int enemy, int role, double x, double y, bool recycled = false, bool boss = false)
        {
            var e = Of(SimEventKind.Spawn, t);
            e.I0 = enemy;
            e.I1 = role;
            e.I2 = (recycled ? 1 : 0) | (boss ? 2 : 0);
            e.D0 = x;
            e.D1 = y;
            return e;
        }

        /// <summary>An enemy knocked back, at once and between beats: I0 enemy; D0/D1 from, D2/D3 to, D4 start, D5 end.</summary>
        public static SimEvent EnemyKnock(double t, int enemy, double fromX, double fromY, double toX, double toY, double start, double end)
        {
            var e = Of(SimEventKind.EnemyKnock, t);
            e.I0 = enemy;
            e.D0 = fromX;
            e.D1 = fromY;
            e.D2 = toX;
            e.D3 = toY;
            e.D4 = start;
            e.D5 = end;
            return e;
        }

        /// <summary>
        /// A heavy strike's cleave wave sets out from where the hero struck: I0 wave, I1 its bands;
        /// D0/D1 centre, D2 inner radius (the heavy strike's reach), D3 outer radius, D4 the first
        /// band's time, D5 the time between bands. Band k reaches <c>min(inner + (k + 1) * band, outer)</c>
        /// at <c>D4 + k * D5</c>, hitting each enemy it passes once.
        /// </summary>
        public static SimEvent Cleave(double t, int wave, int bands, double x, double y, double inner, double outer,
            double firstBand, double bandSeconds)
        {
            var e = Of(SimEventKind.Cleave, t);
            e.I0 = wave;
            e.I1 = bands;
            e.D0 = x;
            e.D1 = y;
            e.D2 = inner;
            e.D3 = outer;
            e.D4 = firstBand;
            e.D5 = bandSeconds;
            return e;
        }

        /// <summary>
        /// A bolt of the hero's lightning lands: I0 the weapon's content index, I1 <see cref="BoltKind"/>,
        /// I2 its place in its volley or chain (0 the first); D0/D1 where it comes from (a strike's own
        /// point, from the sky; a jump's hero or enemy before), D2/D3 where it lands, D4 the radius it hits.
        /// Its hits follow it at the same time.
        /// </summary>
        public static SimEvent Bolt(double t, int weapon, BoltKind kind, int index, double fromX, double fromY, double x, double y,
            double radius)
        {
            var e = Of(SimEventKind.Bolt, t);
            e.I0 = weapon;
            e.I1 = (int)kind;
            e.I2 = index;
            e.D0 = fromX;
            e.D1 = fromY;
            e.D2 = x;
            e.D3 = y;
            e.D4 = radius;
            return e;
        }

        /// <summary>A dying enemy drops an item: I0 item, I1 item kind (<see cref="ItemKind"/>); D0/D1 position, D2 value (a gem's experience).</summary>
        public static SimEvent Drop(double t, int item, ItemKind itemKind, double x, double y, double value)
        {
            var e = Of(SimEventKind.Drop, t);
            e.I0 = item;
            e.I1 = (int)itemKind;
            e.D0 = x;
            e.D1 = y;
            e.D2 = value;
            return e;
        }

        /// <summary>A dying boss drops its chest: a <see cref="SimEventKind.Drop"/> of <see cref="ItemKind.Chest"/>, D2 its tier (1-3), D3 the schedule minute whose treasure it holds.</summary>
        public static SimEvent Chest(double t, int item, double x, double y, int tier, int minute)
        {
            var e = Drop(t, item, ItemKind.Chest, x, y, tier);
            e.D3 = minute;
            return e;
        }

        /// <summary>The hero picks up an item: I0 item, I1 item kind; D0/D1 position, D2 value.</summary>
        public static SimEvent Pickup(double t, int item, int itemKind, double x, double y, double value)
        {
            var e = Of(SimEventKind.Pickup, t);
            e.I0 = item;
            e.I1 = itemKind;
            e.D0 = x;
            e.D1 = y;
            e.D2 = value;
            return e;
        }

        /// <summary>
        /// A level-up's offer opens, freezing the world: I0 the level it is for, I1 the offer, I2 its
        /// three cards packed 10 bits each (<see cref="LevelUpEvent.Card"/>); D0-D2 the song times the
        /// cards flip in.
        /// </summary>
        public static SimEvent LevelUp(double t, int level, int offer, int cards, double reveal0, double reveal1, double reveal2)
        {
            var e = Of(SimEventKind.LevelUp, t);
            e.I0 = level;
            e.I1 = offer;
            e.I2 = cards;
            e.D0 = reveal0;
            e.D1 = reveal1;
            e.D2 = reveal2;
            return e;
        }

        /// <summary>An offer's card is selected, or confirmed: I0 the offer, I1 the card (0 to 2), I2 1 when confirmed.</summary>
        public static SimEvent Card(double t, int offer, int card, bool confirmed)
        {
            var e = Of(SimEventKind.Card, t);
            e.I0 = offer;
            e.I1 = card;
            e.I2 = confirmed ? 1 : 0;
            return e;
        }

        /// <summary>A gem in reach takes off for the hero: I0 the gem (its drop's item); D0/D1 where it lay. Its pickup follows.</summary>
        public static SimEvent GemFlight(double t, int gem, double x, double y)
        {
            var e = Of(SimEventKind.GemFlight, t);
            e.I0 = gem;
            e.D0 = x;
            e.D1 = y;
            return e;
        }

        /// <summary>
        /// A chest's roulette (<see cref="Chests"/>): I0 the chest (its drop's item), I1 its phase
        /// (<see cref="ChestPhase"/>) | tier &lt;&lt; 4, I2 the phase's value: the schedule minute when it
        /// opens, the tick's index (0 to 7), the rewards' count when it lands, and a reward's card packed
        /// as a level-up's (<see cref="LevelUpEvent.Card"/>) | its place &lt;&lt; 10; D0/D1 the chest's position.
        /// </summary>
        public static SimEvent Chest(double t, int chest, ChestPhase phase, int tier, int value, double x, double y)
        {
            var e = Of(SimEventKind.Chest, t);
            e.I0 = chest;
            e.I1 = (int)phase | (tier << 4);
            e.I2 = value;
            e.D0 = x;
            e.D1 = y;
            return e;
        }

        /// <summary>The world freezes or resumes: I0 reason, I1 1 when it starts, 0 when it ends.</summary>
        public static SimEvent Freeze(double t, FreezeReason reason, bool starts)
        {
            var e = Of(SimEventKind.Freeze, t);
            e.I0 = (int)reason;
            e.I1 = starts ? 1 : 0;
            return e;
        }

        /// <summary>The run starts on this beat: the run clock counts from here.</summary>
        public static SimEvent RunStart(double t) => Of(SimEventKind.RunStart, t);

        /// <summary>The finale's blast on its downbeat: D0/D1 its centre (the hero), D2 its radius.</summary>
        public static SimEvent Finale(double t, double x, double y, double radius)
        {
            var e = Of(SimEventKind.Finale, t);
            e.D0 = x;
            e.D1 = y;
            e.D2 = radius;
            return e;
        }

        /// <summary>The run ends: I0 why (<see cref="PS.Game.Sim.RunEnd"/>).</summary>
        public static SimEvent RunEnd(double t, PS.Game.Sim.RunEnd why)
        {
            var e = Of(SimEventKind.RunEnd, t);
            e.I0 = (int)why;
            return e;
        }

        /// <summary>
        /// A music-driven event starts (unity-game.md, Music-Driven Events): I0 <see cref="SetPieceKind"/>, I1 the
        /// enemies it brought in; D0 its cause's song time (the loudness jump it waited for, or the beat it
        /// started on), D1 when the cause was known.
        /// </summary>
        public static SimEvent SetPiece(double t, SetPieceKind kind, int members, double causeT, double knownAt)
        {
            var e = Of(SimEventKind.SetPiece, t);
            e.I0 = (int)kind;
            e.I1 = members;
            e.D0 = causeT;
            e.D1 = knownAt;
            return e;
        }

        /// <summary>An enemy leaves the field alive, out of sight (a swarm or set piece done, the minotaur's retreat, room for one): I0 enemy, I1 role; D0/D1 position.</summary>
        public static SimEvent Leave(double t, int enemy, int role, double x, double y)
        {
            var e = Of(SimEventKind.Leave, t);
            e.I0 = enemy;
            e.I1 = role;
            e.D0 = x;
            e.D1 = y;
            return e;
        }

        /// <summary>
        /// A trail weapon's pulse burns one tile of its wake (Fire Trail): I0 the weapon's content index;
        /// D0/D1 the tile's centre, D2 the burn's radius there.
        /// </summary>
        public static SimEvent Burn(double t, int weapon, double x, double y, double radius)
        {
            var e = Of(SimEventKind.Burn, t);
            e.I0 = weapon;
            e.D0 = x;
            e.D1 = y;
            e.D2 = radius;
            return e;
        }

        /// <summary>A sound to schedule: I0 cue, I1 variant; D0 its song time, D1 pitch (semitones), D2 gain, D3/D4 position.</summary>
        public static SimEvent SoundCue(double t, int cue, int variant, double at, double pitch, double gain, double x, double y)
        {
            var e = Of(SimEventKind.SoundCue, t);
            e.I0 = cue;
            e.I1 = variant;
            e.D0 = at;
            e.D1 = pitch;
            e.D2 = gain;
            e.D3 = x;
            e.D4 = y;
            return e;
        }

        /// <summary>
        /// The hero moves (a stride, a bump or a stomp): I0 its stride step, I1 flags (1 a press's, 2 bump,
        /// 4 stomp), I2 facing; D0/D1 from, D2/D3 to (from again for a bump or stomp), D4 start, D5 end.
        /// </summary>
        public static SimEvent HeroStep(double t, int step, bool pressed, bool bump, bool stomp, Direction facing,
            double fromX, double fromY, double toX, double toY, double start, double end)
        {
            var e = Of(SimEventKind.HeroStep, t);
            e.I0 = step;
            e.I1 = (pressed ? 1 : 0) | (bump ? 2 : 0) | (stomp ? 4 : 0);
            e.I2 = (int)facing;
            e.D0 = fromX;
            e.D1 = fromY;
            e.D2 = toX;
            e.D3 = toY;
            e.D4 = start;
            e.D5 = end;
            return e;
        }

        /// <summary>
        /// The free hero's velocity changes (<see cref="FreeHero"/>): I2 facing; D0/D1 where it is at the start,
        /// D2/D3 its velocity (m/s) until the next glide, D4 the start.
        /// </summary>
        public static SimEvent HeroGlide(double t, double start, double fromX, double fromY, double vx, double vy, Direction facing)
        {
            var e = Of(SimEventKind.HeroGlide, t);
            e.I2 = (int)facing;
            e.D0 = fromX;
            e.D1 = fromY;
            e.D2 = vx;
            e.D3 = vy;
            e.D4 = start;
            return e;
        }

        /// <summary>
        /// The free hero dashes (<see cref="Dash"/>, `--dash`): I0 the dash, I1 1 on the beat (the long lunge) or 0 (the
        /// short hop), I2 its facing; D0 its start (the press in heard time, as the hero's glide from it), D1 its end,
        /// D2/D3 where the hero was at its start, D4/D5 its direction.
        /// </summary>
        public static SimEvent HeroDash(double t, int dash, bool onBeat, Direction facing, double start, double end,
            double fromX, double fromY, double dirX, double dirY)
        {
            var e = Of(SimEventKind.HeroDash, t);
            e.I0 = dash;
            e.I1 = onBeat ? 1 : 0;
            e.I2 = (int)facing;
            e.D0 = start;
            e.D1 = end;
            e.D2 = fromX;
            e.D3 = fromY;
            e.D4 = dirX;
            e.D5 = dirY;
            return e;
        }

        /// <summary>
        /// A finisher ring (<see cref="Finishers"/>): I0 the ring, I1 its <see cref="FinisherPhase"/>, I2 its downbeat's
        /// beat index; D0 the downbeat (as predicted then), D1 when the ring started closing, D2 its window either side
        /// of the downbeat; on a hit, D3 the slam's impact and D4 when the press was heard.
        /// </summary>
        public static SimEvent Finisher(double t, int ring, FinisherPhase phase, int beat, double downbeat, double start, double window,
            double impact = 0, double heard = 0)
        {
            var e = Of(SimEventKind.Finisher, t);
            e.I0 = ring;
            e.I1 = (int)phase;
            e.I2 = beat;
            e.D0 = downbeat;
            e.D1 = start;
            e.D2 = window;
            e.D3 = impact;
            e.D4 = heard;
            return e;
        }

        void Expect(SimEventKind kind)
        {
            if (Kind != kind) throw new System.InvalidOperationException($"a {Kind} event read as a {kind}");
        }

        public BeatEvent AsBeat { get { Expect(SimEventKind.Beat); return new BeatEvent(this); } }
        public StrideStepEvent AsStrideStep { get { Expect(SimEventKind.StrideStep); return new StrideStepEvent(this); } }
        public BeatStateEvent AsBeatState { get { Expect(SimEventKind.BeatState); return new BeatStateEvent(this); } }
        public AttackEvent AsAttack { get { Expect(SimEventKind.Attack); return new AttackEvent(this); } }
        public EnemyStepEvent AsEnemyStep { get { Expect(SimEventKind.EnemyStep); return new EnemyStepEvent(this); } }
        public HitEvent AsHit { get { Expect(SimEventKind.Hit); return new HitEvent(this); } }
        public DeathEvent AsDeath { get { Expect(SimEventKind.Death); return new DeathEvent(this); } }
        public StructureBreakEvent AsStructureBreak { get { Expect(SimEventKind.StructureBreak); return new StructureBreakEvent(this); } }
        public RegrowthEvent AsRegrowth { get { Expect(SimEventKind.Regrowth); return new RegrowthEvent(this); } }
        public SpawnEvent AsSpawn { get { Expect(SimEventKind.Spawn); return new SpawnEvent(this); } }
        public PickupEvent AsPickup { get { Expect(SimEventKind.Pickup); return new PickupEvent(this); } }
        public LevelUpEvent AsLevelUp { get { Expect(SimEventKind.LevelUp); return new LevelUpEvent(this); } }
        public ChestEvent AsChest { get { Expect(SimEventKind.Chest); return new ChestEvent(this); } }
        public FreezeEvent AsFreeze { get { Expect(SimEventKind.Freeze); return new FreezeEvent(this); } }
        public SoundCueEvent AsSoundCue { get { Expect(SimEventKind.SoundCue); return new SoundCueEvent(this); } }
        public HeroStepEvent AsHeroStep { get { Expect(SimEventKind.HeroStep); return new HeroStepEvent(this); } }
        public EnemyKnockEvent AsEnemyKnock { get { Expect(SimEventKind.EnemyKnock); return new EnemyKnockEvent(this); } }
        public CleaveEvent AsCleave { get { Expect(SimEventKind.Cleave); return new CleaveEvent(this); } }
        public DropEvent AsDrop { get { Expect(SimEventKind.Drop); return new DropEvent(this); } }
        public GlintEvent AsGlint { get { Expect(SimEventKind.Glint); return new GlintEvent(this); } }
        public ZoneChangeEvent AsZoneChange { get { Expect(SimEventKind.ZoneChange); return new ZoneChangeEvent(this); } }
        public FinaleEvent AsFinale { get { Expect(SimEventKind.Finale); return new FinaleEvent(this); } }
        public RunEndEvent AsRunEnd { get { Expect(SimEventKind.RunEnd); return new RunEndEvent(this); } }
        public GemFlightEvent AsGemFlight { get { Expect(SimEventKind.GemFlight); return new GemFlightEvent(this); } }
        public CardEvent AsCard { get { Expect(SimEventKind.Card); return new CardEvent(this); } }
        public SetPieceEvent AsSetPiece { get { Expect(SimEventKind.SetPiece); return new SetPieceEvent(this); } }
        public LeaveEvent AsLeave { get { Expect(SimEventKind.Leave); return new LeaveEvent(this); } }
        public BurnEvent AsBurn { get { Expect(SimEventKind.Burn); return new BurnEvent(this); } }
        public BoltEvent AsBolt { get { Expect(SimEventKind.Bolt); return new BoltEvent(this); } }
        public HeroGlideEvent AsHeroGlide { get { Expect(SimEventKind.HeroGlide); return new HeroGlideEvent(this); } }
        public FinisherEvent AsFinisher { get { Expect(SimEventKind.Finisher); return new FinisherEvent(this); } }
        public HeroDashEvent AsHeroDash { get { Expect(SimEventKind.HeroDash); return new HeroDashEvent(this); } }
    }
}
