using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Conversion;

/// <summary>
///     This class stores a <see cref="List{T}" /> of <see cref="string" /> in one column as JSON,
///     since the four relational providers have no common array column type.
///     <see cref="JsonStringListValueComparer" /> lets the change tracker see edits to the list.
/// </summary>
public class JsonStringListValueConverter()
    : ValueConverter<List<string>, string>(
        list => JsonSerializer.Serialize(list ?? new List<string>(), (JsonSerializerOptions)null),
        json =>
            string.IsNullOrWhiteSpace(json)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions)null)
    );
