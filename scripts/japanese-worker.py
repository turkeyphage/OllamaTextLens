"""Private stdin/stdout protocol. No network, text files or audio files."""
import base64
import json
from pathlib import Path
import re
import sys
import os
import threading

def watch_parent():
    """Exit immediately even if the app crashes during model inference."""
    if len(sys.argv)<3:
        return
    import ctypes
    from ctypes import wintypes
    kernel=ctypes.WinDLL("kernel32",use_last_error=True)
    kernel.OpenProcess.argtypes=[wintypes.DWORD,wintypes.BOOL,wintypes.DWORD]
    kernel.OpenProcess.restype=wintypes.HANDLE
    kernel.WaitForSingleObject.argtypes=[wintypes.HANDLE,wintypes.DWORD]
    handle=kernel.OpenProcess(0x00100000,False,int(sys.argv[2]))
    if not handle:
        os._exit(0)
    kernel.WaitForSingleObject(handle,0xffffffff)
    os._exit(0)

threading.Thread(target=watch_parent,daemon=True).start()

sys.stdin.reconfigure(encoding="utf-8")
sys.stdout.reconfigure(encoding="utf-8")
import numpy as np
import onnxruntime as ort
import unidic
import unidic_lite
unidic.DICDIR = unidic_lite.DICDIR
from misaki.ja import JAG2P

def send(value):
    print(json.dumps(value, ensure_ascii=True), flush=True)

def main():
    folder = Path(sys.argv[1])
    options = ort.SessionOptions()
    options.intra_op_num_threads = 4
    options.inter_op_num_threads = 1
    session = ort.InferenceSession(str(folder / "model.int8.onnx"), sess_options=options, providers=["CPUExecutionProvider"])
    voices = np.fromfile(folder / "voices.bin", dtype=np.float32).reshape(54,510,1,256)
    vocab = {s:int(i) for s,i in (line.rsplit(" ",1) for line in (folder / "tokens.txt").read_text(encoding="utf-8").splitlines())}
    g2p = JAG2P()
    send({"ready": 24000})
    for line in sys.stdin:
        try:
            request = json.loads(line)
            text, speaker, speed = request["text"], request["speaker"], request["speed"]
            if speaker not in (37,41) or not 0.5 <= speed <= 2 or not 0 < len(text) <= 12000:
                raise ValueError("Invalid request")
            for sentence in re.findall(r"[^。！？!?]+[。！？!?]?", text):
                # Bound phoneme counts and cancellation latency even without punctuation.
                for start in range(0,len(sentence),80):
                    fragment=sentence[start:start+80]
                    phonemes, _ = g2p(fragment)
                    if not phonemes.strip():
                        continue
                    if set(phonemes)-set(vocab):
                        raise ValueError("Unsupported phonemes")
                    ids=[vocab[p] for p in phonemes]
                    if not 0 < len(ids) <= 508:
                        raise ValueError("Phoneme sequence too long")
                    audio=np.asarray(session.run(None,{"tokens":np.array([[0,*ids,0]],dtype=np.int64),"style":voices[speaker,len(ids)-1],"speed":np.array([speed],dtype=np.float32)})[0],dtype="<f4").ravel()
                    if not np.isfinite(audio).all() or len(audio)>24000*60:
                        raise ValueError("Invalid output")
                    send({"audio":base64.b64encode(audio.tobytes()).decode("ascii")})
            send({"done":True})
        except (BrokenPipeError, EOFError):
            return
        except Exception:
            # Do not echo source text or phonemes into diagnostics.
            send({"error":"Japanese speech generation failed"})

if __name__ == "__main__":
    main()
