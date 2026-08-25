using Microsoft.Maui.Graphics;

namespace SolitaireNET;

public class DominoGame : IDrawable
{
    readonly Random rng = new();

    public List<DominoPlayer> Players { get; } =
        Enumerable.Range(0, 4).Select(i => new DominoPlayer(i)).ToList();

    public List<DominoTile> Board { get; } = new();
    public List<DominoTile> SleepingTiles { get; } = new();

    public int CurrentPlayer { get; private set; }
    public int? LeftEnd { get; private set; }
    public int? RightEnd { get; private set; }

    public bool GameOver { get; private set; }
    public string Message { get; private set; } = "";

    float width;
    float height;

    public void NewGame()
    {
        Board.Clear();
        SleepingTiles.Clear();
        GameOver = false;
        Message = "";

        foreach (var p in Players)
            p.Hand.Clear();

        var deck = new List<DominoTile>();

        for (int a = 0; a <= 6; a++)
            for (int b = a; b <= 6; b++)
                deck.Add(new DominoTile(a, b));

        deck = deck.OrderBy(_ => rng.Next()).ToList();

        for (int i = 0; i < 24; i++)
            Players[i % 4].Hand.Add(deck[i]);

        SleepingTiles.AddRange(deck.Skip(24));

        CurrentPlayer = FindStartingPlayer();
        LeftEnd = null;
        RightEnd = null;

        Message = $"{Players[CurrentPlayer].Name} começa.";
    }

    int FindStartingPlayer()
    {
        for (int value = 6; value >= 0; value--)
        {
            int p = Players.FindIndex(x => x.Hand.Any(t => t.A == value && t.B == value));
            if (p >= 0) return p;
        }

        return Players
            .OrderByDescending(p => p.Hand.Max(t => t.Sum))
            .First()
            .Index;
    }

    public bool PlayTile(int tileIndex, bool playLeft)
    {
        if (GameOver)
            return false;

        var player = Players[CurrentPlayer];

        if (tileIndex < 0 || tileIndex >= player.Hand.Count)
            return false;

        var tile = player.Hand[tileIndex];

        if (Board.Count == 0)
        {
            Board.Add(tile);
            LeftEnd = tile.A;
            RightEnd = tile.B;
            player.Hand.RemoveAt(tileIndex);
            AfterMove();
            return true;
        }

        if (playLeft)
        {
            if (!tile.Matches(LeftEnd!.Value))
                return false;

            if (tile.B != LeftEnd.Value)
                tile.Flip();

            Board.Insert(0, tile);
            LeftEnd = tile.A;
            player.Hand.RemoveAt(tileIndex);
            AfterMove();
            return true;
        }
        else
        {
            if (!tile.Matches(RightEnd!.Value))
                return false;

            if (tile.A != RightEnd.Value)
                tile.Flip();

            Board.Add(tile);
            RightEnd = tile.B;
            player.Hand.RemoveAt(tileIndex);
            AfterMove();
            return true;
        }
    }

    public bool CurrentPlayerCanPlay()
    {
        return Players[CurrentPlayer].Hand.Any(CanPlay);
    }

    public bool CanPlay(DominoTile tile)
    {
        if (Board.Count == 0)
            return true;

        return tile.Matches(LeftEnd!.Value) || tile.Matches(RightEnd!.Value);
    }

    public void PassTurn()
    {
        if (GameOver)
            return;

        Message = $"{Players[CurrentPlayer].Name} passou.";
        NextPlayer();
    }

    void AfterMove()
    {
        var player = Players[CurrentPlayer];

        if (player.Hand.Count == 0)
        {
            GameOver = true;
            Message = $"{player.Name} bateu! Dupla {player.Team + 1} venceu.";
            return;
        }

        NextPlayer();
    }

    void NextPlayer()
    {
        int passes = 0;

        do
        {
            CurrentPlayer = (CurrentPlayer + 1) % 4;
            passes++;
        }
        while (passes < 4 && !CurrentPlayerCanPlay());

        if (passes >= 4 && !CurrentPlayerCanPlay())
        {
            GameOver = true;
            Message = "Jogo fechado. Ninguém tem peça.";
            return;
        }

        Message = $"Vez de {Players[CurrentPlayer].Name}";
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        width = dirtyRect.Width;
        height = dirtyRect.Height;

        canvas.FillColor = Color.FromArgb("#0B6B3A");
        canvas.FillRectangle(dirtyRect);

        DrawTable(canvas);
        DrawBoard(canvas);
        DrawPlayersTopView(canvas);
        DrawCurrentHandTopView(canvas);
        DrawMessage(canvas);
    }

