// =============================================================================
//  Garden Guardians — Emergent Survival Simulation
// -----------------------------------------------------------------------------
//  Design (see Garden_Guardians_Roadmap.md/Garden_Guardians_Design.md):
//    * A fixed isometric camera looking down at a 100 m x 100 m patch of
//      procedurally-hilly terrain, with a mobile-friendly one-finger-pan/
//      two-finger-pinch camera controller layered on top.
//    * No factions, no top-down economy. The map is the terrain and whatever
//      loose things live on it: wild Berries (Food), fallen Twigs, Hornet
//      swarms, a Wolf Spider, Grubs, Stag Beetles, and the Bramblekin.
//    * Every Bramblekin is an individual agent with its own randomly rolled
//      Personality (Aggression, Sociability, Intelligence, Rebelliousness,
//      Persuasiveness, Courage, Diligence) and a strict
//      hierarchy of needs: Hunger, then Safety, then its group Duty, then
//      Settling (its home), then Social.
//    * Groups are emergent, not assigned: two Bramblekin that cross paths
//      resolve the encounter from their situation and traits — a starving,
//      aggressive one may rob the other; two sociable ones (or two that are
//      both being hunted) may band together under a shared GroupId, led by
//      whichever member has the best claim to lead (Intelligence plus
//      earned Reputation). The Leader picks the group's goal, hands out
//      jobs and decides who eats first; disloyal followers walk out,
//      split off, or challenge it.
//    * Loners build tents from fallen twigs and stock them with food;
//      groups share a home they upgrade into a house.
//    * The player has no lever on the world; the only tap left is inspecting
//      a single Bramblekin (WorldTapInput).
//
//  Safety: entities are created and destroyed constantly (arrivals, predator
//  kills, deaths in combat or to starvation), so every list that can change
//  size mid-frame is either walked with a reverse for-loop or mutated through
//  a deferred pending-add/pending-remove queue processed once at the end of
//  the frame, never directly inside another entity's Update().
//
//  Scale convention: 1 world unit = 1 meter. The terrain is a 100 m x 100 m plane
//  centred on the origin, and "up" is +Y.
//
//  Layout: this file is only the entry point. Everything else lives under
//  Source/, one type per file:
//    Source/Engine    camera, terrain, tap input, spatial grid, movement
//    Source/World     the World (a partial class split by concern), Food,
//                     Garden Props
//    Source/Kin       the Bramblekin (a partial class, one file per need)
//                     and its Personality, relationships and groups
//    Source/Wildlife  the Wolf Spider, Hornets and Grubs
//    Source/Game      the main loop, the UI and headless mode
// =============================================================================

using System.Globalization;

namespace GardenGuardians;

/// <summary>
/// Desktop entry point. Android starts the game from MainActivity instead
/// (see Platforms/Android/MainActivity.cs); both end up in <see cref="Game.Run"/>.
///
/// <c>--headless [seconds] [--seed N] [--load FILE] [--save FILE]</c> skips
/// the window entirely and steps the simulation on its own, printing
/// periodic population reports — a quick way to check the survival loop end
/// to end without a GPU. <c>--load</c> carries on a saved garden (for
/// <c>seconds</c> more) and <c>--save</c> writes it out at the end.
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        int headlessIndex = Array.IndexOf(args, "--headless");
        if (headlessIndex < 0)
        {
            Game.Run(GamePlatform.Desktop);
            return;
        }

        float seconds = 600f;
        if (headlessIndex + 1 < args.Length &&
            float.TryParse(args[headlessIndex + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedSeconds))
            seconds = parsedSeconds;

        int? seed = null;
        int seedIndex = Array.IndexOf(args, "--seed");
        if (seedIndex >= 0 && seedIndex + 1 < args.Length && int.TryParse(args[seedIndex + 1], out int parsedSeed))
            seed = parsedSeed;

        // --load <file> carries on a saved garden; --save <file> saves it at the end.
        string? load = OptionValue(args, "--load");
        string? save = OptionValue(args, "--save");

        Game.RunHeadless(seconds, seed, load, save);
    }

    private static string? OptionValue(string[] args, string option)
    {
        int index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

/// <summary>Which host is running the game; controls a few window settings.</summary>
public enum GamePlatform
{
    Desktop,
    Android,
}
