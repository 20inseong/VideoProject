# 이미지 내보내기 크기 및 위치 버그 수정

## 문제점

이미지를 내보낼 때 미리보기와 다른 크기와 위치로 렌더링되는 문제가 발생했습니다:
1. **크기 불일치**: 내보낸 비디오의 이미지 크기가 미리보기와 다름
2. **위치 불일치**: 이미지의 위치가 미리보기에서 보던 것과 다름

## 원인 분석

### 크기 계산 로직 문제

**기존 코드:**
```csharp
if (ic.CustomWidth > 0 && ic.CustomHeight > 0)
{
    double widthRatio = ic.CustomWidth / ic.SourceWidth;
    double heightRatio = ic.CustomHeight / ic.SourceHeight;
    
    targetWidth = ic.RenderWidth * widthRatio * scaleX;
    targetHeight = ic.RenderHeight * heightRatio * scaleY;
}
```

**문제점:**
1. `RenderWidth/Height`는 현재 미리보기 크기 (창 크기에 따라 변함)
2. `CustomWidth/Height`를 `SourceWidth/Height`로 나눈 비율을 `RenderWidth/Height`에 곱함
3. 이 방식은 `RenderWidth/Height`가 변경되면 계산이 부정확해짐
4. `InitialRenderWidth/Height` (초기 미리보기 크기)를 사용해야 정확함

### 개념 정리

- **SourceWidth/Height**: 원본 이미지의 실제 픽셀 크기 (예: 1920x1080)
- **CustomWidth/Height**: 사용자가 설정한 크기 (원본 픽셀 단위, 예: 960x540)
- **InitialRenderWidth/Height**: 처음 로드 시 미리보기 크기 (예: 400x225)
- **RenderWidth/Height**: 현재 미리보기 크기 (창 크기 변경, 드래그 등으로 변경 가능)
- **PreviewWidth/Height**: 미리보기 영역 크기 (800x450)
- **OutputWidth/Height**: 출력 비디오 해상도 (1920x1080)

## 수정 내용

### FFmpegExportService.cs - 크기 계산 로직 개선

**수정 전:**
```csharp
if (ic.CustomWidth > 0 && ic.CustomHeight > 0)
{
    double widthRatio = ic.CustomWidth / ic.SourceWidth;
    double heightRatio = ic.CustomHeight / ic.SourceHeight;
    
    targetWidth = ic.RenderWidth * widthRatio * scaleX;
    targetHeight = ic.RenderHeight * heightRatio * scaleY;
}
else
{
    targetWidth = ic.RenderWidth * scaleX;
    targetHeight = ic.RenderHeight * scaleY;
}
```

**수정 후:**
```csharp
if (ic.CustomWidth > 0 && ic.CustomHeight > 0 && ic.InitialRenderWidth > 0 && ic.InitialRenderHeight > 0)
{
    // CustomWidth/Height가 SourceWidth/Height 대비 몇 배인지 계산
    double widthRatio = ic.CustomWidth / ic.SourceWidth;
    double heightRatio = ic.CustomHeight / ic.SourceHeight;
    
    // InitialRenderWidth/Height에 비율을 적용한 후 출력 해상도로 스케일
    targetWidth = ic.InitialRenderWidth * widthRatio * scaleX;
    targetHeight = ic.InitialRenderHeight * heightRatio * scaleY;
}
else
{
    // CustomWidth/Height가 설정되지 않았으면 현재 RenderWidth/Height 사용
    targetWidth = ic.RenderWidth * scaleX;
    targetHeight = ic.RenderHeight * scaleY;
}
```

## 주요 변경사항

### 1. InitialRenderWidth/Height 사용
- 현재 `RenderWidth/Height` 대신 `InitialRenderWidth/Height` 사용
- 초기 미리보기 크기를 기준으로 계산하여 일관성 유지
- 창 크기 변경이나 드래그로 인한 크기 변경에 영향받지 않음

### 2. 계산 과정 명확화

