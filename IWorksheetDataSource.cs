using System.Text.Json;

namespace Chlorella;

/// <summary>Supplies worksheet state and executes requests for the interface.</summary>
public interface IWorksheetDataSource
{
    Task<JsonElement> SendAsync(string method, object parameters);
}
