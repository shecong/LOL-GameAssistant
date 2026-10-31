namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>Check only the visible mask glyphs. Never read, OCR, save or log password text.</summary>
internal static class TclsPasswordFeedback
{
    internal static bool IsComplete(Bitmap field, int expectedLength)
    {
        if (expectedLength <= 0) return false;
        var visited = new bool[field.Width, field.Height];
        var glyphs = new List<Rectangle>();
        bool Bright(int x, int y)
        {
            var pixel = field.GetPixel(x, y);
            return pixel.R >= 150 && pixel.G >= 150 && pixel.B >= 110;
        }
        for (int y = 0; y < field.Height; y++)
        for (int x = 0; x < field.Width; x++)
        {
            if (visited[x, y] || !Bright(x, y)) continue;
            var pending = new Stack<Point>();pending.Push(new(x, y));visited[x, y] = true;
            int left = x, right = x, top = y, bottom = y, pixels = 0;
            while (pending.TryPop(out var point))
            {
                left = Math.Min(left, point.X);right = Math.Max(right, point.X);
                top = Math.Min(top, point.Y);bottom = Math.Max(bottom, point.Y);pixels++;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = point.X + dx, ny = point.Y + dy;
                    if (nx < 0 || ny < 0 || nx >= field.Width || ny >= field.Height || visited[nx, ny] || !Bright(nx, ny)) continue;
                    visited[nx, ny] = true;pending.Push(new(nx, ny));
                }
            }
            int width = right - left + 1, height = bottom - top + 1;
            // Exclude the caret, field outline and mouse highlight. Masks form a compact row.
            if (pixels >= 4 && width >= 2 && height >= 2 && width <= field.Height * .65 &&
                height <= field.Height * .65 && width <= height * 2 && height <= width * 2 &&
                top > 0 && bottom < field.Height - 1)
                glyphs.Add(Rectangle.FromLTRB(left, top, right + 1, bottom + 1));
        }
        if (glyphs.Count != expectedLength) return false;
        glyphs.Sort((a, b) => a.Left.CompareTo(b.Left));
        return glyphs.All(glyph => Math.Abs(glyph.Top + glyph.Height / 2 - field.Height / 2) <= field.Height * .25) &&
            glyphs.Zip(glyphs.Skip(1)).All(pair => pair.Second.Left > pair.First.Right &&
                pair.Second.Left - pair.First.Right <= field.Height);
    }
}
