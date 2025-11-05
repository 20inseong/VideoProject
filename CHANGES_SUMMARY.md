# 이미지 클립 기능 추가 요약

## 추가된 기능

### 1. 회전 (Rotation)
- 0-360도 범위의 회전 각도 설정
- UI에 슬라이더와 텍스트 입력 제공
- 미리보기에서 실시간 회전 표시
- 내보내기 시 회전 적용

### 2. 투명도 (Opacity)
- 0-100% 범위의 투명도 설정
- UI에 슬라이더와 텍스트 입력 제공
- 미리보기에서 실시간 투명도 표시
- 내보내기 시 투명도 적용

### 3. 크기 조절 (Custom Size)
- 너비와 높이를 픽셀 단위로 입력
- 한 값만 입력하면 비율에 맞게 다른 값 자동 계산
- 초기값은 미디어의 원본 크기로 표시
- 원본 크기는 프로그램 창 크기에 관계없이 고정
- **미리보기에서 크기 조절 시 자동으로 값 업데이트**
- **크기 값 변경 시 미리보기에 즉시 반영**
- 내보내기 시 지정된 크기 적용

## 수정된 파일

### 1. Models/ImageClip.cs
- `Rotation` 속성 추가 (double, 0-360도)
- `Opacity` 속성 추가 (double, 0-100%)
- `CustomWidth` 속성 추가 (double, 픽셀 단위)
- `CustomHeight` 속성 추가 (double, 픽셀 단위)
- `InitialRenderWidth`, `InitialRenderHeight` 속성 추가 (초기 미리보기 크기 저장)
- 너비/높이 입력 시 비율 자동 계산 로직 구현
- `UpdateRenderSizeFromCustomSize()` 메서드: CustomWidth/Height 변경 시 RenderWidth/Height 업데이트
- `UpdateCustomSizeFromRenderSize()` 메서드: RenderWidth/Height 변경 시 CustomWidth/Height 업데이트
- 무한 루프 방지 플래그 (`_isUpdatingCustomSize`) 추가
- Clone() 메서드에 새 속성 복사 추가

### 2. Views/ClipEditorView.xaml
- ImageClip 편집기 UI 업데이트
- 원본 크기 표시 추가
- 크기 조절 입력 필드 추가 (너비, 높이)
- 회전 슬라이더 및 입력 필드 추가
- 투명도 슬라이더 및 입력 필드 추가
- ScrollViewer로 감싸서 모든 옵션 접근 가능하도록 개선

### 3. Views/OverlayWindow.xaml
- ImageClip DataTemplate 업데이트
- Opacity 바인딩 추가 (OpacityPercentToDecimalConverter 사용)
- RenderTransform 추가하여 회전 적용
- OpacityPercentToDecimalConverter 리소스 추가

### 4. Common/Converters.cs
- `OpacityPercentToDecimalConverter` 추가
  - 0-100 범위의 퍼센트를 0-1 범위의 소수로 변환
  - 미리보기에서 투명도 표시에 사용

### 5. Common/Adorners/ClipAdorner.cs
- TopLeft_DragDelta, TopRight_DragDelta, BottomLeft_DragDelta, BottomRight_DragDelta 메서드 업데이트
- ImageClip의 경우 RenderWidth/Height 변경 시 `UpdateCustomSizeFromRenderSize()` 호출
- 플레이어에서 크기 조절 시 CustomWidth/Height가 자동으로 업데이트되도록 개선

### 6. ViewModels/VideoEditorViewModel.cs
- CreateImageClipAsync 메서드 업데이트
  - CustomWidth와 CustomHeight를 원본 크기로 초기화
  - InitialRenderWidth와 InitialRenderHeight 설정
- SplitClip 메서드의 ImageClip 케이스 업데이트
  - Rotation, Opacity, CustomWidth, CustomHeight, InitialRenderWidth, InitialRenderHeight 복사 추가

