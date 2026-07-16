namespace SolitaireNET;

public sealed record PokerHandResult(long Score, string Name);

public static class PokerHandEvaluator
{
    public static PokerHandResult Evaluate(IReadOnlyList<PokerCard> cards)
    {
        if (cards.Count < 5)
            throw new ArgumentException("São necessárias pelo menos cinco cartas.");

        PokerHandResult? best = null;

        for (int a = 0; a < cards.Count - 4; a++)
        for (int b = a + 1; b < cards.Count - 3; b++)
        for (int c = b + 1; c < cards.Count - 2; c++)
        for (int d = c + 1; d < cards.Count - 1; d++)
        for (int e = d + 1; e < cards.Count; e++)
        {
            var result = EvaluateFive(
                new[]
                {
                    cards[a],
                    cards[b],
                    cards[c],
                    cards[d],
                    cards[e]
                });

            if (best == null || result.Score > best.Score)
                best = result;
        }

        return best!;
    }

    static PokerHandResult EvaluateFive(IReadOnlyList<PokerCard> cards)
    {
        var ranks = cards
            .Select(c => c.Rank)
            .OrderByDescending(r => r)
            .ToList();

        var groups = ranks
            .GroupBy(r => r)
            .Select(g => new
            {
                Rank = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(g => g.Count)
            .ThenByDescending(g => g.Rank)
            .ToList();

        bool flush = cards.All(c => c.Suit == cards[0].Suit);
        int straightHigh = GetStraightHigh(ranks);

        if (flush && straightHigh > 0)
            return BuildResult(8, "Straight Flush", straightHigh);

        if (groups[0].Count == 4)
        {
            int quad = groups[0].Rank;
            int kicker = groups.First(g => g.Count == 1).Rank;

            return BuildResult(7, "Quadra", quad, kicker);
        }

        if (groups[0].Count == 3 &&
            groups.Count > 1 &&
            groups[1].Count == 2)
        {
            return BuildResult(
                6,
                "Full House",
                groups[0].Rank,
                groups[1].Rank);
        }

        if (flush)
            return BuildResult(5, "Flush", ranks.ToArray());

        if (straightHigh > 0)
            return BuildResult(4, "Sequência", straightHigh);

        if (groups[0].Count == 3)
        {
            var kickers = groups
                .Where(g => g.Count == 1)
                .Select(g => g.Rank)
                .OrderByDescending(r => r)
                .ToArray();

            return BuildResult(
                3,
                "Trinca",
                new[] { groups[0].Rank }
                    .Concat(kickers)
                    .ToArray());
        }

        if (groups.Count > 1 &&
            groups[0].Count == 2 &&
            groups[1].Count == 2)
        {
            int highPair = Math.Max(groups[0].Rank, groups[1].Rank);
            int lowPair = Math.Min(groups[0].Rank, groups[1].Rank);
            int kicker = groups.First(g => g.Count == 1).Rank;

            return BuildResult(
                2,
                "Dois pares",
                highPair,
                lowPair,
                kicker);
        }

        if (groups[0].Count == 2)
        {
            var kickers = groups
                .Where(g => g.Count == 1)
                .Select(g => g.Rank)
                .OrderByDescending(r => r)
                .ToArray();

            return BuildResult(
                1,
                "Um par",
                new[] { groups[0].Rank }
                    .Concat(kickers)
                    .ToArray());
        }

        return BuildResult(0, "Carta alta", ranks.ToArray());
    }

    static int GetStraightHigh(IEnumerable<int> source)
    {
        var ranks = source
            .Distinct()
            .OrderByDescending(r => r)
            .ToList();

        if (ranks.Contains(14))
            ranks.Add(1);

        int sequenceLength = 1;

        for (int i = 1; i < ranks.Count; i++)
        {
            if (ranks[i - 1] - 1 == ranks[i])
            {
                sequenceLength++;

                if (sequenceLength >= 5)
                    return ranks[i - 4];
            }
            else
            {
                sequenceLength = 1;
            }
        }

        return 0;
    }

    static PokerHandResult BuildResult(
        int category,
        string name,
        params int[] values)
    {
        long score = category;

        foreach (int value in values.Take(5))
            score = score * 15 + value;

        for (int i = values.Length; i < 5; i++)
            score *= 15;

        return new PokerHandResult(score, name);
    }
}
