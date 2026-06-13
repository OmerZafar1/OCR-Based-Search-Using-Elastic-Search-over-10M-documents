# Run DocumentSearch with hot reload (two terminals)

# Terminal 1 - API
dotnet watch run --project src/DocumentSearch.Api --launch-profile http

# Terminal 2 - Worker
dotnet watch run --project src/DocumentSearch.Worker
