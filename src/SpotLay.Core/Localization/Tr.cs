using System.Globalization;

namespace SpotLay.Core.Localization;

/// <summary>
/// Idioma de la interfaz. Los textos se escriben en español en el código y aquí se
/// buscan en el diccionario del idioma elegido (si falta una traducción, sale en
/// español). El idioma se fija al arrancar; cambiarlo reinicia SpotLay.
/// </summary>
public static class Tr
{
    public const string Spanish = "es";
    public const string English = "en";

    private static string s_language = Spanish;

    public static string Language
    {
        get => s_language;
        set => s_language = value == English ? English : Spanish;
    }

    public static bool IsEnglish => s_language == English;

    /// <summary>Formato de números y fechas del idioma ("57,1" / "57.1").</summary>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(IsEnglish ? "en-US" : "es-ES");

    /// <summary>Idioma de Windows: español si Windows está en español, si no inglés.</summary>
    public static string SystemDefault => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == Spanish ? Spanish : English;

    /// <summary>Traduce un texto escrito en español.</summary>
    public static string T(string spanish) =>
        IsEnglish && EnglishText.Map.TryGetValue(spanish, out var english) ? english : spanish;

    /// <summary>Traduce a un idioma concreto, aunque no sea el actual (p. ej. el que se acaba de elegir).</summary>
    public static string In(string language, string spanish) =>
        language == English && EnglishText.Map.TryGetValue(spanish, out var english) ? english : spanish;

    /// <summary>Traduce un texto con huecos {0}, {1}… y los rellena con el formato del idioma.</summary>
    public static string F(string spanish, params object?[] args) => string.Format(Culture, T(spanish), args);
}
