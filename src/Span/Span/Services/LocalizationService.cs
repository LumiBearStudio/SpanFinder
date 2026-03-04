using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Windows.Globalization;

namespace Span.Services
{
    /// <summary>
    /// Dictionary-based localization service with key-centric tuple storage.
    /// Supports runtime language switching without app restart.
    /// Translation data lives in LocalizationData.cs (partial class).
    /// </summary>
    public partial class LocalizationService
    {
        private string _language;

        public event Action? LanguageChanged;

        // Supported language codes (order matches tuple fields in LocalizationData.cs)
        public static readonly string[] LangCodes = { "en", "ko", "ja", "zh-Hans", "zh-Hant", "de", "es", "fr", "pt-BR" };

        private static readonly Dictionary<string, Dictionary<string, string>> Strings = BuildStrings();

        private static Dictionary<string, Dictionary<string, string>> BuildStrings()
        {
            var result = new Dictionary<string, Dictionary<string, string>>();
            foreach (var code in LangCodes)
                result[code] = new Dictionary<string, string>(Entries.Length);

            foreach (var e in Entries)
            {
                result["en"][e.key] = e.en;
                result["ko"][e.key] = e.ko;
                result["ja"][e.key] = e.ja;
                result["zh-Hans"][e.key] = e.zhHans;
                result["zh-Hant"][e.key] = e.zhHant;
                result["de"][e.key] = e.de;
                result["es"][e.key] = e.es;
                result["fr"][e.key] = e.fr;
                result["pt-BR"][e.key] = e.ptBR;
            }
            return result;
        }

        public LocalizationService()
        {
            _language = ResolveSystemLanguage();
            ApplyPrimaryLanguageOverride(_language);
        }

        public string Language
        {
            get => _language;
            set
            {
                var resolved = ResolveLanguage(value);
                if (_language != resolved)
                {
                    _language = resolved;
                    ApplyPrimaryLanguageOverride(resolved);
                    LanguageChanged?.Invoke();
                }
            }
        }

        public IReadOnlyList<string> AvailableLanguages => LangCodes;

        public string Get(string key)
        {
            if (Strings.TryGetValue(_language, out var dict) && dict.TryGetValue(key, out var value))
                return value;
            if (Strings["en"].TryGetValue(key, out var fallback))
                return fallback;
            return key;
        }

        /// <summary>
        /// Service 레이어에서 DI 없이 로컬라이제이션 접근을 위한 static 헬퍼.
        /// App.Current.Services에서 LocalizationService를 가져와 Get(key) 호출.
        /// 실패 시 영문 fallback (Strings["en"]) 사용.
        /// </summary>
        public static string L(string key)
        {
            try
            {
                var svc = App.Current?.Services?.GetService(typeof(LocalizationService)) as LocalizationService;
                if (svc != null) return svc.Get(key);
            }
            catch { }
            // fallback to English
            if (Strings.TryGetValue("en", out var en) && en.TryGetValue(key, out var val))
                return val;
            return key;
        }

        private static string ResolveLanguage(string lang)
        {
            if (lang == "system")
                return ResolveSystemLanguage();
            // Direct match (en, ko, ja, de, es, fr)
            if (Strings.ContainsKey(lang))
                return lang;
            // Fallback for 2-letter codes
            return lang switch
            {
                "zh" => ResolveChineseVariant(),
                "pt" => "pt-BR",
                _ => "en"
            };
        }

        private static string ResolveSystemLanguage()
        {
            var culture = CultureInfo.CurrentUICulture;
            var name = culture.Name;          // e.g. "zh-CN", "zh-TW", "ko-KR"
            var twoLetter = culture.TwoLetterISOLanguageName; // e.g. "zh", "ko"

            // Chinese needs special handling (zh-CN→zh-Hans, zh-TW→zh-Hant)
            if (twoLetter == "zh")
                return ResolveChineseVariant(name);

            // Portuguese: pt-BR vs pt-PT → always pt-BR for now
            if (twoLetter == "pt")
                return "pt-BR";

            // Direct match for other supported languages
            if (Strings.ContainsKey(twoLetter))
                return twoLetter;

            return "en";
        }

        private static string ResolveChineseVariant(string? cultureName = null)
        {
            cultureName ??= CultureInfo.CurrentUICulture.Name;
            // zh-TW, zh-HK, zh-MO → Traditional; everything else → Simplified
            return cultureName.Contains("TW") || cultureName.Contains("HK") || cultureName.Contains("MO")
                ? "zh-Hant"
                : "zh-Hans";
        }

        /// <summary>
        /// Set Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride
        /// so that system dialogs respect the app's configured language.
        /// </summary>
        private static void ApplyPrimaryLanguageOverride(string lang)
        {
            try
            {
                ApplicationLanguages.PrimaryLanguageOverride = lang switch
                {
                    "ko" => "ko-KR",
                    "ja" => "ja-JP",
                    "zh-Hans" => "zh-CN",
                    "zh-Hant" => "zh-TW",
                    "de" => "de-DE",
                    "es" => "es-ES",
                    "fr" => "fr-FR",
                    "pt-BR" => "pt-BR",
                    _ => "" // empty = use system default
                };
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[LocalizationService] PrimaryLanguageOverride failed: {ex.Message}");
            }
        }
    }
}
