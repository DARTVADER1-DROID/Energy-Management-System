using EnergyManagement.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapHealthEndpoints();

app.Run();

// Exposed for future integration tests using WebApplicationFactory.
public partial class Program;
