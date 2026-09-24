using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using Ninlil.Common.Serializer.Attribute;
using Ninlil.Common.Serializer.Converter;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.Model;

/// <summary>
///     This class represents a model for handling TimeSpan strings in the format "000d00h00m00s000ms".
/// </summary>
[JsonConverter<StructuredStringJsonConverter<TimeStringModel>>]
[XmlRoot("timeSpan", DataType = "string", IsNullable = true)]
public class TimeStringModel : StructuredStringModel
{
    /// <summary>
    ///     This constant represents a TimeSpan string of one day.
    /// </summary>
    public const string OneDay = "1d";

    /// <summary>
    ///     This constant represents a TimeSpan string of one hour.
    /// </summary>
    public const string OneHour = "1h";

    /// <summary>
    ///     This constant represents a TimeSpan string of one minute.
    /// </summary>
    public const string OneMinute = "1m";

    /// <summary>
    ///     This constant represents a TimeSpan string of one second.
    /// </summary>
    public const string OneSecond = "1s";

    /// <summary>
    ///     This constant represents a TimeSpan string of 5 minutes.
    /// </summary>
    public const string FiveMinutes = "5m";

    /// <summary>
    ///     This operator overload allows implicit conversion from <see cref="TimeStringModel" /> to <see cref="string" />.
    /// </summary>
    /// <param name="model">The <see cref="TimeStringModel" /> instance to convert.</param>
    /// <returns>The converted <see cref="string" />.</returns>
    public static implicit operator string(TimeStringModel model) => model?.ToString();

    /// <summary>
    ///     This operator overload allows implicit conversion from <see cref="TimeStringModel" /> to <see cref="TimeSpan" />.
    /// </summary>
    /// <param name="model">The <see cref="TimeStringModel" /> instance to convert.</param>
    /// <returns>The converted <see cref="TimeSpan" />.</returns>
    public static implicit operator TimeSpan(TimeStringModel model) =>
        model?.ToTimeSpan() ?? TimeSpan.Zero;

    /// <summary>
    ///     This operator overload allows implicit conversion from <see cref="string" /> to <see cref="TimeStringModel" />.
    /// </summary>
    /// <param name="input">The string representation of a TimeSpan in the format "000d00h00m00s000ms".</param>
    /// <returns>The created <see cref="TimeStringModel" />.</returns>
    public static implicit operator TimeStringModel(string input) =>
        TryParse(input, out TimeStringModel model) ? model : null;

    /// <summary>
    ///     This operator overload allows implicit conversion from <see cref="TimeSpan" /> to <see cref="TimeStringModel" />.
    /// </summary>
    /// <param name="input">The <see cref="TimeSpan" /> to initialize the instance from.</param>
    /// <returns>The created <see cref="TimeStringModel" />.</returns>
    public static implicit operator TimeStringModel(TimeSpan input) => FromTimeSpan(input);

    /// <summary>
    ///     This operator overload allows implicit conversion from <see cref="TimeSpan" /> to <see cref="TimeStringModel" />.
    /// </summary>
    /// <param name="input">The <see cref="TimeSpan" /> to initialize the instance from.</param>
    /// <returns>The created <see cref="TimeStringModel" />.</returns>
    public static implicit operator TimeStringModel(TimeSpan? input) =>
        input is null ? null : FromTimeSpan(input.Value);

    /// <summary>
    ///     This field contains an empty <see cref="TimeStringModel" /> instance.
    /// </summary>
    public static TimeStringModel Empty => new();

