using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace VideoEditor.Common
{
    public static class FontManager
    {
        public static ObservableCollection<FontFamily> ValidFontFamilies { get; } = new ObservableCollection<FontFamily>();

        private static bool _isLoaded = false;

        // 폰트가 반드시 포함해야 하는 필수 문자들
        private const string EssentialChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";

        public static void LoadValidFonts()
        {
            if (_isLoaded) return;

            foreach (var fontFamily in Fonts.SystemFontFamilies.OrderBy(f => f.Source))
            {
                try
                {
                    var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

                    GlyphTypeface glyph;
                    if (typeface.TryGetGlyphTypeface(out glyph))
                    {
                        // 심볼 폰트인지 확인
                        if (glyph.Symbol)
                        {
                            Debug.WriteLine($"[FontManager] 심볼 폰트이므로 건너뜀: {fontFamily.Source}");
                            continue;
                        }

                        // 필수 문자 세트를 지원하는지 확인
                        bool hasEssentialChars = EssentialChars.All(c => glyph.CharacterToGlyphMap.ContainsKey(c));
                        if (!hasEssentialChars)
                        {
                            Debug.WriteLine($"[FontManager] 필수 문자 미지원으로 건너뜀: {fontFamily.Source}");
                            continue;
                        }

                        // 폰트가 자신의 이름을 렌더링할 수 있는지 확인
                        bool canRenderOwnName = fontFamily.Source.All(c => glyph.CharacterToGlyphMap.ContainsKey(c));
                        if (!canRenderOwnName)
                        {
                            Debug.WriteLine($"[FontManager] 이름 렌더링 불가로 건너뜀: {fontFamily.Source}");
                            continue;
                        }

                        // 모든 필터링 규칙을 통과한 폰트만 목록에 추가
                        ValidFontFamilies.Add(fontFamily);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[FontManager] 손상된 폰트이므로 건너뜀: {fontFamily.Source}. 오류: {ex.Message}");
                }
            }
            _isLoaded = true;
        }
    }
}