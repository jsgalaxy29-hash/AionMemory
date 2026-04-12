# AI providers

This repository exposes pluggable AI providers through a single set of interfaces and a central factory. All AI call sites should rely on the `IChatModel`, `IEmbeddingsModel`, `ITranscriptionModel`, and `IVisionModel` abstractions registered by `AddAionAi`.

## Provider selection

`AiProviderSelector` inspects `Aion:Ai` settings (`Provider`, `BaseEndpoint`, `ApiKey`, specific endpoints) and chooses the active provider:

- If no configuration is provided, the selector marks the AI as **inactive** and routes calls to no-op providers that fail fast.
- If an unknown provider name is supplied, the selector falls back to the **mock** provider.
- Supported provider names: `mock` (offline), `http` (generic OpenAI-compatible), `openai`, `mistral`.
- The selector normalizes names to lowercase and trims whitespace.

`AiModelFactory` routes each AI capability to the keyed implementation for the selected provider and automatically falls back to the mock provider when the requested provider is not registered. No prompts or sensitive payloads are logged.

## Available providers

| Provider name | Capabilities | Notes |
|---------------|--------------|-------|
| `mock`        | Chat, embeddings, transcription, vision | Offline-safe, deterministic responses for tests and local development. |
| `http`        | Chat, embeddings, transcription, vision | Generic OpenAI-compatible HTTP endpoints with retry handling and configurable timeouts. |
| `openai`      | Chat, embeddings, transcription         | Uses OpenAI endpoints; honors `RequestTimeout`, `ApiKey`, and optional `Organization`. |
| `mistral`     | Chat, embeddings, transcription         | Uses Mistral endpoints; honors `RequestTimeout` and `ApiKey`. |
| `local`       | Chat, embeddings, transcription         | Echo/deterministic stubs for explicit offline runs. |

Vision currently flows through the HTTP provider with retry logic. The mock provider also implements vision for offline validation.

## Dependency injection

- Call `services.AddAionAi(configuration)` to register the core factory, options, and the mock/http providers.
- Add optional providers when needed:
  - `services.AddAionOpenAi();`
  - `services.AddAionMistral();`
- Inject `IChatModel`/`IEmbeddingsModel`/`ITranscriptionModel`/`IVisionModel` (or the legacy aliases `ILLMProvider`, `IEmbeddingProvider`, `IAudioTranscriptionProvider`, `IVisionService`) wherever AI capabilities are required.

## Resilience and safety

- HTTP-based providers share retry helpers for 429/5xx responses and honor `Aion:Ai:RequestTimeout`.
- Authentication headers are only configured when an API key is provided.
- Mock providers avoid any network access and keep responses deterministic, making them suitable for automated tests.
