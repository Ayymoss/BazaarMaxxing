using BazaarCompanionWeb.Models.Api.Items;
using BazaarCompanionWeb.Models.Pagination;

namespace BazaarCompanionWeb.Services;

public class SearchService
{
    /// <summary>
    /// Calculates Levenshtein distance between two strings for fuzzy matching
    /// </summary>
    public static int LevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s))
            return string.IsNullOrEmpty(t) ? 0 : t.Length;
        if (string.IsNullOrEmpty(t))
            return s.Length;

        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }

    /// <summary>
    /// Checks if search query matches product name with typo tolerance
    /// </summary>
    public static bool FuzzyMatch(string productName, string searchQuery, int maxDistance = 2)
    {
        if (string.IsNullOrWhiteSpace(searchQuery) || string.IsNullOrWhiteSpace(productName))
            return false;

        var searchLower = searchQuery.ToLowerInvariant();
        var productLower = productName.ToLowerInvariant();

        // Exact match
        if (productLower.Contains(searchLower))
            return true;

        // Fuzzy match for words
        var searchWords = searchLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var productWords = productLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var searchWord in searchWords)
        {
            bool foundMatch = false;
            foreach (var productWord in productWords)
            {
                if (productWord.Contains(searchWord))
                {
                    foundMatch = true;
                    break;
                }

                var distance = LevenshteinDistance(searchWord, productWord);
                if (distance <= maxDistance && Math.Max(searchWord.Length, productWord.Length) > 0)
                {
                    var similarity = 1.0 - (double)distance / Math.Max(searchWord.Length, productWord.Length);
                    if (similarity >= 0.7) // 70% similarity threshold
                    {
                        foundMatch = true;
                        break;
                    }
                }
            }

            if (!foundMatch)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Parses natural language search query and extracts filter criteria
    /// </summary>
    public static ParsedSearchQuery ParseNaturalLanguage(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new ParsedSearchQuery { OriginalQuery = query };

        var lowerQuery = query.ToLowerInvariant();
        var parsed = new ParsedSearchQuery { OriginalQuery = query };

        // Price range patterns - parse using regex
        var underMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"under\s+(\d+)");
        if (underMatch.Success && double.TryParse(underMatch.Groups[1].Value, out var underVal))
            parsed.MaxPrice = underVal;

        var belowMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"below\s+(\d+)");
        if (belowMatch.Success && double.TryParse(belowMatch.Groups[1].Value, out var belowVal))
            parsed.MaxPrice = belowVal;

        var lessThanMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"less\s+than\s+(\d+)");
        if (lessThanMatch.Success && double.TryParse(lessThanMatch.Groups[1].Value, out var lessVal))
            parsed.MaxPrice = lessVal;

        var overMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"over\s+(\d+)");
        if (overMatch.Success && double.TryParse(overMatch.Groups[1].Value, out var overVal))
            parsed.MinPrice = overVal;

        var aboveMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"above\s+(\d+)");
        if (aboveMatch.Success && double.TryParse(aboveMatch.Groups[1].Value, out var aboveVal))
            parsed.MinPrice = aboveVal;

        var moreThanMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"more\s+than\s+(\d+)");
        if (moreThanMatch.Success && double.TryParse(moreThanMatch.Groups[1].Value, out var moreVal))
            parsed.MinPrice = moreVal;

        var betweenMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"between\s+(\d+)\s+and\s+(\d+)");
        if (betweenMatch.Success && 
            double.TryParse(betweenMatch.Groups[1].Value, out var minVal) &&
            double.TryParse(betweenMatch.Groups[2].Value, out var maxVal))
        {
            parsed.MinPrice = minVal;
            parsed.MaxPrice = maxVal;
        }

        // Volume patterns
        if (lowerQuery.Contains("high volume") || lowerQuery.Contains("high vol"))
            parsed.VolumeTier = VolumeTierFilter.High;
        else if (lowerQuery.Contains("low volume") || lowerQuery.Contains("low vol"))
            parsed.VolumeTier = VolumeTierFilter.Low;
        else if (lowerQuery.Contains("medium volume") || lowerQuery.Contains("medium vol"))
            parsed.VolumeTier = VolumeTierFilter.Medium;

        // Spread patterns
        if (lowerQuery.Contains("low spread") || lowerQuery.Contains("tight spread"))
            parsed.MaxSpread = 100; // Arbitrary low threshold
        else if (lowerQuery.Contains("high spread") || lowerQuery.Contains("wide spread"))
            parsed.MinSpread = 500; // Arbitrary high threshold

        // Fire sale / manipulation
        if (lowerQuery.Contains("fire sale") || lowerQuery.Contains("firesale") || lowerQuery.Contains("manipulated"))
            parsed.ManipulationStatus = ManipulationFilter.Manipulated;

        // Tier patterns
        var tierMap = new Dictionary<string, ItemTier>
        {
            { "common", ItemTier.Common },
            { "uncommon", ItemTier.Uncommon },
            { "rare", ItemTier.Rare },
            { "epic", ItemTier.Epic },
            { "legendary", ItemTier.Legendary },
            { "mythic", ItemTier.Mythic },
            { "supreme", ItemTier.Supreme },
            { "special", ItemTier.Special },
            { "very special", ItemTier.VerySpecial },
            { "unobtainable", ItemTier.Unobtainable }
        };

        foreach (var (key, tier) in tierMap)
        {
            if (lowerQuery.Contains($"tier:{key}") || lowerQuery.Contains($"tier {key}"))
            {
                parsed.SelectedTiers.Add(tier);
            }
        }

        // Extract remaining text as product name search
        var nameQuery = query;
        // Remove parsed patterns to get clean product name
        foreach (var pattern in new[] { "under", "over", "high volume", "low volume", "fire sale", "tier:" })
        {
            nameQuery = System.Text.RegularExpressions.Regex.Replace(
                nameQuery, pattern, "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        parsed.ProductNameQuery = nameQuery.Trim();

        return parsed;
    }
}

public class ParsedSearchQuery
{
    public string OriginalQuery { get; set; } = string.Empty;
    public string ProductNameQuery { get; set; } = string.Empty;
    public double? MinPrice { get; set; }
    public double? MaxPrice { get; set; }
    public VolumeTierFilter VolumeTier { get; set; } = VolumeTierFilter.All;
    public double? MinSpread { get; set; }
    public double? MaxSpread { get; set; }
    public ManipulationFilter ManipulationStatus { get; set; } = ManipulationFilter.All;
    public List<ItemTier> SelectedTiers { get; set; } = new();
}
