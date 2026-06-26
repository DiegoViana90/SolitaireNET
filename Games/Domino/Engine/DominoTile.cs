namespace SolitaireNET;

public class DominoTile
{
    public int A { get; set; }
    public int B { get; set; }

    public DominoTile(int a, int b)
    {
        A = a;
        B = b;
    }

    public int Sum => A + B;
    public bool IsDouble => A == B;

    public bool Matches(int value)
    {
        return A == value || B == value;
    }

    public void Flip()
    {
        (A, B) = (B, A);
    }

    public override string ToString()
    {
        return $"{A}|{B}";
    }
}