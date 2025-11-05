# 이미지 회전 검은색 배경 버그 수정

## 문제점

이미지를 회전한 후 내보내기를 하면 다음과 같은 문제가 발생했습니다:
1. 미리보기에는 보이지 않는 검은색 사각형이 회전된 이미지를 감싸고 있음
2. 투명도를 조절하면 검은색 사각형의 투명도도 함께 변경됨
3. 회전된 이미지만 보이지 않고 검은 배경이 함께 렌더링됨
4. **회전 시 위치가 미리보기와 다르게 렌더링됨**

## 원인 분석

### 1. FFmpeg 필터 순서 문제
**기존 필터 순서:**
```
크기 조절 → 회전(검은 배경) → 투명도
```

1. **rotate 필터**: `c=black` 옵션으로 회전 시 빈 공간을 검은색으로 채움
2. **투명도 필터**: 회전 후 전체 이미지(검은 배경 포함)에 투명도를 적용
3. **결과**: 검은 배경까지 투명해져서 반투명 검은 사각형이 보임

### 2. 회전 시 크기 변화로 인한 위치 문제

rotate 필터는 이미지가 잘리지 않도록 출력 크기를 대각선 길이로 확대합니다:
```
ow='hypot(iw,ih)' → 출력 너비 = √(너비² + 높이²)
oh='hypot(iw,ih)' → 출력 높이 = √(너비² + 높이²)
```

**예시: 100x100 이미지를 45도 회전**
- 회전 전 크기: 100x100
- 회전 후 크기: √(100² + 100²) ≈ 141x141
- 크기 증가: 41x41

이로 인해:
- 회전 전 좌측 상단 기준 위치가 (50, 50)이었다면
- 회전 후에도 좌측 상단은 (50, 50)이지만
- 이미지가 41픽셀 더 커졌으므로 실제 이미지 중심이 약 20픽셀 이동
- **결과**: 미리보기와 다른 위치에 렌더링

### rotate 필터의 동작
- `rotate=angle:c=color:ow=width:oh=height`
- `c=black`: 빈 공간을 검은색으로 채움
- `c=none`: 빈 공간을 투명하게 처리
- `ow`, `oh`: 출력 크기 (회전 후 잘리지 않도록 설정)

## 수정 내용

### 1. FFmpegExportService.cs - 필터 순서 변경

**수정 전:**
```csharp
// 필터 빌드: 크기 조절 -> 회전 -> 투명도
filterParts.Add(resizeFilter);
filterParts.Add(rotateFilter); // c=black
filterParts.Add(alphaFilter);
```

**수정 후:**
```csharp
// 필터 빌드: 투명도 -> 크기 조절 -> 회전(투명 배경)
filterParts.Add(alphaFilter);  // 투명도 먼저
filterParts.Add(resizeFilter);
filterParts.Add(rotateFilter); // c=none (투명 배경)
```

### 2. FFmpegExportService.cs - 회전 배경색 변경

**수정 전:**
```csharp
string rotateFilter = $"rotate={rotationRadians}:c=black:ow='hypot(iw,ih)':oh='hypot(iw,ih)'";
```

**수정 후:**
```csharp
string rotateFilter = $"rotate={rotationRadians}:c=none:ow='hypot(iw,ih)':oh='hypot(iw,ih)'";
```

### 3. FFmpegExportService.cs - 회전 위치 오프셋 계산 및 적용

**추가된 코드 - 오프셋 계산:**
```csharp
// 회전 시 위치 오프셋 계산
double offsetX = 0;
double offsetY = 0;

if (Math.Abs(ic.Rotation) > 0.01)
{
    // 회전 후 크기: hypot(w, h) = sqrt(w^2 + h^2)
    double rotatedSize = Math.Sqrt(targetWidth * targetWidth + targetHeight * targetHeight);
    
    // 회전으로 인한 크기 증가분
    double widthIncrease = rotatedSize - targetWidth;
    double heightIncrease = rotatedSize - targetHeight;
    
    // 중심을 유지하기 위해 offset 조정 (크기 증가의 절반만큼 뒤로 이동)
    offsetX = -widthIncrease / 2.0;
    offsetY = -heightIncrease / 2.0;
    
    rotatedSizeOffsets[ic.Id] = (offsetX, offsetY);
}
```

**추가된 코드 - 오버레이 위치 적용:**
```csharp
else if (clip is ImageClip imageClip)
{
    string streamToOverlay = clipIdToProcessedStreamMap[imageClip.Id];
    double targetX = imageClip.X * positionScaleX;
    double targetY = imageClip.Y * positionScaleY;
    
    // 회전으로 인한 오프셋 적용
    if (rotatedSizeOffsets.TryGetValue(imageClip.Id, out var offset))
    {
        targetX += offset.offsetX;
        targetY += offset.offsetY;
    }
    
    string ffmpegX = targetX.ToString("F2", culture);
    string ffmpegY = targetY.ToString("F2", culture);
    // ... overlay 적용
}
```

## 주요 변경사항

