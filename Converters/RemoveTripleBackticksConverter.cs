using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;

namespace AIBridge.Converters
{
    public class RemoveTripleBackticksConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                // 1. Pulizia "Markdown" iniziale se presente
                if (text.StartsWith("Markdown", StringComparison.OrdinalIgnoreCase))
                {
                    text = text.Substring("Markdown".Length).TrimStart();
                }

                // 2. Rimuoviamo i tre backtick
                text = text.Replace("```", "");

                // FIX DEFINITIVO PER LISTE E A CAPO:
                // Usiamo una Lookbehind e sostituzione che preserva la struttura.
                // Sostituiamo il marcatore di lista (* o -) seguito da spazio, 
                // lasciando intatto tutto il resto della stringa.
                //text = Regex.Replace(text, @"(?m)^(?<indent>\s*)([\*\-])(\s+)", "${indent}• ");                
                // Modifichiamo la Regex affinché agisca SOLTANTO se la riga inizia con un marcatore di lista.
                // L'uso di Environment.NewLine assicura che il carattere di a capo sia quello corretto per Windows.
                text = Regex.Replace(text, @"(?m)^(?<indent>\s*)([\*\-])(\s+)", Environment.NewLine + "${indent}• ");


                // 4. Conversione dei titoli in Unicode Bold Serif (già funzionante)               
                text = Regex.Replace(text, @"(?m)^#{1,6}\s+(.+)$", m =>
                {
                    string plainText = m.Groups[1].Value;
                    return ToUnicodeBold(plainText);
                });

                // Invece di .Trim(), facciamo solo una pulizia degli spazi bianchi in eccesso agli estremi 
                // che non intacchi i ritorni a capo interni
                return text.TrimEnd('\r', '\n', ' ').TrimStart('\r', '\n', ' ');
            }
            return value ?? " ";
        }

        private static string ToUnicodeBold(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var sb = new System.Text.StringBuilder();
            foreach (char c in input)
            {
                if (c >= 'A' && c <= 'Z') sb.Append(char.ConvertFromUtf32(0x1D400 + (c - 'A')));
                else if (c >= 'a' && c <= 'z') sb.Append(char.ConvertFromUtf32(0x1D41A + (c - 'a')));
                else if (c >= '0' && c <= '9') sb.Append(char.ConvertFromUtf32(0x1D7CE + (c - '0')));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}