### 7. Services/FFmpegExportService.cs
- ImageClip 처리 로직 업데이트
- CustomWidth/CustomHeight가 설정된 경우 해당 크기 사용
- 회전 필터 추가 (rotate 필터, 0도가 아닌 경우)
  - 회전 후 잘림 방지를 위해 출력 크기 자동 조정
- 투명도 필터 추가 (colorchannelmixer, 100%가 아닌 경우)
- 필터 체인: 크기 조절 → 회전 → 투명도 순서로 적용

## 사용 방법

### 이미지 회전
1. 타임라인에서 이미지 클립 선택
2. 오른쪽 속성 패널에서 "회전 (도)" 슬라이더 조정
3. 또는 텍스트 박스에 직접 각도 입력 (0-360)
4. 미리보기에서 즉시 확인 가능
5. 내보내기 시 회전된 상태로 저장됨

### 투명도 조절
1. 타임라인에서 이미지 클립 선택
2. 오른쪽 속성 패널에서 "투명도 (%)" 슬라이더 조정
3. 또는 텍스트 박스에 직접 값 입력 (0-100)
4. 미리보기에서 즉시 확인 가능
5. 내보내기 시 투명도가 적용됨

### 크기 조절
1. 타임라인에서 이미지 클립 선택
2. 오른쪽 속성 패널에서 "원본 크기" 확인
3. "크기 조절" 섹션에서 너비 또는 높이 입력
   - 값을 입력하면 미리보기에서 즉시 크기가 변경됨
   - 한 값만 입력하면 비율에 맞게 다른 값이 자동으로 계산됨
4. **또는** 미리보기에서 이미지 모서리를 드래그하여 크기 조절
   - 크기를 조절하면 "크기 조절" 섹션의 값이 자동으로 업데이트됨
5. 내보내기 시 지정된 크기로 출력됨

## 기술적 세부사항

### 양방향 동기화
- **CustomWidth/Height → RenderWidth/Height**: 사용자가 크기 값을 입력하면 미리보기 크기가 비례적으로 변경
- **RenderWidth/Height → CustomWidth/Height**: 미리보기에서 크기를 조절하면 크기 값이 자동으로 업데이트
- 무한 루프 방지를 위해 `_isUpdatingCustomSize` 플래그 사용
- `InitialRenderWidth/Height`를 기준으로 비율 계산하여 정확한 변환 보장

### 미리보기 렌더링
- WPF의 RenderTransform을 사용하여 회전 구현
- Opacity 속성을 사용하여 투명도 구현
- 회전 중심점은 이미지의 중앙 (RenderTransformOrigin="0.5,0.5")

### FFmpeg 내보내기
- `rotate` 필터: 라디안 단위로 각도 지정, 회전 후 크기 자동 조정
- `colorchannelmixer` 필터: 알파 채널 조정으로 투명도 구현
- `scale` 필터: 사용자 지정 크기 또는 기본 렌더 크기 적용
- 필터는 체인으로 연결되어 순차적으로 적용됨

### 크기 계산
- CustomWidth/Height는 원본 픽셀 단위로 저장
- RenderWidth/Height는 미리보기 크기
- InitialRenderWidth/Height는 초기 미리보기 크기 (비율 계산 기준점)
- 내보내기 시 PreviewWidth/Height 대비 OutputWidth/Height 비율로 스케일링
- 원본 대비 CustomWidth/Height 비율을 계산하여 최종 출력 크기 결정

## 주의사항

1. 회전 시 이미지가 잘리지 않도록 출력 크기가 자동으로 조정됩니다
2. 투명도는 배경이 검정색인 경우 더 잘 보입니다
3. 크기 조절 시 비율이 자동으로 유지되어 왜곡을 방지합니다
4. 원본 크기는 프로그램 창 크기와 무관하게 항상 동일합니다
5. 미리보기와 크기 조절 값이 실시간으로 양방향 동기화됩니다
