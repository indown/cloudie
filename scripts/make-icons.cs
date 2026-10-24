#:package SkiaSharp@4.153.1
#:package SkiaSharp.NativeAssets.Linux.NoDependencies@4.153.1
// Renders the Cloudie mark — a warm gold cloud with an eighth note cut out of it, on a dark tile — into every
// asset the app ships: the MSIX tile logos, the in-app logo PNG and the multi-size .ico the exe and the
// window use when unpackaged. Also writes Assets/Logo.svg with the same geometry for anyone with a vector tool.
//
//     dotnet run scripts/make-icons.cs            (any OS with the .NET 10 SDK; nothing else to install)
using System.Runtime.InteropServices;
using SkiaSharp;

var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "Cloudie.slnx")))
{
    root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("run from inside the repository");
}
var assets = Path.Combine(root, "src", "Cloudie.App", "Assets");
Directory.CreateDirectory(assets);

// Palette: the app's dark surface and its accent (Styles/Theme.xaml, dark dictionary).
var tileTop = SKColor.Parse("#2C2C34");
var tileBottom = SKColor.Parse("#131316");
var gold = SKColor.Parse("#E9B872");
var hole = SKColor.Parse("#18181C");
const float Design = 256f;

// MSIX / tile assets: full-bleed squares and rectangles (the shell applies its own shape).
Save("Square44x44Logo.png", Render(44, 44, c => Tile(c, 44, 44, rounded: false, markSize: 44)));
Save("StoreLogo.png", Render(50, 50, c => Tile(c, 50, 50, rounded: false, markSize: 50)));
Save("Square150x150Logo.png", Render(150, 150, c => Tile(c, 150, 150, rounded: false, markSize: 150)));
Save("Wide310x150Logo.png", Render(310, 150, c => Tile(c, 310, 150, rounded: false, markSize: 130)));
Save("SplashScreen.png", Render(620, 300, c => Tile(c, 620, 300, rounded: false, markSize: 240)));
// In-app logo (title bar) and the window / exe icon: a rounded tile.
Save("Cloudie.png", Render(256, 256, c => Tile(c, 256, 256, rounded: true, markSize: 256)));
WriteIco(Path.Combine(assets, "Cloudie.ico"), [16, 20, 24, 32, 40, 48, 64, 128, 256]);
File.WriteAllText(Path.Combine(assets, "Logo.svg"), Svg());
Console.WriteLine($"wrote icons to {assets}");

// --preview: a contact sheet of the small sizes blown up 4x (nearest neighbour) on light and dark, for checking
// legibility; written next to the script, not into Assets.
if (args.Contains("--preview"))
{
    int[] small = [16, 20, 24, 32, 48, 64];
    const int cell = 300;
    using var sheet = SKSurface.Create(new SKImageInfo(cell * small.Length, cell * 2));
    var canvas = sheet.Canvas;
    for (var row = 0; row < 2; row++)
    {
        canvas.DrawRect(new SKRect(0, row * cell, cell * small.Length, (row + 1) * cell), new SKPaint { Color = row == 0 ? SKColor.Parse("#F7F7F8") : SKColor.Parse("#202020") });
        for (var i = 0; i < small.Length; i++)
        {
            using var icon = Render(small[i], small[i], c => Tile(c, small[i], small[i], rounded: true, markSize: small[i]));
            var zoom = Math.Min(4, (cell - 40) / small[i]);
            var dest = new SKRect(i * cell + 20, row * cell + 20, i * cell + 20 + small[i] * zoom, row * cell + 20 + small[i] * zoom);
            canvas.DrawImage(icon, dest, new SKSamplingOptions(SKFilterMode.Nearest));
        }
    }
    using var preview = sheet.Snapshot();
    using var data = preview.Encode(SKEncodedImageFormat.Png, 100);
    using var file = File.Create(Path.Combine(root, "scripts", "icon-preview.png"));
    data.SaveTo(file);
    Console.WriteLine("wrote scripts/icon-preview.png");
}

void Tile(SKCanvas canvas, float width, float height, bool rounded, float markSize)
{
    using var background = new SKPaint
    {
        IsAntialias = true,
        Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, height), [tileTop, tileBottom], SKShaderTileMode.Clamp)
    };
    var rect = new SKRect(0, 0, width, height);
    if (rounded) canvas.DrawRoundRect(rect, width * 56f / Design, height * 56f / Design, background);
    else canvas.DrawRect(rect, background);
    // The mark is drawn in its 256-unit design box, centred and scaled to markSize (a little over, so the cloud
    // fills about 70 % of the tile; the smallest icons need it).
    const float Fill = 1.1f;
    canvas.Save();
    canvas.Translate((width - markSize * Fill) / 2, (height - markSize * Fill) / 2);
    canvas.Scale(markSize * Fill / Design);
    Mark(canvas);
    canvas.Restore();
}

