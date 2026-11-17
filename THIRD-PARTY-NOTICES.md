# Speech dependency notices

OllamaTextLens source remains MIT licensed. The local speech preview also uses:

- **Kokoro v1.0 weights**, by hexgrad, Apache-2.0: https://huggingface.co/hexgrad/Kokoro-82M
- **sherpa-onnx 1.12.17**, Apache-2.0: https://github.com/k2-fsa/sherpa-onnx/tree/v1.12.17
- **NAudio 2.2.1**, MIT: https://github.com/naudio/NAudio/tree/v2.2.1
- **NTextCat 0.3.65** and bundled Core14 language profile, MIT: https://github.com/ivanakcheurov/ntextcat . The profile is pinned to upstream commit `40adcc8945bfccbbfb3d0d859d706b1e4464aba8`; its license is included in `Assets/NTextCat-LICENSE.txt` in the source and `NTextCat-LICENSE.txt` in builds. Identification runs entirely offline.
- **ONNX Runtime**, supplied by the sherpa-onnx runtime package, MIT: https://github.com/microsoft/onnxruntime
- Kokoro's phonemization data includes **eSpeak NG** data. Its upstream project is licensed under GPL-3.0-or-later: https://github.com/espeak-ng/espeak-ng . These resources keep their upstream terms and are not relicensed as MIT by this application.

The model is downloaded separately from the official sherpa-onnx release and its license files are preserved when extracted. Source/model redistributions must retain the applicable upstream notices and obligations. No paid speech service is used.

Pinned model: `kokoro-int8-multi-lang-v1_0.tar.bz2`

The optional local Japanese speech component bundles Python, ONNX Runtime 1.23.2, NumPy, Misaki 0.9.4 (Apache-2.0, including adapted MIT Cutlet code), fugashi, UniDic-lite and their dependencies. The packaged component includes upstream license files in `speech-ja/JapaneseSpeech/licenses` and dictionary license notices in its bundled data. Japanese uses the same separately installed Kokoro weights. Its source is `scripts/japanese-worker.py`; build it with `scripts/build-japanese-speech.ps1`. No paid or remote speech API is used.

SHA256: `72c347230107ef8a31098ec4d468a605244beef5f1ed00db97efa13518800653`
