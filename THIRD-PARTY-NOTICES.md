# Speech dependency notices

OllamaTextLens source remains MIT licensed. The local speech preview also uses:

- **Kokoro v1.0 weights**, by hexgrad, Apache-2.0: https://huggingface.co/hexgrad/Kokoro-82M
- **sherpa-onnx 1.12.17**, Apache-2.0: https://github.com/k2-fsa/sherpa-onnx/tree/v1.12.17
- **NAudio 2.2.1**, MIT: https://github.com/naudio/NAudio/tree/v2.2.1
- **ONNX Runtime**, supplied by the sherpa-onnx runtime package, MIT: https://github.com/microsoft/onnxruntime
- Kokoro's phonemization data includes **eSpeak NG** data. Its upstream project is licensed under GPL-3.0-or-later: https://github.com/espeak-ng/espeak-ng . These resources keep their upstream terms and are not relicensed as MIT by this application.

The model is downloaded separately from the official sherpa-onnx release and its license files are preserved when extracted. Source/model redistributions must retain the applicable upstream notices and obligations. No paid speech service is used.

Pinned model: `kokoro-int8-multi-lang-v1_0.tar.bz2`

SHA256: `72c347230107ef8a31098ec4d468a605244beef5f1ed00db97efa13518800653`
