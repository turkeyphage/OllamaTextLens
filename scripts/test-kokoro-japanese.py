"""Standalone Japanese audition using the existing Kokoro ONNX weights."""
import html
import importlib.metadata
import json
import os
from pathlib import Path
import re
import time

started = time.perf_counter()
import numpy as np
import onnxruntime as ort
import soundfile as sf
import unidic
import unidic_lite
# The bundled lightweight dictionary keeps setup offline after installation.
unidic.DICDIR = unidic_lite.DICDIR
from misaki.ja import JAG2P

folder = Path(os.environ["LOCALAPPDATA"]) / "OllamaTextLens/speech/kokoro-int8-multi-lang-v1_0"
out = Path(__file__).resolve().parents[1] / "artifacts/test/kokoro-japanese"
out.mkdir(parents=True, exist_ok=True)
g2p_started = time.perf_counter()
g2p = JAG2P()
frontend_load_seconds = time.perf_counter() - g2p_started
options = ort.SessionOptions()
options.intra_op_num_threads = 4
options.inter_op_num_threads = 1
load_started = time.perf_counter()
session = ort.InferenceSession(str(folder / "model.int8.onnx"), sess_options=options, providers=["CPUExecutionProvider"])
voices = np.fromfile(folder / "voices.bin", dtype=np.float32).reshape(54, 510, 1, 256)
vocab = {}
for line in (folder / "tokens.txt").read_text(encoding="utf-8").splitlines():
    symbol, number = line.rsplit(" ", 1)
    vocab[symbol] = int(number)
model_load_seconds = time.perf_counter() - load_started
startup_seconds = time.perf_counter() - started
paragraph = "こんにちは。これは日本語の読み上げテストです。文章を選択すると、自然な声で原文を聞くことができます。途中で一時停止したり、読み上げの速さを変えたりできます。"
reading = "こん にち は。これ は にほんご の よみあげ テスト です。ぶんしょう を せんたく する と、しぜん な こえ で げんぶん を きく こと が できます。とちゅう で いちじていし したり、よみあげ の はやさ を かえたり できます。"
cases = [("japanese-short-female", "こんにちは。今日はいい天気ですね。", 37, "Alpha 女聲", 1.0),
         ("japanese-female", paragraph, 37, "Alpha 女聲", 1.0),
         ("japanese-male", paragraph, 41, "Kumo 男聲", 1.0),
         ("japanese-female-fast", paragraph, 37, "Alpha 女聲", 1.25)]
results = []
print(f"READY model={model_load_seconds:.2f}s frontend={frontend_load_seconds:.2f}s startup={startup_seconds:.2f}s", flush=True)
for name, text, speaker, voice, speed in cases:
    watch = time.perf_counter()
    chunks, parts, first_audio, g2p_seconds = [], [], None, 0.0
    for sentence in re.findall(r"[^。！？!?]+[。！？!?]?", text):
        tick = time.perf_counter()
        phonemes, _ = g2p(sentence)
        g2p_seconds += time.perf_counter() - tick
        missing = sorted(set(phonemes) - set(vocab))
        if missing:
            raise RuntimeError(f"Unmapped Japanese phonemes (never silently dropped): {missing!r}; {phonemes!r}")
        ids = [vocab[p] for p in phonemes]
        if not ids or len(ids) > 508:
            raise RuntimeError("Invalid phoneme sequence length")
        wav = np.asarray(session.run(None, {"tokens": np.array([[0, *ids, 0]], dtype=np.int64), "style": voices[speaker, len(ids)-1], "speed": np.array([speed], dtype=np.float32)})[0]).ravel()
        first_audio = first_audio if first_audio is not None else time.perf_counter() - watch
        if not np.isfinite(wav).all() or np.max(np.abs(wav)) < 0.001:
            raise RuntimeError("Invalid or silent Japanese synthesis")
        parts.append(wav)
        chunks.append(dict(text=sentence, phonemes=phonemes, seconds=len(wav)/24000))
    elapsed = time.perf_counter() - watch
    combined = np.concatenate(parts)
    sf.write(out / f"{name}.wav", combined, 24000, subtype="PCM_16")
    result = dict(name=name, text=text, speaker=speaker, voice=voice, speed=speed, audio_seconds=len(combined)/24000, generation_seconds=elapsed, first_audio_seconds=first_audio, g2p_seconds=g2p_seconds, rms=float(np.sqrt(np.mean(combined**2))), chunks=chunks)
    results.append(result)
    print(json.dumps({k:v for k,v in result.items() if k!="chunks"}, ensure_ascii=False), flush=True)

report = dict(model="existing kokoro-int8-multi-lang-v1_0", runtime=ort.__version__, provider="CPUExecutionProvider", threads=4, frontend="Misaki 0.9.4 default cutlet + UniDic-lite", frontend_load_seconds=frontend_load_seconds, model_load_seconds=model_load_seconds, startup_seconds=startup_seconds, packages={p:importlib.metadata.version(p) for p in ["misaki","pyopenjtalk-plus","fugashi","unidic-lite","onnxruntime","numpy","soundfile"]}, results=results)
(out / "results.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
cards = []
for r in results:
    cards.append(f'<section><h2>{r["voice"]} · {r["speed"]}×</h2><p lang="ja">{html.escape(r["text"])}</p><audio controls preload="none" src="{r["name"]}.wav"></audio><p>首句音訊就緒 {r["first_audio_seconds"]:.2f} 秒；整段生成 {r["generation_seconds"]:.2f} 秒；音訊長度 {r["audio_seconds"]:.2f} 秒。</p></section>')
(out / "listen.html").write_text('<!doctype html><html lang="zh-Hant"><meta charset="utf-8"><title>Kokoro 日文試聽</title><style>body{font:18px system-ui;max-width:850px;margin:40px auto;padding:0 20px;background:#faf8f1;color:#242424}section{padding:20px;background:white;margin:20px 0;border-radius:12px}audio{width:100%}h2{font-size:22px}</style><h1>Kokoro 日文男女聲試聽</h1><p>獨立實驗，尚未整合 App。沿用本機量化模型，CPU 4 執行緒，日文 Misaki 發音前處理。</p>' + f'<p>模型載入 {model_load_seconds:.2f} 秒；發音工具初始化 {frontend_load_seconds:.2f} 秒；含 Python 匯入等啟動時間共 {startup_seconds:.2f} 秒。以下生成時間不含啟動，包含文字轉發音。</p>' + ''.join(cards) + '<section><h2>聽感檢查</h2><p>請留意「日本語、文章、原文、一時停止」的讀音，以及句尾語氣與句間停頓。男女聲使用完全相同的段落。</p><p>預期讀音提示：' + html.escape(reading) + '</p></section><p>音檔通過有限數值、非靜音檢查；這不代表發音與自然度已經確認。首句就緒指生成完成，不含音效裝置延遲；未實測連續播放。此次 Python/ONNX 路徑不同於 App 的 sherpa-onnx，速度不能直接視為整合後表現。</p></html>', encoding="utf-8")
print(f"DONE {out}", flush=True)