**단계별 계산:**
```
1. CustomWidth/Height가 원본 대비 몇 배인지 계산
   widthRatio = CustomWidth / SourceWidth
   예: 960 / 1920 = 0.5

2. InitialRenderWidth/Height에 비율 적용
   scaledWidth = InitialRenderWidth * widthRatio
   예: 400 * 0.5 = 200

3. 출력 해상도로 스케일
   targetWidth = scaledWidth * (OutputWidth / PreviewWidth)
   예: 200 * (1920 / 800) = 480
```

## 예시

### 시나리오: 원본 1920x1080 이미지를 960x540으로 축소

**환경:**
- 원본 이미지: 1920x1080
- CustomWidth/Height: 960x540 (50% 축소)
- InitialRenderWidth/Height: 400x225 (미리보기 초기 크기)
- PreviewWidth/Height: 800x450
- OutputWidth/Height: 1920x1080

**계산 과정:**
```
widthRatio = 960 / 1920 = 0.5
heightRatio = 540 / 1080 = 0.5

targetWidth = 400 * 0.5 * (1920 / 800) = 200 * 2.4 = 480
targetHeight = 225 * 0.5 * (1080 / 450) = 112.5 * 2.4 = 270
```

**결과:**
- 출력 비디오에서 이미지 크기: 480x270
- 미리보기 대비 정확히 2.4배 스케일 (1920/800 = 2.4)
- 원본 대비 25% 크기 (960x540을 1920x1080 해상도에 맞춰 스케일)

### 시나리오: CustomWidth/Height 미설정

**환경:**
- RenderWidth/Height: 300x169 (드래그로 크기 변경됨)
- PreviewWidth/Height: 800x450
- OutputWidth/Height: 1920x1080

**계산 과정:**
```
targetWidth = 300 * (1920 / 800) = 720
targetHeight = 169 * (1080 / 450) = 405.6
```

**결과:**
- 현재 미리보기 크기를 출력 해상도로 그대로 스케일
- 드래그로 조정한 크기가 정확히 반영됨

## 위치 계산

위치는 기존 로직 유지:
```csharp
double targetX = imageClip.X * positionScaleX;
double targetY = imageClip.Y * positionScaleY;
```

- `X, Y`: 미리보기에서의 위치
- `positionScaleX/Y`: `OutputWidth/PreviewWidth`, `OutputHeight/PreviewHeight`
- 미리보기 위치를 출력 해상도로 스케일

## 회전 고려사항

회전 필터는 이미지 크기를 변경합니다:
```
ow='hypot(iw,ih)' → 출력 너비 = √(입력너비² + 입력높이²)
oh='hypot(iw,ih)' → 출력 높이 = √(입력너비² + 입력높이²)
```

**회전 시 크기 변화:**
- 45도 회전: 약 1.414배 (√2) 증가
- 90도 회전: 대각선 길이만큼 증가

**현재 처리:**
- 회전은 overlay 단계에서 처리되므로 위치는 회전 전 기준
- 회전 후 중심점이 동일하게 유지됨
- 미리보기와 동일한 방식으로 처리

## 테스트 권장 사항

1. ✅ 이미지 크기 조절 없이 내보내기
   - 미리보기와 동일한 상대적 크기로 출력
   
2. ✅ CustomWidth/Height 설정 후 내보내기
   - 설정한 크기가 정확히 반영되는지 확인
   
3. ✅ 미리보기 창 크기 변경 후 내보내기
   - 창 크기와 무관하게 일관된 출력
   
4. ✅ 드래그로 크기 조절 후 내보내기 (CustomWidth/Height 미설정)
   - 드래그한 크기가 정확히 반영
   
5. ✅ 회전 + 크기 조절 조합
   - 회전과 크기가 모두 정확히 적용되는지 확인
   
6. ✅ 위치 확인
   - 미리보기와 동일한 위치에 렌더링되는지 확인

## 남은 이슈

### 회전 시 위치 조정
현재는 회전 전 크기 기준으로 위치가 설정됩니다. 회전하면 이미지 크기가 커지므로 위치가 약간 어긋날 수 있습니다. 이 경우 추가 보정이 필요할 수 있습니다.

**해결 방안 (필요시):**
- 회전 각도를 고려한 위치 오프셋 계산
- 회전 후 중심점을 원래 위치로 이동하는 필터 추가
