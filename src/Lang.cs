using System.Globalization;

namespace ArcBright
{
    internal static class Lang
    {
        public static bool Romanian;

        public static void Init(Settings settings)
        {
            if (settings.Language == "ro")
            {
                Romanian = true;
            }
            else if (settings.Language == "en")
            {
                Romanian = false;
            }
            else
            {
                Romanian = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ro"
                    || CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "ro";
            }
        }

        public static string T(string english, string romanian)
        {
            return Romanian ? romanian : english;
        }
    }
}
