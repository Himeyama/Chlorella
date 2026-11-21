namespace Chlorella;

sealed partial class WorksheetEngine
{
    static string NormalizeFullWidthDigits(string input)
    {
        if (input.Length == 0 || input.Any(c => c is < '０' or > '９')) return input;
        return new string(input.Select(c => (char)('0' + c - '０')).ToArray());
    }

    static string CompleteFormulaParentheses(string input)
    {
        if (!input.StartsWith('=')) return input;
        int depth = 0;
        bool quoted = false;
        for (int i = 1; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '"')
            {
                if (quoted && i + 1 < input.Length && input[i + 1] == '"') { i++; continue; }
                quoted = !quoted;
            }
            else if (!quoted && c == '(') depth++;
            else if (!quoted && c == ')' && --depth < 0) return input;
        }
        return quoted || depth == 0 ? input : input + new string(')', depth);
    }
}
