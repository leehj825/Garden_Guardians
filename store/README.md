# Google Play store assets

| File | Where it goes | Spec |
| --- | --- | --- |
| `icon-512.png` | App icon | 512 x 512 PNG, under 1 MB (the game's own icon, `Assets/icon.png`) |
| `feature-graphic-1024x500.png` | Feature graphic | 1024 x 500 PNG, under 15 MB |
| `screenshot-portrait-1080x1920.png` | Phone screenshots | 9:16 |
| `screenshot-landscape-1920x1080.png` | Phone screenshots | 16:9 |
| `screenshot-oak-1920x1080.png` | Phone screenshots | 16:9 |
| `screenshot-town-landscape-1920x1080.png` | Phone screenshots | 16:9, a village up close |
| `screenshot-town2-landscape-1920x1080.png` | Phone screenshots | 16:9, another village |
| `screenshot-town-portrait-1080x1920.png` | Phone screenshots | 9:16, Bramblekin at work in a village |

Google Play takes 2 to 8 phone screenshots (PNG or JPEG, up to 8 MB each, exactly 16:9 or 9:16, each side 320 to 3840 px). All of these meet that. The screenshots were rendered by the Release build on a virtual display (`GARDEN_SIZE`, `GARDEN_SCREENSHOT`, see the README); replace them with shots from a phone if you like. The town shots come from a garden grown by the headless simulation (`GARDEN_START_ERA=2 dotnet run ... -- --headless 4000 --seed 5 --save garden.json`, copied over the saved garden) with `GARDEN_CAMERA=village:1`; alerts were switched off for them (`alerts=Off` in settings.txt). The feature graphic is a first draft: swap in better art when there is some.
