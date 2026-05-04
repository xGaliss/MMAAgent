namespace MMAAgent.Infrastructure.Generation;

/// <summary>
/// Small reusable helper for deterministic weighted picks.
/// We always feed it the generator's seeded Random instance so world generation
/// stays reproducible for the same save seed.
/// </summary>
public static class WeightedRandomPicker
{
    public static T PickOrDefault<T>(IReadOnlyList<WeightedValue<T>> items, Random rng, T defaultValue)
    {
        if (items.Count == 0)
            return defaultValue;

        var totalWeight = 0;
        for (var i = 0; i < items.Count; i++)
            totalWeight += Math.Max(0, items[i].Weight);

        if (totalWeight <= 0)
            return defaultValue;

        var roll = rng.Next(totalWeight);
        var cumulative = 0;

        for (var i = 0; i < items.Count; i++)
        {
            var weight = Math.Max(0, items[i].Weight);
            if (weight <= 0)
                continue;

            cumulative += weight;
            if (roll < cumulative)
                return items[i].Item;
        }

        return items[^1].Item;
    }
}

public sealed record WeightedValue<T>(T Item, int Weight);
