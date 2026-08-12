using System.Linq;

namespace SolitaireNET;

public sealed class RemoteSolitaireGame : IDrawable
{
    float CW = 82, CH = 118, GAP = 18, DOWN = 34;
    float Scale = 1, LeftPad = 30, TopPad = 24, TableTop = 170;
    float Width = 1100, Height = 760;

    RemoteSolitaireState? state;
    RemoteCard? dragging;
    readonly List<RemoteCard> draggingStack = new();

    PointF dragOffset;
    PointF dragPosition;
    RemotePileRef? dragSource;

    DateTime lastTapTime = DateTime.MinValue;
    string? lastTapCardId;

    public bool IsBusy { get; private set; }

    public string StatusText
    {
        get
        {
            if (state == null)
                return IsBusy ? "Carregando..." : "Sem partida";

            if (state.Won)
                return "Vitoria!";

            string suffix = IsBusy ? " | sincronizando..." : "";
            return $"Monte: {state.StockCount} | Lixo: {state.WasteCount}{suffix}";
        }
    }

    public event Action? StatusChanged;

    public void SetSize(float width, float height)
    {
        if (width > 0) Width = width;
        if (height > 0) Height = height;

        AtualizarLayoutMesa();
    }

    public void SetState(RemoteSolitaireState nextState)
    {
        state = nextState;
        ClearDragging();
        StatusChanged?.Invoke();
    }

    public void SetBusy(bool busy)
    {
        IsBusy = busy;
        StatusChanged?.Invoke();
    }

    public RemoteGameAction? TouchStart(PointF p)
    {
        AtualizarLayoutMesa();

        if (state == null || IsBusy)
            return null;

        if (Hit(p, StockX(), TopPad, CW, CH))
        {
            if (state.StockCount > 0)
                return new RemoteGameAction { Type = "drawStock" };

            if (state.WasteCount > 0)
                return new RemoteGameAction { Type = "resetStock" };

            return null;
        }

        if (!TryGetCardAt(p, out HitCard hit))
            return null;

        RemoteCard card = hit.Card;

        if (!card.FaceUp)
        {
            if (hit.Kind == "tableau" &&
                hit.Row == state.Tableau[hit.Index].Count - 1)
            {
                return new RemoteGameAction
                {
                    Type = "flipTableau",
                    Source = new RemotePileRef
                    {
                        Kind = "tableau",
                        Index = hit.Index,
                        Row = hit.Row
                    }
                };
            }

            return null;
        }

        string? cardId = card.Id;
        bool doubleTap =
            !string.IsNullOrWhiteSpace(cardId) &&
            lastTapCardId == cardId &&
            DateTime.Now - lastTapTime < TimeSpan.FromMilliseconds(350);

        lastTapCardId = cardId;
        lastTapTime = DateTime.Now;

        if (doubleTap && TryBuildAutoFoundationMove(card, hit, out RemoteGameAction? action))
            return action;

        dragging = card;
        dragSource = new RemotePileRef
        {
            Kind = hit.Kind,
            Index = hit.Index,
            Row = hit.Row
        };
        dragOffset = new PointF(p.X - hit.X, p.Y - hit.Y);
        dragPosition = new PointF(hit.X, hit.Y);

        PrepareDragStack(hit);

        return null;
    }

    public void TouchMove(PointF p)
    {
        if (dragging == null || IsBusy)
            return;

        dragPosition = new PointF(p.X - dragOffset.X, p.Y - dragOffset.Y);
    }