### 1. 필터 순서 변경
- **투명도 → 크기 조절 → 회전** 순서로 변경
- 투명도를 원본 이미지에 먼저 적용하여 회전 전 알파 채널 설정

### 2. 회전 배경색 변경
- `c=black` → `c=none`으로 변경
- `c=none`: 회전 시 빈 공간을 투명하게 처리
- 알파 채널이 있는 PNG 형식의 투명도 유지

### 3. 회전 위치 오프셋 보정
- 회전으로 인한 크기 증가량 계산
- 크기 증가의 절반만큼 위치를 뒤로 이동하여 중심점 유지
- 미리보기와 동일한 위치에 렌더링

## 효과

### 수정 전
```
[원본 이미지] 
  ↓ scale
[크기 조절된 이미지]
  ↓ rotate (c=black)
[회전된 이미지 + 검은 배경]  ← 검은 사각형 생성
  ↓ colorchannelmixer
[반투명 이미지 + 반투명 검은 배경]  ← 문제 발생!
  ↓ overlay (x, y)
[위치가 어긋난 이미지]  ← 위치 문제!
```

### 수정 후
```
[원본 이미지]
  ↓ colorchannelmixer
[투명도가 적용된 이미지]
  ↓ scale
[크기 조절된 이미지]
  ↓ rotate (c=none)
[회전된 이미지 + 투명 배경]  ← 투명 배경으로 깔끔!
  ↓ overlay (x + offsetX, y + offsetY)
[정확한 위치에 렌더링]  ← 위치 정확!
```

## 위치 오프셋 계산 예시

### 100x100 이미지를 45도 회전

**계산:**
```
targetWidth = 100
targetHeight = 100
rotatedSize = √(100² + 100²) = 141.42
widthIncrease = 141.42 - 100 = 41.42
heightIncrease = 141.42 - 100 = 41.42
offsetX = -41.42 / 2 = -20.71
offsetY = -41.42 / 2 = -20.71
```

**적용:**
```
원래 위치: (50, 50)
보정된 위치: (50 - 20.71, 50 - 20.71) = (29.29, 29.29)
```

**결과:**
- 회전 후 크기가 141x141로 확대됨
- 위치를 20.71픽셀 왼쪽 위로 이동
- 이미지 중심이 원래 위치에 정확히 유지됨

### 200x100 이미지를 90도 회전

**계산:**
```
targetWidth = 200
targetHeight = 100
rotatedSize = √(200² + 100²) = 223.61
widthIncrease = 223.61 - 200 = 23.61
heightIncrease = 223.61 - 100 = 123.61
offsetX = -23.61 / 2 = -11.8
offsetY = -123.61 / 2 = -61.8
```

**결과:**
- 가로와 세로 크기 증가량이 다름
- 각각 적절히 보정하여 중심 유지

## 동작 예시

### 예시 1: 45도 회전 + 투명도 50%
**수정 전:**
- 검은색 사각형이 50% 투명도로 보임
- 이미지도 50% 투명도
- 위치가 어긋남
- 전체적으로 어두운 느낌

**수정 후:**
- 투명 배경 (보이지 않음)
- 이미지만 50% 투명도
- 위치가 미리보기와 동일
- 깔끔한 회전 효과

### 예시 2: 90도 회전 + 투명도 100%
**수정 전:**
- 검은색 사각형이 선명하게 보임
- 위치가 오른쪽 아래로 이동
- 이미지는 정상

**수정 후:**
- 투명 배경 (보이지 않음)
- 위치가 미리보기와 정확히 일치
- 이미지만 깔끔하게 회전
- 미리보기와 동일한 결과

## FFmpeg 필터 옵션 설명

### rotate 필터
```
rotate=angle:c=fillcolor:ow=output_width:oh=output_height
```

- `angle`: 라디안 단위 회전 각도
- `c=fillcolor`: 빈 공간 채울 색상
  - `c=black`: 검은색 (0x000000FF)
  - `c=white`: 흰색 (0xFFFFFFFF)
  - `c=none`: 투명 (0x00000000) ← **수정 후 사용**
- `ow`, `oh`: 출력 크기 (회전 후 잘리지 않도록 설정)

### colorchannelmixer 필터
```
colorchannelmixer=aa=alpha_value
```

- `aa`: 알파 채널 조정 (0.0 ~ 1.0)
- 원본 이미지의 알파 채널에 곱셈 적용
- 투명도를 조절하는 데 사용

## 테스트 권장 사항

1. ✅ 회전만 적용한 이미지: 검은 배경 없이 깔끔하게 회전, 위치 정확
2. ✅ 회전 + 투명도 적용: 투명 배경에 회전된 이미지만 표시, 위치 정확
3. ✅ 다양한 각도 테스트: 45도, 90도, 135도, 180도 등
4. ✅ 다양한 크기 이미지: 정사각형, 직사각형 (가로/세로)
5. ✅ PNG와 JPG 모두 테스트: 투명도 지원 확인
6. ✅ 미리보기와 내보내기 결과 일치 확인 (위치 및 크기)
7. ✅ 회전 + 크기 조절 + 투명도 조합 테스트

