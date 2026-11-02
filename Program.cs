using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
var builder = WebApplication.CreateBuilder(args);


// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddHttpClient();
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.MapGet("/matilda/models-check", async (IConfiguration configuration) =>
{
    var apiKey = configuration["Matilda:ApiKey"];

    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return Results.Problem("Matilda API key is missing.");
    }

    using var httpClient = new HttpClient();

    httpClient.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", apiKey);

    var response = await httpClient.GetAsync(
        "https://matilda.maincode.com/api/v1/models");

    var responseBody = await response.Content.ReadAsStringAsync();

    return Results.Content(
        responseBody,
        response.Content.Headers.ContentType?.ToString() ?? "text/plain",
        statusCode: (int)response.StatusCode);
});

app.MapPost("/matilda/ask", async (
    ChatRequest request,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory) =>
{
    // 1. Retrieve the API key
    var apiKey = configuration["Matilda:ApiKey"];

    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return Results.Json(new { error = "API key is missing." },
            statusCode: 500);
    }

    // 2. Prepare the question
    var payload = new
    {
        model = "matilda",
        messages = new[]
        {
            new
            {
                role = "user",
                content = request.Message
            }
        }
    };

    // 3. Create an HTTP request
    var httpRequest = new HttpRequestMessage(
        HttpMethod.Post,
        "https://matilda.maincode.com/api/v1/chat/completions");

    // 4. Add authentication
    httpRequest.Headers.Authorization =
        new AuthenticationHeaderValue("Bearer", apiKey);

    // 5. Attach the question as JSON
    httpRequest.Content = JsonContent.Create(payload);

    // 6. Send the request to Matilda
    var client = httpClientFactory.CreateClient();

    using var response = await client.SendAsync(httpRequest);

    // 7. Read Matilda's response
    var responseBody = await response.Content.ReadAsStringAsync();

    // 8. Check whether the request succeeded
    if (!response.IsSuccessStatusCode)
    {
        return Results.Json(
            new { error = responseBody },
            statusCode: (int)response.StatusCode);
    }

    // 9. Extract the AI-generated answer
    using var jsonDocument = JsonDocument.Parse(responseBody);

    var choices = jsonDocument.RootElement.GetProperty("choices");

    var answer = choices[0]
        .GetProperty("message")
        .GetProperty("content")
        .GetString();

    // 10. Return the answer
    return Results.Json(new
    {
        answer = answer
    });
});


app.Run();

public record ChatRequest(string Message);
record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
