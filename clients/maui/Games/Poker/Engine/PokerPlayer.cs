namespace SolitaireNET;

public sealed class PokerPlayer
{
    public string Name { get; }
    public bool IsHuman { get; }

    public int Chips { get; set; } = 1000;
    public int CurrentBet { get; set; }

    public bool Folded { get; set; }
    public bool AllIn { get; set; }
    public bool Acted { get; set; }

    public bool IsDealer { get; set; }
    public bool IsSmallBlind { get; set; }
    public bool IsBigBlind { get; set; }

    public List<PokerCard> HoleCards { get; } = new();

    public PokerPlayer(string name, bool isHuman)
    {
        Name = name;
        IsHuman = isHuman;
    }

    public bool Eliminated => Chips <= 0 && HoleCards.Count == 0;

    public bool IsInHand => !Folded && HoleCards.Count == 2;

    public bool CanAct =>
        IsInHand &&
        !AllIn &&
        Chips > 0;

    public string PositionText
    {
        get
        {
            var positions = new List<string>();

            if (IsDealer)
                positions.Add("DEALER");

            if (IsSmallBlind)
                positions.Add("SMALL BLIND");

            if (IsBigBlind)
                positions.Add("BIG BLIND");

            return string.Join(" / ", positions);
        }
    }
}
