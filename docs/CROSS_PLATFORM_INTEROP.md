# Cross-platform interoperability

The CI workflow validates the optical-transfer wire in both directions against the TypeScript reference implementation in `bashalarmistalt/decimen-optical-transfer`.

The test sequence is:

1. TypeScript reference generates DCF2, frame, and carousel fixtures.
2. C# decodes and verifies those fixtures, including SHA-256 and FNV-1a.
3. C# generates DCF2, frame, and carousel fixtures.
4. TypeScript reference parses and decodes the C# fixtures and verifies the recovered file bytes.

This is intentionally separate from the in-process C# golden-vector tests: both implementations are exercised independently.
