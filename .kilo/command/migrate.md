---
description: Create a new EF Core migration
---
Create a new EF Core migration for the Melodee database.

Usage: `/migrate <MigrationName>`

```bash
dotnet ef migrations add $1 --project src/Melodee.Common/Melodee.Common.csproj --startup-project src/Melodee.Blazor/Melodee.Blazor.csproj
```

After creating the migration, review the generated Up/Down methods for correctness.
