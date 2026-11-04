# Requirements

- Remove nonempty LINE channel access token values from the tracked ChurchReport base `appsettings.json`; leave unrelated settings untouched.
- Keep Development LINE credentials isolated to `LineMessaging:DevTest:ChannelAccessToken` in User Secrets. Development must reject nonempty production aliases and the generic `LINE_CHANNEL_ACCESS_TOKEN` fallback.
- Require a nonempty Development DevTest token to originate from the host's User Secrets provider.
- Load production LINE alias tokens from the `LINE__`-prefixed environment configuration provider in Production and other non-Development environments.
- Preserve normal provider ownership, avoid reload watchers or token caches, and do not expose secret values in errors, tests, logs, or task evidence.
- Do not edit the user's existing User Secrets, configure host or GitHub secrets, or connect to LINE or D365.
- Preserve unrelated in-progress files and do not create a new Trellis task.