    void DrawTable(ICanvas canvas)
    {
        float tableW = width * 0.82f;
        float tableH = height * 0.48f;
        float x = (width - tableW) / 2;
        float y = height * 0.24f;

        canvas.FillColor = Color.FromArgb("#07582F");
        canvas.StrokeColor = Colors.White.WithAlpha(0.20f);
        canvas.StrokeSize = 3;
        canvas.FillRoundedRectangle(x, y, tableW, tableH, 28);
        canvas.DrawRoundedRectangle(x, y, tableW, tableH, 28);
    }

    void DrawPlayersTopView(ICanvas canvas)
    {
        DrawPlayerSeat(canvas, 2, width / 2 - 60, 70, 120, 42);
        DrawPlayerSeat(canvas, 3, 12, height / 2 - 24, 120, 42);
        DrawPlayerSeat(canvas, 1, width - 132, height / 2 - 24, 120, 42);
        DrawPlayerSeat(canvas, 0, width / 2 - 60, height - 132, 120, 42);
    }

    void DrawPlayerSeat(ICanvas canvas, int playerIndex, float x, float y, float w, float h)
    {
        var p = Players[playerIndex];
        bool current = playerIndex == CurrentPlayer;

        canvas.FillColor = current ? Color.FromArgb("#FFD966") : Color.FromArgb("#AA000000");
        canvas.StrokeColor = current ? Colors.White : Colors.White.WithAlpha(0.30f);
        canvas.StrokeSize = current ? 2.5f : 1.5f;

        canvas.FillRoundedRectangle(x, y, w, h, 12);
        canvas.DrawRoundedRectangle(x, y, w, h, 12);

        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.FontSize = 13;
        canvas.FontColor = current ? Colors.Black : Colors.White;

        string team = p.Team == 0 ? "Dupla A" : "Dupla B";

        canvas.DrawString(
            $"{p.Name}",
            x,
            y + 4,
            w,
            18,
            HorizontalAlignment.Center,
            VerticalAlignment.Center);

        canvas.FontSize = 11;
        canvas.DrawString(
            $"{team} • {p.Hand.Count} peças",
            x,
            y + 21,
            w,
            16,
            HorizontalAlignment.Center,
            VerticalAlignment.Center);
    }

    void DrawBoard(ICanvas canvas)
    {
        if (Board.Count == 0)
        {
            canvas.FontColor = Colors.White.WithAlpha(0.7f);
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.FontSize = 14;

            canvas.DrawString(
                "Mesa vazia",
                0,
                height * 0.46f,
                width,
                32,
                HorizontalAlignment.Center,
                VerticalAlignment.Center);

            return;
        }

        float tileW = 40;
        float tileH = 24;
        float gap = 3;

        float tableX = width * 0.14f;
        float tableY = height * 0.28f;
        float tableW = width * 0.72f;
        float tableH = height * 0.34f;

        int perRow = Math.Max(5, (int)(tableW / (tileW + gap)));

        float startY = tableY + tableH / 2 - tileH / 2;

        for (int i = 0; i < Board.Count; i++)
        {
            int row = i / perRow;
            int col = i % perRow;

            bool reverse = row % 2 == 1;

            int itemsInRow = Math.Min(perRow, Board.Count - row * perRow);
            float rowWidth = itemsInRow * tileW + Math.Max(0, itemsInRow - 1) * gap;

            float startX = width / 2 - rowWidth / 2;

            float x = reverse
                ? startX + (itemsInRow - 1 - col) * (tileW + gap)
                : startX + col * (tileW + gap);

            float y = startY + row * (tileH + 8);

            DrawTile(canvas, Board[i], x, y, tileW, tileH, false);
        }
    }

