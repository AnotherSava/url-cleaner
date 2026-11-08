---
created: 2026-09-24 22:50:53
---

# Stop UrlSanitizer percent-encoding braces and other characters in a URL it rewrites

Seen 2026-09-24 in the plugin-host acceptance run: copying https://example.com/?q={{term}}&utm_source=news with the default feature order leaves https://example.com/?q=%7B%7Bterm%7D%7D on the clipboard. The tracking parameter is stripped correctly, but the kept parameter's braces come back percent-encoded.

Cause, from src/Core/UrlSanitizer.cs: the query is read from uri.Query and the path from uri.AbsolutePath, and the result is rebuilt from those (the return near the end: baseUrl + path + (cleanedQuery ?? query) + fragment). System.Uri has already escaped characters such as { and } in those properties, so the rebuilt URL differs from what was copied in parts the cleaner never meant to touch. The project memory's Architecture Notes claim the query parsing is manual to preserve the original encoding, which this contradicts.

Next step: take the path, query and fragment as substrings of the original text (Uri only for validation and the host), so kept parameters and path segments come back byte for byte. Add UrlSanitizerTests cases with {{placeholder}} braces, a | and a space-encoded value, checking that only the stripped parameters change. Then correct the memory note.

The configuration page's Feature order example is unaffected: it runs placeholders before the URL cleaner, so no braces reach it.
