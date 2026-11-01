// The managed bytes each thread allocates, for the frame profile (FrameProfile.cs, `--profile-out`).
// IL2CPP doesn't implement GC.GetAllocatedBytesForCurrentThread, so a player counts them from IL2CPP's
// allocation profiler hook. The hook is native: IL2CPP compiles this file into GameAssembly. A managed
// hook re-enters itself on any thread that attaches while it is on, since attaching allocates the
// thread's object and the hook's reverse P/Invoke attaches first.
//
// Beside them, the calling thread's CPU time (game_thread_cpu_ns), so a profile tells a frame's own work from
// the time the OS gave the thread to others.
//
// The census: the main thread's allocations by type and by the part of the frame that made them (a tag
// the profile sets as the frame goes), from game_census_start to game_census_write, so a profile names
// what makes its garbage.
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#if !defined(_WIN32)
#include <time.h>
#endif

#if defined(_MSC_VER)
#define GAME_THREAD_LOCAL __declspec(thread)
#else
#define GAME_THREAD_LOCAL _Thread_local
#endif

#define GAME_PROFILE_ALLOCATIONS (1 << 7) // IL2CPP_PROFILE_ALLOCATIONS
#define GAME_CENSUS_SLOTS 8192 // a power of two, well over the types and tags a run allocates

void il2cpp_profiler_install(void* profiler, void (*shutdown)(void*));
void il2cpp_profiler_install_allocation(void (*callback)(void*, void*, void*));
void il2cpp_profiler_set_events(int events);
uint32_t il2cpp_object_get_size(void* obj);
const void* il2cpp_class_get_type(void* klass);
char* il2cpp_type_get_name(const void* type);
void il2cpp_free(void* ptr);

typedef struct
{
    void* klass;
    int32_t tag;
    int64_t count, bytes, largest;
} game_census_entry;

static GAME_THREAD_LOCAL int64_t game_allocated;
static GAME_THREAD_LOCAL int game_census_here;
static int game_installed;
static int32_t game_tag;
static int game_census_full;
static game_census_entry* game_census;

static void game_count(void* klass, uint32_t size)
{
    uintptr_t h = ((uintptr_t)klass >> 3) * 31 + (uintptr_t)game_tag;
    for (int probe = 0; probe < GAME_CENSUS_SLOTS; probe++)
    {
        game_census_entry* e = &game_census[(h + probe) & (GAME_CENSUS_SLOTS - 1)];
        if (e->klass == 0)
        {
            e->klass = klass;
            e->tag = game_tag;
        }
        else if (e->klass != klass || e->tag != game_tag) continue;
        e->count++;
        e->bytes += size;
        if (size > e->largest) e->largest = size;
        return;
    }
    game_census_full = 1;
}

static void game_on_allocation(void* profiler, void* obj, void* klass)
{
    (void)profiler;
    uint32_t size = il2cpp_object_get_size(obj);
    game_allocated += size;
    if (game_census_here) game_count(klass, size);
}

// Starts counting (the hook is installed once; IL2CPP has no way to remove it, only to turn it off).
void game_allocations_start(void)
{
    if (!game_installed)
    {
        il2cpp_profiler_install(0, 0);
        il2cpp_profiler_install_allocation(game_on_allocation);
        game_installed = 1;
    }
    il2cpp_profiler_set_events(GAME_PROFILE_ALLOCATIONS);
}

void game_allocations_stop(void)
{
    game_census_here = 0;
    if (game_installed) il2cpp_profiler_set_events(0);
}

// The bytes the calling thread allocated while counting.
int64_t game_allocated_bytes(void)
{
    return game_allocated;
}

// The calling thread's CPU time in ns, or -1 where it can't be read finely (Windows' thread times move in
// scheduler ticks, about 16 ms).
int64_t game_thread_cpu_ns(void)
{
#if defined(_WIN32)
    return -1;
#else
    struct timespec t;
    if (clock_gettime(CLOCK_THREAD_CPUTIME_ID, &t) != 0) return -1;
    return (int64_t)t.tv_sec * 1000000000 + t.tv_nsec;
#endif
}

// Starts the calling thread's census afresh (counting must be started).
void game_census_start(void)
{
    if (game_census == 0) game_census = (game_census_entry*)calloc(GAME_CENSUS_SLOTS, sizeof(game_census_entry));
    else
        for (int i = 0; i < GAME_CENSUS_SLOTS; i++) game_census[i] = (game_census_entry){0};
    game_census_full = 0;
    game_census_here = game_census != 0;
}

// The part of the frame allocating from now on; returns the one before, to restore.
int32_t game_census_tag(int32_t tag)
{
    int32_t before = game_tag;
    game_tag = tag;
    return before;
}

static int game_by_bytes(const void* a, const void* b)
{
    int64_t x = ((const game_census_entry*)a)->bytes, y = ((const game_census_entry*)b)->bytes;
    return x < y ? 1 : x > y ? -1 : 0;
}

// Writes the census as CSV, most bytes first: the part (tags[tag], or the tag's number past the
// names), the type, its allocations, bytes and largest one. Returns 0, or -1 if the file wasn't written.
int32_t game_census_write(const char* path, const char** tags, int32_t tagCount)
{
    if (game_census == 0) return -1;
    FILE* f = fopen(path, "w");
    if (f == 0) return -1;
    int was = game_census_here;
    game_census_here = 0;
    qsort(game_census, GAME_CENSUS_SLOTS, sizeof(game_census_entry), game_by_bytes);
    fprintf(f, "part,type,count,bytes,largest\n");
    for (int i = 0; i < GAME_CENSUS_SLOTS && game_census[i].klass != 0; i++)
    {
        game_census_entry* e = &game_census[i];
        char* name = il2cpp_type_get_name(il2cpp_class_get_type(e->klass));
        if (e->tag >= 0 && e->tag < tagCount) fprintf(f, "%s,", tags[e->tag]);
        else fprintf(f, "%d,", e->tag);
        // Generic names have commas: quoted.
        fprintf(f, "\"%s\",%lld,%lld,%lld\n", name ? name : "?", (long long)e->count, (long long)e->bytes, (long long)e->largest);
        if (name) il2cpp_free(name);
    }
    if (game_census_full) fprintf(f, "full,\"(the census ran out of slots: some allocations aren't listed)\",0,0,0\n");
    fclose(f);
    // Sorted, the table no longer hashes: it starts afresh if counting goes on.
    if (was) game_census_start();
    return 0;
}
