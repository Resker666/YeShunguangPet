# Character chat implementation plan

Goal: Implement the approved per-character persona and basic continuous chat, retaining the existing AI sidebar entry.

Architecture: Extend AiPrompt with optional typed history and plain-text mode while keeping existing structured suggestion requests unchanged. Store bounded recent conversations and editable personas under local user settings, keyed by a hash of the skin ID. A themed chat window reuses DesktopSession modal ownership and shortcut suppression.

Tech stack: .NET 8, WPF, System.Text.Json, existing OpenAI-compatible HTTP provider. No new dependencies.

Scope: Editable background, personality, speaking style, examples; right-click chat; 12 recent complete turns; clear history; cancellation; resend failed input. No streaming or long-term inferred memory in this iteration. No cloud publishing or real-key automated tests.

- [x] Add failing tests for character isolation, history bounds, persona prompts, cancellation, failure, and provider request compatibility.
- [x] Implement AiChat domain/store and backward-compatible provider history/plain-text support.
- [x] Build chat/persona UI, integrate role menu and existing speech prompts.
- [x] Run isolated tests and new UI smoke checks; render light/dark compact windows.
- [x] Build portable artifact and document usage and limitations.

Validation: 970 base checks passed; isolated fake provider tests and light/dark compact renders inspected. Read-only code review completed. No real credentials/API calls. Release validation: 970 base checks and 721 native UI checks pass. Corrected mixed DPI units in screenshot toolbar test. Local timer visual baseline differs at 150% DPI; GitHub Windows runner must pass unchanged timer quality gates before tagging.
