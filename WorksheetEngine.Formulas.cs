using System.Globalization;
using System.Text.RegularExpressions;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    sealed class FormulaException(string error) : Exception(error);

    (string Text, bool Numeric) DisplayCell(Sheet sheet, (int Row, int Col) cell)
    {
        string input = sheet.Cells.GetValueOrDefault(cell, "");
        if (!input.StartsWith('=')) return (input, double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out _));
        try
        {
            double number = EvaluateCell(sheet, cell, new HashSet<(int, int)>());
            if (!double.IsFinite(number)) return ("#NUM!", false);
            return (number.ToString("G15", CultureInfo.InvariantCulture), true);
        }
        catch (FormulaException ex)
        {
            return (ex.Message, false);
        }
    }

    double EvaluateCell(Sheet sheet, (int Row, int Col) cell, HashSet<(int, int)> visiting)
    {
        string input = sheet.Cells.GetValueOrDefault(cell, "");
        if (input.Length == 0) return 0;
        if (!input.StartsWith('='))
        {
            if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture,
                out double value) && double.IsFinite(value)) return value;
            throw new FormulaException("#VALUE!");
        }
        if (visiting.Count >= 256 || !visiting.Add(cell)) throw new FormulaException("#REF!");
        try
        {
            return new FormulaParser(this, sheet, input[1..], visiting).Parse();
        }
        finally
        {
            visiting.Remove(cell);
        }
    }

    sealed class FormulaParser(WorksheetEngine engine, Sheet sheet, string text, HashSet<(int, int)> visiting)
    {
        int position;
        void Space()
        {
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
        }
        bool Take(char c)
        {
            Space();
            if (position < text.Length && text[position] == c)
            {
                position++;
                return true;
            }
            return false;
        }
        public double Parse()
        {
            double result = Expression();
            Space();
            if (position != text.Length) throw new FormulaException("#VALUE!");
            return result;
        }
        double Expression()
        {
            double value = Term();
            while (true)
            {
                if (Take('+')) value += Term();
                else if (Take('-')) value -= Term();
                else return value;
            }
        }
        double Term()
        {
            double value = Power();
            while (true)
            {
                if (Take('*')) value *= Power();
                else if (Take('/'))
                {
                    double divisor = Power();
                    if (divisor == 0) throw new FormulaException("#DIV/0!");
                    value /= divisor;
                }
                else return value;
            }
        }
        double Power()
        {
            double value = Unary();
            while (Take('^')) value = Math.Pow(value, Unary());
            return value;
        }
        double Unary()
        {
            if (Take('+')) return Unary();
            if (Take('-')) return -Unary();
            double value = Primary();
            while (Take('%')) value /= 100;
            return value;
        }
        double Primary()
        {
            if (Take('('))
            {
                double result = Expression();
                if (!Take(')')) throw new FormulaException("#VALUE!");
                return result;
            }
            Space();
            if (text[position..].StartsWith("#REF!", StringComparison.OrdinalIgnoreCase)) throw new FormulaException("#REF!");
            Match number = Regex.Match(text[position..], @"^(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[Ee][+-]?[0-9]+)?");
            if (number.Success)
            {
                position += number.Length;
                if (double.TryParse(number.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return value;
                throw new FormulaException("#NUM!");
            }
            Match token = Regex.Match(text[position..], @"^\$?[A-Za-z]+\$?[0-9]*");
            if (!token.Success) throw new FormulaException("#VALUE!");
            position += token.Length;
            if (Take('(')) return Function(token.Value.ToUpperInvariant());
            try
            {
                return engine.EvaluateCell(sheet, ParseCell(token.Value), visiting);
            }
            catch (InvalidOperationException)
            {
                throw new FormulaException("#NAME?");
            }
        }
        double Function(string name)
        {
            if (name is not ("SUM" or "AVERAGE" or "MIN" or "MAX" or "COUNT")) throw new FormulaException("#NAME?");
            List<double> values = [];
            if (!Take(')'))
            {
                do
                {
                    Space();
                    Match range = Regex.Match(text[position..], @"^\$?[A-Za-z]{1,3}\$?[1-9][0-9]*\s*:\s*\$?[A-Za-z]{1,3}\$?[1-9][0-9]*");
                    if (range.Success)
                    {
                        position += range.Length;
                        (int top, int left, int bottom, int right) bounds;
                        try
                        {
                            bounds = ParseRange(range.Value.Replace(" ", ""));
                        }
                        catch (InvalidOperationException)
                        {
                            throw new FormulaException("#REF!");
                        }
                        if ((long)(bounds.bottom - bounds.top + 1) * (
                            bounds.right - bounds.left + 1) > 100_000) throw new FormulaException("#NUM!");
                        for (int r = bounds.top; r <= bounds.bottom; r++) for (int c = bounds.left; c <= bounds.right; c++)
                            {
                                (int, int) key = (r, c);
                                string input = sheet.Cells.GetValueOrDefault(key, "");
                                if (input.Length == 0 || (!input.StartsWith('=') && !double.TryParse(input,
                                    NumberStyles.Float, CultureInfo.InvariantCulture, out _))) continue;
                                values.Add(engine.EvaluateCell(sheet, key, visiting));
                            }
                    }
                    else
                    {
                        Match reference = Regex.Match(text[position..], @"^\$?[A-Za-z]{1,3}\$?[1-9][0-9]*(?=\s*[,\)])");
                        if (reference.Success)
                        {
                            position += reference.Length;
                            (int, int) key;
                            try
                            {
                                key = ParseCell(reference.Value);
                            }
                            catch (InvalidOperationException)
                            {
                                throw new FormulaException("#REF!");
                            }
                            string input = sheet.Cells.GetValueOrDefault(key, "");
                            if (input.StartsWith('=') || double.TryParse(input, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out _)) values.Add(engine.EvaluateCell(sheet, key, visiting));
                        }
                        else values.Add(Expression());
                    }
                }
                while (Take(','));
                if (!Take(')')) throw new FormulaException("#VALUE!");
            }
            return name switch
            {
                "SUM" => values.Sum(),
                "COUNT" => values.Count,
                "AVERAGE" => values.Count == 0 ? throw new FormulaException("#DIV/0!") : values.Average(),
                "MIN" => values.Count == 0 ? 0 : values.Min(),
                "MAX" => values.Count == 0 ? 0 : values.Max(),
                _ => 0
            };
        }
    }
}