    public RemoteGameAction? TouchEnd(PointF p)
    {
        if (dragging == null || dragSource == null || state == null)
            return null;

        RemoteGameAction? action = null;

        if (TryFindFoundationTarget(p, out int foundationIndex) &&
            CanMoveToFoundation(foundationIndex))
        {
            action = MoveAction("foundation", foundationIndex);
        }
        else if (TryFindTableauTarget(p, out int tableauIndex) &&
                 CanMoveToTableau(tableauIndex))
        {
            action = MoveAction("tableau", tableauIndex);
        }

        ClearDragging();
        return action;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        Width = dirtyRect.Width;
        Height = dirtyRect.Height;
        AtualizarLayoutMesa();

        canvas.FillColor = Color.FromArgb("#0B6B3A");
        canvas.FillRectangle(dirtyRect);

        DrawSlot(canvas, StockX(), TopPad, "MONTE");
        DrawSlot(canvas, WasteX(), TopPad, "LIXO");

        if (state != null)
        {
            DrawPileCount(canvas, StockX(), TopPad, state.StockCount);
            DrawPileCount(canvas, WasteX(), TopPad, state.WasteCount);
        }

        for (int i = 0; i < 4; i++)
            DrawSlot(canvas, FoundationX(i), TopPad, $"BASE {i + 1}");

        if (state == null)
        {
            DrawMessage(canvas, dirtyRect, StatusText);
            return;
        }

        if (state.StockCount > 0)
            DrawCardBack(canvas, StockX(), TopPad);

        if (state.WasteTop is { FaceUp: true } wasteTop)
            DrawCard(canvas, wasteTop, WasteX(), TopPad);

        for (int i = 0; i < Math.Min(4, state.Foundations.Count); i++)
        {
            RemoteCard? card = state.Foundations[i];
            if (card is { FaceUp: true })
                DrawCard(canvas, card, FoundationX(i), TopPad);
        }

        for (int col = 0; col < Math.Min(7, state.Tableau.Count); col++)
        {
            List<RemoteCard> pile = state.Tableau[col];
            float x = TableauX(col);

            for (int row = 0; row < pile.Count; row++)
            {
                RemoteCard card = pile[row];

                if (draggingStack.Contains(card))
                    continue;

                float y = TableTop + row * DOWN;

                if (card.FaceUp)
                    DrawCard(canvas, card, x, y);
                else
                    DrawCardBack(canvas, x, y);
            }
        }

        if (dragging != null)
        {
            for (int i = 0; i < draggingStack.Count; i++)
            {
                DrawCard(
                    canvas,
                    draggingStack[i],
                    dragPosition.X,
                    dragPosition.Y + i * DOWN);
            }
        }
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

    bool TryGetCardAt(PointF p, out HitCard hit)
    {
        hit = default;

        if (state == null)
            return false;

        if (state.WasteTop is { FaceUp: true } wasteTop &&
            Hit(p, WasteX(), TopPad, CW, CH))
        {
            hit = new HitCard(wasteTop, "waste", 0, null, WasteX(), TopPad);
            return true;
        }

        for (int i = 0; i < Math.Min(4, state.Foundations.Count); i++)
        {
            RemoteCard? card = state.Foundations[i];
            if (card is { FaceUp: true } &&
                Hit(p, FoundationX(i), TopPad, CW, CH))
            {
                hit = new HitCard(card, "foundation", i, null, FoundationX(i), TopPad);
                return true;
            }
        }

        for (int col = Math.Min(7, state.Tableau.Count) - 1; col >= 0; col--)
        {
            List<RemoteCard> pile = state.Tableau[col];

            for (int row = pile.Count - 1; row >= 0; row--)
            {
                float x = TableauX(col);
                float y = TableTop + row * DOWN;
                float h = row == pile.Count - 1 ? CH : DOWN;

                if (Hit(p, x, y, CW, h))
                {
                    hit = new HitCard(pile[row], "tableau", col, row, x, y);
                    return true;
                }
            }
        }

        return false;
    }

    void PrepareDragStack(HitCard hit)
    {
        draggingStack.Clear();

        if (state == null)
            return;

        if (hit.Kind == "tableau" && hit.Row.HasValue)
        {
            List<RemoteCard> pile = state.Tableau[hit.Index];
            draggingStack.AddRange(pile.Skip(hit.Row.Value));
            return;
        }

        draggingStack.Add(hit.Card);
    }

    bool TryBuildAutoFoundationMove(RemoteCard card, HitCard hit, out RemoteGameAction? action)
    {
        action = null;

        if (!card.Rank.HasValue || draggingStack.Count > 1)
            return false;

        var previousDragging = dragging;
        var previousSource = dragSource;

        dragging = card;
        dragSource = new RemotePileRef
        {
            Kind = hit.Kind,
            Index = hit.Index,
            Row = hit.Row
        };
        PrepareDragStack(hit);

        for (int i = 0; i < 4; i++)
        {
            if (CanMoveToFoundation(i))
            {
                action = MoveAction("foundation", i);
                break;
            }
        }

        ClearDragging();
        dragging = previousDragging;
        dragSource = previousSource;

        return action != null;
    }

    bool TryFindFoundationTarget(PointF p, out int index)
    {
        for (int i = 0; i < 4; i++)
        {
            if (Hit(p, FoundationX(i), TopPad, CW, CH))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    bool TryFindTableauTarget(PointF p, out int index)
    {
        for (int i = 0; i < 7; i++)
        {
            if (Hit(p, TableauX(i), TableTop, CW, Math.Max(CH, Height - TableTop)))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    bool CanMoveToFoundation(int index)
    {
        if (state == null ||
            dragging == null ||
            draggingStack.Count != 1 ||
            !dragging.Rank.HasValue ||
            string.IsNullOrWhiteSpace(dragging.Suit))
        {
            return false;
        }

        RemoteCard? foundation =
            index >= 0 && index < state.Foundations.Count
                ? state.Foundations[index]
                : null;

        if (foundation == null)
            return dragging.Rank == 1;

        return foundation.FaceUp &&
               foundation.Suit == dragging.Suit &&
               dragging.Rank == foundation.Rank + 1;
    }

    bool CanMoveToTableau(int index)
    {
        if (state == null ||
            dragging == null ||
            !dragging.Rank.HasValue)
        {
            return false;
        }

        if (index < 0 || index >= state.Tableau.Count)
            return false;

        List<RemoteCard> pile = state.Tableau[index];
        if (pile.Count == 0)
            return dragging.Rank == 13;

        RemoteCard top = pile[^1];
        return top.FaceUp &&
               top.Rank.HasValue &&
               top.IsRed != dragging.IsRed &&
               dragging.Rank == top.Rank - 1;
    }

    RemoteGameAction MoveAction(string targetKind, int targetIndex)
    {
        return new RemoteGameAction
        {
            Type = "move",
            Source = dragSource,
            Target = new RemotePileRef
            {
                Kind = targetKind,
                Index = targetIndex
            }
        };
    }

    void ClearDragging()
    {
        dragging = null;
        draggingStack.Clear();
        dragSource = null;
    }

    static bool Hit(PointF p, float x, float y, float w, float h)
    {
        return p.X >= x && p.X <= x + w && p.Y >= y && p.Y <= y + h;
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

    void DrawMessage(ICanvas canvas, RectF dirtyRect, string message)
    {
        canvas.FontColor = Colors.White.WithAlpha(0.75f);
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.FontSize = 18 * Scale;
        canvas.DrawString(
            message,
            dirtyRect.Left,
            dirtyRect.Center.Y - 18,
            dirtyRect.Width,
            36,
            HorizontalAlignment.Center,
            VerticalAlignment.Center);
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

    void DrawCard(ICanvas canvas, RemoteCard card, float x, float y)
    {
        if (!card.FaceUp || !card.Rank.HasValue)
        {
            DrawCardBack(canvas, x, y);
            return;
        }

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

    readonly record struct HitCard(
        RemoteCard Card,
        string Kind,
        int Index,
        int? Row,
        float X,
        float Y);
}
