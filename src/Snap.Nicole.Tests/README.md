# Reliability regression tests

Run `dotnet test src/Snap.Nicole.Tests/Snap.Nicole.Tests.csproj`.

The tests source-link the production settings provider, AI content models, endpoint validator, and Chat Completions history provider. They run without a WinUI window or the native C++ project. Only the app-specific settings directory, dispatcher context, and Sentry initialization constant are substituted. Sentry remains uninitialized, and no provider API requests are made.

Coverage includes malformed or locked settings, recovery before writes resume, trailing assistant text, reasoning replay after a serialized session is restored, multiple approval requests and partial decisions, and HTTPS/loopback endpoint validation. UI loading and image-link behavior still require a Windows UI smoke test.
