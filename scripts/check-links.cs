// Checks the relative links and heading anchors in the repository's Markdown files. External links are not
// checked, so the result doesn't depend on the network. Without arguments it checks every tracked Markdown file.
// Usage: dotnet run scripts/check-links.cs [file.md ...]

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

var root = Git("rev-parse", "--show-toplevel").Trim();
Directory.SetCurrentDirectory(root);

var files = args.Length > 0
    ? args
    : Git("ls-files", "--cached", "--others", "--exclude-standard", "*.md")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Where(File.Exists)
        .ToArray();

var anchors = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
var errors = new List<string>();

foreach (var file in files)
{
    var lines = File.ReadAllLines(file);
    var inFence = false;
    for (var i = 0; i < lines.Length; i++)
    {
        if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            inFence = !inFence;
            continue;
        }

        if (inFence)
        {
            continue;
        }

        var text = Regexes.InlineCode().Replace(lines[i], string.Empty);
        foreach (Match match in Regexes.Link().Matches(text))
        {
            var error = Check(file, match.Groups["target"].Value);
            if (error is not null)
            {
                errors.Add($"{file}:{i + 1}: {error}");
            }
        }
    }
}

foreach (var error in errors)
{
    Console.Error.WriteLine(error);
}

Console.WriteLine($"Checked {files.Length} Markdown files: {errors.Count} broken links.");
return errors.Count == 0 ? 0 : 1;

string? Check(string file, string target)
{
    target = target.Trim('<', '>');
    if (Regexes.External().IsMatch(target))
    {
        return null;
    }

    var hash = target.IndexOf('#', StringComparison.Ordinal);
    var path = Uri.UnescapeDataString(hash < 0 ? target : target[..hash]);
    var anchor = hash < 0 ? null : target[(hash + 1)..];

    var resolved = path.Length == 0
        ? file
        : Path.GetRelativePath(root, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file) ?? string.Empty, path)));

    if (!File.Exists(resolved) && !Directory.Exists(resolved))
    {
        return $"'{target}' points to a missing file";
    }

    if (string.IsNullOrEmpty(anchor) || !resolved.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    return AnchorsOf(resolved).Contains(anchor) ? null : $"'{target}' points to a missing heading";
}

HashSet<string> AnchorsOf(string file)
{
    if (anchors.TryGetValue(file, out var known))
    {
        return known;
    }

    // GitHub's heading IDs: lower case, punctuation removed, spaces as dashes, and -1, -2 for repeats
    var result = new HashSet<string>(StringComparer.Ordinal);
    var inFence = false;
    foreach (var line in File.ReadLines(file))
    {
        if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            inFence = !inFence;
        }

        var heading = inFence ? null : Regexes.Heading().Match(line);
        if (heading is not { Success: true })
        {
            continue;
        }

        var slug = new StringBuilder();
        foreach (var c in heading.Groups["text"].Value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_')
            {
                slug.Append(c);
            }
            else if (c == ' ')
            {
                slug.Append('-');
            }
        }

        var id = slug.ToString();
        for (var n = 1; !result.Add(id); n++)
        {
            id = $"{slug}-{n}";
        }
    }

    anchors[file] = result;
    return result;
}

static string Git(params string[] arguments)
{
    var start = new ProcessStartInfo("git") { RedirectStandardOutput = true };
    foreach (var argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }

    using var process = Process.Start(start) ?? throw new InvalidOperationException("git could not be started.");
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return output;
}

static partial class Regexes
{
    [GeneratedRegex(@"`[^`]*`")]
    public static partial Regex InlineCode();

    // [text](target) and ![alt](target), with an optional "title"
    [GeneratedRegex(@"\]\((?<target><[^>]+>|[^)\s]+)(?:\s+""[^""]*"")?\)")]
    public static partial Regex Link();

    [GeneratedRegex(@"^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase)]
    public static partial Regex External();

    [GeneratedRegex(@"^#{1,6}\s+(?<text>.+?)\s*#*\s*$")]
    public static partial Regex Heading();
}
