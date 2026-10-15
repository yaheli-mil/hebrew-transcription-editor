# Third-party notices

## Fonts (`src/app/fonts/`)

Rubik and Noto Sans Hebrew are licensed under the SIL Open Font License 1.1. The license text and their copyright notices are in [`src/app/fonts/OFL.txt`](src/app/fonts/OFL.txt).

## Icons

The icons in `src/app/index.html` and the app icon (`src/installer/app.ico`, drawn after Lucide's "audio-lines") come from [Lucide](https://lucide.dev):

> ISC License
>
> Copyright (c) for portions of Lucide are held by Cole Bemis 2013-2022 as part of Feather (MIT). All other copyright (c) for Lucide are held by Lucide Contributors 2022.
>
> Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby granted, provided that the above copyright notice and this permission notice appear in all copies.
>
> THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

## Used at run time, not included in this repository

- **Speech model:** [ivrit-ai/whisper-large-v3-turbo-ct2](https://huggingface.co/ivrit-ai/whisper-large-v3-turbo-ct2). See its model card for its license.
- **Python packages:**
  - [faster-whisper](https://github.com/SYSTRAN/faster-whisper)
  - [CTranslate2](https://github.com/OpenNMT/CTranslate2)
  - [PyAV](https://github.com/PyAV-Org/PyAV), which bundles FFmpeg
  - [ONNX Runtime](https://github.com/microsoft/onnxruntime)
  - [tokenizers](https://github.com/huggingface/tokenizers)
  - [NumPy](https://numpy.org)
  - others

  Each is under its own license, and `build.py` copies their license files into the installed app.
