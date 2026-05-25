---
description: Generate code coverage report
---
Run tests with code coverage and generate a report.

Required environment variables:
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
dotnet test --no-build --verbosity normal --collect:"XPlat Code Coverage" --settings coverage.runsettings --results-directory coverage/results
dotnet tool install -g dotnet-reportgenerator-globaltool 2>/dev/null || true
reportgenerator -reports:"coverage/results/**/coverage.cobertura.xml" -targetdir:"coverage/report" -reporttypes:"Html;TextSummary" -assemblyfilters:"+Melodee.*;+server;+mcli;-Melodee.Tests.*"
cat coverage/report/Summary.txt
```
