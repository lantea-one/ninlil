using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Conversion;

/// <summary>
///     This class lets a <see cref="List{T}" /> of <see cref="string" /> be stored in a single
///     relational column by round-tripping it through JSON, since none of the four relational
///     providers Ninlil supports offer a portable native array column type.
/// </summary>
public class JsonStringListValueConverter()
    : ValueConverter<List<string>, string>(
        list => JsonSerializer.Serialize(list ?? new List<string>(), (JsonSerializerOptions)null),
        json =>
            string.IsNullOrWhiteSpace(json)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions)null)
    );
