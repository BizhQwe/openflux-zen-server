# Database migrations

The SQLite schema is created on first start with EF Core EnsureCreated for immediate fresh installs. The project remains migration-ready; generate a named migration with `dotnet ef migrations add InitialCreate --project src/OpenFlux.Zen.Server` when EF tooling is available.
