---
description: Run all unit tests
---
Run all Melodee unit tests.

Required environment variables for test execution:
```bash
export ConnectionStrings__DefaultConnection="Host=localhost;Database=melodee_test;Username=test;Password=test"
export ConnectionStrings__ArtistSearchEngineConnection="Data Source=:memory:"
export ConnectionStrings__MusicBrainzConnection="Data Source=:memory:"
export Jwt__Key="testkeytestkeytestkeytestkeytestkeytestkeytestkeytestkey"
export Jwt__Issuer="test"
export Jwt__Audience="test"
export security__secretKey="testsecretkeytestsecretkeytestsecretkey"
export QuartzDisabled="true"
```

Run:
```bash
dotnet test --no-build --verbosity normal
```

Report test results including pass/fail counts and any failures with details.
