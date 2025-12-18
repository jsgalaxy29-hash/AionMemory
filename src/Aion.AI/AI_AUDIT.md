# AION AI audit (quick pass)

- Options now cover dedicated endpoints/models for LLM, embeddings, transcription and vision but still require environment configuration to be effective.
- HTTP providers are ready for OpenAI-like APIs (chat/completions, embeddings, audio/transcriptions, vision/analyze) with stub fallbacks when endpoints are missing.
- Orchestrators (intent detection, module design, CRUD/report interpreters, vision) rely on LLM prompting and basic heuristics; they remain lightweight until concrete model hooks are provided.
- Dependency injection is centralized through `AddAionAi`, wiring named `HttpClientFactory` clients for each AI capability.
