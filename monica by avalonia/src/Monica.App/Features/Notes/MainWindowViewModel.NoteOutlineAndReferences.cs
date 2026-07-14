using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Services;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static IReadOnlyList<NoteOutlineItem> BuildNoteOutlineItems(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var items = new List<NoteOutlineItem>();
        var inCodeFence = false;
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCodeFence = !inCodeFence;
                continue;
            }

            if (inCodeFence)
            {
                continue;
            }

            var match = HeadingRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }
            var level = match.Groups[1].Value.Length;
            var title = match.Groups[2].Value.Trim();
            if (title.Length == 0)
            {
                continue;
            }

            items.Add(new NoteOutlineItem(
                level,
                title,
                index + 1,
                new Thickness(Math.Min(level - 1, 5) * 12, 0, 0, 0)));
        }

        return items;
    }

    private IReadOnlyList<NoteReferenceItem> BuildNoteReferenceItems(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var items = new List<NoteReferenceItem>();
        var inCodeFence = false;
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCodeFence = !inCodeFence;
                continue;
            }

            if (inCodeFence)
            {
                continue;
            }

            var markdownLinkRanges = new List<(int Start, int End)>();
            foreach (Match match in MarkdownLinkRegex().Matches(line))
            {
                var isImage = match.Groups[1].Value == "!";
                var label = match.Groups[2].Value.Trim();
                var target = match.Groups[3].Value.Trim();
                if (string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }

                markdownLinkRanges.Add((match.Index, match.Index + match.Length));
                items.Add(new NoteReferenceItem(
                    string.IsNullOrWhiteSpace(label) ? (isImage ? _localization.Get("NoteImage") : target) : label,
                    target,
                    index + 1,
                    isImage));
            }

            foreach (Match match in BareUrlRegex().Matches(line))
            {
                var start = match.Index;
                if (markdownLinkRanges.Any(range => start >= range.Start && start < range.End))
                {
                    continue;
                }

                var target = match.Value.TrimEnd('.', ',', ';', ':');
                items.Add(new NoteReferenceItem(target, target, index + 1, IsImageUrl(target)));
            }
        }

        return items
            .DistinctBy(item => (item.Target, item.LineNumber))
            .ToArray();
    }

    private static bool IsImageUrl(string target) =>
        target.StartsWith("monica-image://", StringComparison.OrdinalIgnoreCase) ||
        target.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
        target.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
        target.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
        target.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
        target.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

    private static bool TryCreateExternalReferenceUri(string? target, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(target) ||
            !Uri.TryCreate(target.Trim(), UriKind.Absolute, out var candidate) ||
            candidate.Scheme is not ("http" or "https"))
        {
            return false;
        }

        uri = candidate;
        return true;
    }
    [GeneratedRegex("^\\s{0,3}(#{1,6})\\s+(.+?)\\s*#*\\s*$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex("(!?)\\[([^\\]]*)\\]\\(([^\\)\\s]+)\\)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex("https?://[^\\s<>()]+")]
    private static partial Regex BareUrlRegex();

    private static string InferImageContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

}