    void DrawCurrentHandTopView(ICanvas canvas)
    {
        var hand = Players[CurrentPlayer].Hand;

        float tileW = 54;
        float tileH = 34;
        float gap = 6;

        float total = hand.Count * tileW + Math.Max(0, hand.Count - 1) * gap;
        float startX = Math.Max(8, (width - total) / 2);
        float y = height - tileH - 74;

        for (int i = 0; i < hand.Count; i++)
        {
            bool playable = CanPlay(hand[i]);
            DrawTile(canvas, hand[i], startX + i * (tileW + gap), y, tileW, tileH, playable);
        }
    }

    void DrawMessage(ICanvas canvas)
    {
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.FontColor = Colors.White;
        canvas.FontSize = 13;

        canvas.DrawString(
            Message,
            8,
            height - 58,
            width - 16,
            22,
            HorizontalAlignment.Center,
            VerticalAlignment.Center);

        canvas.FontSize = 11;
        canvas.FontColor = Colors.White.WithAlpha(0.75f);

        canvas.DrawString(
            $"Dormindo: {SleepingTiles.Count} peças",
            8,
            height - 38,
            width - 16,
            20,
            HorizontalAlignment.Center,
            VerticalAlignment.Center);
    }

    void DrawTile(ICanvas canvas, DominoTile tile, float x, float y, float w, float h, bool highlight)
    {
        canvas.FillColor = highlight
            ? Color.FromArgb("#FFF6B3")
            : Color.FromArgb("#F7F4EA");

        canvas.StrokeColor = Colors.Black;
        canvas.StrokeSize = 1.4f;

        canvas.FillRoundedRectangle(x, y, w, h, 5);
        canvas.DrawRoundedRectangle(x, y, w, h, 5);

        bool horizontal = w > h;

        if (horizontal)
        {
            canvas.DrawLine(x + w / 2, y + 4, x + w / 2, y + h - 4);

            DrawPips(canvas, tile.A, x, y, w / 2, h);
            DrawPips(canvas, tile.B, x + w / 2, y, w / 2, h);
        }
        else
        {
            canvas.DrawLine(x + 4, y + h / 2, x + w - 4, y + h / 2);

            DrawPips(canvas, tile.A, x, y, w, h / 2);
            DrawPips(canvas, tile.B, x, y + h / 2, w, h / 2);
        }
    }

    void DrawPips(ICanvas canvas, int value, float x, float y, float w, float h)
    {
        float r = Math.Max(1.4f, Math.Min(w, h) * 0.085f);

        float left = x + w * 0.27f;
        float center = x + w * 0.50f;
        float right = x + w * 0.73f;

        float top = y + h * 0.27f;
        float middle = y + h * 0.50f;
        float bottom = y + h * 0.73f;

        void Dot(float px, float py)
        {
            canvas.FillColor = Colors.Black;
            canvas.FillCircle(px, py, r);
        }

        switch (value)
        {
            case 1:
                Dot(center, middle);
                break;

            case 2:
                Dot(left, top);
                Dot(right, bottom);
                break;

            case 3:
                Dot(left, top);
                Dot(center, middle);
                Dot(right, bottom);
                break;

            case 4:
                Dot(left, top);
                Dot(right, top);
                Dot(left, bottom);
                Dot(right, bottom);
                break;

            case 5:
                Dot(left, top);
                Dot(right, top);
                Dot(center, middle);
                Dot(left, bottom);
                Dot(right, bottom);
                break;

            case 6:
                Dot(left, top);
                Dot(right, top);
                Dot(left, middle);
                Dot(right, middle);
                Dot(left, bottom);
                Dot(right, bottom);
                break;
        }
    }


    public int HitHandTile(PointF p)
    {
        var hand = Players[CurrentPlayer].Hand;

        float tileW = 48;
        float tileH = 28;
        float gap = 6;

        float total = hand.Count * tileW + Math.Max(0, hand.Count - 1) * gap;
        float startX = Math.Max(8, (width - total) / 2);
        float y = height - tileH - 74;

        for (int i = 0; i < hand.Count; i++)
        {
            float x = startX + i * (tileW + gap);

            if (p.X >= x && p.X <= x + tileW && p.Y >= y && p.Y <= y + tileH)
                return i;
        }

        return -1;
    }


}