using System.Globalization;
using System.Text;

const float IceHalf = 5.4f;
const float BrushHalf = 5.8f;
const float PlazaPeak = 9.2f;
const float AlongMin = -56f;
const float AlongMax = 56f;
const float Step = 1f;

var brushes = new (float min, float max)[]
{
    (-40f, -29f), (-20f, -9f), (1f, 12f), (21f, 32f)
};

var left = new List<(float along, float across)>();
var right = new List<(float along, float across)>();
for (float along = AlongMin; along <= AlongMax + 1e-4f; along += Step)
{
    float half = HalfWidth(along);
    left.Add((along, -half));
    right.Add((along, half));
}

var poly = new List<(float along, float across)>(left.Count + right.Count);
poly.AddRange(left);
for (int i = right.Count - 1; i >= 0; i--)
    poly.Add(right[i]);

if (!PointIn(poly, -50f, 0f) || !PointIn(poly, 50f, 0f) || !PointIn(poly, 0f, 0f))
    throw new InvalidOperationException("Generated boundary excludes spawn or mid lane");

var sb = new StringBuilder();
sb.Append("{\n  \"MapId\": \"HowlingAbyss\",\n  \"UseUvMapBounds\": false,\n  \"Blockers\": [],\n  \"WalkBoundary\": [\n");
for (int i = 0; i < poly.Count; i++)
{
    var p = poly[i];
    sb.Append("    {\n      \"Along\": ");
    sb.Append(p.along.ToString("0.###", CultureInfo.InvariantCulture));
    sb.Append(",\n      \"Across\": ");
    sb.Append(p.across.ToString("0.###", CultureInfo.InvariantCulture));
    sb.Append("\n    }");
    if (i + 1 < poly.Count) sb.Append(',');
    sb.Append('\n');
}
sb.Append("  ]\n}\n");
string json = sb.ToString();

string repo = FindRepo();
string[] dest =
{
    Path.Combine(repo, "Config", "Maps", "Map_HowlingAbyss_Collision.json"),
    Path.Combine(repo, "Client", "Assets", "Game", "Configs", "Maps", "Map_HowlingAbyss_Collision.json"),
    Path.Combine(repo, "Server", "Config", "Maps", "Map_HowlingAbyss_Collision.json")
};
foreach (var p in dest)
    File.WriteAllText(p, json);
Console.WriteLine($"walk boundary verts={poly.Count} plazaHalf={HalfWidth(-50f):0.00} midHalf={HalfWidth(0f):0.00}");

float HalfWidth(float along)
{
    float a = MathF.Abs(along);
    float plaza = 0f;
    if (a >= 42f && a <= 56.01f)
    {
        if (a < 50f)
            plaza = 5.4f + (PlazaPeak - 5.4f) * (a - 42f) / 8f;
        else
            plaza = PlazaPeak + (6.4f - PlazaPeak) * (a - 50f) / 6f;
    }

    float brush = 0f;
    for (int i = 0; i < brushes.Length; i++)
    {
        if (along >= brushes[i].min && along <= brushes[i].max)
            brush = BrushHalf;
    }

    return MathF.Max(IceHalf, MathF.Max(plaza, brush));
}

static bool PointIn(List<(float along, float across)> poly, float along, float across)
{
    bool inside = false;
    int n = poly.Count;
    var last = poly[n - 1];
    for (int i = 0; i < n; i++)
    {
        var cur = poly[i];
        bool yCross = (last.across > across) != (cur.across > across);
        if (yCross)
        {
            float xAt = last.along + (across - last.across) * (cur.along - last.along) / (cur.across - last.across);
            if (along < xAt)
                inside = !inside;
        }

        last = cur;
    }

    return inside;
}

static string FindRepo()
{
    string dir = AppContext.BaseDirectory;
    for (int i = 0; i < 8; i++)
    {
        if (File.Exists(Path.Combine(dir, "Config", "Maps", "Map_HowlingAbyss_Collision.json")))
            return dir;
        dir = Path.GetFullPath(Path.Combine(dir, ".."));
    }

    throw new DirectoryNotFoundException("Cannot find repo root");
}
