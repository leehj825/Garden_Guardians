#!/usr/bin/env bash
# =============================================================================
#  Android emulator smoke test for the Phase 1 prototype.
# -----------------------------------------------------------------------------
#  Installs the APK on a running emulator/device, launches it, and checks
#  that the whole native chain works end to end:
#    NativeActivity -> libraylib.so -> main() bridge -> C# Game.Run -> GLES
#  Then taps "Equip Pebble" and the ground, and confirms a pebble was drawn.
#
#  Usage:  smoke-test.sh <path-to-apk> [output-dir]
#  Writes screenshots and the full logcat to the output dir.
# =============================================================================
set -euo pipefail

APK="$1"
OUT="${2:-smoke-test-output}"
PKG=com.gardenguardians.game
mkdir -p "$OUT"

fail() { echo "SMOKE TEST FAILED: $*"; dump_logs; exit 1; }

dump_logs() {
    adb logcat -d > "$OUT/logcat.txt" || true
    echo "----- relevant logcat -----"
    grep -E "raylib|GardenGuardians|monodroid|DOTNET|AndroidRuntime|FATAL|Fatal signal|DEBUG  :|linker" \
        "$OUT/logcat.txt" | grep -v "raylib: GL:" | tail -n 150 || true
    echo "---------------------------"
}

logcat_has() { adb logcat -d | grep -q -- "$1"; }

echo "Native libraries in APK:"
unzip -l "$APK" | grep -E "lib/.*\.so$" || true

echo "Installing $APK"
adb install -r "$APK"
adb logcat -c

echo "Launching $PKG"
adb shell am start -W -n "$PKG/.MainActivity"

# --- 1. Wait for raylib to create the GL window ------------------------------
for _ in $(seq 1 90); do
    logcat_has "DISPLAY: Device initialized successfully" && break
    logcat_has "FATAL EXCEPTION\|Fatal signal\|Unhandled exception in game loop" && fail "app crashed during startup"
    sleep 1
done
logcat_has "DISPLAY: Device initialized successfully" || fail "raylib never initialized the display"
logcat_has "Native game thread started" || fail "managed GameMain was never called"
sleep 3  # Let a few frames render.

adb logcat -d -s raylib > "$OUT/raylib.txt"
read_pair() { grep -m1 "$1" "$OUT/raylib.txt" | sed -E 's/.*: *([0-9]+)[ ,x]+([0-9]+).*/\1 \2/'; }
read -r DISP_W DISP_H <<< "$(read_pair 'Display size')"
read -r SCR_W SCR_H   <<< "$(read_pair 'Screen size')"
read -r OFF_X OFF_Y   <<< "$(read_pair 'Viewport offsets')"
echo "Display ${DISP_W}x${DISP_H}, virtual screen ${SCR_W}x${SCR_H}, offsets ${OFF_X},${OFF_Y}"

# Converts game (virtual 1280x720) coordinates to device pixels. This is the
# inverse of the touch normalization in raylib's rcore_android.c.
tap() {
    local vx="$1" vy="$2" px py
    px=$(python3 -c "print(int(($vx + $OFF_X/2) * $DISP_W / ($SCR_W + $OFF_X)))")
    py=$(python3 -c "print(int(($vy + $OFF_Y/2) * $DISP_H / ($SCR_H + $OFF_Y)))")
    echo "tap virtual ($vx,$vy) -> device ($px,$py)"
    adb shell input tap "$px" "$py"
}

adb exec-out screencap -p > "$OUT/01-launched.png"

# --- 2. Cast the Pebble-Drop miracle ------------------------------------------
tap 110 45      # "Equip Pebble" button (Rectangle 20,20,180,50).
sleep 1
adb exec-out screencap -p > "$OUT/02-equipped.png"
tap 640 360     # Screen centre = world origin, on the terrain.
sleep 3         # A 10 m fall takes ~1.4 s.
adb exec-out screencap -p > "$OUT/03-pebble-dropped.png"

adb shell pidof "$PKG" > /dev/null || fail "app is no longer running"
logcat_has "FATAL EXCEPTION\|Fatal signal\|Unhandled exception in game loop" && fail "app crashed"

# --- 3. Check the screenshots -------------------------------------------------
python3 - "$OUT" <<'PY' || fail "screenshot checks"
import sys
from PIL import Image

out = sys.argv[1]

def stats(name):
    img = Image.open(f"{out}/{name}").convert("RGB")
    raw = img.tobytes()
    px = list(zip(raw[0::3], raw[1::3], raw[2::3]))
    n = len(px)
    # Terrain colour is (86,150,60); sky is (135,190,235); pebble is (130,130,130).
    green = sum(1 for r, g, b in px if g > 110 and g > r + 30 and g > b + 40) / n
    gold = sum(1 for r, g, b in px if r > 200 and 160 < g < 220 and b < 110)
    gray = sum(1 for r, g, b in px if 100 < r < 160 and abs(r - g) < 8 and abs(g - b) < 8)
    return green, gold, gray

g1, gold1, gray1 = stats("01-launched.png")
g2, gold2, gray2 = stats("02-equipped.png")
g3, gold3, gray3 = stats("03-pebble-dropped.png")
print(f"launched:  green={g1:.1%} gold_px={gold1} gray_px={gray1}")
print(f"equipped:  green={g2:.1%} gold_px={gold2} gray_px={gray2}")
print(f"dropped:   green={g3:.1%} gold_px={gold3} gray_px={gray3}")

errors = []
if g1 < 0.10:
    errors.append("terrain not visible after launch (renderer not drawing?)")
if gold2 <= gold1 + 500:
    errors.append("'Equip Pebble' button did not turn gold after tapping it")
if gray3 <= gray2 + 300:
    errors.append("no pebble appeared after tapping the ground")
if errors:
    print("SCREENSHOT CHECK FAILED: " + "; ".join(errors))
    sys.exit(1)
print("Screenshot checks passed.")
PY

dump_logs
echo "SMOKE TEST PASSED"
