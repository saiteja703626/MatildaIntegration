using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddPolicy("LoungePolicy", policy =>
    {
        policy
            .WithOrigins(
                "https://saitejar.com",
                "https://www.saitejar.com")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

//Rate limiting — 10 requests per minute per client IP
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
{
    context.HttpContext.Response.ContentType = "application/json";

    await context.HttpContext.Response.WriteAsJsonAsync(new
    {
        error = "Too many requests. Please try again later."
    }, cancellationToken);
};

    options.AddPolicy("MatildaPolicy", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});



builder.Services.AddHttpClient("Matilda", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("LoungePolicy");  //Enable the CORS middleware here
app.UseRateLimiter();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
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


    // Validate the conversation
    if (request.Messages is null || request.Messages.Count == 0)
    {
        return Results.BadRequest(new
        {
            error = "Conversation cannot be empty."
        });
    }

    // Limit the number of messages
    if (request.Messages.Count > 50)
    {
        return Results.BadRequest(new
        {
            error = "Conversation cannot contain more than 50 messages."
        });
    }

    // Validate individual messages
    foreach (var message in request.Messages)
    {
        if (message is null)
        {
            return Results.BadRequest(new
            {
                error = "Message cannot be null."
            });
        }

        if (string.IsNullOrWhiteSpace(message.Content))
        {
            return Results.BadRequest(new
            {
                error = "Message content cannot be empty."
            });
        }

        if (message.Content.Length > 4000)
        {
            return Results.BadRequest(new
            {
                error = "Message cannot exceed 4000 characters."
            });
        }

        if (message.Role != "user" && message.Role != "assistant")
        {
            return Results.BadRequest(new
            {
                error = "Invalid message role."
            });
        }
    }

    // The latest message must come from the user
    if (request.Messages[^1].Role != "user")
    {
        return Results.BadRequest(new
        {
            error = "The latest message must be from the user."
        });
    }


    // 2. Prepare the question
    var payload = new
    {
        model = "matilda",
        messages = request.Messages.Select(message => new
        {
            role = message.Role,
            content = message.Content
        }).ToArray()
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

    try
    {
        // 6. Send the request to Matilda
        var client = httpClientFactory.CreateClient("Matilda");

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
    }
    catch (HttpRequestException)
    {
        return Results.Json(
            new { error = "Unable to connect to Matilda. Please try again later." },
            statusCode: 503);
    }
    catch (JsonException)
    {
        return Results.Json(
            new { error = "Matilda returned an invalid response. Please try again later." },
            statusCode: 502);
    }
    catch (TaskCanceledException)
    {
        return Results.Json(
            new { error = "Matilda took too long to respond. Please try again later." },
            statusCode: 504);
    }
})
.RequireRateLimiting("MatildaPolicy");



app.Run();

//Instead of sending just one question, we can now send the entire conversation.
//example
//new ChatMessage("user", "What is C#?"),
//new ChatMessage("assistant", "C# is a programming language."),
//new ChatMessage("user", "Who developed it?")
public record ChatRequest(List<ChatMessage> Messages);
public record ChatMessage(string Role, string Content);

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
