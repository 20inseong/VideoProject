# 버그 수정 요약 - 다른 프로그램 가리기 문제

## 문제점

1. **렌더링 진행률 창**: ExportProgressWindow가 `Topmost="True"` 설정으로 인해 모든 프로그램 위에 표시되어 다른 작업을 방해했습니다.

2. **OverlayWindow**: 이미지/텍스트 미리보기를 담당하는 OverlayWindow가 MainWindow가 비활성화되어도 계속 표시되어 다른 프로그램을 가렸습니다.

## 수정 내용

### 1. ExportProgressWindow.xaml
**변경 전:**
```xml
Topmost="True"
```

**변경 후:**
```xml
Topmost="False"
```

**효과**: 렌더링 진행률 창이 더 이상 다른 프로그램을 가리지 않습니다. MainWindow의 자식 창으로 정상적으로 작동하며, 다른 프로그램으로 전환 시 뒤로 갑니다.

### 2. OverlayWindow.xaml - IsHitTestVisible 조건부 설정

**변경 전:**
```xml
<Window ... IsHitTestVisible="True">
    <Grid>
        <ItemsControl ItemsSource="{Binding ActiveWpfOverlays}">
```

**변경 후:**
```xml
<Window ... Topmost="False" ShowInTaskbar="False">
    <Grid Background="Transparent">
        <Grid.Style>
            <Style TargetType="Grid">
                <Setter Property="IsHitTestVisible" Value="True"/>
                <Style.Triggers>
                    <DataTrigger Binding="{Binding ActiveWpfOverlays.Count}" Value="0">
                        <Setter Property="IsHitTestVisible" Value="False"/>
                    </DataTrigger>
                </Style.Triggers>
            </Style>
        </Grid.Style>
        <ItemsControl ItemsSource="{Binding ActiveWpfOverlays}" Background="Transparent">
```

**효과**: 
- 오버레이 항목(이미지/텍스트)이 없을 때는 마우스 이벤트를 통과시켜 아래의 UI를 클릭할 수 있습니다
- 오버레이 항목이 있을 때만 마우스 이벤트를 받아 선택/드래그가 가능합니다
- Background를 Transparent로 명시하여 투명 영역도 올바르게 처리됩니다

### 3. MainWindow.xaml.cs - BringOverlayToFront() 메서드

**변경:**
```csharp
private void BringOverlayToFront()
{
    // MainWindow가 활성화되어 있을 때만 OverlayWindow를 최상위로 설정
    if (_overlayWindow != null && _overlayWindow.IsLoaded && this.IsActive)
    {
        var overlayHandle = new WindowInteropHelper(_overlayWindow).Handle;
        SetWindowPos(overlayHandle, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
```

**효과**: OverlayWindow가 MainWindow가 활성화되어 있을 때만 최상위로 유지됩니다.

## 수정 히스토리

### 첫 번째 시도 (문제 발생)
- MainWindow의 Activated/Deactivated 이벤트로 OverlayWindow를 Show/Hide 처리
- **문제**: 다시 돌아왔을 때 UI 클릭이 안 되는 버그 발생
- **원인**: OverlayWindow가 투명 배경으로 전체 영역을 덮어 마우스 이벤트를 가로챔

### 두 번째 시도 (최종 해결)
- OverlayWindow는 항상 표시 상태 유지
- Grid의 IsHitTestVisible을 조건부로 설정 (ActiveWpfOverlays.Count에 따라)
- BringOverlayToFront는 MainWindow가 활성화되어 있을 때만 실행
- **결과**: UI 클릭 문제 해결, 다른 프로그램도 가리지 않음

## 동작 시나리오

### 시나리오 1: 렌더링 진행 중 다른 프로그램 사용
1. 비디오 렌더링 시작
2. ExportProgressWindow가 표시됨
3. 사용자가 다른 프로그램(예: 크롬 브라우저)을 클릭
4. ✅ ExportProgressWindow가 브라우저 뒤로 가서 방해하지 않음

### 시나리오 2: 편집 중 다른 프로그램 사용
1. 비디오 편집기에서 작업 중 (오버레이 항목 없음)
2. 사용자가 다른 프로그램으로 전환
3. ✅ OverlayWindow가 다른 프로그램을 가리지 않음
4. ✅ 비디오 편집기로 돌아와도 모든 UI 클릭 정상 작동

### 시나리오 3: 이미지/텍스트 편집 중
1. 이미지나 텍스트 클립을 타임라인에 추가
2. OverlayWindow에 미리보기 표시됨 (IsHitTestVisible=True)
3. ✅ 이미지/텍스트를 클릭하여 선택/드래그 가능
4. ✅ 오버레이 밖의 영역을 클릭하면 아래 UI 정상 작동

## 기술적 세부사항

### Window Z-Order 관리
- `HWND_TOP`: Topmost가 아닌 창들 중 최상위에 배치
- `this.IsActive` 체크: MainWindow가 활성 창인지 확인
- Owner 관계: OverlayWindow의 Owner를 MainWindow로 설정하여 함께 최소화/복원

### Hit Test 처리
- `IsHitTestVisible="False"`: 마우스 이벤트를 아래로 통과
- `IsHitTestVisible="True"`: 마우스 이벤트를 처리
- DataTrigger로 ActiveWpfOverlays.Count를 감시하여 동적 전환
- Background="Transparent" 설정으로 투명 영역도 올바르게 처리

### OverlayWindow의 특수성
- OverlayWindow는 투명한 배경의 별도 창
- 이미지, 텍스트 등 WPF 기반 오버레이 렌더링 담당
- HwndHost 기반 비디오 위에 표시되어야 함
- 오버레이 항목이 없을 때는 마우스 이벤트를 통과시켜야 함

## 테스트 권장 사항

1. ✅ 렌더링 진행 중 다른 프로그램으로 전환했을 때 ExportProgressWindow가 뒤로 가는지 확인
2. ✅ 편집 중 다른 프로그램으로 전환했을 때 다른 프로그램이 정상 작동하는지 확인
3. ✅ 다시 비디오 편집기로 전환했을 때 모든 UI가 클릭 가능한지 확인
4. ✅ 이미지/텍스트 클립이 있을 때 선택/드래그가 정상 작동하는지 확인
5. ✅ 이미지/텍스트 클립이 없을 때 비디오 플레이어 영역의 UI가 클릭 가능한지 확인

