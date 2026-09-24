#!/usr/bin/env bash
# =============================================================================
#  Builds libraylib.so for Android with the NDK.
# -----------------------------------------------------------------------------
#  Raylib-cs ships native raylib for desktop only, so we compile raylib from
#  source for each Android ABI we ship. Each resulting libraylib.so contains:
#    * raylib itself (PLATFORM_ANDROID, OpenGL ES 2.0)
#    * android_native_app_glue  (provides ANativeActivity_onCreate)
#    * native/gg_android_main.c (provides main() -> managed C# game loop)
#
#  Output: Platforms/Android/libs/<abi>/libraylib.so, which
#  GardenGuardians.csproj packs into the APK.
#
#  Usage:  Platforms/Android/build-raylib.sh
#  Env:    ANDROID_NDK_ROOT (or ANDROID_NDK_HOME / ANDROID_NDK_LATEST_HOME /
#          ANDROID_HOME/ndk/<ver>) must point at an installed NDK.
#          RAYLIB_VERSION   raylib git tag (default 6.0, matches Raylib-cs 8.x).
#          ANDROID_ABIS     space-separated ABIs (default "arm64-v8a x86_64").
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RAYLIB_VERSION="${RAYLIB_VERSION:-6.0}"
ANDROID_ABIS="${ANDROID_ABIS:-arm64-v8a x86_64}"
API_LEVEL=24   # Must match SupportedOSPlatformVersion in GardenGuardians.csproj.

BUILD_DIR="$SCRIPT_DIR/.build"
SRC_DIR="$BUILD_DIR/raylib-$RAYLIB_VERSION"
OUT_DIR="$SCRIPT_DIR/libs"

# --- Locate the NDK ----------------------------------------------------------
find_ndk() {
    for candidate in "${ANDROID_NDK_ROOT:-}" "${ANDROID_NDK_HOME:-}" "${ANDROID_NDK_LATEST_HOME:-}"; do
        if [[ -n "$candidate" && -d "$candidate/toolchains/llvm" ]]; then
            echo "$candidate"; return
        fi
    done
    local sdk="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
    if [[ -n "$sdk" && -d "$sdk/ndk" ]]; then
        local latest
        latest="$(ls -1 "$sdk/ndk" | sort -V | tail -n 1)"
        if [[ -n "$latest" ]]; then echo "$sdk/ndk/$latest"; return; fi
    fi
    echo "error: Android NDK not found. Set ANDROID_NDK_ROOT." >&2
    exit 1
}

NDK="$(find_ndk)"
case "$(uname -s)" in
    Linux)  HOST_TAG=linux-x86_64 ;;
    Darwin) HOST_TAG=darwin-x86_64 ;;
    *)      echo "error: unsupported host $(uname -s)" >&2; exit 1 ;;
esac
TOOLCHAIN="$NDK/toolchains/llvm/prebuilt/$HOST_TAG"
GLUE_DIR="$NDK/sources/android/native_app_glue"
echo "Using NDK: $NDK"

# --- Fetch raylib source (cached between runs) -------------------------------
if [[ ! -f "$SRC_DIR/src/raylib.h" ]]; then
    rm -rf "$SRC_DIR"
    mkdir -p "$BUILD_DIR"
    git clone --quiet --depth 1 --branch "$RAYLIB_VERSION" \
        https://github.com/raysan5/raylib.git "$SRC_DIR"
fi

RAYLIB_SOURCES=(rcore.c rshapes.c rtextures.c rtext.c rmodels.c raudio.c)

# --- Build one ABI -----------------------------------------------------------
build_abi() {
    local abi="$1" triple arch_flags=()
    case "$abi" in
        arm64-v8a) triple=aarch64-linux-android; arch_flags=(-mfix-cortex-a53-835769) ;;
        x86_64)    triple=x86_64-linux-android ;;
        armeabi-v7a) triple=armv7a-linux-androideabi; arch_flags=(-march=armv7-a -mfloat-abi=softfp -mfpu=vfpv3-d16) ;;
        x86)       triple=i686-linux-android ;;
        *) echo "error: unknown ABI $abi" >&2; exit 1 ;;
    esac

    local cc="$TOOLCHAIN/bin/${triple}${API_LEVEL}-clang"
    local obj_dir="$BUILD_DIR/obj/$abi"
    mkdir -p "$obj_dir" "$OUT_DIR/$abi"

    # Same defines/flags raylib's own Makefile uses for PLATFORM_ANDROID.
    local cflags=(
        -std=gnu99 -O2 -fPIC -ffunction-sections -funwind-tables -fstack-protector-strong
        -Wall -Wno-missing-braces -Werror=pointer-arith -fno-strict-aliasing
        -D_GNU_SOURCE -DPLATFORM_ANDROID -DGRAPHICS_API_OPENGL_ES2
        -I"$SRC_DIR/src" -I"$GLUE_DIR"
        "${arch_flags[@]}"
    )

    echo "Building raylib $RAYLIB_VERSION for $abi..."
    local objs=()
    for src in "${RAYLIB_SOURCES[@]}"; do
        "$cc" "${cflags[@]}" -c "$SRC_DIR/src/$src" -o "$obj_dir/${src%.c}.o"
        objs+=("$obj_dir/${src%.c}.o")
    done
    "$cc" "${cflags[@]}" -c "$GLUE_DIR/android_native_app_glue.c" -o "$obj_dir/android_native_app_glue.o"
    "$cc" "${cflags[@]}" -c "$SCRIPT_DIR/native/gg_android_main.c" -o "$obj_dir/gg_android_main.o"
    objs+=("$obj_dir/android_native_app_glue.o" "$obj_dir/gg_android_main.o")

    # Link flags:
    #   -u ANativeActivity_onCreate  keep the glue entry point NativeActivity calls
    #   --wrap=fopen                 raylib redirects fopen to APK assets (rcore_android.c)
    #   --no-undefined               fail here, not on device, if a symbol is missing
    #   max-page-size=16384          16 KB page alignment required by Android 15+ devices
    "$cc" -shared -o "$OUT_DIR/$abi/libraylib.so" "${objs[@]}" \
        -Wl,-soname,libraylib.so \
        -u ANativeActivity_onCreate \
        -Wl,--wrap=fopen \
        -Wl,--no-undefined \
        -Wl,--build-id -Wl,-z,noexecstack -Wl,-z,relro -Wl,-z,now \
        -Wl,-z,max-page-size=16384 \
        -llog -landroid -lEGL -lGLESv2 -lOpenSLES -ldl -lm

    echo "  -> $OUT_DIR/$abi/libraylib.so"
}

for abi in $ANDROID_ABIS; do
    build_abi "$abi"
done
