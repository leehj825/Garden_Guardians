
// =============================================================================
//  Garden Guardians patch: follow the window when the phone is turned
// -----------------------------------------------------------------------------
//  Appended to raylib's src/platforms/rcore_android.c by build-raylib.sh (so it
//  sees the platform and CORE data, which are not exported). raylib 6.0 fixes
//  its screen size, viewport and touch mapping when the window is created, and
//  pins the window's buffers to that size, so a phone turned from landscape to
//  portrait would show the old picture stretched over the new shape.
//
//  The managed game (Game.SyncWindowSize) learns the new size from Android and
//  calls this from the game thread, between frames.
// =============================================================================
__attribute__((visibility("default"))) void gg_resize_window(int width, int height)
{
    if (platform.app == NULL || platform.app->window == NULL || platform.device == EGL_NO_DISPLAY) return;
    if (width <= 0 || height <= 0) return;
    if (width == CORE.Window.screen.width && height == CORE.Window.screen.height) return;

    // Let the window's buffers take the new size (they were pinned to the old one).
    EGLint displayFormat = 0;
    eglGetConfigAttrib(platform.device, platform.config, EGL_NATIVE_VISUAL_ID, &displayFormat);
    ANativeWindow_setBuffersGeometry(platform.app->window, width, height, displayFormat);

    CORE.Window.display.width = width;
    CORE.Window.display.height = height;
    CORE.Window.screen.width = width;
    CORE.Window.screen.height = height;
    CORE.Window.render.width = width;
    CORE.Window.render.height = height;
    CORE.Window.currentFbo.width = width;
    CORE.Window.currentFbo.height = height;
    CORE.Window.renderOffset.x = 0;
    CORE.Window.renderOffset.y = 0;
    CORE.Window.screenScale = MatrixIdentity();

    rlSetFramebufferWidth(width);
    rlSetFramebufferHeight(height);
    SetupViewport(width, height);
    TRACELOG(LOG_INFO, "ANDROID: Window resized to %i x %i", width, height);
}
