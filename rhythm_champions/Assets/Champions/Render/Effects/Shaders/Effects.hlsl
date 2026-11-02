// The hero's weapon effects' records (Effects.cs; design.md, The Look > Weapon effects), shared by Slash.shader, the
// sparks' compute pass (Sparks.compute) and their draw (Spark.shader). Every record is a moment's whole life: the
// shaders draw it at the frame's song time (_EffectNow), so a record is written once, when its cue fires.
#ifndef CHAMPIONS_EFFECTS_INCLUDED
#define CHAMPIONS_EFFECTS_INCLUDED

// How a Shape record draws (EffectShape.Mode).
#define SHAPE_ARC 0     // a Hovl slash mesh as an arc round its centre, swept by its head
#define SHAPE_RIBBON 1  // a flat strip from its centre to its end (a dash's smear, a trail), swept along it
#define SHAPE_SOLID 2   // a Hovl mesh as a solid glowing shape (a spectral sword), lit at its rim
#define SHAPE_UPRIGHT 3 // a ribbon standing up from its centre: a figure's glow

// Flags (EffectShape.Flags).
#define SHAPE_MIRRORED 1 // the sweep runs the other way (the other hand's swing)

// Matches Champions.Render.EffectShape (96 bytes).
struct Shape
{
    float3 position; float born;    // its centre at its foot (world), or a ribbon's start; the song time it starts
    float2 facing; float radius; float arc; // its middle's way on the ground (unit); outer radius or length (m); sweep (rad) or half width (m)
    float life; float headIn; float width; float brightness; // s; the share of its life the head sweeps; the band's share lit at the head; HDR gain
    float tilt; float height; float mode; float flags;  // rad about its facing (a solid's pitch); m above the ground; SHAPE_*; SHAPE_* flags
    float3 travel; float grow;      // m it moves over its travel (a wave's roll, a sword's fall); its scale at birth, easing to 1 over it
    float streak; float fade; float travelIn; float taper; // the noise streaks' depth (0-1); the share of its life it fades over at the end; the share its travel takes; 1 Hovl's crescent taper, 0 an even band
};

// Matches Champions.Render.SparkBurst (80 bytes): one burst the compute pass expands into its sparks.
struct Burst
{
    float3 origin; float born;
    float3 direction; float spread;  // unit; the cone's half angle (rad)
    float count; float first; float seed; float brightness; // sparks; their first slot in the ring
    float2 speed; float2 life;       // m/s and s, min and max
    float size; float drag; float gravity; float up;  // m; 1/s; m/s²; the share of each spark's speed thrown up
};

// Matches the compute pass's output (48 bytes): one spark, its motion in closed form from its birth.
struct Spark
{
    float3 origin; float born;
    float3 velocity; float life;
    float size; float drag; float gravity; float brightness;
};

float _EffectNow; // the frame's song time

// Where a spark is `age` seconds after its birth: drag slows it exponentially, gravity pulls it down, the floor
// stops it (it slides).
float3 SparkAt(Spark s, float age)
{
    float k = max(s.drag, 1e-3);
    float e = (1 - exp(-k * age)) / k;
    float g = s.gravity / k;
    float3 p = s.origin + s.velocity * e;
    p.y += -g * age + g * e;
    p.y = max(p.y, 0.02);
    return p;
}

#endif
