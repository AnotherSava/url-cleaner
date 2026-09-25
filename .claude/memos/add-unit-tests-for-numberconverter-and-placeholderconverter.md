---
created: 2026-09-24 15:39:58
---

# Add unit tests for NumberConverter and PlaceholderConverter

tests/ holds only UrlSanitizerTests.cs and PathConverterTests.cs, and neither mentions NumberConverter or PlaceholderConverter (checked 2026-09-24). The commit gate (.claude/commit-checks.sh) runs dotnet test tests/ on every commit, but that can't catch a regression in either converter.

Next step: add NumberConverterTests.cs and PlaceholderConverterTests.cs in the same xUnit [Theory]/[InlineData] style as PathConverterTests.cs.
- NumberConverter.TryConvert(string): converts only a whole-value number with properly grouped comma thousands (10,871.69 becomes 10871.69), leaves European decimals like 10,5 alone, and returns null when nothing changes.
- PlaceholderConverter.TryConvert(string, IReadOnlyList<string> history) and ContainsPlaceholder: {{kebab-case}} placeholders are filled from the clipboard history, most recent first, FIFO by reading order. The first-copied value fills the first placeholder, and a single placeholder takes the most recent copy. The Convert placeholders section of docs/pages/configuration.md describes the full behaviour.
