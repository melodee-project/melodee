---
description: Run code formatting and analyzer checks
---
Run formatting verification and code analyzer checks (mirrors CI pipeline).

```bash
dotnet restore
dotnet format --verify-no-changes --no-restore --verbosity quiet
dotnet build --no-restore -c Release /p:EnforceCodeStyleInBuild=true /p:TreatWarningsAsErrors=false
```

Report any formatting issues or analyzer warnings that need fixing.
