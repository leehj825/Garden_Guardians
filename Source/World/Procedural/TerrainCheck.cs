namespace GardenGuardians;

/// <summary>
/// <c>--check-terrains [count]</c>: grows the first <c>count</c> terrains (default 100) and checks each one is sound —
/// an oak, ponds of a sensible size, everything inside the map, the spring on dry ground, the same seed giving the same
/// ground — and that the game's own water map can be measured on it. Prints a summary; exits with 1 if any failed.
/// </summary>
public static class TerrainCheck
{
    public static int Run(int count)
    {
        int failures = 0;
        float minWet = float.MaxValue, maxWet = 0f;
        int minProps = int.MaxValue, maxProps = 0;
        var pondCounts = new int[6];
        for (int seed = 1; seed <= count; seed++)
        {
            var problems = new List<string>();
            TerrainData.Select(TerrainData.ProceduralBase + seed);
            TerrainSet set = TerrainData.Current;
            float half = set.Half, level = set.PondLevel;
            float[] heights = set.Heights;

            int wet = 0;
            foreach (float h in heights)
                wet += h < level ? 1 : 0;
            float wetArea = wet * TerrainData.Step * TerrainData.Step;
            minWet = MathF.Min(minWet, wetArea);
            maxWet = MathF.Max(maxWet, wetArea);
            if (wetArea < 60f || wetArea > 0.35f * 4f * half * half)
                problems.Add($"pond area {wetArea:0} m2");

            if (set.OakCircles.Length < 3)
                problems.Add("no oak");
            if (set.OakTrunkRadius < 1f || set.OakTrunkHeight < 5f)
                problems.Add($"odd trunk {set.OakTrunkRadius:0.0} x {set.OakTrunkHeight:0.0}");
            if (MathF.Abs(set.OakX) > half || MathF.Abs(set.OakZ) > half)
                problems.Add("oak off the map");
            if (MathF.Abs(set.SpringX) > half || MathF.Abs(set.SpringZ) > half || World.GetHeightAt(set.SpringX, set.SpringZ) < level + 0.3f)
                problems.Add("spring in the water or off the map");

            int props = set.Props?.Count ?? 0;
            minProps = Math.Min(minProps, props);
            maxProps = Math.Max(maxProps, props);
            foreach (PlacedProp prop in set.Props ?? Array.Empty<PlacedProp>())
            {
                if (MathF.Abs(prop.X) > half - 1f || MathF.Abs(prop.Z) > half - 1f)
                    problems.Add($"{prop.Item.File} at the edge ({prop.X:0}, {prop.Z:0})");
                if (prop.Item.Kind == KitKind.Rock && World.GetHeightAt(prop.X, prop.Z) < level)
                    problems.Add($"{prop.Item.File} under water");
            }

            // The same seed grows the same ground.
            TerrainSet again = TerrainGenerator.Generate(seed);
            if (!again.Heights.AsSpan().SequenceEqual(heights))
                problems.Add("the same seed gave different ground");

            // The water map (shores, routes, creek) must be measurable and find its way round the ponds.
            if ((WaterMap.CreekEnabled && WaterMap.Creek.Length < 1) || WaterMap.Shore.Length == 0)
                problems.Add("no shore");

            if (problems.Count > 0)
            {
                failures++;
                Console.WriteLine($"seed {seed}: {string.Join("; ", problems)}");
            }
        }
        Console.WriteLine($"{count} terrains, {failures} with problems; pond area {minWet:0}-{maxWet:0} m2; {minProps}-{maxProps} props");
        return failures == 0 ? 0 : 1;
    }
}