void Mark(SKCanvas canvas)
{
    using var cloudPaint = new SKPaint { IsAntialias = true, Color = gold };
    var cloudBuilder = new SKPathBuilder();
    cloudBuilder.FillType = SKPathFillType.Winding;   // overlapping contours merge into one cloud
    cloudBuilder.AddCircle(128, 108, 46);
    cloudBuilder.AddCircle(86, 130, 34);
    cloudBuilder.AddCircle(174, 126, 40);
    cloudBuilder.AddRoundRect(new SKRect(52, 124, 212, 170), 23, 23);
    using var cloud = cloudBuilder.Detach();
    canvas.DrawPath(cloud, cloudPaint);

    using var holePaint = new SKPaint { IsAntialias = true, Color = hole };
    canvas.Save();
    canvas.RotateDegrees(-20, 112, 148);   // the note head leans like a written note
    canvas.DrawOval(new SKRect(111 - 17, 148 - 12, 111 + 17, 148 + 12), holePaint);
    canvas.Restore();
    canvas.DrawRoundRect(new SKRect(123, 92, 132, 148), 2, 2, holePaint);
    var flagBuilder = new SKPathBuilder();
    flagBuilder.MoveTo(132, 92);
    flagBuilder.CubicTo(156, 98, 170, 118, 156, 144);
    flagBuilder.CubicTo(162, 122, 150, 112, 132, 112);
    flagBuilder.Close();
    using var flag = flagBuilder.Detach();
    canvas.DrawPath(flag, holePaint);
}

SKImage Render(int width, int height, Action<SKCanvas> draw)
{
    using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
    surface.Canvas.Clear(SKColors.Transparent);
    draw(surface.Canvas);
    return surface.Snapshot();
}

void Save(string name, SKImage image)
{
    using (image)
    using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
    using (var file = File.Create(Path.Combine(assets, name)))
    {
        data.SaveTo(file);
    }
}

// ICO container: PNG entries for the large sizes, classic 32-bit DIBs (with an empty AND mask) below 64 px,
// which every Windows shell component reads.
void WriteIco(string path, int[] sizes)
{
    var entries = new List<byte[]>();
    foreach (var size in sizes)
    {
        using var image = Render(size, size, c => Tile(c, size, size, rounded: true, markSize: size));
        if (size >= 64)
        {
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            entries.Add(data.ToArray());
            continue;
        }
        var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var pixels = new byte[info.BytesSize];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            using var pixmap = new SKPixmap(info, handle.AddrOfPinnedObject(), info.RowBytes);
            if (!image.ReadPixels(pixmap)) throw new InvalidOperationException("ReadPixels failed");
        }
        finally
        {
            handle.Free();
        }
        var maskRow = (size + 31) / 32 * 4;
        using var dib = new MemoryStream();
        using var w = new BinaryWriter(dib);
        w.Write(40); w.Write(size); w.Write(size * 2); w.Write((short)1); w.Write((short)32); w.Write(0);
        w.Write(size * size * 4 + maskRow * size); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (var y = size - 1; y >= 0; y--) w.Write(pixels, y * info.RowBytes, size * 4);
        w.Write(new byte[maskRow * size]);
        w.Flush();
        entries.Add(dib.ToArray());
    }
    using var file = File.Create(path);
    using var writer = new BinaryWriter(file);
    writer.Write((short)0); writer.Write((short)1); writer.Write((short)sizes.Length);
    var offset = 6 + 16 * sizes.Length;
    for (var i = 0; i < sizes.Length; i++)
    {
        var size = sizes[i];
        writer.Write((byte)(size >= 256 ? 0 : size)); writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)0); writer.Write((byte)0); writer.Write((short)1); writer.Write((short)32);
        writer.Write(entries[i].Length); writer.Write(offset);
        offset += entries[i].Length;
    }
    foreach (var entry in entries) writer.Write(entry);
}

string Svg() => $"""
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">
      <!-- Cloudie mark: gold cloud, eighth note cut out, dark rounded tile. Source of truth is scripts/make-icons.cs. -->
      <defs>
        <linearGradient id="tile" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stop-color="{Hex(tileTop)}"/><stop offset="1" stop-color="{Hex(tileBottom)}"/>
        </linearGradient>
      </defs>
      <rect width="256" height="256" rx="56" fill="url(#tile)"/>
      <g fill="{Hex(gold)}">
        <circle cx="128" cy="108" r="46"/><circle cx="86" cy="130" r="34"/><circle cx="174" cy="126" r="40"/>
        <rect x="52" y="124" width="160" height="46" rx="23"/>
      </g>
      <g fill="{Hex(hole)}">
        <ellipse cx="111" cy="148" rx="17" ry="12" transform="rotate(-20 111 148)"/>
        <rect x="123" y="92" width="9" height="56" rx="2"/>
        <path d="M132 92 C156 98 170 118 156 144 C162 122 150 112 132 112 Z"/>
      </g>
    </svg>
    """;

static string Hex(SKColor c) => $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}";
