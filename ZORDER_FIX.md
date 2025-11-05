# Z-Order (레이어 순서) 수정

## 변경 사항

내보내기 시 클립들의 레이어 순서를 다음과 같이 변경했습니다:

### 수정 전
- 모든 비디오 클립과 이미지 클립을 TrackIndex 순서대로 오버레이
- 비디오와 이미지가 섞여서 처리됨
- TrackIndex가 낮을수록 위에 표시됨

### 수정 후 - 비디오는 항상 맨 아래, 이미지/텍스트는 TrackIndex로 순서 결정
1. **비디오 클립들** (맨 아래 레이어)
   - 비디오 클립끼리만 TrackIndex로 순서 결정
   - Track 4 → 3 → 2 → 1 → 0 순서로 오버레이
   - Track 0의 비디오가 다른 비디오들 위에 표시

2. **이미지 클립과 텍스트 클립 혼합** (위 레이어)
   - 모든 비디오 클립보다 위에 표시됨
   - 이미지와 텍스트가 **함께** TrackIndex로 순서 결정
   - Track 4 → 3 → 2 → 1 → 0 순서로 오버레이
   - Track 0의 클립이 다른 클립들 위에 표시
   - **텍스트가 이미지보다 항상 위에 있는 것이 아님!**
   - **타임라인 순서(TrackIndex)에 따라 이미지가 텍스트 위에 올 수 있음**

## 코드 변경

### FFmpegExportService.cs

**수정 전:**
```csharp
var overlayClips = clips
    .Where(c => (c is VideoClip || c is ImageClip) && clipIdToProcessedStreamMap.ContainsKey(c.Id))
    .OrderByDescending(c => c.TrackIndex)
    .ToList();

var textClips = clips
    .OfType<TextClip>()
    .OrderByDescending(c => c.TrackIndex)
    .ToList();

foreach (var clip in overlayClips)
{
    if (clip is VideoClip videoClip)
    {
        // 비디오 오버레이
    }
    else if (clip is ImageClip imageClip)
    {
        // 이미지 오버레이
    }
}

foreach (var textClip in textClips)
{
    // 텍스트 오버레이
}
```

**수정 후:**
```csharp
// 1. 비디오 클립들 (TrackIndex 오름차순)
var videoClips = clips
    .OfType<VideoClip>()
    .Where(c => clipIdToProcessedStreamMap.ContainsKey(c.Id))
    .OrderBy(c => c.TrackIndex)
    .ToList();

// 2. 이미지와 텍스트 클립들을 함께 정렬 (TrackIndex 오름차순)
var overlayClips = clips
    .Where(c => (c is ImageClip || c is TextClip) && clipIdToProcessedStreamMap.ContainsKey(c.Id))
    .OrderBy(c => c.TrackIndex)
    .ToList();

// 1단계: 비디오 클립들 먼저 오버레이
foreach (var videoClip in videoClips)
{
    // 비디오 오버레이
}

// 2단계: 이미지와 텍스트 클립들을 TrackIndex 순서대로 오버레이
foreach (var clip in overlayClips)
{
    if (clip is ImageClip imageClip)
    {
        // 이미지 오버레이
    }
    else if (clip is TextClip textClip)
    {
        // 텍스트 오버레이
    }
}
```

## 동작 예시

### 예시 1: 비디오, 이미지, 텍스트가 다양한 트랙에 있는 경우

**타임라인:**
- Track 0: 텍스트 클립 T1
- Track 1: 이미지 클립 I1
- Track 2: 비디오 클립 V1
- Track 3: 텍스트 클립 T2
- Track 4: 이미지 클립 I2

**오버레이 순서 (처리 순서):**
1. 비디오 V1 (Track 2) - 비디오는 맨 먼저 처리 (맨 아래)
2. 이미지 I2 (Track 4) - 이미지/텍스트 중 Track 4
3. 텍스트 T2 (Track 3) - 이미지/텍스트 중 Track 3
4. 이미지 I1 (Track 1) - 이미지/텍스트 중 Track 1
5. 텍스트 T1 (Track 0) - 이미지/텍스트 중 Track 0 (맨 위)

