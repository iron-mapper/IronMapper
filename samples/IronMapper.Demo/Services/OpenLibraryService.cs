using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using IronMapper.Demo.Models.Api;

namespace IronMapper.Demo.Services;

public class OpenLibraryService
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string BaseUrl = "https://openlibrary.org";

    public OpenLibraryService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add(
            "User-Agent",
            "IronMapper-Demo/1.0 (github.com/algmironov/IronMapper)");
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<BookSearchResponse?> SearchBooksAsync(string query, int limit = 5)
    {
        var url = $"{BaseUrl}/search.json?q={Uri.EscapeDataString(query)}" +
                  $"&limit={limit}" +
                  "&fields=key,title,author_name,first_publish_year,edition_count,subject,cover_i";
        try
        {
            var json = await _http.GetStringAsync(url);
            return JsonSerializer.Deserialize<BookSearchResponse>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            PrintApiError("SearchBooks", ex);
            return null;
        }
    }

    public async Task<WorkResponse?> GetWorkAsync(string workId)
    {
        var url = $"{BaseUrl}/works/{workId}.json";
        try
        {
            var json = await _http.GetStringAsync(url);
            return JsonSerializer.Deserialize<WorkResponse>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            PrintApiError("GetWork", ex);
            return null;
        }
    }

    public async Task<AuthorResponse?> GetAuthorAsync(string authorId)
    {
        var url = $"{BaseUrl}/authors/{authorId}.json";
        try
        {
            var json = await _http.GetStringAsync(url);
            return JsonSerializer.Deserialize<AuthorResponse>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            PrintApiError("GetAuthor", ex);
            return null;
        }
    }

    public async Task<List<AuthorResponse>> GetMultipleAuthorsAsync(params string[] authorIds)
    {
        var results = new List<AuthorResponse>();
        foreach (var id in authorIds)
        {
            var author = await GetAuthorAsync(id);
            if (author is not null)
                results.Add(author);
        }
        return results;
    }

    private static void PrintApiError(string operation, Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"  [API] {operation} недоступен: {ex.Message}");
        Console.WriteLine("  Используем статические демо-данные.");
        Console.ResetColor();
    }
}
