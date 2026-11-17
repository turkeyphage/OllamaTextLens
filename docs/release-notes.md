## OllamaTextLens v0.2.5

- Offline Kokoro read-aloud for English, Spanish and Japanese.
- Local source-language detection with manual language selection.
- Female/male voices: Heart/Michael (English), Dora/Alex (Spanish), Alpha/Kumo (Japanese).
- Play, pause/resume, stop, and reading-speed controls beside the original text.
- Explicit speech-model download with readiness checks and cancellation.
- Voice tests in settings work without an Ollama connection.

### Download and run

**Download the ZIP for all three languages.** Extract everything, keep the `speech-ja` folder next to `OllamaTextLens.exe`, and run the EXE. Python and .NET do not need to be installed separately for this release package.

The separate EXE supports English/Spanish read-aloud; Japanese also requires the component supplied in the ZIP. Missing Japanese components are reported in the app rather than falling back to an English voice.

Install the speech model through **Download speech model** in settings before first playback (about 126 MiB). Translation still requires your own local Ollama installation and chat model. After setup, speech and language detection run locally with no paid API or subscription.

Japanese text with kana is detected automatically. For kanji-only fragments, select **日本語** manually. Mixed-language passages may require a manual choice; each selection uses one pronunciation language.

The Windows executables are unsigned, so Windows SmartScreen may show an unrecognized-app warning. SHA256 checksums are provided with the release assets.
