# Ratioscope

[![Logo](./Assets/Bundles/Texture/2D/logo.png)](./)

A local LLM chatbot that renders its own inference as a visual field. As the model generates a reply, the Cortex turns the loaded transformer structure, completed forward passes, next-token candidates, probabilities, and uncertainty into light.

Everything runs on your machine: inference, tokenization, chat history, and visualization. No Python service, third-party runtime, or cloud model at runtime.

The telemetry is real, but the visual composition is artistic. It is not a view of hidden states, nor a claim that the model's thoughts can be read. The in-app Help panel explains which data is measured and which parts are interpretation.

[![Preview](./Docs/demo.gif)](./)

## Install

Download a build from [Releases](https://github.com/Elykdez/Ratioscope/releases). Windows, macOS, and Android builds **do not contain model weights**.

The macOS build is a universal binary for Apple silicon and Intel, requiring macOS 12 or newer. It is not notarized, so clear the quarantine flag once after unzipping:

```sh
xattr -dr com.apple.quarantine Ratioscope.app
```

On first launch the app downloads the default model. You can pick another CPU or GPU profile; when its artifact is missing, **Settings > Download Models** fetches the selected one. Downloads are streamed to a `.part` file, verified, then stored under `Application.persistentDataPath/Models`, surviving app updates but removed on uninstall.

### Side-loading Models

If the in-app download fails on your device, place the model file manually:

- **Windows**: put the `.sentis` file under `Ratioscope_Data/StreamingAssets/Sentis`.
- **macOS**: put the `.sentis` file under `Ratioscope.app/Contents/Resources/Data/StreamingAssets/Sentis`. Right-click the app and choose **Show Package Contents** to open the bundle.
- **Android**: install [adb](https://developer.android.com/tools/adb) and put it on your `PATH`, clone this repository (Unity is not required), download the model into `Assets/StreamingAssets/Sentis`, then run `Tools/push-model.bat` to push it to the connected device. Start the app once first so its data directory exists.

The 4B model has no prebuilt artifact; converting it to a Sentis graph requires the Unity Editor. See [CONTRIBUTION.md](./CONTRIBUTION.md).

### Android Backends

CPU decoding runs at roughly 1-2 tokens per second, so the GPU profiles are the forward-looking option. Selecting a non-CPU model on a Vulkan device without enough memory will end in an OOM quit; if it survives, your phone is capable of running it.

## Documentation

In-app help, also readable here:

[English](./Assets/StreamingAssets/doc/desc_en.md) | [简体中文](./Assets/StreamingAssets/doc/desc_zh.md)

Building from source, model export, and module internals: [CONTRIBUTION.md](./CONTRIBUTION.md).

## License

Project code: [MIT](./LICENSE).

Model weights are not distributed with the builds. The default model is derived from [Qwen/Qwen3-1.7B](https://huggingface.co/Qwen/Qwen3-1.7B); both AI models are distributed under Apache 2.0.
