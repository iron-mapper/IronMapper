using System;
using System.Collections.Generic;

namespace IronMapper.Demo.Models.App;

public class SearchResult
{
    public string Query { get; set; } = string.Empty;
    public int TotalFound { get; set; }
    public List<BookSummary> Books { get; set; } = new();
    public DateTime SearchedAt { get; set; }
}
