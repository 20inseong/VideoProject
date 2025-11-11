# -*- coding: utf-8 -*-

import os
import cv2
import json
import sys
from ultralytics import YOLO
import onnxruntime as ort
import numpy as np

# --- (preprocess_face, emotion_inference 함수는 변경 없이 그대로 유지) ---
def preprocess_face(face_img, size=224):
    face_resized = cv2.resize(face_img, (size, size))
    img = face_resized.astype(np.float32) / 255.0
    mean = np.array([0.485, 0.456, 0.406], dtype=np.float32)
    std = np.array([0.229, 0.224, 0.225], dtype=np.float32)
    img = (img - mean) / std
    img = img.transpose(2, 0, 1)
    img = np.expand_dims(img, axis=0)
    return img

def emotion_inference(session, face_img):
    input_tensor = preprocess_face(face_img)
    inputs = {session.get_inputs()[0].name: input_tensor}
    outputs = session.run(None, inputs)
    scores = outputs[0].squeeze()
    classes = ["happy", "surprise", "angry", "sad", "neutral"]
    max_idx = np.argmax(scores)
    max_score = scores[max_idx]
    if max_score < 0.7:
        return "Unknown"
    return classes[max_idx]

def log(msg):
    # 표준 에러(stderr)로 로그를 출력합니다.
    print(msg, file=sys.stderr, flush=True)

# --- (경로 설정 부분은 이전과 동일) ---
if len(sys.argv) < 2:
    log("Error: Frame folder path not provided.")
    sys.exit(1)
folder_path = sys.argv[1]

base_dir = os.path.dirname(os.path.abspath(__file__))
face_model_path = os.path.join(base_dir, "..", "ffmpeg", "best.onnx")
emotion_model_path = os.path.join(base_dir, "..", "ffmpeg", "emotion.onnx")
output_json_path = os.path.join(folder_path, "result.json")

# --- (모델 로드 부분은 이전과 동일) ---
face_model = YOLO(face_model_path)
emotion_session = ort.InferenceSession(emotion_model_path)

single_results = []
log(f"Input folder: {folder_path}")
log(f"Loaded face model from {face_model_path}")
log(f"Loaded emotion model from {emotion_model_path}")

if not os.path.isdir(folder_path):
    log(f"Error: Directory not found at {folder_path}")
    sys.exit(1)

png_files = sorted([f for f in os.listdir(folder_path) if f.endswith(".png")])
total_frames = len(png_files)
log(f"Found {total_frames} frames to process.")

# --- ✨ [핵심 수정] 프레임 처리 루프에 진행률 출력 추가 ---
for idx, filename in enumerate(png_files):
    # 표준 출력(stdout)으로 진행 상황을 C#에 알립니다.
    # flush=True는 출력이 버퍼링되지 않고 즉시 전송되게 합니다.
    progress_percent = (idx + 1) / total_frames * 100
    print(f"Progress:{progress_percent:.2f}", flush=True)

    frame_path = os.path.join(folder_path, filename)
    frame = cv2.imread(frame_path)
    # ... (이하 이미지 처리 및 감정 분석 로직은 그대로 유지) ...
    if frame is None:
        log(f"Failed to load frame: {frame_path}")
        continue

    orig_h, orig_w = frame.shape[:2]
    input_frame = cv2.resize(frame, (640, 640))
    results = face_model(input_frame, imgsz=640)

    frame_emotion = None
    for r in results:
        boxes = r.boxes
        for box in boxes:
            cls_id = int(box.cls[0])
            if cls_id == 1:
                x1, y1, x2, y2 = map(int, box.xyxy[0])
                x1 = int(x1 * orig_w / 640); y1 = int(y1 * orig_h / 640)
                x2 = int(x2 * orig_w / 640); y2 = int(y2 * orig_h / 640)
                face_crop = frame[y1:y2, x1:x2]
                if face_crop.size == 0:
                    emotion_label = "Unknown"
                else:
                    emotion_label = emotion_inference(emotion_session, face_crop)
                frame_emotion = emotion_label
                break
        if frame_emotion: break

    if frame_emotion and frame_emotion != "Unknown":
        single_results.append({"Timestamp": float(filename[:-4]), "Emotion": frame_emotion})

# --- (결과 저장 로직은 그대로 유지) ---
single_results = sorted(single_results, key=lambda x: x['Timestamp'])
with open(output_json_path, "w", encoding="utf-8") as f:
    json.dump(single_results, f, ensure_ascii=False, indent=2)

log(f"Analysis complete. Results saved to {output_json_path}")