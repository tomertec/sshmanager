namespace SshManager.App.Services;

/// <summary>
/// Provides fuzzy string matching for search functionality.
/// </summary>
public static class FuzzyMatcher
{
    /// <summary>
    /// Performs fuzzy matching of a pattern against text.
    /// </summary>
    /// <param name="pattern">The search pattern.</param>
    /// <param name="text">The text to match against.</param>
    /// <returns>A tuple containing whether it matched, the score, and matched character indices.</returns>
    public static (bool IsMatch, int Score, List<int> MatchedIndices) Match(string pattern, string text)
    {
        if (string.IsNullOrEmpty(pattern))
            return (true, 0, new List<int>());
            
        if (string.IsNullOrEmpty(text))
            return (false, 0, new List<int>());
        
        var indices = new List<int>();
        var patternIndex = 0;
        var score = 0;
        var consecutiveBonus = 0;
        var lastMatchIndex = -1;
        
        for (int i = 0; i < text.Length && patternIndex < pattern.Length; i++)
        {
            if (char.ToLowerInvariant(text[i]) == char.ToLowerInvariant(pattern[patternIndex]))
            {
                indices.Add(i);
                
                // Score based on position
                if (i == 0)
                {
                    // First character bonus
                    score += 15;
                }
                else if (!char.IsLetterOrDigit(text[i - 1]))
                {
                    // Word boundary bonus (after space, dash, underscore, etc.)
                    score += 10;
                }
                else
                {
                    // Regular match
                    score += 1;
                }
                
                // Consecutive character bonus
                if (lastMatchIndex == i - 1)
                {
                    consecutiveBonus++;
                    score += consecutiveBonus * 2;
                }
                else
                {
                    consecutiveBonus = 0;
                }
                
                // Case match bonus
                if (text[i] == pattern[patternIndex])
                {
                    score += 1;
                }
                
                lastMatchIndex = i;
                patternIndex++;
            }
        }
        
        var isMatch = patternIndex == pattern.Length;
        
        // Bonus for exact match
        if (isMatch && text.Length == pattern.Length)
        {
            score += 50;
        }
        
        // Bonus for shorter matches (tighter match)
        if (isMatch && indices.Count > 0)
        {
            var matchSpan = indices[^1] - indices[0] + 1;
            if (matchSpan == pattern.Length)
            {
                // All characters are consecutive
                score += 20;
            }
        }
        
        return (isMatch, score, indices);
    }
    
    /// <summary>
    /// Performs multi-field fuzzy matching and returns the best score.
    /// </summary>
    /// <param name="pattern">The search pattern.</param>
    /// <param name="fields">Fields to match against with their weight multipliers.</param>
    /// <returns>The best match result across all fields.</returns>
    public static (bool IsMatch, int Score, string MatchedField, List<int> MatchedIndices) MatchMultiple(
        string pattern, 
        IEnumerable<(string Text, int Weight)> fields)
    {
        var bestMatch = (IsMatch: false, Score: 0, MatchedField: "", MatchedIndices: new List<int>());
        
        foreach (var (text, weight) in fields)
        {
            if (string.IsNullOrEmpty(text)) continue;
            
            var result = Match(pattern, text);
            var weightedScore = result.Score * weight;
            
            if (result.IsMatch && weightedScore > bestMatch.Score)
            {
                bestMatch = (true, weightedScore, text, result.MatchedIndices);
            }
        }
        
        return bestMatch;
    }
}
