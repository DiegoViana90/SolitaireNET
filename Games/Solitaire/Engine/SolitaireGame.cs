using Microsoft.Maui.Graphics;
using System.Linq;
using System.Text.Json;

namespace SolitaireNET;

public class SolitaireGame : IDrawable
{
    float CW = 82, CH = 118, GAP = 18, DOWN = 34;
    float Scale = 1, LeftPad = 30, TopPad = 24, TableTop = 170;
    float Width = 1100, Height = 760;

    readonly List<Card> deck = new();
    readonly List<Card> stock = new();
    readonly List<Card> waste = new();
    readonly List<Card>[] tableau = Enumerable.Range(0, 7).Select(_ => new List<Card>()).ToArray();
    readonly List<Card>[] foundations = Enumerable.Range(0, 4).Select(_ => new List<Card>()).ToArray();

    readonly Random rng = new();

    bool jaReciclouMonte;
    int mudancasDesdeReciclagem;

    Card? dragging;
    readonly List<Card> draggingStack = new();

    PointF dragOffset;
    PointF dragPosition;

    PileKind dragFromKind;
    int dragFromIndex;

    DateTime lastTapTime = DateTime.MinValue;
    Card? lastTapCard;

    public bool SemSaida { get; private set; }

    public string StatusText =>
        SemSaida
            ? "Sem movimentos úteis detectados."
            : $"Monte: {stock.Count} | Lixo: {waste.Count}";

    public event Action? StatusChanged;

    public void SetSize(float width, float height)
    {
        if (width > 0) Width = width;
        if (height > 0) Height = height;

        AtualizarLayoutMesa();
    }

    public void NovoJogo()
    {
        deck.Clear();
        stock.Clear();
        waste.Clear();
        dragging = null;
        draggingStack.Clear();

        jaReciclouMonte = false;
        mudancasDesdeReciclagem = 0;
        SemSaida = false;

        foreach (var p in tableau) p.Clear();
        foreach (var f in foundations) f.Clear();

        string[] suits = { "S", "H", "D", "C" };

        foreach (var s in suits)
            for (int r = 1; r <= 13; r++)
                deck.Add(new Card(r, s));

        foreach (var c in deck.OrderBy(_ => rng.Next()))
            stock.Add(c);

        for (int col = 0; col < 7; col++)
        {
            for (int i = 0; i <= col; i++)
            {
                var c = stock[^1];
                stock.RemoveAt(stock.Count - 1);
                c.FaceUp = i == col;
                tableau[col].Add(c);
            }
        }

        StatusChanged?.Invoke();
    }

    void AtualizarLayoutMesa()
    {
        const float baseW = 82;
        const float baseH = 118;
        const float baseGap = 18;
        const float baseDown = 34;

        float neededW = 7 * baseW + 6 * baseGap + 60;
        float neededH = 170 + baseH + 18 * baseDown + 40;

        Scale = Math.Min(Width / neededW, Height / neededH);
        Scale = Math.Clamp(Scale, 0.55f, 1.45f);

        CW = baseW * Scale;
        CH = baseH * Scale;
        GAP = baseGap * Scale;
        DOWN = baseDown * Scale;

        LeftPad = Math.Max(14, (Width - (7 * CW + 6 * GAP)) / 2);
        TopPad = 24 * Scale;
        TableTop = 170 * Scale;
    }

    float StockX() => LeftPad;
    float WasteX() => LeftPad + CW + GAP;
    float FoundationX(int i)
    {
        return LeftPad + 3 * (CW + GAP) + i * (CW + GAP);
    }
    float TableauX(int i) => LeftPad + i * (CW + GAP);

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        Width = dirtyRect.Width;
        Height = dirtyRect.Height;
        AtualizarLayoutMesa();

        canvas.FillColor = Color.FromArgb("#0B6B3A");
        canvas.FillRectangle(dirtyRect);

        DrawSlot(canvas, StockX(), TopPad, "MONTE");
        DrawSlot(canvas, WasteX(), TopPad, "LIXO");
        DrawPileCount(canvas, StockX(), TopPad, stock.Count);
        DrawPileCount(canvas, WasteX(), TopPad, waste.Count);

        for (int i = 0; i < 4; i++)
            DrawSlot(canvas, FoundationX(i), TopPad, "BASE");

        if (stock.Count > 0)
            DrawCardBack(canvas, StockX(), TopPad);

        if (waste.Count > 0)
            DrawCard(canvas, waste[^1], WasteX(), TopPad);

