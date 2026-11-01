// The managed bytes each thread allocates, for the frame profile (FrameProfile.cs, `--profile-out`).
// IL2CPP doesn't implement GC.GetAllocatedBytesForCurrentThread, so a player counts them from IL2CPP's
// allocation profiler hook. The hook is native: IL2CPP compiles this file into GameAssembly. A managed
// hook re-enters itself on any thread that attaches while it is on, since attaching allocates the
// thread's object and the hook's reverse P/Invoke attaches first.
#include <stdint.h>

#if defined(_MSC_VER)
#define GAME_THREAD_LOCAL __declspec(thread)
#else
#define GAME_THREAD_LOCAL _Thread_local
#endif

#define GAME_PROFILE_ALLOCATIONS (1 << 7) // IL2CPP_PROFILE_ALLOCATIONS

void il2cpp_profiler_install(void* profiler, void (*shutdown)(void*));
void il2cpp_profiler_install_allocation(void (*callback)(void*, void*, void*));
void il2cpp_profiler_set_events(int events);
uint32_t il2cpp_object_get_size(void* obj);

static GAME_THREAD_LOCAL int64_t game_allocated;
static int game_installed;

static void game_on_allocation(void* profiler, void* obj, void* klass)
{
    (void)profiler;
    (void)klass;
    game_allocated += il2cpp_object_get_size(obj);
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
    if (game_installed) il2cpp_profiler_set_events(0);
}

// The bytes the calling thread allocated while counting.
int64_t game_allocated_bytes(void)
{
    return game_allocated;
}
