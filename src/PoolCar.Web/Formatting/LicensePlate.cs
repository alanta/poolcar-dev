using System.Text;

namespace PoolCar.Web.Formatting;

public static class LicensePlate
{
    public static string Format(string plate)
    {
        if (string.IsNullOrEmpty(plate))
        {
            return plate;
        }

        var groups = new List<string>();
        var current = new StringBuilder();
        foreach (var c in plate)
        {
            if (current.Length > 0 && char.IsDigit(c) != char.IsDigit(current[^1]))
            {
                groups.Add(current.ToString());
                current.Clear();
            }

            current.Append(c);
        }

        groups.Add(current.ToString());

        if (groups.Count == 2 && groups.Exists(g => g.Length == 4))
        {
            groups = groups.SelectMany(g => g.Length == 4 ? [g[..2], g[2..]] : new[] { g }).ToList();
        }

        return string.Join('-', groups);
    }
}
