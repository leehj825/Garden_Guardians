// =============================================================================
//  Garden Guardians — Android native entry bridge
// -----------------------------------------------------------------------------
//  raylib's Android platform layer (rcore_android.c) implements android_main()
//  from android_native_app_glue. When NativeActivity starts, the glue spawns a
//  thread that runs android_main(), which in turn calls a C function named
//  main() and expects the whole game (InitWindow ... CloseWindow) to run there.
//
//  Our game loop lives in C#, not C, so this file provides that main() and
//  forwards it to a managed callback. MainActivity registers the callback via
//  gg_set_main() *before* NativeActivity loads the library and starts the
//  thread, so it is always set by the time main() runs.
// =============================================================================

#include <stddef.h>
#include <android/log.h>

#define GG_EXPORT __attribute__((visibility("default")))

typedef void (*gg_main_fn)(void);

// Set once from the Android UI thread, read once from the glue thread. The
// write happens-before the glue's pthread_create, so no locking is needed.
static gg_main_fn g_managedMain = NULL;

GG_EXPORT void gg_set_main(gg_main_fn fn)
{
    g_managedMain = fn;
}

// Called by raylib's android_main() on the native-app-glue thread.
int main(int argc, char *argv[])
{
    (void)argc;
    (void)argv;

    if (g_managedMain == NULL)
    {
        __android_log_print(ANDROID_LOG_ERROR, "GardenGuardians",
                            "main(): no managed entry point registered (gg_set_main was not called)");
        return 1;
    }

    g_managedMain();
    return 0;
}
