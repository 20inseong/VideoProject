using System;
﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using VideoEditor.Common;
using VideoEditor.Models;
using VideoEditor.ViewModels;

namespace VideoEditor.Views
{
    public partial class ClipEditorView : UserControl
    {
        private MainViewModel? _mainViewModel;
        
        public ClipEditorView()
        {
            InitializeComponent();
            // UserControl이 로드될 때 이벤트 구독
            this.Loaded += ClipEditorView_Loaded;
            // UserControl이 언로드될 때 이벤트 구독 해지
            this.Unloaded += ClipEditorView_Unloaded;
        }

        private void ClipEditorView_Loaded(object sender, RoutedEventArgs e)
        {
            // MainViewModel 참조를 찾아서 이벤트 구독
            var mainWindow = this.FindAncestor<Window>();
            if (mainWindow?.DataContext is MainViewModel mainViewModel)
            {
                _mainViewModel = mainViewModel;
                _mainViewModel.AnalysisCompleted += OnAnalysisCompleted;
            }
            // 로드된 직후 현재 상태를 한번 업데이트
            UpdateAnalyzeButtonState();
        }

        private void ClipEditorView_Unloaded(object sender, RoutedEventArgs e)
        {
            // 메모리 누수 방지를 위해 반드시 이벤트 구독 해지
            if (_mainViewModel != null)
            {
                _mainViewModel.AnalysisCompleted -= OnAnalysisCompleted;
            }
        }

        // MainViewModel에서 AnalysisCompleted 이벤트가 발생하면 호출될 메서드
        private void OnAnalysisCompleted()
        {
            // UI 스레드에서 버튼 상태를 업데이트하도록 보장
            Dispatcher.Invoke(() => UpdateAnalyzeButtonState());
        }

        // DataContext가 변경될 때 호출될 메서드
        private void ClipEditorView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UpdateAnalyzeButtonState();
        }

        // 실제 버튼 상태를 업데이트하는 핵심 로직
        private void UpdateAnalyzeButtonState()
        {
            var contentPresenter = VisualTreeHelpers.FindVisualChild<ContentPresenter>(this);
            if (contentPresenter == null) return;

            contentPresenter.ApplyTemplate();
            var analyzeButton = contentPresenter.ContentTemplate?.FindName("AnalyzeEmotionButton", contentPresenter) as Button;
            if (analyzeButton == null) return;

            if (this.DataContext is VideoClip currentClip)
            {
                bool canAnalyze = !currentClip.IsEmotionAnalyzed && !currentClip.IsAnalyzingEmotion;
                analyzeButton.IsEnabled = canAnalyze;
            }
            else
            {
                analyzeButton.IsEnabled = false;
            }
        }


        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.SelectAll();
            }
        }

        private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
            {
                textBox.Focus();
                e.Handled = true;
            }
        }
    }
}