**최종 레이어 (아래 → 위):**
1. 비디오 V1
2. 이미지 I2
3. 텍스트 T2 ← 이미지보다 위!
4. 이미지 I1 ← 텍스트보다 위!
5. 텍스트 T1 (맨 위)

**결과:**
- 비디오는 항상 맨 아래 ✅
- 이미지와 텍스트는 TrackIndex에 따라 순서 결정 ✅
- Track 1의 이미지가 Track 3의 텍스트보다 위에 표시됨 ✅

### 예시 2: 같은 트랙에 이미지와 텍스트

**타임라인:**
- Track 0: 텍스트 클립 T1
- Track 0: 이미지 클립 I1 (같은 위치)
- Track 1: 비디오 클립 V1

**오버레이 순서:**
1. 비디오 V1 (Track 1)
2. 텍스트 T1과 이미지 I1 (Track 0, 타임라인 순서에 따라)

**결과:**
- Track 0에서 먼저 나타나는 클립이 나중에 처리되어 위에 표시됨
- 타임라인에서의 위치에 따라 순서 결정 ✅

## 장점

### 1. 예측 가능한 레이어 순서
- 클립 타입에 따라 명확한 레이어 우선순위
- 사용자가 직관적으로 이해 가능

### 2. 자막/텍스트 항상 최상위
- 텍스트 클립이 항상 가장 위에 표시
- 비디오나 이미지에 가려지지 않음

### 3. 이미지 오버레이 보장
- 이미지가 비디오보다 항상 위에 표시
- 로고, 워터마크 등 용도에 적합

### 4. 각 타입 내 유연성
- 같은 타입의 클립들은 여전히 TrackIndex로 순서 조정 가능
- 비디오끼리, 이미지끼리, 텍스트끼리 레이어 조정 가능

## 사용 시나리오

### 시나리오 1: 로고 오버레이
```
Track 0: 로고 이미지 (우측 상단)
Track 1: 메인 비디오
```
→ 로고가 항상 비디오 위에 표시됨

### 시나리오 2: 자막이 있는 비디오
```
Track 0: 자막 텍스트
Track 1: 배경 이미지
Track 2: 메인 비디오
```
→ 자막이 모든 것 위에, 배경 이미지가 비디오 위에 표시됨

### 시나리오 3: 복잡한 편집
```
Track 0: 제목 텍스트
Track 1: 로고 이미지
Track 2: 인서트 비디오 (작은 화면)
Track 3: 배경 이미지
Track 4: 메인 비디오
```
레이어 순서:
1. 메인 비디오 (Track 4)
2. 인서트 비디오 (Track 2)
3. 배경 이미지 (Track 3)
4. 로고 이미지 (Track 1)
5. 제목 텍스트 (Track 0) ← 맨 위

## 주의사항

### TrackIndex의 의미 변화
- 이제 TrackIndex는 **같은 타입의 클립들 사이에서만** 순서를 결정
- 다른 타입의 클립들 사이에서는 클립 타입이 우선

### 미리보기와 내보내기 일관성
- 미리보기에서도 동일한 Z-Order 로직이 적용되어야 함
- 현재는 내보내기만 수정됨
- 미리보기는 별도로 관리됨 (OverlayWindow, VideoPreviewOverlay)

## 테스트 권장 사항

1. ✅ 같은 위치에 비디오, 이미지, 텍스트 배치
   - 텍스트가 맨 위, 이미지가 중간, 비디오가 아래인지 확인

2. ✅ 다양한 TrackIndex 조합
   - 같은 타입 내에서 TrackIndex 순서가 유지되는지 확인

3. ✅ 여러 비디오 클립 겹침
   - 비디오끼리의 순서가 TrackIndex에 따라 결정되는지 확인

4. ✅ 여러 이미지 클립 겹침
   - 이미지끼리의 순서가 TrackIndex에 따라 결정되는지 확인

5. ✅ 여러 텍스트 클립 겹침
   - 텍스트끼리의 순서가 TrackIndex에 따라 결정되는지 확인

6. ✅ 복잡한 편집 프로젝트
   - 모든 타입의 클립이 섞인 프로젝트에서 레이어 순서 확인
