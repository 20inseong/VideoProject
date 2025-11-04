//using Microsoft.ML.OnnxRuntime;
//using Microsoft.ML.OnnxRuntime.Tensors;
//using System;
//using System.Collections.Generic;
//using System.Windows.Input;

//namespace VideoEditor.Common
//{
//    internal class EmotionDetect
//    {
//        public void RunEmotionDetection()
//        {
//            // 1. ONNX 모델 파일 로드 및 세션 생성
//            using var session = new InferenceSession("path/to/your_model.onnx");

//            // 2. 모델 입력 데이터 준비(여기는 이미지라면 적절히 DenseTensor<float>를 생성)
//            var inputTensor = new DenseTensor<float>(new[] { /* ... 입력 데이터 ... */ }, inputShape);
//            var inputs = new List<NamedOnnxValue>
//            {
//                NamedOnnxValue.CreateFromTensor("input", inputTensor)
//            };

//            // 3. 모델 실행
//            using var results = session.Run(inputs);

//            // 4. 결과 처리
//            var outputTensor = results.FirstOrDefault(r => r.Name == "output")?.AsTensor<float>();
//            // 결과 사용 예시...
//        }
//    }
//}
