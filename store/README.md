# Google Play store assets

| File | Where it goes | Spec |
| --- | --- | --- |
| `icon-512.png` | App icon | 512 x 512 PNG, under 1 MB (the game's own icon, `Assets/icon.png`) |
| `feature-graphic-1024x500.png` | Feature graphic | 1024 x 500 PNG, under 15 MB |
| `screenshot-portrait-1080x1920.png` | Phone screenshots | 9:16 |
| `screenshot-landscape-1920x1080.png` | Phone screenshots | 16:9 |
| `screenshot-oak-1920x1080.png` | Phone screenshots | 16:9 |

Google Play wants at least 2 phone screenshots (320 to 3840 px, long side no more than twice the short side). The screenshots were rendered by the Release build on a virtual display (`GARDEN_SIZE`, `GARDEN_SCREENSHOT`, see the README); replace them with shots from a phone once a garden has grown villages. The feature graphic is a first draft: swap in better art when there is some.
