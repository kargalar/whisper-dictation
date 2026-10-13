# Third-party notices

Whisper Dikte uses the following upstream projects. The portable package includes a version and license manifest in `licenses/NuGet-dependencies.md`, package-supplied license files, and NVIDIA CUDA license documents.

- [.NET and WPF](https://github.com/dotnet/wpf): MIT; bundled .NET runtime includes Microsoft's `LICENSE.txt` and `ThirdPartyNotices.txt`.
- [Whisper.net](https://github.com/sandrohanea/whisper.net): MIT.
- [whisper.cpp / ggml](https://github.com/ggml-org/whisper.cpp): MIT; distributed through the Whisper.net native runtime packages.
- [NAudio](https://github.com/naudio/NAudio): MIT.
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet): MIT.
- [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore): MIT, with SQLitePCLRaw and SQLite native dependencies as recorded in the package manifest.
- [SQLite](https://sqlite.org/copyright.html): public domain.
- [NVIDIA CUDA](https://docs.nvidia.com/cuda/eula/): redistributed runtime components under the CUDA Toolkit license. NVIDIA drivers are not included.
- [Microsoft Visual C++ / OpenMP runtime](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files): Microsoft redistributable runtime components, distributed app-locally for the native engine.

Whisper large-v3-turbo and large-v3 model weights are included in GGML format from [ggerganov/whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp), revision `5359861c739e955e79d9a303bcbc70fb988958b1`. The upstream [OpenAI Whisper](https://github.com/openai/whisper) project distributes its code and model weights under the MIT license. `licenses/LICENSE-OpenAI.txt` contains that license, and `models/catalog.json` records exact file sizes and SHA256 checksums. No test helper executables or test recordings are distributed.