        for (int i = 0; i < 4; i++)
            if (foundations[i].Count > 0)
                DrawCard(canvas, foundations[i][^1], FoundationX(i), TopPad);

        for (int col = 0; col < 7; col++)
        {
            float x = TableauX(col);

            for (int row = 0; row < tableau[col].Count; row++)
            {
                var c = tableau[col][row];

                if (draggingStack.Contains(c))
                    continue;

                float y = TableTop + row * DOWN;

                if (c.FaceUp)
                    DrawCard(canvas, c, x, y);
                else
                    DrawCardBack(canvas, x, y);
            }
        }

        if (dragging != null)
        {
            for (int i = 0; i < draggingStack.Count; i++)
            {
                var c = draggingStack[i];
                DrawCard(canvas, c, dragPosition.X, dragPosition.Y + i * DOWN);
            }
        }
    }

    void DrawPileCount(ICanvas canvas, float x, float y, int count)
    {
        canvas.FontColor = Colors.White;
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.FontSize = 14 * Scale;

        canvas.DrawString(
            count.ToString(),
            x,
            y - 22 * Scale,
            CW,
            20 * Scale,
            HorizontalAlignment.Center,
            VerticalAlignment.Center);
    }
    public void TouchStart(PointF p)
    {
        AtualizarLayoutMesa();

        if (Hit(p, StockX(), TopPad, CW, CH))
        {
            if (stock.Count > 0)
                DrawFromStock();
            else
                ResetStock();

            return;
        }

        if (TryGetCardAt(p, out var hit))
        {
            var card = hit.Card;

            if (!card.FaceUp)
            {
                TryFlipTableau(hit.Kind, hit.Index, card);
                return;
            }

            bool doubleTap =
                lastTapCard == card &&
                DateTime.Now - lastTapTime < TimeSpan.FromMilliseconds(350);

            lastTapCard = card;
            lastTapTime = DateTime.Now;

            if (doubleTap)
            {
                if (TryAutoMoveToFoundation(card, hit.Kind, hit.Index))
                {
                    VerificarVitoria();
                    VerificarSemSaida();
                    StatusChanged?.Invoke();
                }

                return;
            }

            dragging = card;
            dragFromKind = hit.Kind;
            dragFromIndex = hit.Index;
            dragOffset = new PointF(p.X - hit.X, p.Y - hit.Y);
            dragPosition = new PointF(hit.X, hit.Y);

            PrepareDragStack(card, hit.Kind, hit.Index);
        }
    }

    public void TouchMove(PointF p)
    {
        if (dragging == null) return;

        dragPosition = new PointF(p.X - dragOffset.X, p.Y - dragOffset.Y);
    }

    public void TouchEnd(PointF p)
    {
        if (dragging == null) return;

        if (TryDropOnFoundation(p) || TryDropOnTableau(p))
        {
            dragging = null;
            draggingStack.Clear();

            VerificarVitoria();
            VerificarSemSaida();
            StatusChanged?.Invoke();
            return;
        }

        dragging = null;
        draggingStack.Clear();
    }

    void DrawFromStock()
    {
        if (stock.Count == 0) return;

        var c = stock[^1];
        stock.RemoveAt(stock.Count - 1);
        c.FaceUp = true;
        waste.Add(c);

        VerificarSemSaida();
        StatusChanged?.Invoke();
    }

    void ResetStock()
    {
        if (stock.Count > 0 || waste.Count == 0) return;

        while (waste.Count > 0)
        {
            var c = waste[^1];
            waste.RemoveAt(waste.Count - 1);
            c.FaceUp = false;
            stock.Add(c);
        }

        jaReciclouMonte = true;
        mudancasDesdeReciclagem = 0;
        StatusChanged?.Invoke();
    }

    void TryFlipTableau(PileKind kind, int index, Card card)
    {
        if (kind != PileKind.Tableau) return;

        var pile = tableau[index];

        if (pile.Count > 0 && pile[^1] == card && !card.FaceUp)
        {
            card.FaceUp = true;
            MarcarMudancaUtil();
            VerificarSemSaida();
            StatusChanged?.Invoke();
        }
    }

    bool TryDropOnFoundation(PointF p)
    {
        for (int i = 0; i < 4; i++)
        {
            if (Hit(p, FoundationX(i), TopPad, CW, CH) && CanMoveToFoundation(i))
            {
                MoveDraggingTo(foundations[i]);
                MarcarMudancaUtil();
                return true;
            }
        }

        return false;
    }

    bool TryDropOnTableau(PointF p)
    {
        for (int i = 0; i < 7; i++)
        {
            if (Hit(p, TableauX(i), TableTop, CW, Math.Max(CH, Height - TableTop)) && CanMoveToTableau(i))
            {
                MoveDraggingTo(tableau[i]);
                MarcarMudancaUtil();
                return true;
            }
        }

        return false;
    }

    bool TryGetCardAt(PointF p, out HitCard hit)
    {
        hit = default;

        if (waste.Count > 0 && Hit(p, WasteX(), TopPad, CW, CH))
        {
            hit = new HitCard(waste[^1], PileKind.Waste, 0, WasteX(), TopPad);
            return true;
        }

        for (int i = 0; i < 4; i++)
        {
            if (foundations[i].Count > 0 && Hit(p, FoundationX(i), TopPad, CW, CH))
            {
                hit = new HitCard(foundations[i][^1], PileKind.Foundation, i, FoundationX(i), TopPad);
                return true;
            }
        }

        for (int col = 6; col >= 0; col--)
        {
            var pile = tableau[col];

            for (int row = pile.Count - 1; row >= 0; row--)
            {
                float x = TableauX(col);
                float y = TableTop + row * DOWN;
                float h = row == pile.Count - 1 ? CH : DOWN;

                if (Hit(p, x, y, CW, h))
                {
                    hit = new HitCard(pile[row], PileKind.Tableau, col, x, y);
                    return true;
                }
            }
        }

        return false;
    }

    void PrepareDragStack(Card card, PileKind kind, int index)
    {
        draggingStack.Clear();

        if (kind == PileKind.Tableau)
        {
            var pile = tableau[index];
            int start = pile.IndexOf(card);

            if (start >= 0)
                draggingStack.AddRange(pile.Skip(start));
        }
        else
        {
            draggingStack.Add(card);
        }
    }

    bool CanMoveToFoundation(int i)
    {
        if (dragging == null || draggingStack.Count != 1) return false;

        var f = foundations[i];

        if (f.Count == 0) return dragging.Rank == 1;

        return f[^1].Suit == dragging.Suit && dragging.Rank == f[^1].Rank + 1;
    }

    bool CanMoveToTableau(int i)
    {
        if (dragging == null) return false;

        var t = tableau[i];

        if (t.Count == 0) return dragging.Rank == 13;

        var top = t[^1];

        return top.FaceUp && top.IsRed != dragging.IsRed && dragging.Rank == top.Rank - 1;
    }

    void MoveDraggingTo(List<Card> target)
    {
        if (dragging == null) return;

        RemoveDragging();

        foreach (var c in draggingStack)
            target.Add(c);
    }

    void RemoveDragging()
    {
        if (dragging == null) return;

        switch (dragFromKind)
        {
            case PileKind.Waste:
                waste.Remove(dragging);
                break;

            case PileKind.Foundation:
                foundations[dragFromIndex].Remove(dragging);
                break;

            case PileKind.Tableau:
                foreach (var c in draggingStack.ToList())
                    tableau[dragFromIndex].Remove(c);
                break;
        }
    }

    bool TryAutoMoveToFoundation(Card card, PileKind kind, int index)
    {
        dragging = card;
        dragFromKind = kind;
        dragFromIndex = index;

        PrepareDragStack(card, kind, index);

        if (draggingStack.Count != 1)
        {
            dragging = null;
            draggingStack.Clear();
            return false;
        }

        for (int i = 0; i < 4; i++)
        {
            if (CanMoveToFoundation(i))
            {
                MoveDraggingTo(foundations[i]);
                MarcarMudancaUtil();

                dragging = null;
                draggingStack.Clear();
                return true;
            }
        }

        dragging = null;
        draggingStack.Clear();
        return false;
    }

    void MarcarMudancaUtil()
    {
        if (jaReciclouMonte)
            mudancasDesdeReciclagem++;
    }

    void VerificarSemSaida()
    {
        if (stock.Count == 0 &&
            jaReciclouMonte &&
            mudancasDesdeReciclagem == 0 &&
            !TemMovimentoPossivel())
        {
            SemSaida = true;
        }
    }

    bool TemMovimentoPossivel()
    {
        if (stock.Count > 0)
            return true;

        if (waste.Count > 0)
        {
            var c = waste[^1];

            if (PodeMoverParaAlgumaBase(c))
                return true;

            if (PodeMoverParaAlgumaColuna(c))
                return true;
        }

        for (int origem = 0; origem < tableau.Length; origem++)
        {
            var origemPile = tableau[origem];

            if (origemPile.Count == 0)
                continue;

            if (!origemPile[^1].FaceUp)
                return true;

            for (int pos = 0; pos < origemPile.Count; pos++)
            {
                var carta = origemPile[pos];

                if (!carta.FaceUp)
                    continue;

                bool revelaCartaFechada = pos > 0 && !origemPile[pos - 1].FaceUp;
                bool moveSequenciaInteira = pos == 0;

                if (PodeMoverParaAlgumaBase(carta))
                    return true;

                for (int destino = 0; destino < tableau.Length; destino++)
                {
                    if (destino == origem)
                        continue;

                    var destinoPile = tableau[destino];

                    if (!PodeIrParaColuna(carta, destinoPile))
                        continue;

                    if (revelaCartaFechada)
                        return true;

                    if (destinoPile.Count == 0 && carta.Rank == 13 && !moveSequenciaInteira)
                        return true;
                }
            }
        }

        return false;
    }

    bool PodeMoverParaAlgumaBase(Card c)
    {
        foreach (var f in foundations)
        {
            if (f.Count == 0 && c.Rank == 1)
                return true;

            if (f.Count > 0 && f[^1].Suit == c.Suit && c.Rank == f[^1].Rank + 1)
                return true;
        }

        return false;
    }

    bool PodeMoverParaAlgumaColuna(Card c)
    {
        foreach (var t in tableau)
        {
            if (PodeIrParaColuna(c, t))
                return true;
        }

        return false;
    }
    bool PodeIrParaColuna(Card c, List<Card> destino)
    {
        if (destino.Count == 0)
            return c.Rank == 13;

        var top = destino[^1];

        return top.FaceUp &&
               top.IsRed != c.IsRed &&
               c.Rank == top.Rank - 1;
    }

    bool PodeMoverCarta(Card c)
    {
        foreach (var f in foundations)
        {
            if (f.Count == 0 && c.Rank == 1) return true;
            if (f.Count > 0 && f[^1].Suit == c.Suit && c.Rank == f[^1].Rank + 1) return true;
        }

        foreach (var t in tableau)
        {
            if (t.Count == 0 && c.Rank == 13) return true;
            if (t.Count == 0) continue;

            var top = t[^1];

            if (top.FaceUp && top.IsRed != c.IsRed && c.Rank == top.Rank - 1)
                return true;
        }

        return false;
    }

    void VerificarVitoria()
    {
        if (foundations.Sum(f => f.Count) == 52)
            SemSaida = false;
    }

    static bool Hit(PointF p, float x, float y, float w, float h)
    {
        return p.X >= x && p.X <= x + w && p.Y >= y && p.Y <= y + h;
    }

    void DrawSlot(ICanvas canvas, float x, float y, string text)
    {
        canvas.StrokeColor = Colors.White.WithAlpha(0.35f);
        canvas.StrokeSize = 2 * Scale;
        canvas.FillColor = Colors.Transparent;
        canvas.DrawRoundedRectangle(x, y, CW, CH, 9 * Scale);

        canvas.FontColor = Colors.White.WithAlpha(0.45f);
        canvas.FontSize = 10 * Scale;
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.DrawString(text, x, y + CH / 2 - 8 * Scale, CW, 20 * Scale, HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    void DrawCardBack(ICanvas canvas, float x, float y)
    {
        DrawCardBase(canvas, x, y);

        canvas.FillColor = Colors.DarkBlue;
        canvas.StrokeColor = Colors.White;
        canvas.StrokeSize = 2 * Scale;
        canvas.FillRoundedRectangle(x + 7 * Scale, y + 7 * Scale, CW - 14 * Scale, CH - 14 * Scale, 7 * Scale);
        canvas.DrawRoundedRectangle(x + 7 * Scale, y + 7 * Scale, CW - 14 * Scale, CH - 14 * Scale, 7 * Scale);

        canvas.StrokeColor = Colors.LightBlue;
        canvas.StrokeSize = 2 * Scale;

        canvas.DrawLine(x + 18 * Scale, y + 18 * Scale, x + 64 * Scale, y + 100 * Scale);
        canvas.DrawLine(x + 64 * Scale, y + 18 * Scale, x + 18 * Scale, y + 100 * Scale);
    }

    void DrawCard(ICanvas canvas, Card card, float x, float y)
    {
        DrawCardBase(canvas, x, y);

        Color color = card.IsRed ? Color.FromArgb("#B22222") : Colors.Black;
        string label = $"{card.RankText}{card.SuitText}";

        canvas.FontColor = color;
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;

        canvas.FontSize = card.Rank == 10 ? 22 * Scale : 26 * Scale;
        canvas.DrawString(label, x + 7 * Scale, y + 4 * Scale, CW, 30 * Scale, HorizontalAlignment.Left, VerticalAlignment.Top);

        canvas.FontSize = 42 * Scale;
        canvas.DrawString(card.SuitText, x, y + 36 * Scale, CW, 48 * Scale, HorizontalAlignment.Center, VerticalAlignment.Center);

        canvas.FontSize = card.Rank == 10 ? 20 * Scale : 23 * Scale;
        canvas.DrawString(label, x, y + 86 * Scale, CW - 6 * Scale, 28 * Scale, HorizontalAlignment.Right, VerticalAlignment.Top);
    }

    void DrawCardBase(ICanvas canvas, float x, float y)
    {
        canvas.FillColor = Colors.White;
        canvas.StrokeColor = Colors.Black;
        canvas.StrokeSize = 1.4f * Scale;
        canvas.FillRoundedRectangle(x, y, CW, CH, 9 * Scale);
        canvas.DrawRoundedRectangle(x, y, CW, CH, 9 * Scale);
    }

    public string ExportState()
    {
        var state = new SaveState
        {
            Stock = stock.Select(CardDto.From).ToList(),
            Waste = waste.Select(CardDto.From).ToList(),
            Tableau = tableau.Select(p => p.Select(CardDto.From).ToList()).ToList(),
            Foundations = foundations.Select(p => p.Select(CardDto.From).ToList()).ToList(),
            JaReciclouMonte = jaReciclouMonte,
            MudancasDesdeReciclagem = mudancasDesdeReciclagem,
            SemSaida = SemSaida
        };

        return JsonSerializer.Serialize(state);
    }

    public bool ImportState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            var state = JsonSerializer.Deserialize<SaveState>(json);

            if (state == null)
                return false;

            stock.Clear();
            waste.Clear();

            foreach (var p in tableau) p.Clear();
            foreach (var f in foundations) f.Clear();

            stock.AddRange(state.Stock.Select(x => x.ToCard()));
            waste.AddRange(state.Waste.Select(x => x.ToCard()));

            for (int i = 0; i < Math.Min(7, state.Tableau.Count); i++)
                tableau[i].AddRange(state.Tableau[i].Select(x => x.ToCard()));

            for (int i = 0; i < Math.Min(4, state.Foundations.Count); i++)
                foundations[i].AddRange(state.Foundations[i].Select(x => x.ToCard()));

            jaReciclouMonte = state.JaReciclouMonte;
            mudancasDesdeReciclagem = state.MudancasDesdeReciclagem;
            SemSaida = state.SemSaida;

            dragging = null;
            draggingStack.Clear();

            StatusChanged?.Invoke();
            return true;
        }
        catch
        {
            return false;
        }
    }

    class SaveState
    {
        public List<CardDto> Stock { get; set; } = new();
        public List<CardDto> Waste { get; set; } = new();
        public List<List<CardDto>> Tableau { get; set; } = new();
        public List<List<CardDto>> Foundations { get; set; } = new();
        public bool JaReciclouMonte { get; set; }
        public int MudancasDesdeReciclagem { get; set; }
        public bool SemSaida { get; set; }
    }

    class CardDto
    {
        public int Rank { get; set; }
        public string Suit { get; set; } = "";
        public bool FaceUp { get; set; }

        public static CardDto From(Card c)
        {
            return new CardDto
            {
                Rank = c.Rank,
                Suit = c.Suit,
                FaceUp = c.FaceUp
            };
        }

        public Card ToCard()
        {
            return new Card(Rank, Suit)
            {
                FaceUp = FaceUp
            };
        }
    }

    enum PileKind
    {
        Stock,
        Waste,
        Tableau,
        Foundation
    }

    readonly record struct HitCard(Card Card, PileKind Kind, int Index, float X, float Y);

    public class Card
    {
        public int Rank { get; }
        public string Suit { get; }
        public bool FaceUp { get; set; }

        public bool IsRed => Suit is "H" or "D";

        public Card(int rank, string suit)
        {
            Rank = rank;
            Suit = suit;
        }

        public string RankText => Rank switch
        {
            1 => "A",
            11 => "J",
            12 => "Q",
            13 => "K",
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
    }
}