    /// <summary>
    ///     This constant contains the regex pattern for parsing a TimeSpan string.
    /// </summary>
    public static Regex Pattern = new(
        @"^((?<days>\d{1,3}d)?(?<hours>\d{1,3}h)?(?<minutes>\d{1,3}m)?(?<seconds>\d{1,3}s)?(?<milliseconds>\d{1,3}ms)?)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    /// <summary>
    ///     This method provides a factory method to create a <see cref="TimeStringModel" />
    ///     from a string representation of a TimeSpan provided in <paramref name="input" />.
    /// </summary>
    /// <param name="input">The string representation of a TimeSpan in the format "000d00h00m00s000ms".</param>
    /// <returns>>The created <see cref="TimeStringModel" />.</returns>
    public static TimeStringModel FromString(string input) => new(input);

    /// <summary>
    ///     This method provides a factory method to create a <see cref="TimeStringModel" />
    ///     from a <see cref="TimeSpan" /> provided in <paramref name="input" />.
    /// </summary>
    /// <param name="input">The <see cref="TimeSpan" /> to initialize the instance from.</param>
    /// <returns>>The created <see cref="TimeStringModel" />.</returns>
    public static TimeStringModel FromTimeSpan(TimeSpan input) => new(input);

    /// <summary>
    ///     This method provides a factory method to create a <see cref="TimeStringModel" />
    ///     representing the time elapsed since the provided <paramref name="timestamp" />.
    /// </summary>
    /// <param name="timestamp">The <see cref="DateTime" /> timestamp to calculate the elapsed time from.</param>
    /// <param name="additional">
    ///     An optional additional <see cref="TimeSpan" /> to subtract from the elapsed time.
    /// </param>
    /// <returns>>The created <see cref="TimeStringModel" />.</returns>
    public static TimeStringModel Since(DateTime timestamp, TimeSpan? additional = null)
    {
        // Localize our timespan for the difference.
        TimeSpan difference = DateTimeOffset.Now.DateTime.Subtract(timestamp);

        // Check for an additional timespan to subtract.
        if (additional is not null)
            difference = difference.Subtract(additional.Value);

        // We're done, return our timespan string model.
        return FromTimeSpan(difference);
    }

    /// <summary>
    ///     This method provides a factory method to create a <see cref="TimeStringModel" />
    ///     representing the time elapsed since the provided <paramref name="timestamp" /> in UTC.
    /// </summary>
    /// <param name="timestamp">The UTC <see cref="DateTime" /> timestamp to calculate the elapsed time from.</param>
    /// <param name="additional">
    ///     An optional additional <see cref="TimeSpan" /> to subtract from the elapsed time.
    /// </param>
    /// <returns>>The created <see cref="TimeStringModel" />.</returns>
    public static TimeStringModel SinceUtc(DateTime timestamp, TimeSpan? additional = null)
    {
        // Localize our timespan for the difference.
        TimeSpan difference = DateTimeOffset.UtcNow.DateTime.Subtract(timestamp);

        // Check for an additional timespan to subtract.
        if (additional is not null)
            difference = difference.Subtract(additional.Value);

        // We're done, return our timespan string model.
        return FromTimeSpan(difference);
    }

    /// <summary>
    ///     This method attempts to parse the <paramref name="input" />
    ///     string into a <see cref="TimeStringModel" />.
    /// </summary>
    /// <param name="input">The string representation of a TimeSpan in the format "000d00h00m00s000ms".</param>
    /// <param name="model">The parsed <see cref="TimeStringModel" /> output.</param>
    /// <returns>>A <see cref="bool" /> indicating whether the parsing was successful or not.</returns>
    public static bool TryParse(string input, out TimeStringModel model) =>
        TryParse<TimeStringModel>(input, out model);

    /// <summary>
    ///     This property contains the days component of the TimeSpan.
    /// </summary>
    [JsonIgnore]
    [XmlIgnore]
    [YamlIgnore]
    public int Days { get; private set; }

    /// <summary>
    ///     This property contains the hours component of the TimeSpan.
    /// </summary>
    [JsonIgnore]
    [XmlIgnore]
    [YamlIgnore]
    public int Hours { get; private set; }

    /// <summary>
    ///     This property contains the milliseconds component of the TimeSpan.
    /// </summary>
    [JsonIgnore]
    [XmlIgnore]
    [YamlIgnore]
    public int Milliseconds { get; private set; }

    /// <summary>
    ///     This property contains the minutes component of the TimeSpan.
    /// </summary>
    [JsonIgnore]
    [XmlIgnore]
    [YamlIgnore]
    public int Minutes { get; private set; }

    /// <summary>
    ///     This property contains the seconds component of the TimeSpan.
    /// </summary>
    [JsonIgnore]
    [XmlIgnore]
    [YamlIgnore]
    public int Seconds { get; private set; }

    /// <summary>
    ///     This private constructor initializes a new instance of the <see cref="TimeStringModel" /> class.
    /// </summary>
    public TimeStringModel()
        : base(true, Pattern) { }

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="TimeStringModel" />
    ///     class from a string representation of a TimeSpan provided in <paramref name="input" />.
    /// </summary>
    /// <param name="input">The string representation of a TimeSpan in the format "000d00h00m00s000ms".</param>
    /// <exception cref="ArgumentException">
    ///     When the <paramref name="input" /> is <see langword="null" /> or empty.
    /// </exception>
    /// <exception cref="FormatException">When the <paramref name="input" /> is not in a valid format.</exception>
    public TimeStringModel(string input)
        : base(true, Pattern)
    {
        // Try to parse as a TimeSpan first.
        if (TimeSpan.TryParse(input, out TimeSpan timeSpan))
        {
            // Pass the days into the instance.
            Days = timeSpan.Days;

            // Pass the hours into the instance.
            Hours = timeSpan.Hours;

            // Pass the milliseconds into the instance.
            Milliseconds = timeSpan.Milliseconds;

            // Pass the minutes.
            Minutes = timeSpan.Minutes;

            // Pass the seconds.
            Seconds = timeSpan.Seconds;

            // Normalize the instance.
            Normalize();

            // We're done, return.
            return;
        }

        // We're done, parse the input string.
        Parse(input);
    }

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="TimeStringModel" />
    ///     class from a <see cref="TimeSpan" /> provided in <paramref name="input" />.
    /// </summary>
    /// <param name="input">The <see cref="TimeSpan" /> to initialize the instance from.</param>
    public TimeStringModel(TimeSpan input)
        : base(true, Pattern)
    {
        // Pass the days into the instance.
        Days = input.Days;

        // Pass the hours into the instance.
        Hours = input.Hours;

        // Pass the milliseconds into the instance.
        Milliseconds = input.Milliseconds;

        // Pass the minutes.
        Minutes = input.Minutes;

        // Pass the seconds.
        Seconds = input.Seconds;

        // Normalize the instance.
        Normalize();
    }

    /// <summary>
    ///     This method overrides the ParseMatch method to extract the TimeSpan components
    ///     from the regex match groups and populate the corresponding properties of the instance.
    /// </summary>
    /// <param name="original">The original input string that was parsed.</param>
    /// <param name="match">
    ///     The regex <see cref="Match" /> containing the matched groups for the TimeSpan components.
    /// </param>
    /// <returns>The <see cref="StructuredStringModel" /> instance with the parsed TimeSpan components.</returns>
    protected override StructuredStringModel ParseMatch(string original, Match match)
    {
        // Check the original for null and a successful match then return the empty instance if unsuccessful.
        if (original?.Trim() is null or { Length: 0 } || match?.Success is not true)
            return null;

        // Parse the days component.
        if (match.Groups["days"].Success)
            Days = int.Parse(match.Groups["days"].Value.TrimEnd('d', 'D'));

        // Parse the hours component.
        if (match.Groups["hours"].Success)
            Hours = int.Parse(match.Groups["hours"].Value.TrimEnd('h', 'H'));

        // Parse the minutes component.
        if (match.Groups["minutes"].Success)
            Minutes = int.Parse(match.Groups["minutes"].Value.TrimEnd('m', 'M'));

        // Parse the seconds component.
        if (match.Groups["seconds"].Success)
            Seconds = int.Parse(match.Groups["seconds"].Value.TrimEnd('s', 'S'));

        // Parse the milliseconds component.
        if (match.Groups["milliseconds"].Success)
            Milliseconds = int.Parse(
                match.Groups["milliseconds"].Value.TrimEnd('m', 's', 'M', 'S')
            );

        // Normalize the instance.
        Normalize();

        // We're done, return the instance.
        return this;
    }

    /// <summary>
    ///     This method overrides the ToString method to provide a formatted string representation of the TimeSpan.
    /// </summary>
    /// <param name="options">
    ///     An optional dictionary of additional parameters for formatting (not used in this implementation).
    /// </param>
    /// <returns>The formatted <see cref="string" /> representation of the TimeSpan.</returns>
    protected override string ToString(IDictionary<string, object> options)
    {
        // Define our string parts.
        List<string> parts = [];

        // Check the timespan for days.
        if (Days > 0)
            parts.Add($"{Days}d");

        // Check the timespan for hours.
        if (Hours > 0)
            parts.Add($"{Hours}h");

        // Check the timespan for minutes.
        if (Minutes > 0)
            parts.Add($"{Minutes}m");

        // Check the timespan for seconds.
        if (Seconds > 0)
            parts.Add($"{Seconds}s");

        // Check the timespan for milliseconds.
        if (Milliseconds > 0)
            parts.Add($"{Milliseconds}ms");

        // We're done, return the formatted string.
        return string.Join(string.Empty, parts);
    }

    /// <summary>
    ///     This method normalizes the TimeSpan components to ensure they are within their valid ranges.
    /// </summary>
    /// <returns>The normalized <see cref="TimeStringModel" /> instance.</returns>
    private TimeStringModel Normalize()
    {
        // Check the milliseconds for seconds.
        if (Milliseconds >= 1000)
        {
            // Calculate the seconds from the milliseconds.
            int seconds = (int)Math.Floor((double)Milliseconds / 1000);

            // Add the extra seconds to the seconds component.
            Seconds += seconds;

            // Reduce the milliseconds component.
            Milliseconds -= seconds * 1000;
        }

        // Check the seconds for minutes.
        if (Seconds >= 60)
        {
            // Calculate the minutes from the seconds.
            int minutes = (int)Math.Floor((double)Seconds / 60);

            // Add the extra minutes to the minutes component.
            Minutes += minutes;

            // Reduce the seconds component.
            Seconds -= minutes * 60;
        }

        // Check the minutes for hours.
        if (Minutes >= 60)
        {
            // Calculate the hours from the minutes.
            int hours = (int)Math.Floor((double)Minutes / 60);

            // Add the extra hours to the hours component.
            Hours += hours;

            // Reduce the minutes component.
            Minutes -= hours * 60;
        }

        // Check the hours for days.
        if (Hours >= 24)
        {
            // Calculate the days from the hours.
            int days = (int)Math.Floor((double)Hours / 24);

            // Add the extra days to the days component.
            Days += days;

            // Reduce the hours component.
            Hours -= days * 24;
        }

        // We're done, return.
        return this;
    }

    /// <summary>
    ///     This method overrides the IsValid method to determine if
    ///     the current instance represents a valid TimeSpan string.
    /// </summary>
    /// <returns>A <see cref="bool" /> indicating whether the current instance is valid or not.</returns>
    public override bool IsValid() =>
        Days != Empty.Days
        || Hours != Empty.Hours
        || Minutes != Empty.Minutes
        || Seconds != Empty.Seconds
        || Milliseconds != Empty.Milliseconds;

    /// <summary>
    ///     This method converts the <see cref="TimeStringModel" /> into a <see cref="TimeSpan" />.
    /// </summary>
    /// <returns>The converted <see cref="TimeSpan" />.</returns>
    public TimeSpan ToTimeSpan() => new(Days, Hours, Minutes, Seconds, Milliseconds);
}
