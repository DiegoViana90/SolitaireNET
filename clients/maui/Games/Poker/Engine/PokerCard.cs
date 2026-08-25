namespace SolitaireNET;

public sealed class PokerCard
{
    public int Rank { get; }
    public string Suit { get; }

    public PokerCard(int rank, string suit)
    {
        Rank = rank;
        Suit = suit;
    }

    public bool IsRed => Suit is "H" or "D";

    public string RankText => Rank switch
    {
        14 => "A",
        13 => "K",
        12 => "Q",
        11 => "J",
        _ => Rank.ToString()
    };

    public string SuitText => Suit switch
    {
        "S" => "♠",
        "H" => "♥",
        "D" => "♦",
        "C" => "♣",
        _ => "?"
    };

    public override string ToString()
    {
        return $"{RankText}{SuitText}";
    }
}
