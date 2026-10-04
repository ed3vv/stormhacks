using UnityEngine;

/// <summary>Best scores per hole and solved puzzles, kept in PlayerPrefs for the level select.</summary>
public static class S3Progress
{
    static string Key(S3World.Biome map, int index) => $"s3.best.{map}.{index}";

    /// <summary>Fewest strokes on a hole (or 1 for a solved puzzle); 0 if never finished.</summary>
    public static int Best(S3World.Biome map, int index) => PlayerPrefs.GetInt(Key(map, index), 0);

    public static void Record(S3World.Biome map, int index, int strokes)
    {
        int best = Best(map, index);
        if (best > 0 && best <= strokes) return;
        PlayerPrefs.SetInt(Key(map, index), strokes);
        PlayerPrefs.Save();
    }
}
