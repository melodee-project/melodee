---
description: Run MQL-specific tests only
---
Run only the MQL (Melodee Query Language) test suite.

```bash
dotnet test tests/Melodee.Mql.Tests/Melodee.Mql.Tests.csproj --no-build --verbosity normal
```

Report results focusing on MQL parsing, execution, and metrics tests.
