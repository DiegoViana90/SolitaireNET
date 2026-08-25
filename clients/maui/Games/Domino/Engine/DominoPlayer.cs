namespace SolitaireNET;

public class DominoPlayer
{
    public int Index { get; }
    public List<DominoTile> Hand { get; } = new();

    public DominoPlayer(int index)
    {
        Index = index;
    }

    public string Name => $"Jogador {Index + 1}";
    public int Team => Index % 2;
}