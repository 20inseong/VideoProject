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

        // ✨ [규칙 2] 폰트가 반드시 포함해야 하는 필수 문자들
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
                        // --- ✨ 필터링 규칙 1: 심볼 폰트인지 확인 ---
                        // glyph.Symbol이 true이면 Wingdings 같은 심볼 폰트이므로 무조건 건너뜀
                        if (glyph.Symbol)
                        {
                            Debug.WriteLine($"[FontManager] 심볼 폰트이므로 건너뜀: {fontFamily.Source}");
                            continue; // 다음 폰트로 넘어감
                        }

                        // --- ✨ 필터링 규칙 2: 필수 문자 세트를 지원하는지 확인 ---
                        // EssentialChars에 정의된 모든 문자가 폰트의 Glyph 맵에 포함되어 있는지 검사
                        bool hasEssentialChars = EssentialChars.All(c => glyph.CharacterToGlyphMap.ContainsKey(c));
                        if (!hasEssentialChars)
                        {
                            Debug.WriteLine($"[FontManager] 필수 문자 미지원으로 건너뜀: {fontFamily.Source}");
                            continue; // 다음 폰트로 넘어감
                        }

                        // --- 기존 규칙: 폰트가 자신의 이름을 렌더링할 수 있는지 확인 ---
                        bool canRenderOwnName = fontFamily.Source.All(c => glyph.CharacterToGlyphMap.ContainsKey(c));
                        if (!canRenderOwnName)
                        {
                            Debug.WriteLine($"[FontManager] 이름 렌더링 불가로 건너뜀: {fontFamily.Source}");
                            continue; // 다음 폰트로 넘어감
                        }

                        // ✅ 모든 필터링 규칙을 통과한 고품질 폰트만 목록에 추가
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